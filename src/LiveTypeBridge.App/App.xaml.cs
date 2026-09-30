using System.IO;
using System.Windows;
using LiveTypeBridge.App.Localization;
using LiveTypeBridge.App.Services;

namespace LiveTypeBridge.App;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private bool _ownsSingleInstance;
    private AppServices? _services;
    private bool _startupCompleted;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = new Mutex(initiallyOwned: true, "AirType_SingleInstance", out _ownsSingleInstance);
        if (!_ownsSingleInstance)
        {
            MessageBox.Show(Loc.Get("ErrAlreadyRunning"), Loc.Get("MsgTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        if (e.Args.Contains("--selftest", StringComparer.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var code = RunHeadlessSelfTest();
            Shutdown(code);
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            _services?.Log.Error($"Unhandled UI exception: {args.Exception}");
            MessageBox.Show(args.Exception.Message, Loc.Get("MsgTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;

            // A presentation failure while constructing/showing the first window leaves
            // no usable UI. With explicit shutdown mode, merely dismissing the error
            // dialog would otherwise keep a headless process and its instance mutex alive.
            if (!_startupCompleted)
                Shutdown(-1);
        };

        _services = AppServices.Create();
        AppServicesHolder.Services = _services;
        Loc.Init(_services.Settings.Language);

        var window = new MainWindow(_services);
        MainWindow = window;
        window.Show();
        _startupCompleted = true;
        _ = CheckForUpdatesAsync(window);
    }

    private async Task CheckForUpdatesAsync(Window owner)
    {
        // Give the main window and local services time to initialise before network I/O.
        await Task.Delay(TimeSpan.FromSeconds(3));
        var updater = new UpdateService(message => _services?.Log.Info(message));
        var update = await updater.CheckAsync();
        if (update is not null && owner.IsVisible)
            ((MainWindow)owner).ShowUpdateNotification(update, updater);
    }

    /// <summary>
    /// Headless mode (--selftest): runs the full local pipeline check without any UI.
    /// Result is written next to the logs; process exit code 0 = pass, 1 = fail.
    /// </summary>
    private int RunHeadlessSelfTest()
    {
        var services = AppServices.Create(headless: true);
        try
        {
            // Run off the UI thread: OnStartup carries a Dispatcher sync context and a
            // bare GetResult() on RunAsync would deadlock on its awaits.
            var result = Task.Run(() => services.SelfTest.RunAsync()).GetAwaiter().GetResult();
            var lines = result.Lines.Append($"OVERALL: {(result.Passed ? "PASS" : "FAIL")}");
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LiveTypeBridge", "selftest-result.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, string.Join(Environment.NewLine, lines));
            return result.Passed ? 0 : 1;
        }
        catch (Exception ex)
        {
            try
            {
                services.Log.Error($"Self test crashed: {ex}");
            }
            catch { }
            return 1;
        }
        finally
        {
            services.Dispose();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Release the single-instance gate before any potentially blocking service
        // cleanup, so a deliberate close can be followed by an immediate relaunch.
        if (_ownsSingleInstance)
        {
            try { _singleInstance?.ReleaseMutex(); } catch (ApplicationException) { }
            _ownsSingleInstance = false;
        }

        _singleInstance?.Dispose();
        _singleInstance = null;
        _services?.Settings.Save();
        _services?.Dispose();
        base.OnExit(e);
    }
}
