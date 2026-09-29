using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using LiveTypeBridge.App.ViewModels;

namespace LiveTypeBridge.App.Views;

public partial class FloatingIndicatorWindow : Window
{
    public event Action? Clicked;
    private bool _working;
    private bool _webReady;

    public FloatingIndicatorWindow()
    {
        InitializeComponent();
        PositionBottomRight();
        Loaded += async (_, _) => await InitializeOrbAsync();
        OrbWebView.WebMessageReceived += OrbWebView_WebMessageReceived;
        OrbWebView.PreviewMouseLeftButtonDown += OrbWebView_PreviewMouseLeftButtonDown;
        OrbWebView.NavigationCompleted += (_, _) =>
        {
            _webReady = true;
            _ = OrbWebView.ExecuteScriptAsync($"window.setOrbState && window.setOrbState('{(_working ? "working" : "breathing")}')");
        };
    }

    public void PositionBottomRight()
    {
        Left = SystemParameters.WorkArea.Right - Width - 26;
        Top = SystemParameters.WorkArea.Bottom - Height - 26;
    }

    public void SetState(UiState state)
    {
        var working = state is UiState.Streaming or UiState.Resync;
        _working = working;
        if (!_webReady) return;
        var name = working ? "working" : "breathing";
        _ = OrbWebView.ExecuteScriptAsync($"window.setOrbState && window.setOrbState('{name}')");
    }

    private async Task InitializeOrbAsync()
    {
        try
        {
            await OrbWebView.EnsureCoreWebView2Async();
            OrbWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            OrbWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            OrbWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            OrbWebView.DefaultBackgroundColor = Color.FromArgb(0, 0, 0, 0);
            var dist = Path.Combine(AppContext.BaseDirectory, "web", "orb", "dist", "index.html");
            if (!File.Exists(dist)) throw new FileNotFoundException("Orb WebView2 assets were not packaged.", dist);
            OrbWebView.Source = new Uri(dist);
        }
        catch
        {
            OrbWebView.Visibility = Visibility.Collapsed;
            FallbackOrb.Visibility = Visibility.Visible;
        }
    }

    private void OrbWebView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var json = JsonDocument.Parse(e.WebMessageAsJson);
            if (json.RootElement.TryGetProperty("type", out var type) && type.GetString() == "click")
                Clicked?.Invoke();
        }
        catch { }
    }

    private void OrbWebView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try { DragMove(); } catch { }
        e.Handled = true;
    }
}
