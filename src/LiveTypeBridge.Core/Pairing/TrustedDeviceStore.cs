using System.Security.Cryptography;
using System.Text;
using LiveTypeBridge.Core.Settings;

namespace LiveTypeBridge.Core.Pairing;

public sealed record TrustedDeviceCredential(string ClientId, string Token);

/// <summary>
/// Manages permanent phone trust. The raw 256-bit bearer token is returned to the phone
/// once; only its SHA-256 hash is persisted in desktop settings.
/// </summary>
public sealed class TrustedDeviceStore
{
    public const int MaxTrustedDevices = 20;
    private readonly AppSettings _settings;
    private readonly Action _save;
    private readonly object _gate;

    public TrustedDeviceStore(AppSettings settings, Action? save = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _save = save ?? settings.Save;
        _gate = settings.SyncRoot;
    }

    public int Count
    {
        get { lock (_gate) return _settings.TrustedDevices.Count; }
    }

    public IReadOnlyList<TrustedDeviceSetting> Snapshot()
    {
        lock (_gate) return _settings.TrustedDevices.ToList();
    }

    /// <summary>Creates or rotates trust after a successful PIN/QR authentication.</summary>
    public TrustedDeviceCredential Enroll(string clientId, string deviceName)
    {
        clientId = ValidateClientId(clientId);
        deviceName = NormalizeDeviceName(deviceName);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var hash = HashToken(token);
        var now = DateTime.UtcNow;

        lock (_gate)
        {
            var existing = _settings.TrustedDevices.FirstOrDefault(device =>
                string.Equals(device.ClientId, clientId, StringComparison.Ordinal));
            var created = existing?.CreatedUtc ?? now;
            var replacement = new TrustedDeviceSetting(clientId, deviceName, hash, created, now);
            _settings.TrustedDevices = _settings.TrustedDevices
                .Where(device => !string.Equals(device.ClientId, clientId, StringComparison.Ordinal))
                .Prepend(replacement)
                .OrderByDescending(device => device.LastSeenUtc)
                .Take(MaxTrustedDevices)
                .ToList();
            _save();
        }

        return new TrustedDeviceCredential(clientId, token);
    }

    public bool TryAuthenticate(string? clientId, string? token, string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(clientId)
            || clientId.Length > 128
            || string.IsNullOrWhiteSpace(token)
            || token.Length != 64)
            return false;

        var candidateHash = HashToken(token);
        lock (_gate)
        {
            var existing = _settings.TrustedDevices.FirstOrDefault(device =>
                string.Equals(device.ClientId, clientId, StringComparison.Ordinal));
            if (existing is null || !FixedTimeHexEquals(existing.TokenHash, candidateHash))
                return false;

            var updated = existing with
            {
                DeviceName = NormalizeDeviceName(deviceName),
                LastSeenUtc = DateTime.UtcNow,
            };
            _settings.TrustedDevices = _settings.TrustedDevices
                .Select(device => string.Equals(device.ClientId, clientId, StringComparison.Ordinal)
                    ? updated
                    : device)
                .OrderByDescending(device => device.LastSeenUtc)
                .ToList();
            _save();
            return true;
        }
    }

    public bool Revoke(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId)) return false;
        lock (_gate)
        {
            var filtered = _settings.TrustedDevices
                .Where(device => !string.Equals(device.ClientId, clientId, StringComparison.Ordinal))
                .ToList();
            if (filtered.Count == _settings.TrustedDevices.Count) return false;
            _settings.TrustedDevices = filtered;
            _save();
            return true;
        }
    }

    public void RevokeAll()
    {
        lock (_gate)
        {
            if (_settings.TrustedDevices.Count == 0) return;
            _settings.TrustedDevices = [];
            _save();
        }
    }

    private static string ValidateClientId(string clientId)
    {
        clientId = clientId.Trim();
        if (clientId.Length is 0 or > 128)
            throw new ArgumentException("clientId must contain 1-128 characters", nameof(clientId));
        return clientId;
    }

    private static string NormalizeDeviceName(string? deviceName)
    {
        var value = string.IsNullOrWhiteSpace(deviceName) ? "Android" : deviceName.Trim();
        return value.Length > 80 ? value[..80] : value;
    }

    private static string HashToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static bool FixedTimeHexEquals(string expected, string candidate)
    {
        try
        {
            var expectedBytes = Convert.FromHexString(expected);
            var candidateBytes = Convert.FromHexString(candidate);
            return CryptographicOperations.FixedTimeEquals(expectedBytes, candidateBytes);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
