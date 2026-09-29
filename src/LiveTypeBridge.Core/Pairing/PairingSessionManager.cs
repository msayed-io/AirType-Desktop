namespace LiveTypeBridge.Core.Pairing;

/// <summary>
/// Owns the pairing sessions. Creating a new user session immediately invalidates the
/// previous one; self-test sessions live on a separate lane so diagnostics never break
/// an active pairing.
/// </summary>
public sealed class PairingSessionManager
{
    private readonly object _gate = new();
    private PairingSession? _active;
    private PairingSession? _selfTest;

    public event Action<PairingSession>? SessionInvalidated;

    /// <summary>Creates a new session and invalidates the previous session of the same lane.</summary>
    public PairingSession Create(TimeSpan ttl, bool selfTest = false)
    {
        var s = PairingSession.CreateNew(ttl, selfTest);
        PairingSession? old = null;
        lock (_gate)
        {
            if (selfTest) { old = _selfTest; _selfTest = s; }
            else { old = _active; _active = s; }
        }
        if (old is not null)
        {
            old.MarkInvalidated();
            SessionInvalidated?.Invoke(old);
        }
        return s;
    }

    public PairingSession? GetActive(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return null;
        lock (_gate)
        {
            var a = _active;
            if (a is not null && a.SessionId == sessionId) return a.IsAlive ? a : null;
            var t = _selfTest;
            if (t is not null && t.SessionId == sessionId) return t.IsAlive ? t : null;
            return null;
        }
    }

    public PairingSession? ActiveUserSession
    {
        get { lock (_gate) { return _active is { IsAlive: true } ? _active : null; } }
    }

    public PairingSession? ActiveSelfTestSession
    {
        get { lock (_gate) { return _selfTest is { IsAlive: true } ? _selfTest : null; } }
    }

    public void Invalidate(PairingSession session)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_active, session)) _active = null;
            if (ReferenceEquals(_selfTest, session)) _selfTest = null;
        }
        if (!session.IsInvalidated)
        {
            session.MarkInvalidated();
            SessionInvalidated?.Invoke(session);
        }
    }

    public void InvalidateActiveUserSession()
    {
        PairingSession? s;
        lock (_gate) { s = _active; _active = null; }
        if (s is not null && !s.IsInvalidated)
        {
            s.MarkInvalidated();
            SessionInvalidated?.Invoke(s);
        }
    }
}
