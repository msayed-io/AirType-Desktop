using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LiveTypeBridge.App.Controls;

/// <summary>
/// A WPF decorator that renders the reverse-engineered iOS continuous-rounded-rectangle
/// Bézier path instead of Border's circular quarter-arcs. It preserves normal WPF layout,
/// hit testing, bindings, templates, gradients, and focus triggers.
/// </summary>
public sealed class SmoothBorder : Decorator
{
    private const double Extent = 1.52866483;

    public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(
        nameof(Background), typeof(Brush), typeof(SmoothBorder),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BorderBrushProperty = DependencyProperty.Register(
        nameof(BorderBrush), typeof(Brush), typeof(SmoothBorder),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BorderThicknessProperty = DependencyProperty.Register(
        nameof(BorderThickness), typeof(Thickness), typeof(SmoothBorder),
        new FrameworkPropertyMetadata(default(Thickness),
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(CornerRadius), typeof(SmoothBorder),
        new FrameworkPropertyMetadata(default(CornerRadius), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PaddingProperty = DependencyProperty.Register(
        nameof(Padding), typeof(Thickness), typeof(SmoothBorder),
        new FrameworkPropertyMetadata(default(Thickness),
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public Brush? Background
    {
        get => (Brush?)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public Brush? BorderBrush
    {
        get => (Brush?)GetValue(BorderBrushProperty);
        set => SetValue(BorderBrushProperty, value);
    }

    public Thickness BorderThickness
    {
        get => (Thickness)GetValue(BorderThicknessProperty);
        set => SetValue(BorderThicknessProperty, value);
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
        var horizontal = BorderThickness.Left + BorderThickness.Right + Padding.Left + Padding.Right;
        var vertical = BorderThickness.Top + BorderThickness.Bottom + Padding.Top + Padding.Bottom;
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
            var x = BorderThickness.Left + Padding.Left;
            var y = BorderThickness.Top + Padding.Top;
            Child.Arrange(new Rect(
                x,
                y,
                Math.Max(0, arrangeSize.Width - x - BorderThickness.Right - Padding.Right),
                Math.Max(0, arrangeSize.Height - y - BorderThickness.Bottom - Padding.Bottom)));
        }
        return arrangeSize;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var stroke = Math.Max(0, Math.Max(
            Math.Max(BorderThickness.Left, BorderThickness.Right),
            Math.Max(BorderThickness.Top, BorderThickness.Bottom)));
        var inset = stroke / 2;
        var rect = new Rect(
            inset,
            inset,
            Math.Max(0, RenderSize.Width - stroke),
            Math.Max(0, RenderSize.Height - stroke));
        if (rect.IsEmpty) return;

        var radius = Math.Max(0, CornerRadius.TopLeft - inset);
        var geometry = CreateContinuousGeometry(rect, radius);
        var pen = BorderBrush is null || stroke <= 0 ? null : new Pen(BorderBrush, stroke);
        drawingContext.DrawGeometry(Background, pen, geometry);
    }

    internal static Geometry CreateContinuousGeometry(Rect rect, double requestedRadius)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return Geometry.Empty;
        var radiusLimit = Math.Min(rect.Width, rect.Height) / 2 / Extent;
        var radius = Math.Min(Math.Max(0, requestedRadius), radiusLimit);
        if (radius <= 0) return new RectangleGeometry(rect);

        Point TL(double x, double y) => new(rect.Left + x * radius, rect.Top + y * radius);
        Point TR(double x, double y) => new(rect.Right - x * radius, rect.Top + y * radius);
        Point BR(double x, double y) => new(rect.Right - x * radius, rect.Bottom - y * radius);
        Point BL(double x, double y) => new(rect.Left + x * radius, rect.Bottom - y * radius);

        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var path = geometry.Open())
        {
            path.BeginFigure(TL(1.52866483, 0), isFilled: true, isClosed: true);
            path.LineTo(TR(1.52866471, 0), true, false);
            path.BezierTo(TR(1.08849323, 0), TR(0.86840689, 0), TR(0.66993427, 0.06549600), true, false);
            path.LineTo(TR(0.63149399, 0.07491100), true, false);
            path.BezierTo(TR(0.37282392, 0.16905899), TR(0.16906013, 0.37282401), TR(0.07491176, 0.63149399), true, false);
            path.BezierTo(TR(0, 0.86840701), TR(0, 1.08849299), TR(0, 1.52866483), true, false);

            path.LineTo(BR(0, 1.52866471), true, false);
            path.BezierTo(BR(0, 1.08849323), BR(0, 0.86840689), BR(0.06549569, 0.66993493), true, false);
            path.LineTo(BR(0.07491111, 0.63149399), true, false);
            path.BezierTo(BR(0.16905883, 0.37282392), BR(0.37282392, 0.16905883), BR(0.63149399, 0.07491111), true, false);
            path.BezierTo(BR(0.86840689, 0), BR(1.08849323, 0), BR(1.52866471, 0), true, false);

            path.LineTo(BL(1.52866483, 0), true, false);
            path.BezierTo(BL(1.08849299, 0), BL(0.86840701, 0), BL(0.66993397, 0.06549569), true, false);
            path.LineTo(BL(0.63149399, 0.07491111), true, false);
            path.BezierTo(BL(0.37282401, 0.16905883), BL(0.16906001, 0.37282392), BL(0.07491100, 0.63149399), true, false);
            path.BezierTo(BL(0, 0.86840689), BL(0, 1.08849323), BL(0, 1.52866471), true, false);

            path.LineTo(TL(0, 1.52866483), true, false);
            path.BezierTo(TL(0, 1.08849299), TL(0, 0.86840701), TL(0.06549600, 0.66993397), true, false);
            path.LineTo(TL(0.07491100, 0.63149399), true, false);
            path.BezierTo(TL(0.16906001, 0.37282401), TL(0.37282401, 0.16906001), TL(0.63149399, 0.07491100), true, false);
            path.BezierTo(TL(0.86840701, 0), TL(1.08849299, 0), TL(1.52866483, 0), true, false);
        }
        geometry.Freeze();
        return geometry;
    }
}
