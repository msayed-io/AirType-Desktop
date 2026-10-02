using System.Runtime.InteropServices;

namespace LiveTypeBridge.Core.Input;

/// <summary>Win32 input structures and SendInput binding. Official Windows API only.</summary>
public static class NativeInput
{
    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_UNICODE = 0x0004;
    public const ushort VK_BACK = 0x08;
    public const ushort VK_TAB = 0x09;
    public const ushort VK_RETURN = 0x0D;

    /// <summary>
    /// Marker written into dwExtraInfo of every input WE inject, so our own low-level
    /// hook can distinguish program streaming from real human keystrokes.
    /// </summary>
    public static readonly UIntPtr StreamMarker = new(0x4C54_4231); // "LTB1"

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    /// <summary>Returns how many inputs were actually accepted by Windows.</summary>
    public static uint SendBulk(INPUT[] inputs)
    {
        if (inputs.Length == 0) return 0;
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    public static INPUT Backspace(bool keyUp = false) => VirtualKey(VK_BACK, keyUp);

    public static INPUT UnicodeChar(char c, bool keyUp = false)
    {
        var input = new INPUT { type = INPUT_KEYBOARD };
        input.U.ki = new KEYBDINPUT
        {
            wVk = 0,
            wScan = c,
            dwFlags = KEYEVENTF_UNICODE | (keyUp ? KEYEVENTF_KEYUP : 0),
            dwExtraInfo = StreamMarker,
        };
        return input;
    }

    public static INPUT VirtualKey(ushort vk, bool keyUp = false)
    {
        var input = new INPUT { type = INPUT_KEYBOARD };
        input.U.ki = new KEYBDINPUT
        {
            wVk = vk,
            dwFlags = keyUp ? KEYEVENTF_KEYUP : 0,
            dwExtraInfo = StreamMarker,
        };
        return input;
    }
}
