using System.Windows;
using System.Windows.Interop;
using Velopack;
using LiveTypeBridge.App.Localization;
using LiveTypeBridge.App.Services;
using LiveTypeBridge.App.ViewModels;
using LiveTypeBridge.App.Views;

namespace LiveTypeBridge.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly AppServices _services;
    private QrWindow? _qrWindow;
    private UpdateInfo? _availableUpdate;
    private UpdateService? _updateService;

    public MainWindow(AppServices services)
    {
        InitializeComponent();
        _services = services;
        _vm = new MainViewModel(services);
        DataContext = _vm;

        _vm.QrRequested += ShowQr;
        _vm.QrDismissed += CloseQr;
        Loc.LanguageChanged += RefreshUpdateBanner;

        SourceInitialized += (_, _) =>
        {
            var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _vm.AttachSource(source!);
        };

        Closed += (_, _) =>
        {
            CloseQr();
            Loc.LanguageChanged -= RefreshUpdateBanner;
            _vm.Dispose();
        };
    }

    public void ShowUpdateNotification(UpdateInfo update, UpdateService service)
    {
        _availableUpdate = update;
        _updateService = service;
        RefreshUpdateBanner();
        UpdateBanner.Visibility = Visibility.Visible;
    }

    private void RefreshUpdateBanner()
    {
        if (_availableUpdate is null) return;
        UpdateMessage.Text = Loc.Format("UpdateAvailable", _availableUpdate.TargetFullRelease.Version);
    }

    private void UpdateLater_Click(object sender, RoutedEventArgs e)
    {
        UpdateBanner.Visibility = Visibility.Collapsed;
    }

    private async void UpdateInstall_Click(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate is null || _updateService is null) return;

        UpdateInstallButton.IsEnabled = false;
        UpdateMessage.Text = Loc.Get("UpdateDownloading");
        try
        {
            await _updateService.DownloadAndRestartAsync(_availableUpdate);
        }
        catch (Exception ex)
        {
            _services.Log.Error($"Update installation failed: {ex}");
            UpdateMessage.Text = Loc.Get("UpdateFailed");
            UpdateInstallButton.IsEnabled = true;
        }
    }

    private void ShowQr(QrViewModel qrVm)
    {
        CloseQr();
        _qrWindow = new QrWindow(qrVm) { Owner = this };
        _qrWindow.Show();
    }

    private void CloseQr()
    {
        if (_qrWindow is null) return;
        var window = _qrWindow;
        _qrWindow = null;
        try { window.Close(); } catch { }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { /* maximized race */ }
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
