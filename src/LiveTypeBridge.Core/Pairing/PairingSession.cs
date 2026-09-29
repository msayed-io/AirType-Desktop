using System.Security.Cryptography;

namespace LiveTypeBridge.Core.Pairing;

/// <summary>
/// One pairing session: created by "ربط هاتف", identified by a random SessionId,
/// guarded by a 6-digit PIN shown on screen, and secured by a random server-side
/// secret that never leaves the process (the auth token is derived from it).
/// </summary>
public sealed class PairingSession
{
    private readonly byte[] _secret;
    private readonly string _authToken;
    private volatile bool _invalidated;

    public string SessionId { get; }
    public string Pin { get; }
    public DateTime CreatedUtc { get; }
    public DateTime ExpiresUtc { get; }
    public bool IsSelfTest { get; }

    /// <summary>Device name reported by the phone after successful pairing.</summary>
    public string? PairedDeviceName { get; private set; }

    /// <summary>Opaque reference to the socket bound to this session (one phone max).</summary>
    internal object? BoundSocket { get; set; }

    public bool IsInvalidated => _invalidated;
    public bool IsExpired => DateTime.UtcNow >= ExpiresUtc;
    public bool IsAlive => !_invalidated && !IsExpired;
    public bool IsPaired => PairedDeviceName is not null;

    private PairingSession(string id, string pin, byte[] secret, DateTime created, DateTime expires, bool selfTest)
    {
        SessionId = id;
        Pin = pin;
        _secret = secret;
        CreatedUtc = created;
        ExpiresUtc = expires;
        IsSelfTest = selfTest;
        _authToken = DeriveToken(secret, id);
    }

    public static PairingSession CreateNew(TimeSpan ttl, bool selfTest = false)
    {
        var idBytes = RandomNumberGenerator.GetBytes(8);
        var secret = RandomNumberGenerator.GetBytes(32);
        // 6-digit PIN, uniform over the full 000000..999999 space, zero-padded.
        var pin = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var now = DateTime.UtcNow;
        return new PairingSession(Convert.ToHexString(idBytes).ToLowerInvariant(), pin, secret, now, now + ttl, selfTest);
    }

    public bool VerifyPin(string? pin)
    {
        if (!IsAlive || string.IsNullOrEmpty(pin) || pin.Length != Pin.Length) return false;
        return FixedTimeEquals(pin, Pin);
    }

    public bool VerifyToken(string? token)
    {
        if (!IsAlive || string.IsNullOrEmpty(token)) return false;
        return FixedTimeEquals(token, _authToken);
    }

    /// <summary>The token handed to the phone exactly once, right after the PIN check.</summary>
    public string IssueAuthToken() => _authToken;

    internal void MarkPaired(string deviceName) => PairedDeviceName = deviceName;
    internal void MarkInvalidated() => _invalidated = true;

    private static string DeriveToken(byte[] secret, string sessionId)
    {
        using var hmac = new HMACSHA256(secret);
        var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes("auth:" + sessionId));
        return Convert.ToHexString(hash).ToLowerInvariant()[..32];
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var ba = System.Text.Encoding.UTF8.GetBytes(a);
        var bb = System.Text.Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(ba, bb);
    }
}
