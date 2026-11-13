namespace FortniteChecker.Models;

public sealed class AppConfig
{
    public string DiscordToken { get; set; } = "";
    public string PublicBaseUrl { get; set; } = "";
    public int Port { get; set; } = 8080;
    public int WorkerCount { get; set; } = 8;
    public int QueueCapacity { get; set; } = 64;
    public int LoginStateMinutes { get; set; } = 10;
    public string ResultBaseUrl { get; set; } = "https://checker.proswapper.xyz";
    public bool OpenResultInBrowser { get; set; }
}
