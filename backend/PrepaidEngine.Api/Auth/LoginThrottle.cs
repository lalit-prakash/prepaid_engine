using System.Collections.Concurrent;

namespace PrepaidEngine.Api.Auth;

/// <summary>
/// Locks a username out for a while after repeated failed sign-ins, to stop password guessing.
/// State is in memory, so it is per API instance; a shared store and IP-level rate limiting come with
/// the hardening step. The number of tracked names is capped so junk usernames cannot grow it without bound.
/// </summary>
public sealed class LoginThrottle
{
    public const int MaxFailures = 5;
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);
    private const int MaxTrackedUsers = 10_000;

    private sealed record State(int Failures, DateTime LastFailureUtc);
    private readonly ConcurrentDictionary<string, State> _state = new();

    /// <summary>How long the caller must wait, or null when sign-in is allowed.</summary>
    public TimeSpan? RetryAfter(string username, DateTime utcNow)
    {
        if (!_state.TryGetValue(Key(username), out var s) || s.Failures < MaxFailures) return null;
        var remaining = s.LastFailureUtc + LockDuration - utcNow;
        if (remaining > TimeSpan.Zero) return remaining;
        _state.TryRemove(Key(username), out _);
        return null;
    }

    public void RecordFailure(string username, DateTime utcNow)
    {
        if (_state.Count >= MaxTrackedUsers) Prune(utcNow);
        _state.AddOrUpdate(Key(username), _ => new State(1, utcNow), (_, s) =>
            utcNow - s.LastFailureUtc > LockDuration ? new State(1, utcNow) : new State(s.Failures + 1, utcNow));
    }

    public void RecordSuccess(string username) => _state.TryRemove(Key(username), out _);

    private void Prune(DateTime utcNow)
    {
        foreach (var (key, s) in _state)
            if (utcNow - s.LastFailureUtc > LockDuration) _state.TryRemove(key, out _);
    }

    private static string Key(string username) => username.Trim().ToLowerInvariant();
}
