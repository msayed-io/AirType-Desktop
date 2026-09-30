using System.IO;
using System.Windows;
using LiveTypeBridge.App.Diagnostics;
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

        const string evidencePrefix = "--pill-evidence=";
        var evidenceArgument = args.FirstOrDefault(arg =>
            arg.StartsWith(evidencePrefix, StringComparison.OrdinalIgnoreCase));
        if (evidenceArgument is not null)
        {
            PillEvidenceRenderer.Render(Path.GetFullPath(evidenceArgument[evidencePrefix.Length..]));
            return;
        }

        app.Run();
    }
}
