using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Xunit;

namespace LiveTypeBridge.Tests;

public sealed class ThemeTokenTests
{
    private static readonly Regex ResourceReference = new(
        @"\{(?:Dynamic|Static)Resource\s+(?<key>[^}]+)\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void Thickness_properties_do_not_reference_scalar_tokens()
    {
        var appRoot = FindAppRoot();
        var tokenTypes = ReadTokenTypes(Path.Combine(appRoot, "Themes", "DesignTokens.xaml"));
        var thicknessProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            "Padding",
            "Margin",
            "BorderThickness",
            "ResizeBorderThickness",
            "GlassFrameThickness",
        };

        var errors = new List<string>();
        foreach (var path in Directory.EnumerateFiles(appRoot, "*.xaml", SearchOption.AllDirectories))
        {
            var document = XDocument.Load(path, LoadOptions.SetLineInfo);
            foreach (var attribute in document.Descendants().Attributes()
                         .Where(attribute => thicknessProperties.Contains(attribute.Name.LocalName)))
            {
                var match = ResourceReference.Match(attribute.Value);
                if (!match.Success) continue;

                var key = match.Groups["key"].Value.Trim();
                if (!tokenTypes.TryGetValue(key, out var type) || type == "Thickness") continue;

                var line = ((IXmlLineInfo)attribute).LineNumber;
                errors.Add($"{Path.GetRelativePath(appRoot, path)}:{line} " +
                           $"{attribute.Name.LocalName} references {key} ({type}), expected Thickness");
            }
        }

        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }

    [Fact]
    public void Pill_renderer_clamps_9999_to_one_circular_half_height_radius()
    {
        var appRoot = FindAppRoot();
        var source = File.ReadAllText(Path.Combine(appRoot, "Controls", "PillBorder.cs"));
        var tokens = XDocument.Load(Path.Combine(appRoot, "Themes", "DesignTokens.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var pillToken = tokens.Root!.Elements().Single(element =>
            (string?)element.Attribute(x + "Key") == "Radius.Pill");

        Assert.Equal("9999", pillToken.Value.Trim());
        Assert.Contains("Math.Min(rect.Width, rect.Height) / 2", source, StringComparison.Ordinal);
        Assert.Contains("effectiveRadius, effectiveRadius", source, StringComparison.Ordinal);
        Assert.Contains("DrawRoundedRectangle", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BezierTo", source, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(appRoot, "Controls", "SmoothBorder.cs")));
    }

    [Fact]
    public void Every_declared_pill_surface_uses_pill_border_and_the_9999_token()
    {
        var appRoot = FindAppRoot();
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace controls = "clr-namespace:LiveTypeBridge.App.Controls";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var styles = XDocument.Load(Path.Combine(appRoot, "Themes", "Styles.xaml"));
        var requiredTemplateParts = new[]
        {
            "ButtonSurface", "InputSurface", "NavSurface", "ChoiceSurface", "Track", "Thumb",
        };
        foreach (var name in requiredTemplateParts)
        {
            var surface = styles.Descendants(controls + "PillBorder").SingleOrDefault(element =>
                (string?)element.Attribute(x + "Name") == name);
            Assert.NotNull(surface);
            Assert.Equal("{DynamicResource Radius.Pill}", (string?)surface!.Attribute("CornerRadius"));
            Assert.DoesNotContain(styles.Descendants(presentation + "Border"), element =>
                (string?)element.Attribute(x + "Name") == name);
        }

        var pillStyleKeys = new[] { "ChromeCapsule", "GlassCapsule" };
        foreach (var key in pillStyleKeys)
        {
            var style = styles.Descendants(presentation + "Style").Single(element =>
                (string?)element.Attribute(x + "Key") == key);
            Assert.Equal("controls:PillBorder", (string?)style.Attribute("TargetType"));
        }

        var navContainer = styles.Descendants(presentation + "Style").Single(element =>
            (string?)element.Attribute(x + "Key") == "NavContainer");
        Assert.Equal("Border", (string?)navContainer.Attribute("TargetType"));
        Assert.Contains(navContainer.Descendants(presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "CornerRadius" &&
            (string?)setter.Attribute("Value") == "{DynamicResource Radius.Navigation}");

        foreach (var path in Directory.EnumerateFiles(appRoot, "*.xaml", SearchOption.AllDirectories))
        {
            var document = XDocument.Load(path);
            Assert.All(document.Descendants(controls + "PillBorder"), element =>
                Assert.Equal("{DynamicResource Radius.Pill}", (string?)element.Attribute("CornerRadius")));
        }
    }

    [Theory]
    [InlineData(44, 22)]
    [InlineData(48, 24)]
    [InlineData(52, 26)]
    [InlineData(24, 12)]
    [InlineData(28, 14)]
    public void Pill_radius_is_exactly_half_the_actual_height(double height, double expectedRadius)
    {
        var effectiveRadius = Math.Min(9999, Math.Min(400, height) / 2);
        Assert.Equal(expectedRadius, effectiveRadius);
    }

    [Fact]
    public void Quick_pairing_pin_is_prominent_and_derived_from_the_verified_session_pin()
    {
        var appRoot = FindAppRoot();
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var qrWindow = XDocument.Load(Path.Combine(appRoot, "Views", "QrWindow.xaml"));
        var display = qrWindow.Descendants(presentation + "TextBlock").SingleOrDefault(element =>
            (string?)element.Attribute("Text") == "{Binding PinDisplayText}");
        Assert.NotNull(display);
        Assert.Equal("{DynamicResource Type.Pin}", (string?)display!.Attribute("FontSize"));
        Assert.Equal("Bold", (string?)display.Attribute("FontWeight"));
        Assert.Equal("LeftToRight", (string?)display.Attribute("FlowDirection"));

        var viewModelSource = File.ReadAllText(Path.Combine(appRoot, "ViewModels", "QrViewModel.cs"));
        Assert.Contains("PinText = pin;", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("PinDisplayText = string.Join(\" \", pin.ToCharArray());", viewModelSource, StringComparison.Ordinal);

        var mainViewModelSource = File.ReadAllText(Path.Combine(appRoot, "ViewModels", "MainViewModel.cs"));
        Assert.Contains("new QrViewModel(ImageHelpers.BitmapFromPng(png), session.Pin", mainViewModelSource, StringComparison.Ordinal);

        foreach (var language in new[] { "ar", "en" })
        {
            var strings = XDocument.Load(Path.Combine(appRoot, "Localization", $"Strings.{language}.xaml"));
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            Assert.Contains(strings.Root!.Elements(), element =>
                (string?)element.Attribute(x + "Key") == "QrQuickPinLabel");
            Assert.Contains(strings.Root!.Elements(), element =>
                (string?)element.Attribute(x + "Key") == "QrQuickPinHint");
        }
    }

    private static Dictionary<string, string> ReadTokenTypes(string path)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return XDocument.Load(path).Root!.Elements()
            .Where(element => element.Attribute(x + "Key") is not null)
            .ToDictionary(
                element => element.Attribute(x + "Key")!.Value,
                element => element.Name.LocalName,
                StringComparer.Ordinal);
    }

    private static string FindAppRoot([CallerFilePath] string testSource = "")
    {
        var testsDirectory = Directory.GetParent(testSource)!.Parent!;
        return Path.GetFullPath(Path.Combine(
            testsDirectory.FullName, "..", "src", "LiveTypeBridge.App"));
    }
}
