using System.Text.Json;
using System.Text.Json.Serialization;
using LiveTypeBridge.Core.Input;

namespace LiveTypeBridge.Core.Settings;

public sealed record TrustedDeviceSetting(
    string ClientId,
    string DeviceName,
    string TokenHash,
    DateTime CreatedUtc,
    DateTime LastSeenUtc);

public sealed class AppSettings
{
    private readonly object _saveGate = new();
    internal object SyncRoot => _saveGate;

    public int Port { get; set; } = 53017;
    public string Language { get; set; } = "ar"; // "ar" | "en"
    public bool RunAtStartup { get; set; }
    public bool EnableLogging { get; set; } = true;

    /// <summary>
    /// Permanent trusted-phone records. Only a SHA-256 token hash is persisted;
    /// the bearer token itself exists only on the phone.
    /// </summary>
    public List<TrustedDeviceSetting> TrustedDevices { get; set; } = [];

    /// <summary>Off by default: streamed text content is never written to the log unless enabled.</summary>
    public bool LogTextContent { get; set; }

    public string HotkeyToggleStream { get; set; } = "Ctrl+Alt+Shift+T";
    public string HotkeyShowQr { get; set; } = "Ctrl+Alt+Shift+Q";
    public string HotkeyEmergencyStop { get; set; } = "Ctrl+Alt+Shift+X";

    [JsonIgnore]
    public static string SettingsFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LiveTypeBridge");

    [JsonIgnore]
    private static string SettingsPath => Path.Combine(SettingsFolder, "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOpts);
                if (s is not null) return Sanitize(s);
            }
        }
        catch { /* corrupted settings fall back to defaults */ }
        return new AppSettings();
    }

    public void Save()
    {
        lock (_saveGate)
        {
            try
            {
                Directory.CreateDirectory(SettingsFolder);
                var tempPath = SettingsPath + ".tmp";
                File.WriteAllText(tempPath, JsonSerializer.Serialize(Sanitize(this), JsonOpts));
                File.Move(tempPath, SettingsPath, overwrite: true);
            }
            catch { /* read-only disk etc.: keep running with in-memory settings */ }
        }
    }

    private static AppSettings Sanitize(AppSettings s)
    {
        if (s.Port is < 1024 or > 65535) s.Port = 53017;
        s.TrustedDevices = (s.TrustedDevices ?? [])
            .Where(device =>
                !string.IsNullOrWhiteSpace(device.ClientId)
                && device.ClientId.Length <= 128
                && device.TokenHash.Length == 64
                && device.TokenHash.All(Uri.IsHexDigit))
            .GroupBy(device => device.ClientId, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(device => device.LastSeenUtc).First())
            .OrderByDescending(device => device.LastSeenUtc)
            .Take(20)
            .ToList();
        if (HotkeyGesture.Parse(s.HotkeyToggleStream) is null) s.HotkeyToggleStream = "Ctrl+Alt+Shift+T";
        if (HotkeyGesture.Parse(s.HotkeyShowQr) is null) s.HotkeyShowQr = "Ctrl+Alt+Shift+Q";
        if (HotkeyGesture.Parse(s.HotkeyEmergencyStop) is null) s.HotkeyEmergencyStop = "Ctrl+Alt+Shift+X";
        if (s.Language is not ("ar" or "en")) s.Language = "ar";
        return s;
    }

    /// <summary>Detects duplicate hotkey assignments (they must be unique).</summary>
    public static List<string> ValidateHotkeys(AppSettings s)
    {
        var gestures = new (string Name, HotkeyGesture? G)[]
        {
            ("toggle", HotkeyGesture.Parse(s.HotkeyToggleStream)),
            ("qr", HotkeyGesture.Parse(s.HotkeyShowQr)),
            ("emergency", HotkeyGesture.Parse(s.HotkeyEmergencyStop)),
        };
        var errors = new List<string>();
        for (var i = 0; i < gestures.Length; i++)
            for (var j = i + 1; j < gestures.Length; j++)
                if (gestures[i].G is { } a && gestures[j].G is { } b && a.ConflictsWith(b))
                    errors.Add($"{gestures[i].Name}+{gestures[j].Name}");
        return errors;
    }
}
