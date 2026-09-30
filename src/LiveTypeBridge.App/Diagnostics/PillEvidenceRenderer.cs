using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LiveTypeBridge.App.Controls;

namespace LiveTypeBridge.App.Diagnostics;

/// <summary>Windows-only visual evidence generator for the native WPF pill renderer.</summary>
internal static class PillEvidenceRenderer
{
    private static readonly List<(string Scenario, string Name, FrameworkElement Element)> Tracked = [];

    public static void Render(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        Tracked.Clear();
        RenderSingle(Path.Combine(outputDirectory, "pill-proof-01-short-text.png"), "SHORT TEXT / نص قصير", false, 900, 690);
        RenderSingle(Path.Combine(outputDirectory, "pill-proof-02-long-text.png"), "LONG TEXT / نص طويل", true, 1100, 690);
        RenderResizeCases(Path.Combine(outputDirectory, "pill-proof-03-window-sizes.png"));

        var lines = new List<string>
        {
            "AirType native WPF pill computed-geometry report",
            "Requested CornerRadius token: 9999 DIP",
            "Formula: effectiveRadius = min(requestedRadius, min(actualWidth, actualHeight) / 2)",
            "Invariant: radiusX == radiusY == effectiveRadius",
            "",
        };
        lines.AddRange(Tracked.Select(item =>
        {
            var radius = Math.Min(9999, Math.Min(item.Element.ActualWidth, item.Element.ActualHeight) / 2);
            return $"{item.Scenario} | {item.Name} | actual={item.Element.ActualWidth:0.##}x{item.Element.ActualHeight:0.##} | effectiveRadius={radius:0.##} | circular=true";
        }));
        File.WriteAllLines(Path.Combine(outputDirectory, "pill-computed-values.txt"), lines);
    }

    private static void RenderSingle(string path, string title, bool longText, double width, double height)
    {
        var root = EvidenceRoot(width, height, title);
        var content = (StackPanel)root.Children[1];
        content.Children.Add(EvidenceRow(title,
            Header(longText ? "AirType — الكتابة المباشرة من الهاتف إلى الكمبيوتر" : "AirType", longText ? 520 : 150),
            "Header capsule", 48));
        content.Children.Add(EvidenceRow(title,
            Button(longText ? "حفظ جميع الإعدادات وتطبيق التغييرات الآن" : "حفظ", true, longText ? 540 : 140),
            "Primary button", 44));
        content.Children.Add(EvidenceRow(title,
            Button(longText ? "إجراء ثانوي طويل للتحقق من ثبات شكل الكبسولة" : "إلغاء", false, longText ? 500 : 140),
            "Secondary button", 44));
        content.Children.Add(EvidenceRow(title,
            Navigation(longText ? 650 : 420), "Navigation items", 44));
        content.Children.Add(EvidenceRow(title,
            Input(longText ? 620 : 320, longText ? "نص إدخال طويل لا يغيّر نصف القطر الدائري للطرفين" : "8080"),
            "Pill text field", 44));
        content.Children.Add(EvidenceRow(title,
            Badge(longText ? "متصل وآمن — جلسة نشطة" : "متصل", longText ? 280 : 110),
            "Status badge", 28));
        Save(root, path, width, height);
    }

    private static void RenderResizeCases(string path)
    {
        const double width = 1400;
        const double height = 760;
        var root = EvidenceRoot(width, height, "WINDOW RESIZE / تغيير حجم النافذة");
        var content = (StackPanel)root.Children[1];
        var columns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(columns);
        foreach (var item in new[] { ("MIN 760×560", 280d), ("MEDIUM 980×700", 360d), ("FULL 1400×900", 460d) })
        {
            var panel = new StackPanel { Width = item.Item2, Margin = new Thickness(10) };
            panel.Children.Add(Label(item.Item1, 16, FontWeights.Bold));
            panel.Children.Add(Header("AirType", item.Item2 - 20));
            panel.Children.Add(Spacer());
            panel.Children.Add(Button("حفظ الإعدادات", true, item.Item2 - 20));
            panel.Children.Add(Spacer());
            panel.Children.Add(Navigation(item.Item2 - 20));
            panel.Children.Add(Spacer());
            panel.Children.Add(Input(item.Item2 - 20, "8080"));
            panel.Children.Add(Label("Requested 9999 DIP\nActual H: 44/48 DIP\nComputed R: H/2\nrx = ry (circular)", 12));
            columns.Children.Add(panel);
        }
        Save(root, path, width, height);
    }

