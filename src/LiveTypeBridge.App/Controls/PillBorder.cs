using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LiveTypeBridge.App.Controls;

/// <summary>
/// WPF equivalent of CSS border-radius: 9999px for pill-shaped surfaces.
/// WPF Border normalizes oversized CornerRadius values independently on each axis,
/// which can produce an ellipse. This decorator instead clamps one circular radius
/// to half the actual shorter side, preserving true semicircular ends at every size.
/// </summary>
public sealed class PillBorder : Decorator
{
    public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(
        nameof(Background), typeof(Brush), typeof(PillBorder),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BorderBrushProperty = DependencyProperty.Register(
        nameof(BorderBrush), typeof(Brush), typeof(PillBorder),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BorderThicknessProperty = DependencyProperty.Register(
        nameof(BorderThickness), typeof(Thickness), typeof(PillBorder),
        new FrameworkPropertyMetadata(default(Thickness),
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(CornerRadius), typeof(PillBorder),
        new FrameworkPropertyMetadata(default(CornerRadius), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PaddingProperty = DependencyProperty.Register(
        nameof(Padding), typeof(Thickness), typeof(PillBorder),
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

        var requestedRadius = Math.Max(0, CornerRadius.TopLeft - inset);
        var effectiveRadius = Math.Min(requestedRadius, Math.Min(rect.Width, rect.Height) / 2);
        var pen = BorderBrush is null || stroke <= 0 ? null : new Pen(BorderBrush, stroke);

        // Equal radiusX/radiusY is essential: it produces circular semicircular ends,
        // never the independent X/Y scaling that turns a rectangular pill into an ellipse.
        drawingContext.DrawRoundedRectangle(
            Background, pen, rect, effectiveRadius, effectiveRadius);
    }
}
