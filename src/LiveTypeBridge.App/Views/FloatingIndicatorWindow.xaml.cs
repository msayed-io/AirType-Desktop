using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LiveTypeBridge.App.ViewModels;

namespace LiveTypeBridge.App.Views;

public partial class FloatingIndicatorWindow : Window
{
    public event Action? Clicked;
    private bool _working;
    private Point _pointerDown;
    private bool _dragging;

    public FloatingIndicatorWindow()
    {
        InitializeComponent();
        PositionBottomRight();
        Loaded += (_, _) => StartOrbAnimation();
        OrbRoot.MouseLeftButtonDown += OrbRoot_MouseLeftButtonDown;
        OrbRoot.MouseMove += OrbRoot_MouseMove;
    }

    public void PositionBottomRight()
    {
        Left = SystemParameters.WorkArea.Right - Width - 24;
        Top = SystemParameters.WorkArea.Bottom - Height - 24;
    }

    public void SetState(UiState state)
    {
        var working = state is UiState.Streaming or UiState.Resync;
        if (_working == working) return;
        _working = working;
        StartOrbAnimation();
    }

    private void StartOrbAnimation()
    {
        if (!IsLoaded) return;
        var duration = TimeSpan.FromMilliseconds(_working ? 900 : 1700);
        var pulse = new DoubleAnimation(0.78, 1.08, duration)
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        GlowScale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        GlowScale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);

        var opacity = new DoubleAnimation(_working ? 0.72 : 0.42, _working ? 1.0 : 0.78, duration)
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        Glow.BeginAnimation(UIElement.OpacityProperty, opacity);
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
        if (!_dragging && (Math.Abs(delta.X) > 4 || Math.Abs(delta.Y) > 4)) _dragging = true;
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
