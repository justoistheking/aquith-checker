using System.Text;
using System.Text.Json;

namespace FortniteChecker;

internal static class QueryProfile
{
    internal enum Profile { athena, creative, campaign, common_public, collections, common_core }

    internal static async Task<Modal.QueryProfileRoot> GetAsync(Auth.Modal.AuthRoot auth, Profile profile, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {auth.access_token}");
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var response = await http.PostAsync($"https://fortnite-public-service-prod11.ol.epicgames.com/fortnite/api/game/v2/profile/{auth.account_id}/client/QueryProfile?profileId={profile}", content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"QueryProfile({profile}) returned {(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<Modal.QueryProfileRoot>(body) ?? throw new InvalidOperationException("Invalid QueryProfile response.");
    }

    internal sealed class Modal
    {
        internal sealed class QueryProfileRoot
        {
            public int profileRevision { get; set; }
            public string? profileId { get; set; }
            public int profileChangesBaseRevision { get; set; }
            public List<ProfileChange> profileChanges { get; set; } = new();
            public int profileCommandRevision { get; set; }
            public DateTime serverTime { get; set; }
            public int responseVersion { get; set; }
        }
        internal sealed class ProfileChange { public Profile profile { get; set; } = new(); }
        internal sealed class Profile
        {
            public string? _id { get; set; }
            public DateTime created { get; set; }
            public DateTime updated { get; set; }
            public int rvn { get; set; }
            public int wipeNumber { get; set; }
            public string? accountId { get; set; }
            public string? profileId { get; set; }
            public string? version { get; set; }
            public Dictionary<string, Item> items { get; set; } = new();
            public Stats stats { get; set; } = new();
            public int commandRevision { get; set; }
        }
        internal sealed class Stats { public Attributes attributes { get; set; } = new(); }
        internal sealed class Item
        {
            public string? templateId { get; set; }
            public Attributes? attributes { get; set; }
            public int? quantity { get; set; }
        }
        public sealed class PastSeason
        {
            public int seasonNumber { get; set; }
            public int numWins { get; set; }
            public int seasonLevel { get; set; }
            public int bookLevel { get; set; }
            public bool purchasedVIP { get; set; }
        }
        public class Season { public int numWins { get; set; } }
        public class QVariant { public string channel { get; set; } = ""; public string active { get; set; } = ""; public string[] owned { get; set; } = Array.Empty<string>(); }
        internal class Attributes
        {
            public List<PastSeason> past_seasons { get; set; } = new();
            public List<string> loadouts { get; set; } = new();
            public QVariant[]? variants { get; set; }
            public bool favorite { get; set; }
            public int book_level { get; set; }
            public bool book_purchased { get; set; }
            public int season_num { get; set; }
            public int lifetime_wins { get; set; }
            public int level { get; set; }
            public GiftHistory? gift_history { get; set; }
            public class GiftHistory { public int num_sent { get; set; } public int num_received { get; set; } }
        }
    }
}
