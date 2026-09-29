using System.Windows.Controls;
using System.Windows.Input;

namespace LiveTypeBridge.App.Controls;

/// <summary>
/// TextBox that records a hotkey combination: click it, press the combo, it shows
/// "Ctrl+Alt+Shift+T". Esc clears. Only Ctrl/Alt/Shift + a real key are accepted;
/// Windows-key combos are rejected outright to avoid clashing with the OS.
/// </summary>
public sealed class HotkeyCaptureBox : TextBox
{
    private bool _listening;

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        _listening = true;
        base.OnPreviewMouseDown(e);
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        _listening = false;
        base.OnLostKeyboardFocus(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!_listening)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            Text = "";
            _listening = false;
            return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                       or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System)
        {
            return; // modifiers alone don't form a gesture
        }

        var mods = Keyboard.Modifiers;
        var ctrl = mods.HasFlag(ModifierKeys.Control);
        var alt = mods.HasFlag(ModifierKeys.Alt);
        var shift = mods.HasFlag(ModifierKeys.Shift);

        if (!ctrl && !alt && !shift)
        {
            return; // require at least one modifier
        }

        var vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0) return;

        var parts = new List<string>(4);
        if (ctrl) parts.Add("Ctrl");
        if (alt) parts.Add("Alt");
        if (shift) parts.Add("Shift");
        parts.Add(KeyToString(key));

        Text = string.Join("+", parts);
        _listening = false;
    }

    private static string KeyToString(Key key)
    {
        var name = key.ToString();
        return name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1])
            ? name[1].ToString()
            : name;
    }
}
