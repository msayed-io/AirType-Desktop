using System.Windows;
using Velopack;

namespace LiveTypeBridge.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Must be the first startup call so install/update/uninstall hooks can exit quickly.
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
