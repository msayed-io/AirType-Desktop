using LiveTypeBridge.Core.Diagnostics;
using LiveTypeBridge.Core.Input;
using LiveTypeBridge.Core.Logging;
using LiveTypeBridge.Core.Networking;
using LiveTypeBridge.Core.Pairing;
using LiveTypeBridge.Core.Settings;

namespace LiveTypeBridge.App.Services;

/// <summary>Pump notifications forwarded to the UI layer (marshalled there).</summary>
public sealed class UiHub
{
    public event Action<PumpMode>? ModeChanged;
    public event Action<PumpStats>? StatsUpdated;
    public event Action<string>? InjectorWarning;
    public event Action<int, int>? ResetNeeded;

    public void RaiseMode(PumpMode m) => ModeChanged?.Invoke(m);
    public void RaiseStats(PumpStats s) => StatsUpdated?.Invoke(s);
    public void RaiseWarning(string code) => InjectorWarning?.Invoke(code);
    public void RaiseReset(int appliedLength, int expectedNext) => ResetNeeded?.Invoke(appliedLength, expectedNext);
}

/// <summary>Composition root: builds and owns every service once per process.</summary>
public sealed class AppServices : IDisposable
{
    public AppSettings Settings { get; private set; } = new();
    public Log Log { get; private set; } = new(enabled: false);
    public PairingSessionManager Sessions { get; private set; } = new();
    public PhoneConnectionManager Connections { get; private set; } = null!;
    public PhoneLinkServer Server { get; private set; } = new();
    public LocalInputMonitor? Monitor { get; private set; }
    public SelfTestRunner SelfTest { get; private set; } = null!;
    public UiHub Hub { get; } = new();

    public static AppServices Create(bool headless = false)
    {
        var services = new AppServices();
        services.Settings = AppSettings.Load();
        services.Log = new Log(services.Settings.EnableLogging);
        services.Log.Info($"LiveType Bridge starting (headless={headless})");

        services.Connections = new PhoneConnectionManager(services.Sessions, session =>
        {
            var injector = session.IsSelfTest
                ? SelfTestRunner.GetOrCreateRecorder(session.SessionId)
                : new SendInputInjector();
            var bridge = new PumpEventsBridge(
                session,
                services.Connections,
                services.Hub.RaiseMode,
                services.Hub.RaiseStats,
                services.Hub.RaiseWarning,
                services.Hub.RaiseReset);
            return new PumpCore(injector, bridge);
        });

        services.Server.ServerLog += line => services.Log.Info($"[server] {line}");
        services.SelfTest = new SelfTestRunner(services.Server, services.Sessions, services.Connections);

        if (!headless)
            services.Monitor = new LocalInputMonitor();

        return services;
    }

    public void Dispose()
    {
        try { Connections.Dispose(); } catch { }
        try { Monitor?.Dispose(); } catch { }
        try { Server.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3)); } catch { }
        try { Log.Info("LiveType Bridge stopped."); Log.Dispose(); } catch { }
    }
}
