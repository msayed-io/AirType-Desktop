using System.Runtime.InteropServices;

namespace LiveTypeBridge.Core.Input;

/// <summary>
/// Watches for REAL local input (keyboard strokes / significant mouse movement or any
/// click) while streaming is armed. When the human types or grabs the mouse, we pause
/// the stream to protect the text — never guess where the cursor should be.
/// Our own injected events carry <see cref="NativeInput.StreamMarker"/> and are ignored.
/// </summary>
public sealed class LocalInputMonitor : IDisposable
{
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int MouseTravelThresholdPx = 150;

    private readonly Thread _hookThread;
    private readonly ManualResetEventSlim _installed = new(false);
    private IntPtr _kbHook, _mouseHook;
    private NativeMethods.HookProc? _kbProc, _mouseProc;
    private uint _threadId;
    private volatile bool _disposed;
    private volatile bool _armed;
    private NativeMethods.POINT? _anchor;

    /// <summary>Raised once per arming when genuine local input is detected (hook thread).</summary>
    public event Action? LocalInputDetected;

    public LocalInputMonitor()
    {
        _hookThread = new Thread(RunHookThread)
        {
            Name = "LTB-LocalInputMonitor",
            IsBackground = true,
        };
        _hookThread.Start();
        _installed.Wait(TimeSpan.FromSeconds(3));
    }

    /// <summary>Arm/disarm protection. Disarming also resets the mouse anchor.</summary>
    public void SetArmed(bool armed)
    {
        _armed = armed;
        if (armed) _anchor = null;
    }

    private void RunHookThread()
    {
        _threadId = NativeMethods.GetCurrentThreadId();
        // Keep the delegate references alive for the lifetime of the hook.
        _kbProc = KbProc;
        _mouseProc = MouseProc;
        _kbHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _kbProc, IntPtr.Zero, 0);
        _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseProc, IntPtr.Zero, 0);
        _installed.Set();

        while (!_disposed)
        {
            NativeMethods.GetMessage(out _, IntPtr.Zero, 0, 0);
        }

        if (_kbHook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_kbHook);
        if (_mouseHook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_mouseHook);
    }

    private IntPtr KbProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _armed && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            if (data.dwExtraInfo != NativeInput.StreamMarker)
            {
                _armed = false;
                LocalInputDetected?.Invoke();
            }
        }
        return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _armed)
        {
            var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
            if (data.dwExtraInfo != NativeInput.StreamMarker)
            {
                var msg = wParam.ToInt64();
                var isClick = msg is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN;
                if (isClick)
                {
                    _armed = false;
                    LocalInputDetected?.Invoke();
                }
                else if (msg == WM_MOUSEMOVE)
                {
                    _anchor ??= data.pt;
                    if (Math.Abs(data.pt.x - _anchor.Value.x) + Math.Abs(data.pt.y - _anchor.Value.y) > MouseTravelThresholdPx)
                    {
                        _armed = false;
                        LocalInputDetected?.Invoke();
                    }
                }
            }
        }
        return NativeMethods.CallWindowsHookExSafe(nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_threadId != 0) NativeMethods.PostThreadMessage(_threadId, 0x0012 /* WM_QUIT */, IntPtr.Zero, IntPtr.Zero);
        }
        catch { /* thread may already be gone */ }
    }

    internal static class NativeMethods
    {
        public const int WH_KEYBOARD_LL = 13;
        public const int WH_MOUSE_LL = 14;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        public static extern int GetMessage(out NativeMethods.MSG message, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public NativeMethods.POINT pt;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

        public static IntPtr CallWindowsHookExSafe(int nCode, IntPtr wParam, IntPtr lParam)
            => CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }
}
