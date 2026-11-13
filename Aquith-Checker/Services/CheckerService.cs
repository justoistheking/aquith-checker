using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using FortniteChecker.Models;

namespace FortniteChecker.Services;

public sealed class CheckerService
{
    private readonly AppConfig _config;

    public CheckerService(AppConfig config) => _config = config;

    public async Task<string> RunAsync(CheckJob job, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // The authorization code is exchanged in-memory only. No password, token,
        // or refresh token is persisted by Aquith Checker.
        var auth = await Auth.GetAuthAsync(AuthClients.fortnitePCGameClient, job.AuthorizationCode, ct);
        if (string.IsNullOrWhiteSpace(auth.access_token) || string.IsNullOrWhiteSpace(auth.account_id))
            throw new InvalidOperationException("Epic authorization failed.");

        var cosmeticsTask = FortniteAPI.DownloadDataAsync<CosmeticsDB.CosmeticsDBRoot>(FortniteAPI.Cosmetics, ct);
        var carsTask = FortniteAPI.DownloadDataAsync<CosmeticsDB.CosmeticsDBRoot>(FortniteAPI.Cars, ct);
        var bannersTask = FortniteAPI.DownloadDataAsync<Banners.Root>(FortniteAPI.Banners, ct);
        await Task.WhenAll(cosmeticsTask, carsTask, bannersTask);

        var cosmetics = cosmeticsTask.Result;
        cosmetics.data.AddRange(carsTask.Result.data);
        var banners = bannersTask.Result;

        var athena = await QueryProfile.GetAsync(auth, QueryProfile.Profile.athena, ct);
        if (athena.profileChanges is null || athena.profileChanges.Count == 0)
            throw new InvalidOperationException("Epic returned an empty Athena profile.");
        var account = athena.profileChanges[0].profile;

        var ownedCosmetics = new List<CosmeticsDB.Datum>();
        string[] types = { "AthenaCharacter", "AthenaBackpack", "AthenaDance", "AthenaPickaxe", "AthenaGlider", "AthenaItemWrap", "AthenaLoadingScreen", "AthenaMusicPack", "AthenaSkyDiveContrail", "VehicleCosmetics_Body" };

        foreach (var item in account.items.Values)
        {
            var parts = item.templateId?.Split(':');
            if (parts is null || parts.Length < 2 || !types.Contains(parts[0])) continue;
            var cosmetic = cosmetics.data.FirstOrDefault(x => x.id.Equals(parts[1], StringComparison.OrdinalIgnoreCase));
            if (cosmetic is null) continue;
            cosmetic.introduction ??= new() { text = "Unknown", backendValue = 0, season = "1", chapter = "1" };
            cosmetic.rarity.backendIntValue = Rarity.RarityToInt(cosmetic.rarity.value);

            if (item.attributes?.variants != null)
                foreach (var variant in item.attributes.variants)
                    foreach (var stage in variant.owned ?? Array.Empty<string>())
                    {
                        var name = stage.Contains('.') ? stage.Split('.')[1] : stage;
                        if (name.Length != 32 && name != "RichColor")
                            cosmetic.OwnedVariant.Add(new OwnedVariant(name, variant.channel));
                    }
            if (item.attributes?.favorite == true) cosmetic.favourite = true;
            ownedCosmetics.Add(cosmetic);
        }

        var pastSeasons = account.stats?.attributes?.past_seasons ?? new List<QueryProfile.Modal.PastSeason>();
        var pastWins = pastSeasons.Sum(s => s.numWins);
        pastSeasons.Add(new QueryProfile.Modal.PastSeason
        {
            seasonNumber = account.stats.attributes.season_num,
            numWins = account.stats.attributes.lifetime_wins - pastWins,
            seasonLevel = account.stats.attributes.level,
            bookLevel = account.stats.attributes.book_level,
            purchasedVIP = account.stats.attributes.book_purchased
        });

        var common = await QueryProfile.GetAsync(auth, QueryProfile.Profile.common_core, ct);
        var vbucks = GetVbucks(common);
        var ownedBanners = GetBanners(common);
        var actualBanners = ownedBanners
            .Select(id => banners.data.FirstOrDefault(x => x.id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            .Where(x => x != null).Cast<Banners.Datum>().ToList();

        var giftsSent = common.profileChanges[0].profile.stats?.attributes?.gift_history?.num_sent.ToString() ?? "0";
        var giftsReceived = common.profileChanges[0].profile.stats?.attributes?.gift_history?.num_received.ToString() ?? "0";
        var epicId = GetHashString(auth.account_id).Substring(0, 12);
        var data = JsonBuilder.CreateJsonFile(ownedCosmetics, pastSeasons, actualBanners, vbucks.ToString(), giftsSent, giftsReceived,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), auth.displayName ?? "Unknown", account.stats.attributes.lifetime_wins.ToString(), epicId);

        using var response = await HttpClientSingleton.Instance.PostAsJsonAsync(_config.ResultBaseUrl.TrimEnd('/') + "/submit", data, ct);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Result server returned {(int)response.StatusCode}.");

        // Keep the same public result format used by the original checker.
        return $"https://bstlar.com/1c/fnchecker?bstid={Uri.EscapeDataString(epicId)}";
    }

    private static int GetVbucks(QueryProfile.Modal.QueryProfileRoot common)
    {
        var total = 0;
        foreach (var item in common.profileChanges[0].profile.items.Values)
            if (item.templateId?.StartsWith("Currency:Mtx", StringComparison.Ordinal) == true && item.quantity.HasValue)
                total += item.quantity.Value;
        return total;
    }

    private static List<string> GetBanners(QueryProfile.Modal.QueryProfileRoot common)
        => common.profileChanges[0].profile.items.Values
            .Where(x => x.templateId?.StartsWith("HomebaseBannerIcon:", StringComparison.Ordinal) == true)
            .Select(x => x.templateId!.Split(':')[1]).ToList();

    private static string GetHashString(string input)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
    }
}
