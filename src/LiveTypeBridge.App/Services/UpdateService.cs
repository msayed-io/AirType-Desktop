using Velopack;
using Velopack.Sources;

namespace LiveTypeBridge.App.Services;

/// <summary>
/// Checks GitHub Releases through Velopack and installs an accepted update.
/// The application must be installed through the Velopack Setup.exe for update hooks to work.
/// </summary>
public sealed class UpdateService
{
    public const string RepositoryUrl = "https://github.com/msayed-io/LiveTypeBridge";

    private readonly Action<string> _log;
    private UpdateManager? _manager;

    public UpdateService(Action<string> log) => _log = log;

    public async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _manager = new UpdateManager(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));
            return await _manager.CheckForUpdatesAsync();
        }
        catch (Velopack.Exceptions.NotInstalledException)
        {
            _log("Update check skipped: the app is not installed through Velopack.");
            return null;
        }
        catch (OperationCanceledException)
        {
            _log("Update check cancelled.");
            return null;
        }
        catch (Exception ex)
        {
            // Update failures must never prevent the application from starting.
            _log($"Update check failed: {ex.Message}");
            return null;
        }
    }

    public async Task DownloadAndRestartAsync(UpdateInfo update, CancellationToken cancellationToken = default)
    {
        if (_manager is null)
            throw new InvalidOperationException("Update check must complete before installation.");

        await _manager.DownloadUpdatesAsync(update, cancelToken: cancellationToken);
        _manager.ApplyUpdatesAndRestart(update.TargetFullRelease);
    }
}
