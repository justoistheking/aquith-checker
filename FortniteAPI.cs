using System.Text.Json;
namespace FortniteChecker;

internal static class FortniteAPI
{
    private const string BASE_URL = "https://fortnite-api.com";
    public static readonly string Cosmetics = BASE_URL + "/v2/cosmetics/br";
    public static readonly string Cars = BASE_URL + "/v2/cosmetics/cars";
    public static readonly string Banners = BASE_URL + "/v1/banners";

    internal static async Task<T> DownloadDataAsync<T>(string url, CancellationToken ct)
    {
        using var response = await HttpClientSingleton.Instance.GetAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Fortnite API returned {(int)response.StatusCode}.");
        return JsonSerializer.Deserialize<T>(body) ?? throw new InvalidOperationException("Fortnite API returned empty data.");
    }
}
