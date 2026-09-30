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
    public void Continuous_corner_geometry_uses_the_published_ios_bezier_coefficients()
    {
        var source = File.ReadAllText(Path.Combine(FindAppRoot(), "Controls", "SmoothBorder.cs"));

        Assert.Contains("1.52866483", source, StringComparison.Ordinal);
        Assert.Contains("0.66993427", source, StringComparison.Ordinal);
        Assert.Contains("0.06549600", source, StringComparison.Ordinal);
        Assert.Contains("0.37282392", source, StringComparison.Ordinal);
        Assert.Contains("0.16906013", source, StringComparison.Ordinal);
        Assert.Contains("path.BezierTo", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DrawRoundedRectangle", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ArcTo", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Requested_compact_surfaces_use_smooth_border_instead_of_circular_border()
    {
        var appRoot = FindAppRoot();
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace controls = "clr-namespace:LiveTypeBridge.App.Controls";

        var styles = XDocument.Load(Path.Combine(appRoot, "Themes", "Styles.xaml"));
        var requiredTemplateParts = new[]
        {
            "ButtonSurface", "InputSurface", "NavSurface", "ChoiceSurface", "Track", "Thumb",
        };
        foreach (var name in requiredTemplateParts)
        {
            Assert.NotNull(styles.Descendants(controls + "SmoothBorder")
                .SingleOrDefault(element => (string?)element.Attribute(
                    XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == name));
            Assert.DoesNotContain(styles.Descendants(presentation + "Border"), element =>
                (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == name);
        }

        var styleTargets = styles.Descendants(presentation + "Style")
            .Where(style => new[] { "ChromeCapsule", "NavCapsule", "GlassCapsule" }
                .Contains((string?)style.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))))
            .Select(style => (string?)style.Attribute("TargetType"))
            .ToArray();
        Assert.Equal(3, styleTargets.Length);
        Assert.All(styleTargets, target => Assert.Equal("controls:SmoothBorder", target));

        var requiredFiles = new[]
        {
            "MainWindow.xaml",
            Path.Combine("Views", "DiagnosticsPage.xaml"),
            Path.Combine("Views", "FloatingIndicatorWindow.xaml"),
            Path.Combine("Views", "QrWindow.xaml"),
            Path.Combine("Views", "StatusPage.xaml"),
        };
        Assert.All(requiredFiles, relativePath =>
            Assert.NotEmpty(XDocument.Load(Path.Combine(appRoot, relativePath))
                .Descendants(controls + "SmoothBorder")));
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
