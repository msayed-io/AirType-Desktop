using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LiveTypeBridge.App.ViewModels;

namespace LiveTypeBridge.App.Views;

public partial class FloatingIndicatorWindow : Window
{
    public event Action? Clicked;

    private readonly ScaleTransform[] _bars;
    private readonly DispatcherTimer _activityRelease;
    private bool _streaming;
    private Point _pointerDown;
    private bool _dragging;

    public FloatingIndicatorWindow()
    {
        InitializeComponent();
        _bars = new[] { Scale1, Scale2, Scale3, Scale4, Scale5 };
        _activityRelease = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(520) };
        _activityRelease.Tick += (_, _) =>
        {
            _activityRelease.Stop();
            AnimateWave(_streaming);
        };

        PositionBottomRight();
        SourceInitialized += (_, _) =>
        {
            ApplyCapsuleWindowRegion();
            EnableAcrylicBackdrop();
        };
        Loaded += (_, _) => AnimateWave(active: false);
        OrbRoot.MouseLeftButtonDown += OrbRoot_MouseLeftButtonDown;
        OrbRoot.MouseMove += OrbRoot_MouseMove;
    }

    public void PositionBottomRight()
    {
        Left = SystemParameters.WorkArea.Right - Width - 18;
        Top = SystemParameters.WorkArea.Bottom - Height - 6;
    }

    private void ApplyCapsuleWindowRegion()
    {
        // Acrylic is composed for the native HWND, whose default shape is rectangular.
        // Clip the HWND itself to the exact 48×34 WPF capsule so no rectangular
        // composition surface can appear around the transparent corners.
        var dpi = VisualTreeHelper.GetDpi(this);
        var left = (int)Math.Round(2 * dpi.DpiScaleX);
        var top = (int)Math.Round(2 * dpi.DpiScaleY);
        var right = (int)Math.Round(50 * dpi.DpiScaleX);
        var bottom = (int)Math.Round(36 * dpi.DpiScaleY);
        var diameterX = (int)Math.Round(34 * dpi.DpiScaleX);
        var diameterY = (int)Math.Round(34 * dpi.DpiScaleY);

        var region = CreateRoundRectRgn(left, top, right + 1, bottom + 1, diameterX, diameterY);
        if (region == IntPtr.Zero) return;

        // On success Windows owns the region handle. Delete it only if assignment fails.
        if (SetWindowRgn(new WindowInteropHelper(this).Handle, region, true) == 0)
            DeleteObject(region);
    }

    public void SetState(UiState state)
    {
        var streaming = state is UiState.Streaming or UiState.Resync;
        if (_streaming == streaming) return;
        _streaming = streaming;
        AnimateWave(streaming);
    }

    /// <summary>
    /// Gives the waveform a short, high-energy response when a stream operation is applied.
    /// This is driven by real stream activity; the application does not capture microphone audio.
    /// </summary>
    public void PulseActivity()
    {
        if (!IsLoaded) return;
        AnimateWave(active: true, burst: true);
        _activityRelease.Stop();
        _activityRelease.Start();
    }

    private void AnimateWave(bool active, bool burst = false)
    {
        if (!IsLoaded) return;

        var duration = TimeSpan.FromMilliseconds(burst ? 150 : active ? 320 : 1150);
        var peaks = new[] { 0.70, 1.00, 0.82, 1.00, 0.66 };
        for (var i = 0; i < _bars.Length; i++)
        {
            var low = active ? 0.30 : 0.44;
            var high = burst ? 1.12 : active ? peaks[i] : 0.62;
            var animation = new DoubleAnimation(low, high, duration)
            {
                BeginTime = TimeSpan.FromMilliseconds(i * (burst ? 18 : active ? 45 : 85)),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            _bars[i].BeginAnimation(ScaleTransform.ScaleYProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }

        GlassBody.Opacity = active || burst ? 1.0 : 0.78;
    }

    private void EnableAcrylicBackdrop()
    {
        // The LiquidGlass2 reference uses WindhawkBlur, which only exists inside Windhawk's
        // taskbar host. For a standalone WPF window the equivalent native composition path
        // is Windows acrylic blur with the same dark translucent tint and a saturated border.
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var accent = new AccentPolicy
            {
                AccentState = AccentEnableAcrylicBlurBehind,
                AccentFlags = 2,
                GradientColor = unchecked((int)0xB0181512), // ABGR: dark translucent tint
                AnimationId = 0,
            };
            var size = Marshal.SizeOf<AccentPolicy>();
            var pointer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, pointer, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WindowCompositionAttributeAccentPolicy,
                    Data = pointer,
                    SizeOfData = size,
                };
                SetWindowCompositionAttribute(new WindowInteropHelper(this).Handle, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }
        }
        catch
        {
            // The layered WPF gradient remains the deterministic fallback on older systems.
        }
    }

    private void OrbRoot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pointerDown = e.GetPosition(this);
        _dragging = false;
        OrbRoot.CaptureMouse();
        e.Handled = true;
    }

    private void OrbRoot_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || !OrbRoot.IsMouseCaptured) return;
        var point = e.GetPosition(this);
        var delta = point - _pointerDown;
        if (!_dragging && (Math.Abs(delta.X) > 3 || Math.Abs(delta.Y) > 3)) _dragging = true;
        if (_dragging)
        {
            Left += delta.X;
            Top += delta.Y;
        }
    }

    private void OrbRoot_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        OrbRoot.ReleaseMouseCapture();
        if (!_dragging) Clicked?.Invoke();
        _dragging = false;
        e.Handled = true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    private const int AccentEnableAcrylicBlurBehind = 4;
    private const int WindowCompositionAttributeAccentPolicy = 19;

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr value);
}
