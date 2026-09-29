using System.Windows;
using System.Windows.Data;

namespace LiveTypeBridge.App.Localization;

/// <summary>
/// Runtime language switching: swaps the merged Strings dictionary, flips window
/// flow direction (RTL for Arabic), persists the choice, and notifies view models.
/// </summary>
public static class Loc
{
    public const string Arabic = "ar";
    public const string English = "en";

    public static string Current { get; private set; } = Arabic;
    public static bool IsRtl => Current == Arabic;

    public static event Action? LanguageChanged;

    public static void Init(string language)
    {
        Current = language == English ? English : Arabic;
        Apply(false);
    }

    public static void Set(string language)
    {
        var next = language == English ? English : Arabic;
        if (next == Current) return;
        Current = next;
        Apply(save: true);
    }

    private static void Apply(bool save)
    {
        if (Application.Current is null) return;
        var merged = Application.Current.Resources.MergedDictionaries;
        var index = -1;
        for (var i = 0; i < merged.Count; i++)
        {
            var source = merged[i].Source?.OriginalString ?? "";
            if (source.Contains("Localization/Strings.", StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }

        var dict = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Localization/Strings.{Current}.xaml", UriKind.Absolute),
        };
        if (index >= 0) merged[index] = dict;
        else merged.Add(dict);

        var flow = IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        foreach (Window window in Application.Current.Windows)
            window.FlowDirection = flow;

        if (save && AppServicesHolder.Services is { } services)
        {
            services.Settings.Language = Current;
            services.Settings.Save();
        }

        LanguageChanged?.Invoke();
    }

    public static string Get(string key)
        => Application.Current?.TryFindResource(key) as string ?? key;

    public static string Format(string key, params object[] args)
    {
        var template = Get(key);
        try { return string.Format(System.Globalization.CultureInfo.CurrentCulture, template, args); }
        catch (FormatException) { return template; }
    }
}

/// <summary>Small bridge so Loc can persist the language without static coupling to App.</summary>
public static class AppServicesHolder
{
    public static LiveTypeBridge.App.Services.AppServices? Services { get; set; }
}
