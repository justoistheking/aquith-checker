using System.Collections.Concurrent;

namespace FortniteChecker.Services;

public sealed record LoginState(ulong DiscordUserId, DateTimeOffset ExpiresAt);

public sealed class LoginStateStore
{
    private readonly ConcurrentDictionary<string, LoginState> _states = new();

    public string Create(ulong discordUserId, TimeSpan lifetime)
    {
        Cleanup();
        var state = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        _states[state] = new LoginState(discordUserId, DateTimeOffset.UtcNow.Add(lifetime));
        return state;
    }

    public bool TryConsume(string state, out LoginState value)
    {
        value = default!;
        if (!_states.TryRemove(state, out var found)) return false;
        if (found.ExpiresAt <= DateTimeOffset.UtcNow) return false;
        value = found;
        return true;
    }

    private void Cleanup()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in _states)
            if (pair.Value.ExpiresAt <= now)
                _states.TryRemove(pair.Key, out _);
    }
}
