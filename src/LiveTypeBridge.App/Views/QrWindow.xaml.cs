using System.Windows;
using LiveTypeBridge.App.Interop;
using LiveTypeBridge.App.ViewModels;

namespace LiveTypeBridge.App.Views;

public partial class QrWindow : Window
{
    private readonly QrViewModel _vm;

    public QrWindow(QrViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        Closed += (_, _) => vm.Dispose();
        SourceInitialized += (_, _) => WindowCorners.ApplyRounded(this);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
