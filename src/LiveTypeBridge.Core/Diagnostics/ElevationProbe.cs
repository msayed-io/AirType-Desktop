using System.Runtime.InteropServices;
using System.Security.Principal;

namespace LiveTypeBridge.Core.Diagnostics;

/// <summary>
/// Helps detect the "elevated target" situation: SendInput is silently dropped (UIPI)
/// when the focused app runs as Administrator and LiveType Bridge does not. We probe
/// read-only — we never attempt to bypass the protection.
/// </summary>
public static class ElevationProbe
{
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    public static bool IsOwnProcessElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// True when the foreground window's process could not be opened with query rights
    /// while we are unelevated — the classic signature of a higher-integrity target.
    /// </summary>
    public static bool ForegroundLooksElevated()
    {
        if (IsOwnProcessElevated()) return false;
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return false;
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return false;
            var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (handle == IntPtr.Zero)
            {
                // Access denied on a plain query is a strong hint of a higher integrity level.
                return Marshal.GetLastWin32Error() == 5;
            }
            CloseHandle(handle);
            return false;
        }
        catch
        {
            return false;
        }
    }
}