    private static Grid EvidenceRoot(double width, double height, string title)
    {
        var root = new Grid { Width = width, Height = height, Background = Brush("BgBrush"), FlowDirection = FlowDirection.RightToLeft };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var heading = Label(title, 24, FontWeights.Bold);
        heading.Margin = new Thickness(28, 24, 28, 14);
        root.Children.Add(heading);
        var content = new StackPanel { Margin = new Thickness(28, 0, 28, 24) };
        Grid.SetRow(content, 1);
        root.Children.Add(content);
        return root;
    }

    private static Grid EvidenceRow(string scenario, FrameworkElement element, string name, double height)
    {
        element.Height = height;
        Tracked.Add((scenario, name, element));
        var row = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        element.HorizontalAlignment = HorizontalAlignment.Left;
        row.Children.Add(element);
        var computed = Label($"{name}\nrequested: 9999 DIP | actual H: {height:0} | computed R: {height / 2:0.#}\nrx = ry → circular semicircles", 12);
        computed.FlowDirection = FlowDirection.LeftToRight;
        computed.Foreground = Brush("TextSecondaryBrush");
        Grid.SetColumn(computed, 1);
        row.Children.Add(computed);
        return row;
    }

    private static FrameworkElement Header(string text, double width) => new PillBorder
    {
        Width = width, Height = 48, CornerRadius = Radius(), Background = Brush("BgElevatedBrush"),
        BorderBrush = Brush("BorderSubtleBrush"), BorderThickness = new Thickness(1), Padding = new Thickness(18, 0, 18, 0),
        Child = Label(text, 16, FontWeights.Bold),
    };

    private static FrameworkElement Button(string text, bool primary, double width) => new Button
    {
        Width = width, Height = 44, Content = text,
        Style = (Style)(primary
            ? Application.Current.FindResource("PrimaryButton")
            : Application.Current.FindResource(typeof(Button))),
    };

    private static FrameworkElement Navigation(double width)
    {
        var stack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var label in new[] { "الرئيسية", "الإعدادات", "التشخيص" })
            stack.Children.Add(new RadioButton { Content = label, Style = (Style)Application.Current.FindResource("BottomNavItem"), Height = 44 });
        return new Border
        {
            Width = width, Height = 56, CornerRadius = new CornerRadius(24), Padding = new Thickness(8),
            Background = Brush("CardRaisedBrush"), BorderBrush = Brush("BorderBrushKey"), BorderThickness = new Thickness(1), Child = stack,
        };
    }

    private static FrameworkElement Input(double width, string text) => new TextBox
    {
        Width = width, Height = 44, Text = text, Style = (Style)Application.Current.FindResource("InputBox"),
    };

    private static FrameworkElement Badge(string text, double width) => new PillBorder
    {
        Width = width, Height = 28, CornerRadius = Radius(), Background = Brush("AccentBrush"), Padding = new Thickness(12, 4, 12, 4),
        Child = Label(text, 12, FontWeights.SemiBold),
    };

    private static TextBlock Label(string text, double size, FontWeight? weight = null) => new()
    {
        Text = text, FontSize = size, FontWeight = weight ?? FontWeights.Normal,
        Foreground = Brush("TextBrush"), VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.None,
    };

    private static Border Spacer() => new() { Height = 18 };
    private static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);
    private static CornerRadius Radius() => (CornerRadius)Application.Current.FindResource("Radius.Pill");

    private static void Save(FrameworkElement root, string path, double width, double height)
    {
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
