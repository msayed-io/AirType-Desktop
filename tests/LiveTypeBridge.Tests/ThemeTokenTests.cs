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
