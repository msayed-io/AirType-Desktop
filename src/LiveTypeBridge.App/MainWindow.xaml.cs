using System.Windows;
using System.Windows.Interop;
using LiveTypeBridge.App.Services;
using LiveTypeBridge.App.ViewModels;
using LiveTypeBridge.App.Views;

namespace LiveTypeBridge.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly AppServices _services;
    private QrWindow? _qrWindow;

    public MainWindow(AppServices services)
    {
        InitializeComponent();
        _services = services;
        _vm = new MainViewModel(services);
        DataContext = _vm;

        _vm.QrRequested += ShowQr;
        _vm.QrDismissed += CloseQr;

        SourceInitialized += (_, _) =>
        {
            var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _vm.AttachSource(source!);
        };

        Closed += (_, _) =>
        {
            CloseQr();
            _vm.Dispose();
        };
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
