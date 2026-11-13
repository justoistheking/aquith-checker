namespace FortniteChecker;

public static class HttpClientSingleton
{
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };
    public static HttpClient Instance => _httpClient;
}
