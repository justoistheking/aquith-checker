using System.Net;
using System.Text;
using System.Text.Json;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using FortniteChecker.Models;
using FortniteChecker.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FortniteChecker;

internal static class Program
{
    private static AppConfig _config = new();
    private static LoginStateStore _states = new();
    private static DiscordShardedClient _discord = null!;
    private static InteractionService _interactions = null!;
    private static CheckWorkerPool _workers = null!;
    private static CheckerService _checker = null!;

    public static async Task Main(string[] args)
    {
        _config = LoadConfig();
        ValidateConfig();

        _discord = new DiscordShardedClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds,
            LogLevel = LogSeverity.Info,
            AlwaysDownloadUsers = false,
            UseInteractionSnowflakeDate = false
        });
        _interactions = new InteractionService(_discord);
        _checker = new CheckerService(_config);
        _workers = new CheckWorkerPool(_discord, _config.WorkerCount, _config.QueueCapacity, _checker.RunAsync);

        _discord.Log += LogAsync;
        _discord.ShardReady += ShardReadyAsync;
        _discord.InteractionCreated += HandleInteractionAsync;
        _interactions.SlashCommandExecuted += SlashCommandExecutedAsync;

        await _interactions.AddModuleAsync<Commands>(_servicesProvider());
        await _discord.LoginAsync(TokenType.Bot, _config.DiscordToken);
        await _discord.StartAsync();
        await _workers.StartAsync();

        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.UseUrls($"http://0.0.0.0:{_config.Port}");
        var app = builder.Build();

        app.MapGet("/", () => Results.Content("Aquith Checker is online.", "text/plain", Encoding.UTF8));
        app.MapGet("/health", () => Results.Json(new
        {
            status = "ok",
            name = "Aquith Checker",
            shards = _discord.Shards.Count,
            workers = _workers.WorkerCount,
            queue = _workers.QueueLength
        }));

        app.MapGet("/epic/callback", async (HttpRequest request) =>
        {
            var state = request.Query["state"].ToString();
            var code = request.Query["code"].ToString();
            var error = request.Query["error"].ToString();

            if (!string.IsNullOrWhiteSpace(error))
                return Results.Content("<h2>Login cancelled</h2><p>You can close this tab and run /login again in Discord.</p>", "text/html");
            if (string.IsNullOrWhiteSpace(state) || string.IsNullOrWhiteSpace(code) || !_states.TryConsume(state, out var login))
                return Results.Content("<h2>Invalid or expired login</h2><p>Please run /login again in Discord.</p>", "text/html");

            try
            {
                await _workers.EnqueueAsync(new CheckJob(login.DiscordUserId, code));
                return Results.Content("<h2>Login successful</h2><p>Your Aquith Checker job is queued. You can close this tab. Your result will be sent to your Discord DMs.</p>", "text/html");
            }
            catch
            {
                return Results.Content("<h2>Queue full</h2><p>Please wait a moment and try /login again.</p>", "text/html", statusCode: 429);
            }
        });

        Console.WriteLine("====================================");
        Console.WriteLine("        AQUITH CHECKER ONLINE       ");
        Console.WriteLine("====================================");
        Console.WriteLine($"Public URL: {_config.PublicBaseUrl}");
        Console.WriteLine($"Workers: {_config.WorkerCount}");
        Console.WriteLine($"Queue: {_config.QueueCapacity}");

        await app.RunAsync();
    }

    private static IServiceProvider _servicesProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_config);
        return services.BuildServiceProvider();
    }

    private static async Task HandleInteractionAsync(SocketInteraction interaction)
    {
        try { await _interactions.ExecuteCommandAsync(new ShardedInteractionContext(_discord, interaction), _servicesProvider()); }
        catch (Exception ex) { Console.WriteLine(ex); }
    }

    private static async Task SlashCommandExecutedAsync(SlashCommandInfo command, IInteractionContext context, IResult result)
    {
        if (!result.IsSuccess)
            Console.WriteLine($"/{command.Name}: {result.ErrorReason}");
        await Task.CompletedTask;
    }

    private static Task ShardReadyAsync(DiscordSocketClient shard)
    {
        Console.WriteLine($"Shard {shard.ShardId} ready as {_discord.CurrentUser?.Username}");
        return Task.CompletedTask;
    }

    private static Task LogAsync(LogMessage msg)
    {
        Console.WriteLine($"[{msg.Severity}] {msg.Source}: {msg.Message} {msg.Exception}");
        return Task.CompletedTask;
    }

    private static AppConfig LoadConfig()
    {
        const string path = "config.json";
        if (!File.Exists(path))
        {
            var defaults = new AppConfig();
            File.WriteAllText(path, JsonSerializer.Serialize(defaults, new JsonSerializerOptions { WriteIndented = true }));
            throw new FileNotFoundException("config.json was created. Fill it in and restart the bot.");
        }
        return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path)) ?? new AppConfig();
    }

    private static void ValidateConfig()
    {
        if (string.IsNullOrWhiteSpace(_config.DiscordToken) || _config.DiscordToken.Contains("PUT_YOUR"))
            throw new InvalidOperationException("Put your Discord bot token in config.json.");
        if (!Uri.TryCreate(_config.PublicBaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("PublicBaseUrl must be a public HTTPS URL, e.g. https://checker.yourdomain.com");
    }

    private sealed class Commands : InteractionModuleBase<ShardedInteractionContext>
    {
        [SlashCommand("login", "Get a secure Epic Games login link for Aquith Checker.")]
        public async Task LoginAsync()
        {
            var state = _states.Create(Context.User.Id, TimeSpan.FromMinutes(_config.LoginStateMinutes));
            var callback = $"{_config.PublicBaseUrl.TrimEnd('/')}/epic/callback?state={Uri.EscapeDataString(state)}";
            var redirect = "https://www.epicgames.com/id/api/redirect?clientId=" + Uri.EscapeDataString(AuthClients.fortnitePCGameClient.ClientID) + "&responseType=code&redirectUrl=" + Uri.EscapeDataString(callback);
            var loginUrl = "https://www.epicgames.com/id/login?redirectUrl=" + Uri.EscapeDataString(redirect);

            var embed = new EmbedBuilder()
                .WithTitle("🔐 Aquith Checker — Epic Login")
                .WithDescription("Click **Login with Epic Games** below. After Epic finishes, this tab will tell you when your check has been queued.\n\nYour authorization code is handled server-side and is not displayed in Discord.")
                .WithColor(Color.DarkBlue)
                .WithFooter("Aquith Checker • Secure OAuth flow")
                .WithCurrentTimestamp()
                .Build();

            var button = new ButtonBuilder("Login with Epic Games", loginUrl, ButtonStyle.Link, emote: new Emoji("🔑"));
            await RespondAsync(embed: embed, components: new ComponentBuilder().WithButton(button).Build(), ephemeral: true);
        }

        [SlashCommand("status", "Show Aquith Checker worker status.")]
        public async Task StatusAsync()
        {
            var embed = new EmbedBuilder()
                .WithTitle("⚡ Aquith Checker Status")
                .AddField("Shards", _discord.Shards.Count, true)
                .AddField("Workers", _workers.WorkerCount, true)
                .AddField("Queued", _workers.QueueLength, true)
                .WithColor(Color.Blue)
                .WithCurrentTimestamp()
                .Build();
            await RespondAsync(embed: embed, ephemeral: true);
        }
    }
}
