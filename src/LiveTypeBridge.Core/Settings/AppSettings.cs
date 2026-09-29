using System.Text.Json;
using System.Text.Json.Serialization;
using LiveTypeBridge.Core.Input;

namespace LiveTypeBridge.Core.Settings;

public sealed class AppSettings
{
    public int Port { get; set; } = 53017;
    public string Language { get; set; } = "ar"; // "ar" | "en"
    public bool RunAtStartup { get; set; }
    public bool EnableLogging { get; set; } = true;

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
        try
        {
            Directory.CreateDirectory(SettingsFolder);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Sanitize(this), JsonOpts));
        }
        catch { /* read-only disk etc.: keep running with in-memory settings */ }
    }

    private static AppSettings Sanitize(AppSettings s)
    {
        if (s.Port is < 1024 or > 65535) s.Port = 53017;
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
