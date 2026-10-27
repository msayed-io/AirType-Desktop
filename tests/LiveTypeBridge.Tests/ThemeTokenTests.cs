using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Xunit;

namespace LiveTypeBridge.Tests;

/// <summary>
/// Guards the desktop design system against drift. Every expected number here is copied
/// from <c>airtype-desktop.html</c> and <c>airtype-desktop-design.md</c>; if a token, a
/// colour or a reference string changes, one of these tests fails on CI.
/// </summary>
public sealed class ThemeTokenTests
{
    private static readonly Regex ResourceReference = new(
        @"\{(?:Dynamic|Static)Resource\s+(?<key>[^}]+)\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

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
        var pillToken = tokens.Root!.Elements().Single(element =>
            (string?)element.Attribute(X + "Key") == "Radius.Pill");

        Assert.Equal("9999", pillToken.Value.Trim());
        Assert.Contains("Math.Min(rect.Width, rect.Height) / 2", source, StringComparison.Ordinal);
        Assert.Contains("effectiveRadius, effectiveRadius", source, StringComparison.Ordinal);
        Assert.Contains("DrawRoundedRectangle", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BezierTo", source, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(appRoot, "Controls", "SmoothBorder.cs")));
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
    public void Glass_renderer_draws_the_rim_inside_the_same_circular_radius()
    {
        var appRoot = FindAppRoot();
        var source = File.ReadAllText(Path.Combine(appRoot, "Controls", "GlassPill.cs"));

        // Same clamping rule as PillBorder, plus the half-thickness inset that puts the
        // specular ring exactly on the border instead of outside it.
        Assert.Contains("Math.Min(RenderSize.Width, RenderSize.Height) / 2", source, StringComparison.Ordinal);
        Assert.Contains("rim / 2", source, StringComparison.Ordinal);
        Assert.Contains("DrawRoundedRectangle(Fill, null, fillRect, fillRadius, fillRadius)", source, StringComparison.Ordinal);
        Assert.Contains("LineJoin = PenLineJoin.Round", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Palette_is_the_dark_reference_palette_without_purple()
    {
        var appRoot = FindAppRoot();
        var colors = XDocument.Load(Path.Combine(appRoot, "Themes", "Colors.xaml"));

        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Color.Bg"] = "#FF000000",
            ["Color.Surface"] = "#FF1C1C1E",
            ["Color.Surface2"] = "#FF2C2C2E",
            ["Color.Blue"] = "#FF0A84FF",
            ["Color.Green"] = "#FF30D158",
            ["Color.Red"] = "#FFFF453A",
            ["Color.Amber"] = "#FFFFD60A",
            ["Color.Label2"] = "#9EEBEBF5",
            ["Color.Label3"] = "#52EBEBF5",
        };

        foreach (var (key, value) in expected)
        {
            var element = colors.Root!.Elements().SingleOrDefault(e => (string?)e.Attribute(X + "Key") == key);
            Assert.NotNull(element);
            Assert.Equal(value, element!.Value.Trim());
        }

        // No purple anywhere: no colour may have a strong red+blue with a weak green.
        foreach (var element in colors.Root!.Elements())
        {
            var raw = element.Value.Trim();
            if (!raw.StartsWith('#')) continue;
            var hex = raw.Length == 9 ? raw[3..] : raw[1..];
            if (hex.Length != 6) continue;
            var r = Convert.ToInt32(hex[..2], 16);
            var g = Convert.ToInt32(hex[2..4], 16);
            var b = Convert.ToInt32(hex[4..], 16);
            Assert.False(r > 90 && b > 90 && g < 70, $"{raw} in {(string?)element.Attribute(X + "Key")} is a purple");
        }

        Assert.Equal("#24FFFFFF", (string?)colors.Root!.Elements()
            .Single(e => (string?)e.Attribute(X + "Key") == "TabSelectedBrush").Attribute("Color"));
    }

    [Fact]
    public void Tokens_match_the_reference_measurements()
    {
        var tokens = ReadTokenTypes(Path.Combine(FindAppRoot(), "Themes", "DesignTokens.xaml"));
        var values = ReadTokenValues(Path.Combine(FindAppRoot(), "Themes", "DesignTokens.xaml"));

        Assert.Equal("Thickness", tokens["Space.TabBarOffset"]);
        Assert.Equal("0,0,0,28", values["Space.TabBarOffset"]);
        Assert.Equal("Thickness", tokens["Space.Main"]);
        Assert.Equal("28,0,28,112", values["Space.Main"]);

        Assert.Equal("CornerRadius", tokens["Radius.Card"]);
        Assert.Equal("28", values["Radius.Card"]);
        Assert.Equal("20", values["Radius.L"]);
        Assert.Equal("9999", values["Radius.Pill"]);

        Assert.Equal("76", values["Size.HeaderHeight"]);
        Assert.Equal("60", values["Size.NavHeight"]);
        Assert.Equal("48", values["Size.TabHeight"]);
        Assert.Equal("44", values["Size.ControlHeight"]);
        Assert.Equal("32", values["Size.StatusPill"]);
        Assert.Equal("48", values["Size.DeviceTile"]);
        Assert.Equal("200", values["Size.Qr"]);
        Assert.Equal("640", values["Size.ContentMax"]);
        Assert.Equal("900", values["Size.WindowMinWidth"]);

        Assert.Equal("34", values["Type.Hero"]);
        Assert.Equal("28", values["Type.Metric"]);
        Assert.Equal("20", values["Type.Wordmark"]);
        Assert.Equal("0.75", values["Stroke.Rim"]);
    }

    [Fact]
    public void Glass_material_matches_the_reference_recipe()
    {
        var appRoot = FindAppRoot();
        var glass = XDocument.Load(Path.Combine(appRoot, "Themes", "Glass.xaml"));

        var fill = glass.Root!.Elements().Single(e => (string?)e.Attribute(X + "Key") == "Glass.Fill");
        var fillStops = fill.Descendants().Where(e => e.Name.LocalName == "GradientStop").ToArray();
        Assert.Equal(2, fillStops.Length);
        Assert.Equal("#24FFFFFF", (string?)fillStops[0].Attribute("Color"));
        Assert.Equal("#12FFFFFF", (string?)fillStops[1].Attribute("Color"));
        Assert.Equal("0.5,0", (string?)fill.Attribute("StartPoint"));
        Assert.Equal("0.5,1", (string?)fill.Attribute("EndPoint"));

        var spec = glass.Root!.Elements().Single(e => (string?)e.Attribute(X + "Key") == "Glass.Spec");
        Assert.Equal(12, spec.Descendants().Count(e => e.Name.LocalName == "GradientStop"));
        Assert.Equal("0,0", (string?)spec.Attribute("StartPoint"));
        Assert.Equal("1,1", (string?)spec.Attribute("EndPoint"));

        var tokens = XDocument.Load(Path.Combine(appRoot, "Themes", "DesignTokens.xaml"));
        var elevation = tokens.Root!.Elements().Single(e => (string?)e.Attribute(X + "Key") == "Elevation.Glass");
        Assert.Equal("22", (string?)elevation.Attribute("BlurRadius"));
        Assert.Equal("8", (string?)elevation.Attribute("ShadowDepth"));
        Assert.Equal("0.6", (string?)elevation.Attribute("Opacity"));
    }

    [Fact]
    public void Glass_is_used_only_on_the_permitted_surfaces_and_cards_stay_flat()
    {
        var appRoot = FindAppRoot();

        var statusPage = File.ReadAllText(Path.Combine(appRoot, "Views", "StatusPage.xaml"));
        var mainWindow = File.ReadAllText(Path.Combine(appRoot, "MainWindow.xaml"));
        var styles = File.ReadAllText(Path.Combine(appRoot, "Themes", "Styles.xaml"));
        var settings = File.ReadAllText(Path.Combine(appRoot, "Views", "SettingsPage.xaml"));
        var diagnostics = File.ReadAllText(Path.Combine(appRoot, "Views", "DiagnosticsPage.xaml"));

        // One capsule in the home screen (the status pill) and exactly three in the shell:
        // the window-button capsule, the toast and the floating tab bar.
        Assert.Single(Regex.Matches(statusPage, @"<controls:GlassPill[\s>]").Cast<Match>());
        Assert.Equal(3, Regex.Matches(mainWindow, @"<controls:GlassPill[\s>]").Count);

        // Glass buttons exist only through the GlassButton template (the two action
        // capsules and the "refresh code" capsule of the not-connected state).
        Assert.Single(Regex.Matches(styles, @"<controls:GlassPill[\s>]").Cast<Match>());
        Assert.DoesNotContain("GlassPill", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("GlassPill", diagnostics, StringComparison.Ordinal);

        // Cards are flat: the Card style has no Effect setter, and no page puts a glass
        // material or a shadow on a card.
        var stylesDoc = XDocument.Load(Path.Combine(appRoot, "Themes", "Styles.xaml"));
        var cardStyle = stylesDoc.Root!.Elements(Presentation + "Style")
            .Single(e => (string?)e.Attribute(X + "Key") == "Card");
        Assert.DoesNotContain(cardStyle.Descendants(Presentation + "Setter"),
            setter => (string?)setter.Attribute("Property") == "Effect");
        Assert.Contains(cardStyle.Descendants(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Background" &&
            (string?)setter.Attribute("Value") == "{DynamicResource CardBrush}");

        foreach (var page in new[] { statusPage, settings, diagnostics })
            Assert.DoesNotContain("Elevation.Glass", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Tab_selector_is_a_flat_14_percent_fill_with_a_blue_label()
    {
        var appRoot = FindAppRoot();
        var styles = XDocument.Load(Path.Combine(appRoot, "Themes", "Styles.xaml"));
        var nav = styles.Root!.Elements(Presentation + "Style")
            .Single(e => (string?)e.Attribute(X + "Key") == "BottomNavItem");

        var selected = nav.Descendants(Presentation + "Trigger")
            .Single(trigger => (string?)trigger.Attribute("Property") == "IsChecked");
        Assert.Contains(selected.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Foreground" &&
            (string?)setter.Attribute("Value") == "{DynamicResource AccentBrush}");
        Assert.Contains(selected.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("TargetName") == "Surface" &&
            (string?)setter.Attribute("Property") == "Background" &&
            (string?)setter.Attribute("Value") == "{DynamicResource TabSelectedBrush}");

        // The selected tab is a flat fill: no specular rim, no glass material on it.
        Assert.DoesNotContain("GlassPill", selected.ToString(SaveOptions.DisableFormatting), StringComparison.Ordinal);
        Assert.Equal(20d, double.Parse(ReadTokenValues(Path.Combine(appRoot, "Themes", "DesignTokens.xaml"))["Size.Icon"],
            System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Reference_strings_are_present_in_both_languages()
    {
        var appRoot = FindAppRoot();
        var arabic = ReadStrings(Path.Combine(appRoot, "Localization", "Strings.ar.xaml"));
        var english = ReadStrings(Path.Combine(appRoot, "Localization", "Strings.en.xaml"));

        Assert.Equal(
            arabic.Keys.OrderBy(k => k, StringComparer.Ordinal),
            english.Keys.OrderBy(k => k, StringComparer.Ordinal));

        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HeroStreaming"] = "البث جارٍ",
            ["HeroPaused"] = "البث متوقف",
            ["HeroReady"] = "جاهز للربط",
            ["StatusCapsuleConnected"] = "متصل",
            ["StatusCapsuleReady"] = "جاهز للربط",
            ["ViaWifi"] = "متصل عبر Wi‑Fi",
            ["SubConnected"] = "ما تكتبه على الهاتف يظهر في الخانة النشطة فورًا.",
            ["SubScanPrompt"] = "امسح الرمز من تطبيق AirType على هاتفك.",
            ["HomeLabelLatency"] = "زمن الاستجابة",
            ["HomeLabelChars"] = "الأحرف المكتوبة",
            ["LabelLastReceive"] = "آخر استقبال",
            ["UnitMs"] = "م.ث",
            ["RefreshCode"] = "تحديث الرمز",
            ["LabelOps"] = "العمليات المطبّقة",
            ["LabelChars"] = "الأحرف",
            ["LabelBackspaces"] = "عمليات المسح",
            ["LabelInjectMs"] = "زمن الإدخال",
            ["LabelLastMessage"] = "آخر رسالة",
            ["LabelRtt"] = "نبضة RTT",
        };

        foreach (var (key, value) in expected)
        {
            Assert.True(arabic.ContainsKey(key), $"missing Arabic string {key}");
            Assert.Equal(value, arabic[key]);
        }
    }

    [Fact]
    public void Home_screen_covers_every_state_with_real_bindings()
    {
        var appRoot = FindAppRoot();
        var page = File.ReadAllText(Path.Combine(appRoot, "Views", "StatusPage.xaml"));

        foreach (var binding in new[]
                 {
                     "HasQrCode", "NoQrCode", "ShowSessionCard", "IsStreamingNow", "PulseDot", "StateBrush",
                     "HomeTitle", "HomeSub", "CapsuleText",
                     "PhoneDisplay", "LatencyCellText", "CharsCellText", "LastReceiveCellText",
                     "{Binding PairPhoneCommand}", "{Binding ToggleStreamCommand}", "{Binding DisconnectCommand}",
                     "{Binding QrVm.QrImage}", "{Binding QrVm.PinDisplayText}",
                 })
            Assert.Contains(binding, page, StringComparison.Ordinal);

        // The pairing action must never appear while a phone is attached: both pairing
        // buttons are gated on the pairing card, and the session buttons on the session card.
        foreach (var button in Regex.Matches(page, @"<Button[^>]*PairPhoneCommand[^>]*/>", RegexOptions.Singleline).Cast<Match>())
            Assert.True(button.Value.Contains("HasQrCode", StringComparison.Ordinal) ||
                        button.Value.Contains("NoQrCode", StringComparison.Ordinal), button.Value);
        Assert.All(Regex.Matches(page, @"<Button[^>]*ShowSessionCard[^>]*>", RegexOptions.Singleline).Cast<Match>(),
            match => Assert.DoesNotContain("PairPhoneCommand", match.Value, StringComparison.Ordinal));
    }

    [Fact]
    public void Settings_page_keeps_every_existing_setting()
    {
        var appRoot = FindAppRoot();
        var page = File.ReadAllText(Path.Combine(appRoot, "Views", "SettingsPage.xaml"));

        foreach (var binding in new[]
                 {
                     "SaveCommand", "OpenLogsCommand", "ForgetTrustedDevicesCommand",
                     "SetLangArCommand", "SetLangEnCommand",
                     "PortText", "HkShowApp", "HkToggle", "HkQr", "HkEmergency",
                     "RunAtStartup", "EnableLogging", "LogTextContent", "TrustedDevicesSummary",
                 })
            Assert.Contains(binding, page, StringComparison.Ordinal);
    }

    [Fact]
    public void Diagnostics_page_lists_the_seven_reference_rows_with_technical_details()
    {
        var appRoot = FindAppRoot();
        var page = File.ReadAllText(Path.Combine(appRoot, "Views", "DiagnosticsPage.xaml"));

        // Reference rows: applied operations, characters, deletions, injection time,
        // last message, RTT ping, last received. Technical details stay on this tab only.
        foreach (var key in new[]
                 {
                     "LabelOps", "LabelChars", "LabelBackspaces", "LabelInjectMs",
                     "LabelLastMessage", "LabelRtt", "LabelLastReceive",
                 })
            Assert.Contains("{DynamicResource " + key + "}", page, StringComparison.Ordinal);

        foreach (var binding in new[]
                 {
                     "OpsCellText", "CharsCellText", "BackspacesCellText", "InjectCellText",
                     "LastMessageCellText", "RttCellText", "LastReceiveCellText",
                 })
            Assert.Contains(binding, page, StringComparison.Ordinal);

        Assert.Contains("DiagTitle", page, StringComparison.Ordinal);

        var statusPage = File.ReadAllText(Path.Combine(appRoot, "Views", "StatusPage.xaml"));
        Assert.DoesNotContain("RttCellText", statusPage, StringComparison.Ordinal);
        Assert.DoesNotContain("BackspacesCellText", statusPage, StringComparison.Ordinal);
    }

    [Fact]
    public void Quick_pairing_pin_is_prominent_and_derived_from_the_verified_session_pin()
    {
        var appRoot = FindAppRoot();

        var qrWindow = XDocument.Load(Path.Combine(appRoot, "Views", "QrWindow.xaml"));
        var display = qrWindow.Descendants(Presentation + "TextBlock").SingleOrDefault(element =>
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
            Assert.Contains(strings.Root!.Elements(), element =>
                (string?)element.Attribute(X + "Key") == "QrQuickPinLabel");
            Assert.Contains(strings.Root!.Elements(), element =>
                (string?)element.Attribute(X + "Key") == "QrQuickPinHint");
        }
    }

    private static Dictionary<string, string> ReadStrings(string path) =>
        XDocument.Load(path).Root!.Elements()
            .Where(element => element.Attribute(X + "Key") is not null)
            .ToDictionary(
                element => element.Attribute(X + "Key")!.Value,
                element => element.Value,
                StringComparer.Ordinal);

    private static Dictionary<string, string> ReadTokenTypes(string path) =>
        XDocument.Load(path).Root!.Elements()
            .Where(element => element.Attribute(X + "Key") is not null)
            .ToDictionary(
                element => element.Attribute(X + "Key")!.Value,
                element => element.Name.LocalName,
                StringComparer.Ordinal);

    private static Dictionary<string, string> ReadTokenValues(string path) =>
        XDocument.Load(path).Root!.Elements()
            .Where(element => element.Attribute(X + "Key") is not null)
            .ToDictionary(
                element => element.Attribute(X + "Key")!.Value,
                element => element.Value.Trim(),
                StringComparer.Ordinal);

    private static string FindAppRoot([CallerFilePath] string testSource = "")
    {
        var testsDirectory = Directory.GetParent(testSource)!.Parent!;
        return Path.GetFullPath(Path.Combine(
            testsDirectory.FullName, "..", "src", "LiveTypeBridge.App"));
    }
}
