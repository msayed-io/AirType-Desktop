using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LiveTypeBridge.App.Controls;

/// <summary>
/// Native renderer for the Liquid Glass recipe of <c>airtype-desktop.html</c>.
///
/// The CSS material is three layers, and all three are reproduced here:
///  - <c>--fill</c>: a vertical white gradient (14% → 7%) drawn inside the shape,
///  - <c>--spec</c>: the 12-stop 135° specular gradient, drawn as a ring exactly on the
///    border (CSS does this with <c>mask-composite: exclude</c>; WPF does it with a
///    stroked rounded rectangle whose pen is inset by half its width, which puts the
///    highlight on the edge itself instead of outside it),
///  - the drop shadow, applied by the consumer through <c>Elevation.Glass</c>.
///
/// Backdrop blur is the one thing WPF has no equivalent for: the material relies on the
/// near-black backdrop and the white gradient for its depth, which is what the design
/// shows anyway (see the report for this limitation).
///
/// The shape is a true pill (or any corner radius) with a single circular radius, the
/// same rule as <see cref="PillBorder"/>, so ends never become elliptical.
/// </summary>
public sealed class GlassPill : Decorator
{
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(GlassPill),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RimBrushProperty = DependencyProperty.Register(
        nameof(RimBrush), typeof(Brush), typeof(GlassPill),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RimThicknessProperty = DependencyProperty.Register(
        nameof(RimThickness), typeof(double), typeof(GlassPill),
        new FrameworkPropertyMetadata(0.75, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(CornerRadius), typeof(GlassPill),
        new FrameworkPropertyMetadata(default(CornerRadius), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PaddingProperty = DependencyProperty.Register(
        nameof(Padding), typeof(Thickness), typeof(GlassPill),
        new FrameworkPropertyMetadata(default(Thickness),
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public Brush? RimBrush
    {
        get => (Brush?)GetValue(RimBrushProperty);
        set => SetValue(RimBrushProperty, value);
    }

    public double RimThickness
    {
        get => (double)GetValue(RimThicknessProperty);
        set => SetValue(RimThicknessProperty, value);
    }

    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public Thickness Padding
    {
        get => (Thickness)GetValue(PaddingProperty);
        set => SetValue(PaddingProperty, value);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        var horizontal = Padding.Left + Padding.Right;
        var vertical = Padding.Top + Padding.Bottom;
        var available = new Size(
            Math.Max(0, constraint.Width - horizontal),
            Math.Max(0, constraint.Height - vertical));
        Child?.Measure(available);
        var desired = Child?.DesiredSize ?? default;
        return new Size(desired.Width + horizontal, desired.Height + vertical);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        if (Child is not null)
        {
            Child.Arrange(new Rect(
                Padding.Left,
                Padding.Top,
                Math.Max(0, arrangeSize.Width - Padding.Left - Padding.Right),
                Math.Max(0, arrangeSize.Height - Padding.Top - Padding.Bottom)));
        }

        return arrangeSize;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (RenderSize.Width <= 0 || RenderSize.Height <= 0) return;

        var radius = Math.Min(
            Math.Max(0, CornerRadius.TopLeft),
            Math.Min(RenderSize.Width, RenderSize.Height) / 2);
        var rect = new Rect(0, 0, RenderSize.Width, RenderSize.Height);

        // Fill: inset by half the rim so the ring sits exactly on the edge.
        var rim = Math.Max(0.5, RimThickness);
        var fillRect = new Rect(rim / 2, rim / 2, Math.Max(0, rect.Width - rim), Math.Max(0, rect.Height - rim));
        var fillRadius = Math.Max(0, radius - rim / 2);
        if (Fill is not null)
        {
            drawingContext.DrawRoundedRectangle(Fill, null, fillRect, fillRadius, fillRadius);
        }

        if (RimBrush is not null)
        {
            var pen = new Pen(RimBrush, rim) { LineJoin = PenLineJoin.Round };
            drawingContext.DrawRoundedRectangle(null, pen, fillRect, fillRadius, fillRadius);
        }
    }
}
