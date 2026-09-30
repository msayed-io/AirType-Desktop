using System.Windows;
using System.Windows.Input;
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
            SetWaveRestState();
        };

        PositionBottomRight();
        Loaded += (_, _) => SetWaveRestState();
        OrbRoot.MouseLeftButtonDown += OrbRoot_MouseLeftButtonDown;
        OrbRoot.MouseMove += OrbRoot_MouseMove;
    }

    public void PositionBottomRight()
    {
        Left = SystemParameters.WorkArea.Right - Width - 18;
        Top = SystemParameters.WorkArea.Bottom - Height - 6;
    }

    public void SetState(UiState state)
    {
        var streaming = state is UiState.Streaming or UiState.Resync;
        if (_streaming == streaming) return;
        _streaming = streaming;
        _activityRelease.Stop();
        SetWaveRestState();
    }

    /// <summary>
    /// Plays one short response only when a real stream operation updates the session stats.
    /// There is no idle or state-driven looping animation.
    /// </summary>
    public void PulseActivity()
    {
        if (!IsLoaded || !_streaming) return;
        AnimateActivityBurst();
        _activityRelease.Stop();
        _activityRelease.Start();
    }

    private void AnimateActivityBurst()
    {
        const double restScale = 0.42;
        var duration = TimeSpan.FromMilliseconds(140);
        var peaks = new[] { 0.74, 1.00, 0.86, 1.00, 0.70 };
        for (var i = 0; i < _bars.Length; i++)
        {
            var animation = new DoubleAnimation(restScale, peaks[i], duration)
            {
                BeginTime = TimeSpan.FromMilliseconds(i * 18),
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(1),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            _bars[i].BeginAnimation(ScaleTransform.ScaleYProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }
        GlassBody.Opacity = 1.0;
    }

    private void SetWaveRestState()
    {
        const double restScale = 0.42;
        foreach (var bar in _bars)
        {
            bar.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            bar.ScaleY = restScale;
        }
        GlassBody.Opacity = _streaming ? 0.92 : 0.82;
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

}
