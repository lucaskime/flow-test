using System.Collections.Concurrent;
using QuickFix;

namespace OrderGenerator.Infrastructure.Fix;

public sealed record FixSessionInfo(string Session, DateTimeOffset Since);

/// <summary>Estado observável das sessões FIX, alimentado pelos callbacks OnLogon/OnLogout.</summary>
public sealed class FixSessionMonitor
{
    private readonly ConcurrentDictionary<string, (SessionID Id, DateTimeOffset Since)> _loggedOn = new();

    /// <summary>Disparado a cada logon/logout.</summary>
    public event Action? Changed;

    public bool IsLoggedOn => !_loggedOn.IsEmpty;

    public IReadOnlyCollection<string> LoggedOnSessions => _loggedOn.Keys.ToArray();

    public IReadOnlyList<FixSessionInfo> Sessions =>
        _loggedOn.Select(kv => new FixSessionInfo(kv.Key, kv.Value.Since)).ToArray();

    internal SessionID? FirstLoggedOn => _loggedOn.Values.Select(v => v.Id).FirstOrDefault();

    internal void MarkLoggedOn(SessionID session)
    {
        _loggedOn[session.ToString()] = (session, DateTimeOffset.UtcNow);
        Changed?.Invoke();
    }

    internal void MarkLoggedOut(SessionID session)
    {
        _loggedOn.TryRemove(session.ToString(), out _);
        Changed?.Invoke();
    }
}
