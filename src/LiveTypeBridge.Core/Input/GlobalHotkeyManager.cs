using System.Runtime.InteropServices;

namespace LiveTypeBridge.Core.Input;

/// <summary>
/// Registers global hotkeys against a window handle via RegisterHotKey (official API).
/// The window's message loop must forward WM_HOTKEY to <see cref="TryHandleHotkey"/>.
/// </summary>
public sealed class GlobalHotkeyManager : IDisposable
{
    public const int IdToggleStream = 1;
    public const int IdShowQr = 2;
    public const int IdEmergencyStop = 3;
    public const int IdShowApp = 4;
    private const uint MOD_NOREPEAT = 0x4000;
    private const int WM_HOTKEY = 0x0312;

    private readonly IntPtr _hwnd;
    private readonly Dictionary<int, Action> _actions = new();
    private readonly HashSet<int> _registered = new();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public GlobalHotkeyManager(IntPtr hwnd) => _hwnd = hwnd;

    public int LastRegistrationError { get; private set; }

    public bool TryRegister(int id, HotkeyGesture gesture, Action action)
    {
        Unregister(id);
        var ok = RegisterHotKey(
            _hwnd,
            id,
            gesture.ModifierFlags | MOD_NOREPEAT,
            (uint)gesture.VirtualKey);
        if (!ok)
        {
            LastRegistrationError = Marshal.GetLastWin32Error();
            return false;
        }
        LastRegistrationError = 0;
        _registered.Add(id);
        _actions[id] = action;
        return true;
    }

    public void Unregister(int id)
    {
        if (_hwnd == IntPtr.Zero) return;
        if (_registered.Remove(id))
        {
            UnregisterHotKey(_hwnd, id);
            _actions.Remove(id);
        }
    }

    public void UnregisterAll()
    {
        foreach (var id in _registered.ToList()) Unregister(id);
    }

    /// <summary>Feed WM_HOTKEY here; returns true when the message was one of ours.</summary>
    public bool TryHandleHotkey(IntPtr wParam)
    {
        var id = wParam.ToInt32();
        if (_actions.TryGetValue(id, out var action))
        {
            action();
            return true;
        }
        return false;
    }

    public void Dispose() => UnregisterAll();
}

/// <summary>A hotkey combo: modifier flags + a virtual-key code, serializable as "Ctrl+Alt+Shift+T".</summary>
public sealed record HotkeyGesture(uint ModifierFlags, int VirtualKey)
{
    public const uint MOD_ALT = 0x1;
    public const uint MOD_CONTROL = 0x2;
    public const uint MOD_SHIFT = 0x4;

    public static HotkeyGesture DefaultToggleStream => Parse("Ctrl+Alt+Shift+T")!;
    public static HotkeyGesture DefaultShowQr => Parse("Ctrl+Alt+Shift+Q")!;
    public static HotkeyGesture DefaultEmergencyStop => Parse("Ctrl+Alt+Shift+X")!;
    public static HotkeyGesture DefaultShowApp => Parse("Ctrl+Alt+A")!;

    public override string ToString()
    {
        var parts = new List<string>(4);
        if ((ModifierFlags & MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((ModifierFlags & MOD_ALT) != 0) parts.Add("Alt");
        if ((ModifierFlags & MOD_SHIFT) != 0) parts.Add("Shift");
        parts.Add(VkNames.NameOf(VirtualKey) ?? $"VK 0x{VirtualKey:X2}");
        return string.Join("+", parts);
    }

    /// <summary> Parses gestures like "Ctrl+Alt+Shift+T". Requires at least one modifier.</summary>
    public static HotkeyGesture? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        uint mods = 0;
        int vk = 0;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var part = raw.ToLowerInvariant();
            switch (part)
            {
                case "ctrl": case "control": mods |= MOD_CONTROL; continue;
                case "alt": mods |= MOD_ALT; continue;
                case "shift": mods |= MOD_SHIFT; continue;
            }
            var key = VkNames.CodeOf(part);
            if (key is null) return null;
            if (vk != 0) return null; // more than one non-modifier key
            vk = key.Value;
        }
        if (vk == 0 || mods == 0) return null;
        return new HotkeyGesture(mods, vk);
    }

    public bool ConflictsWith(HotkeyGesture other) =>
        other.ModifierFlags == ModifierFlags && other.VirtualKey == VirtualKey;
}

/// <summary>Virtual-key code mapping for the keys we allow in hotkeys.</summary>
public static class VkNames
{
    private static readonly Dictionary<string, int> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["a"]=0x41, ["b"]=0x42, ["c"]=0x43, ["d"]=0x44, ["e"]=0x45, ["f"]=0x46, ["g"]=0x47,
        ["h"]=0x48, ["i"]=0x49, ["j"]=0x4A, ["k"]=0x4B, ["l"]=0x4C, ["m"]=0x4D, ["n"]=0x4E,
        ["o"]=0x4F, ["p"]=0x50, ["q"]=0x51, ["r"]=0x52, ["s"]=0x53, ["t"]=0x54, ["u"]=0x55,
        ["v"]=0x56, ["w"]=0x57, ["x"]=0x58, ["y"]=0x59, ["z"]=0x5A,
        ["0"]=0x30, ["1"]=0x31, ["2"]=0x32, ["3"]=0x33, ["4"]=0x34,
        ["5"]=0x35, ["6"]=0x36, ["7"]=0x37, ["8"]=0x38, ["9"]=0x39,
        ["f1"]=0x70, ["f2"]=0x71, ["f3"]=0x72, ["f4"]=0x73, ["f5"]=0x74,
        ["f6"]=0x75, ["f7"]=0x76, ["f8"]=0x77, ["f9"]=0x78, ["f10"]=0x79, ["f11"]=0x7A, ["f12"]=0x7B,
        ["space"]=0x20, ["enter"]=0x0D, ["up"]=0x26, ["down"]=0x28, ["left"]=0x25, ["right"]=0x27,
        ["pause"]=0x13, ["insert"]=0x2D, ["delete"]=0x2E, ["home"]=0x24, ["end"]=0x23,
        ["pagedown"]=0x22, ["pageup"]=0x21,
    };

    public static int? CodeOf(string name) => Map.TryGetValue(name, out var v) ? v : null;

    public static string? NameOf(int vk)
    {
        foreach (var (name, code) in Map)
            if (code == vk) return name.ToUpperInvariant();
        return null;
    }
}
