using System.Windows;
using Velopack;
using Velopack.Sources;
using LiveTypeBridge.App.Localization;

namespace LiveTypeBridge.App.Services;

/// <summary>
/// Checks GitHub Releases through Velopack and applies updates only after the user accepts.
/// The application must be installed through the Velopack Setup.exe for update hooks to work.
/// </summary>
public sealed class UpdateService
{
    public const string RepositoryUrl = "https://github.com/msayed-io/LiveTypeBridge";

    private readonly Action<string> _log;

    public UpdateService(Action<string> log) => _log = log;

    public async Task CheckAndPromptAsync(Window owner, CancellationToken cancellationToken = default)
    {
        try
        {
            var source = new GithubSource(RepositoryUrl, accessToken: null, prerelease: false);
            var manager = new UpdateManager(source);
            var update = await manager.CheckForUpdatesAsync();
            if (update is null) return;

            var version = update.TargetFullRelease.Version.ToString();
            var answer = MessageBox.Show(
                owner,
                Loc.Format("UpdateAvailable", version),
                Loc.Get("MsgTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Information,
                MessageBoxResult.Yes,
                MessageBoxOptions.DefaultDesktopOnly);

            if (answer != MessageBoxResult.Yes) return;

            await manager.DownloadUpdatesAsync(update, cancelToken: cancellationToken);
            manager.ApplyUpdatesAndRestart(update.TargetFullRelease);
        }
        catch (Velopack.Exceptions.NotInstalledException)
        {
            // Running from bin/Debug or bin/Release is expected during development.
            _log("Update check skipped: the app is not installed through Velopack.");
        }
        catch (OperationCanceledException)
        {
            _log("Update check cancelled.");
        }
        catch (Exception ex)
        {
            // Update failures must never prevent the application from starting.
            _log($"Update check failed: {ex.Message}");
        }
    }
}
