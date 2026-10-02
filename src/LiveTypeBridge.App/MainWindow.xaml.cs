using System.ComponentModel;
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
    private readonly FloatingIndicatorWindow _indicator;
    private QrWindow? _qrWindow;
    private UpdateInfo? _availableUpdate;
    private UpdateService? _updateService;
    private bool _shutdownRequested;

    public MainWindow(AppServices services)
    {
        InitializeComponent();
        _services = services;
        _vm = new MainViewModel(services);
        DataContext = _vm;

        _indicator = new FloatingIndicatorWindow();
        _indicator.Clicked += RestoreFromIndicator;
        _vm.QrRequested += ShowQr;
        _vm.QrDismissed += CloseQr;
        _vm.ShowMainWindowRequested += RestoreFromIndicator;
        _vm.PropertyChanged += VmPropertyChanged;
        Loc.LanguageChanged += RefreshUpdateBanner;

        SourceInitialized += (_, _) =>
        {
            var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _vm.AttachSource(source!);
        };

        Closing += (_, args) =>
        {
            if (_shutdownRequested) return;

            // Alt+F4 and any native close request follow the exact same explicit
            // shutdown path as the title-bar close button.
            args.Cancel = true;
            Dispatcher.BeginInvoke(RequestShutdown);
        };
        Closed += (_, _) =>
        {
            CloseQr();
            try { _indicator.Close(); } catch { }
            Loc.LanguageChanged -= RefreshUpdateBanner;
            _vm.ShowMainWindowRequested -= RestoreFromIndicator;
            _vm.PropertyChanged -= VmPropertyChanged;
            try { _vm.Dispose(); } catch { }
        };
    }

    private void VmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.StatsText))
        {
            Dispatcher.BeginInvoke(_indicator.PulseActivity);
            return;
        }
        if (e.PropertyName != nameof(MainViewModel.State)) return;
        Dispatcher.BeginInvoke(() =>
        {
            _indicator.SetState(_vm.State);
            if (_vm.State is UiState.Streaming or UiState.Resync)
                HideToIndicator();
            else if (!_vm.State.Equals(UiState.Streaming) && !IsVisible && _indicator.IsVisible)
                RestoreFromIndicator();
        });
    }

    private void HideToIndicator()
    {
        _indicator.SetState(_vm.State);
        _indicator.Show();
        Hide();
    }

    private void RestoreFromIndicator()
    {
        _indicator.Hide();
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Focus();
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

    private void UpdateLater_Click(object sender, RoutedEventArgs e) => UpdateBanner.Visibility = Visibility.Collapsed;

    private async void UpdateInstall_Click(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate is null || _updateService is null) return;
        UpdateInstallButton.IsEnabled = false;
        UpdateMessage.Text = Loc.Get("UpdateDownloading");
        try { await _updateService.DownloadAndRestartAsync(_availableUpdate); }
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
        var window = new QrWindow(qrVm) { Owner = this };
        window.Closed += (_, _) => _vm.PairingWindowClosed(qrVm.SessionIdText);
        _qrWindow = window;
        window.Show();
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
            try { DragMove(); } catch { }
        }
    }

    private void RequestShutdown()
    {
        if (_shutdownRequested) return;
        _shutdownRequested = true;
        _indicator.Hide();
        Application.Current.Shutdown();
    }

    private void FloatingToggle_Click(object sender, RoutedEventArgs e) => HideToIndicator();
    private void Minimize_Click(object sender, RoutedEventArgs e) => HideToIndicator();
    private void Close_Click(object sender, RoutedEventArgs e) => RequestShutdown();
}
