using System.Threading.Channels;
using Discord;
using Discord.WebSocket;

namespace FortniteChecker.Services;

public sealed class CheckWorkerPool
{
    private readonly Channel<CheckJob> _queue;
    private readonly List<Task> _workers = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly DiscordShardedClient _client;
    private readonly int _workerCount;
    private readonly Func<CheckJob, CancellationToken, Task<string>> _processor;

    public int QueueLength => _queue.Reader.Count;
    public int WorkerCount => _workerCount;

    public CheckWorkerPool(DiscordShardedClient client, int workerCount, int capacity,
        Func<CheckJob, CancellationToken, Task<string>> processor)
    {
        _client = client;
        _workerCount = Math.Clamp(workerCount, 1, 64);
        _processor = processor;
        _queue = Channel.CreateBounded<CheckJob>(new BoundedChannelOptions(Math.Max(1, capacity))
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        });
    }

    public async Task StartAsync()
    {
        for (var i = 0; i < _workerCount; i++)
            _workers.Add(Task.Run(() => WorkerLoopAsync(i, _cts.Token)));
        await Task.CompletedTask;
    }

    public ValueTask EnqueueAsync(CheckJob job, CancellationToken cancellationToken = default)
        => _queue.Writer.WriteAsync(job, cancellationToken);

    private async Task WorkerLoopAsync(int workerId, CancellationToken ct)
    {
        await foreach (var job in _queue.Reader.ReadAllAsync(ct))
        {
            try
            {
                var result = await _processor(job, ct);
                var user = await FindUserAsync(job.DiscordUserId);
                if (user != null)
                {
                    await user.SendMessageAsync($"✅ **Aquith Checker**\nYour authorized check is complete.\n\n🔗 {result}");
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex)
            {
                Console.WriteLine($"[Worker {workerId}] {ex}");
                try
                {
                    var user = await FindUserAsync(job.DiscordUserId);
                    if (user != null)
                        await user.SendMessageAsync("❌ Aquith Checker could not finish your check. Please try `/login` again.");
                }
                catch { }
            }
        }
    }

    private async Task<SocketUser?> FindUserAsync(ulong id)
    {
        foreach (var shard in _client.Shards)
        {
            var user = shard.GetUser(id);
            if (user != null) return user;
        }
        return null;
    }

    public async Task StopAsync()
    {
        _queue.Writer.TryComplete();
        _cts.Cancel();
        try { await Task.WhenAll(_workers); } catch { }
    }
}
