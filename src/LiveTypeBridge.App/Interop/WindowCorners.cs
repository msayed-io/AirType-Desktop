using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LiveTypeBridge.App.Interop;

/// <summary>
/// Rounds the real outer frame of a window through the window manager instead of drawing an
/// inner rounded element over a square frame. Windows 11 (build 22000+) exposes
/// <c>DWMWA_WINDOW_CORNER_PREFERENCE</c>; on older builds the call is skipped and the frame
/// stays square, so drag, resize, maximise and the caption buttons keep native behaviour.
/// </summary>
internal static class WindowCorners
{
    private const int DwmWindowCornerPreference = 33;
    private const int DwmWindowCornerRound = 2;

    [DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void ApplyRounded(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        if (Environment.OSVersion.Version.Build < 22000) return;

        var preference = DwmWindowCornerRound;
        try
        {
            _ = DwmSetWindowAttribute(handle, DwmWindowCornerPreference, ref preference, sizeof(int));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }
}
