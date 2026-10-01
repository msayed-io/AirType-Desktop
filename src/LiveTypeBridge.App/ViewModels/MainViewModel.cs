using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using LiveTypeBridge.App.Localization;
using LiveTypeBridge.App.Mvvm;
using LiveTypeBridge.App.Services;
using LiveTypeBridge.Core.Input;
using LiveTypeBridge.Core.Networking;
using LiveTypeBridge.Core.Pairing;
using LiveTypeBridge.Core.Protocol;

namespace LiveTypeBridge.App.ViewModels;

public enum UiState
{
    NotConnected,
    ReadyToScan,
    Streaming,
    PausedManual,
    PausedProtect,
    Resync,
    EmergencyStopped,
    Error,
}

/// <summary>
/// Drives the main window: connection state, pairing flow, stream control, hotkeys,
/// and honest measured latency. Never stores any focus handle — injection always
/// lands in whatever field is focused when the op is applied.
/// </summary>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly AppServices _s;
    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _clock;
    private readonly SemaphoreSlim _trustedDiscoveryGate = new(1, 1);
    private GlobalHotkeyManager? _hotkeys;

    private UiState _state = UiState.NotConnected;
    private string _phoneName = "";
    private string _statsText = "";
    private string _lastErrorText = "";
    private string _hotkeyWarning = "";
    private bool _elevatedWarning;
    private bool _busy;
    private bool _suppressTrustedReconnect;
    private bool _disposed;
    private object _currentPage;
    private double _lastLagMs;
    private int? _lastRttMs;
    private PumpStats? _lastStats;
    private PumpCore? _activePump;
    private PairingSession? _pendingSession;

    public event Action<QrViewModel>? QrRequested;
    public event Action? QrDismissed;

    public MainViewModel(AppServices services)
    {
        _s = services;
        _ui = Dispatcher.CurrentDispatcher;
        _currentPage = this;
        _statsText = Loc.Get("StatsEmpty");
        SettingsVm = new SettingsViewModel(services, this);
        DiagVm = new DiagnosticsViewModel(services, this);

        PairPhoneCommand = new RelayCommand(_ => _ = PairPhoneAsync(), _ => !_busy);
        ToggleStreamCommand = new RelayCommand(_ => ToggleStream(), _ => _activePump is not null);
        DisconnectCommand = new RelayCommand(_ => _ = DisconnectAsync(), _ => _activePump is not null || _state == UiState.ReadyToScan);
        ResumeStreamCommand = new RelayCommand(_ => ResumeStream(), _ => _activePump is not null);
        EmergencyStopCommand = new RelayCommand(_ => EmergencyStop(), _ => _activePump is not null);
        ToggleLanguageCommand = new RelayCommand(_ => Loc.Set(Loc.IsRtl ? Loc.English : Loc.Arabic));
        NavCommand = new RelayCommand(param => Navigate(param as string ?? "home"));

        _s.Connections.PhonePaired += (name, sid) => RunOnUi(() =>
        {
            PhoneName = name;
            _activePump = _s.Connections.TryGetPump(sid);
            _pendingSession = null;
            _suppressTrustedReconnect = false;
            _ = StopDiscoveryAsync();
            _elevatedWarning = false;
            SettingsVm.NotifyTrustedDevicesChanged();
            Raise(nameof(ShowElevatedCard));
            QrDismissed?.Invoke();
            SetState(UiState.Streaming);
            _s.Log.Info($"Phone paired: {name}");
        });

        _s.Connections.PhoneDisconnected += reason => RunOnUi(() =>
        {
            _activePump = null;
            PhoneName = "";
            SetState(UiState.NotConnected);
            _s.Log.Info($"Phone disconnected ({reason}) — trusted discovery will resume when allowed");
            if (!_suppressTrustedReconnect)
                _ = StartTrustedDiscoveryAsync();
        });

        _s.Connections.LatencySample += (lag, rtt) => RunOnUi(() =>
        {
            _lastLagMs = lag;
            if (rtt is int r) _lastRttMs = r;
            Raise(nameof(LatencyText));
            Raise(nameof(LastReceiveText));
        });

        _s.Connections.ProtocolError += (code, detail) => RunOnUi(() =>
        {
            LastErrorText = DescribeError(code);
            DiagVm.NotifyError();
            _s.Log.Warn($"protocol error {code}: {detail}");
        });

        _s.Hub.ModeChanged += mode => RunOnUi(() => MapPumpMode(mode));
        _s.Hub.StatsUpdated += stats => RunOnUi(() =>
        {
            _lastStats = stats;
            StatsText = Loc.Format("StatsFmt", stats.OpsApplied, stats.CharsTyped, stats.Backspaces, stats.LastInjectMs);
        });
        _s.Hub.InjectorWarning += code => RunOnUi(() =>
        {
            if (code != "target-may-be-elevated") return;
            _elevatedWarning = true;
            Raise(nameof(ShowElevatedCard));
            _s.Log.Warn("Injection refused: foreground app likely elevated (UIPI)");
        });
        _s.Hub.ResetNeeded += (applied, next) => RunOnUi(() =>
            _s.Log.Info($"Resync requested (applied={applied}, expectedNext={next})"));

        Loc.LanguageChanged += OnLanguageChanged;

        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) =>
        {
            Raise(nameof(LastReceiveText));
            if (_pendingSession is { } ps && ps.IsExpired && _state == UiState.ReadyToScan)
            {
                _pendingSession = null;
                _suppressTrustedReconnect = false;
                QrDismissed?.Invoke();
                SetState(UiState.NotConnected);
                _ = RestartTrustedDiscoveryAsync();
            }
        };
        _clock.Start();
        _ = StartTrustedDiscoveryAsync();
    }

    // ---- Bound properties --------------------------------------------------

    public SettingsViewModel SettingsVm { get; }
    public DiagnosticsViewModel DiagVm { get; }

    public ICommand PairPhoneCommand { get; }
    public ICommand ToggleStreamCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand ResumeStreamCommand { get; }
    public ICommand EmergencyStopCommand { get; }
    public ICommand ToggleLanguageCommand { get; }
    public ICommand NavCommand { get; }

    public object CurrentPage
    {
        get => _currentPage;
        private set { if (Set(ref _currentPage, value)) { Raise(nameof(IsHome)); Raise(nameof(IsSettings)); Raise(nameof(IsDiagnostics)); } }
    }

    public bool IsHome => ReferenceEquals(_currentPage, this);
    public bool IsSettings => ReferenceEquals(_currentPage, SettingsVm);
    public bool IsDiagnostics => ReferenceEquals(_currentPage, DiagVm);

    public UiState State
    {
        get => _state;
        private set
        {
            if (!Set(ref _state, value)) return;
            Raise(nameof(StatusText));
            Raise(nameof(StatusSub));
            Raise(nameof(StateBrush));
            Raise(nameof(PulseOn));
            Raise(nameof(ShowElevatedCard));
            Raise(nameof(StreamButtonLabel));
            Raise(nameof(StreamButtonEnabled));
            Raise(nameof(DisconnectEnabled));
            Raise(nameof(PhoneDisplay));
        }
    }

    public string StatusText => Loc.Get(State switch
    {
        UiState.ReadyToScan => "StatusReadyToScan",
        UiState.Streaming => "StatusStreaming",
        UiState.PausedManual => "StatusPausedManual",
        UiState.PausedProtect => "StatusPausedProtect",
        UiState.Resync => "StatusResync",
        UiState.EmergencyStopped => "StatusEmergency",
        UiState.Error => "StatusError",
        _ => "StatusNotConnected",
    });

    public string StatusSub => Loc.Get(State switch
    {
        UiState.ReadyToScan => "SubReadyToScan",
        UiState.Streaming => "SubStreaming",
        UiState.PausedManual => "SubPausedManual",
        UiState.PausedProtect => "SubPausedProtect",
        UiState.Resync => "SubResync",
        UiState.EmergencyStopped => "SubEmergency",
        UiState.Error => "SubError",
        _ => "SubNotConnected",
    });

    public Brush StateBrush
    {
        get
        {
            var key = State switch
            {
                UiState.ReadyToScan => "AccentBrush",
                UiState.Streaming => "SuccessBrush",
                UiState.PausedManual or UiState.PausedProtect or UiState.Resync => "WarnBrush",
                UiState.EmergencyStopped or UiState.Error => "DangerBrush",
                _ => "IdleBrush",
            };
            return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
        }
    }

    public bool PulseOn => State == UiState.Streaming;

    public string PhoneName
    {
        get => _phoneName;
        private set { if (Set(ref _phoneName, value)) Raise(nameof(PhoneDisplay)); }
    }

    public string PhoneDisplay => string.IsNullOrEmpty(PhoneName) ? Loc.Get("PhoneEmpty") : PhoneName;

    public string LastReceiveText
    {
        get
        {
            var utc = _s.Connections.LastReceiveUtc;
            if (utc is not { } received) return Loc.Get("LastReceiveNever");
            var ago = DateTime.UtcNow - received;
            if (ago.TotalSeconds < 5) return Loc.Format("AgoFmt", 0);
            if (ago.TotalMinutes < 60) return Loc.Format("AgoFmt", (int)ago.TotalSeconds);
            return received.ToLocalTime().ToString("HH:mm:ss");
        }
    }

    public string LatencyText
    {
        get
        {
            if (_lastLagMs <= 0 && _lastRttMs is null) return Loc.Get("LatencyNone");
            var text = Loc.Format("LatencyLastMs", _lastLagMs.ToString("0"));
            if (_lastRttMs is int rtt) text += Loc.Format("LatencyRttMs", rtt);
            return text;
        }
    }

    public string StatsText
    {
        get => _statsText;
        private set => Set(ref _statsText, value);
    }

    public string LastErrorText
    {
        get => _lastErrorText;
        private set => Set(ref _lastErrorText, value);
    }

    public string HotkeyWarning
    {
        get => _hotkeyWarning;
        private set { if (Set(ref _hotkeyWarning, value)) Raise(nameof(HasHotkeyWarning)); }
    }

    public bool HasHotkeyWarning => !string.IsNullOrEmpty(HotkeyWarning);

    public bool ShowElevatedCard => _elevatedWarning;
    public bool IsBusy => _busy;

    public bool StreamButtonEnabled => _activePump is not null;
    public bool DisconnectEnabled => _activePump is not null || State == UiState.ReadyToScan;

    public string StreamButtonLabel => Loc.Get(IsStreamActive ? "BtnStopStream" : "BtnResumeStream");

    public bool IsStreamActive => State is UiState.Streaming or UiState.Resync;

    // ---- Pairing ------------------------------------------------------------

    private async Task StopDiscoveryAsync()
    {
        await _trustedDiscoveryGate.WaitAsync();
        try { await _s.Discovery.StopAsync(); }
        finally { _trustedDiscoveryGate.Release(); }
    }

    private async Task RestartTrustedDiscoveryAsync()
    {
        await _trustedDiscoveryGate.WaitAsync();
        try
        {
            await _s.Discovery.StopAsync();
            await StartTrustedDiscoveryCoreAsync();
        }
        finally
        {
            _trustedDiscoveryGate.Release();
        }
    }

    private async Task StartTrustedDiscoveryAsync()
    {
        await _trustedDiscoveryGate.WaitAsync();
        try { await StartTrustedDiscoveryCoreAsync(); }
        finally { _trustedDiscoveryGate.Release(); }
    }

    private async Task StartTrustedDiscoveryCoreAsync()
    {
        try
        {
            if (_disposed
                || _suppressTrustedReconnect
                || _s.TrustedDevices.Count == 0
                || _s.Connections.HasPairedPhone)
                return;

            if (!_s.Server.IsRunning)
            {
                var started = await _s.Server.StartAsync(_s.Settings.Port, _s.Connections);
                if (!started)
                {
                    _s.Log.Warn("Trusted auto-reconnect could not start the WebSocket server");
                    return;
                }
            }

            var ip = LocalIpFinder.GetPrimary();
            if (ip is null)
            {
                _s.Log.Warn("Trusted auto-reconnect is waiting for a LAN IPv4 address");
                return;
            }

            var session = _s.Sessions.ActiveUserSession
                          ?? _s.Sessions.Create(TimeSpan.FromDays(3650));
            var startedDiscovery = await _s.Discovery.StartAsync(new LiveTypeAdvertisement(
                Name: Environment.MachineName,
                Host: ip.Address,
                Port: _s.Server.ActualPort,
                SessionId: session.SessionId,
                ExpiresUtc: session.ExpiresUtc));
            if (startedDiscovery)
                _s.Log.Info($"Trusted auto-reconnect ready on {ip.Address}:{_s.Server.ActualPort}");
            else
                _s.Log.Warn("Trusted auto-reconnect could not bind UDP discovery port 53018");
        }
        catch (Exception ex)
        {
            _s.Log.Warn($"Trusted auto-reconnect unavailable: {ex.Message}");
        }
    }

    private async Task PairPhoneAsync()
    {
        if (_busy) return;
        _busy = true;
        _suppressTrustedReconnect = true;
        Raise(nameof(IsBusy));
        try
        {
            await StopDiscoveryAsync();
            if (!_s.Server.IsRunning)
            {
                var started = await _s.Server.StartAsync(_s.Settings.Port, _s.Connections);
                if (!started)
                {
                    SetError(Loc.Get("ErrPortRange"));
                    return;
                }
                if (_s.Server.ActualPort != _s.Settings.Port)
                    _s.Log.Info($"Port {_s.Settings.Port} busy — using {_s.Server.ActualPort}");
            }

            // Any old phone/session is cut off the moment a new pairing starts.
            await _s.Connections.DisconnectAllAsync();

            var ip = LocalIpFinder.GetPrimary();
            if (ip is null)
            {
                SetError(Loc.Get("ErrNoNetwork"));
                return;
            }

            var session = _s.Sessions.Create(TimeSpan.FromHours(12));
            var address = $"ws://{ip.Address}:{_s.Server.ActualPort}/livetype";
            var payload = QrPayload.Build(session, ip.Address, _s.Server.ActualPort);
            var png = QrPayload.RenderPng(payload);

            _pendingSession = session;
            PhoneName = "";
            _lastStats = null;
            StatsText = Loc.Get("StatsEmpty");

            var discoveryStarted = await _s.Discovery.StartAsync(new LiveTypeAdvertisement(
                Name: Environment.MachineName,
                Host: ip.Address,
                Port: _s.Server.ActualPort,
                SessionId: session.SessionId,
                ExpiresUtc: session.ExpiresUtc));
            if (!discoveryStarted)
                _s.Log.Warn("LAN auto-discovery unavailable; QR/manual pairing remains active");

            var qrVm = new QrViewModel(ImageHelpers.BitmapFromPng(png), session.Pin, address, session.SessionId, session.ExpiresUtc);
            qrVm.Expired += () => RunOnUi(() =>
            {
                if (State != UiState.ReadyToScan) return;
                _pendingSession = null;
                _suppressTrustedReconnect = false;
                QrDismissed?.Invoke();
                SetState(UiState.NotConnected);
                _ = RestartTrustedDiscoveryAsync();
            });
            QrRequested?.Invoke(qrVm);
            SetState(UiState.ReadyToScan);
            _s.Log.Info($"Pairing session created for {ip.Address}:{_s.Server.ActualPort}");
        }
        catch (Exception ex)
        {
            await StopDiscoveryAsync();
            _suppressTrustedReconnect = false;
            _s.Log.Error($"PairPhone failed: {ex.Message}");
            SetError(ex.Message);
        }
        finally
        {
            _busy = false;
            Raise(nameof(IsBusy));
            if (_pendingSession is null && _activePump is null && State == UiState.Error)
            {
                _suppressTrustedReconnect = false;
                _ = StartTrustedDiscoveryAsync();
            }
        }
    }

    private async Task DisconnectAsync()
    {
        _suppressTrustedReconnect = true;
        QrDismissed?.Invoke();
        _pendingSession = null;
        await StopDiscoveryAsync();
        await _s.Connections.DisconnectAllAsync();
        SetState(UiState.NotConnected);
    }

    // ---- Stream control -------------------------------------------------------

    private void ToggleStream()
    {
        if (_activePump is null) return;
        if (IsStreamActive)
        {
            _activePump.PauseManual();
            SetState(UiState.PausedManual);
        }
        else
        {
            ResumeStream();
        }
    }

    private void ResumeStream()
    {
        if (_activePump is null) return;
        _activePump.Resume();
        _elevatedWarning = false;
        Raise(nameof(ShowElevatedCard));
        SetState(UiState.Streaming);
    }

    private void EmergencyStop()
    {
        if (_activePump is null) return;
        _activePump.EmergencyStop();
        SetState(UiState.EmergencyStopped);
        _s.Log.Info("EMERGENCY STOP — all input suspended");
    }

    // ---- Hotkeys ---------------------------------------------------------------

    public void AttachSource(HwndSource source)
    {
        _hotkeys = new GlobalHotkeyManager(source.Handle);
        source.AddHook(WndProc);
        RegisterAllHotkeys();
    }

    /// <summary>Called by settings after hotkeys change.</summary>
    public void ReRegisterHotkeys() => RegisterAllHotkeys();

    private void RegisterAllHotkeys()
    {
        if (_hotkeys is null) return;
        var failed = new List<string>();

        var toggle = HotkeyGesture.Parse(_s.Settings.HotkeyToggleStream) ?? HotkeyGesture.DefaultToggleStream;
        var qr = HotkeyGesture.Parse(_s.Settings.HotkeyShowQr) ?? HotkeyGesture.DefaultShowQr;
        var emergency = HotkeyGesture.Parse(_s.Settings.HotkeyEmergencyStop) ?? HotkeyGesture.DefaultEmergencyStop;

        if (!_hotkeys.TryRegister(GlobalHotkeyManager.IdToggleStream, toggle,
            () => RunOnUi(ToggleStream)))
            failed.Add(Loc.Get("SetHkToggle"));

        if (!_hotkeys.TryRegister(GlobalHotkeyManager.IdShowQr, qr,
            () => RunOnUi(() => _ = PairPhoneAsync())))
            failed.Add(Loc.Get("SetHkQr"));

        if (!_hotkeys.TryRegister(GlobalHotkeyManager.IdEmergencyStop, emergency,
            () => RunOnUi(EmergencyStop)))
            failed.Add(Loc.Get("SetHkEmergency"));

        HotkeyWarning = failed.Count == 0 ? "" : Loc.Format("HotkeyRegFail", string.Join(", ", failed));
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;
        if (msg == WM_HOTKEY && _hotkeys is not null)
        {
            handled = _hotkeys.TryHandleHotkey(wParam);
        }
        return IntPtr.Zero;
    }

    // ---- Internal helpers --------------------------------------------------------

    private void SetState(UiState state) => State = state;

    private void SetError(string message)
    {
        LastErrorText = message;
        SetState(UiState.Error);
        _s.Log.Error($"UI error state: {message}");
    }

    private void MapPumpMode(PumpMode mode)
    {
        switch (mode)
        {
            case PumpMode.Active:
                if (_activePump is not null) SetState(UiState.Streaming);
                break;
            case PumpMode.PausedManual:
                SetState(UiState.PausedManual);
                break;
            case PumpMode.PausedProtect:
                SetState(UiState.PausedProtect);
                break;
            case PumpMode.EmergencyStopped:
                SetState(UiState.EmergencyStopped);
                break;
            case PumpMode.Resync:
                SetState(UiState.Resync);
                break;
        }
    }

    private static string DescribeError(string code) => code switch
    {
        Envelope.Errors.BadPin => Loc.Get("ErrBadPin"),
        Envelope.Errors.BadToken => Loc.Get("ErrBadToken"),
        Envelope.Errors.UnknownSession or Envelope.Errors.SessionExpired => Loc.Get("ErrSessionGone"),
        Envelope.Errors.AlreadyPaired => Loc.Get("ErrAlreadyPaired"),
        Envelope.Errors.TooLarge or Envelope.Errors.Malformed => Loc.Get("ErrMalformedMsg"),
        Envelope.Errors.UnexpectedMessage => Loc.Get("ErrUnexpectedMsg"),
        _ => code,
    };

    private void OnLanguageChanged()
    {
        StatsText = _lastStats is null
            ? Loc.Get("StatsEmpty")
            : Loc.Format("StatsFmt", _lastStats.OpsApplied, _lastStats.CharsTyped, _lastStats.Backspaces, _lastStats.LastInjectMs);
        Raise(string.Empty); // refresh every computed string
    }

    private void RunOnUi(Action action) => _ui.BeginInvoke(action);

    public void PairingWindowClosed(string sessionId)
    {
        if (_pendingSession?.SessionId != sessionId) return;
        _pendingSession = null;
        _suppressTrustedReconnect = false;
        SetState(UiState.NotConnected);
        _ = RestartTrustedDiscoveryAsync();
        _s.Log.Info("Pairing window closed; trusted auto-reconnect resumed when available");
    }

    private void Navigate(string page)
    {
        CurrentPage = page switch
        {
            "settings" => SettingsVm,
            "diag" => DiagVm,
            _ => this,
        };
    }

    public void Dispose()
    {
        _disposed = true;
        _clock.Stop();
        Loc.LanguageChanged -= OnLanguageChanged;
        _hotkeys?.Dispose();
    }
}

public static class ImageHelpers
{
    public static System.Windows.Media.Imaging.BitmapImage BitmapFromPng(byte[] png)
    {
        var image = new System.Windows.Media.Imaging.BitmapImage();
        using var stream = new MemoryStream(png);
        image.BeginInit();
        image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
