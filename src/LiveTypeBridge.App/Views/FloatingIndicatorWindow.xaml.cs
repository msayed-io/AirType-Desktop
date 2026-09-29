using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using LiveTypeBridge.App.ViewModels;

namespace LiveTypeBridge.App.Views;

public partial class FloatingIndicatorWindow : Window
{
    public event Action? Clicked;
    private bool _working;

    public FloatingIndicatorWindow()
    {
        InitializeComponent();
        PositionBottomRight();
        SetWorking(false);
    }

    public void PositionBottomRight()
    {
        Left = SystemParameters.WorkArea.Right - Width - 26;
        Top = SystemParameters.WorkArea.Bottom - Height - 26;
    }

    public void SetState(UiState state) => SetWorking(state is UiState.Streaming or UiState.Resync);

    private void SetWorking(bool working)
    {
        if (_working == working && IsLoaded) return;
        _working = working;
        var animation = new DoubleAnimation
        {
            From = working ? 0.16 : 0.10,
            To = working ? 0.42 : 0.20,
            Duration = TimeSpan.FromSeconds(working ? 0.8 : 1.8),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        Halo.BeginAnimation(OpacityProperty, animation);
    }

    private void OrbRoot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var before = new Point(Left, Top);
        try { DragMove(); } catch { }
        if (Math.Abs(Left - before.X) < 4 && Math.Abs(Top - before.Y) < 4)
            Clicked?.Invoke();
    }
}
