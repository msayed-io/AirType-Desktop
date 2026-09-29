using System.Diagnostics;
using System.Windows.Input;
using LiveTypeBridge.App.Localization;
using LiveTypeBridge.App.Mvvm;
using LiveTypeBridge.App.Services;
using LiveTypeBridge.Core.Input;
using LiveTypeBridge.Core.Networking;
using LiveTypeBridge.Core.Settings;

namespace LiveTypeBridge.App.ViewModels;

/// <summary>Port, hotkeys, startup, language, logging and network details.</summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly AppServices _s;
    private readonly MainViewModel _main;

    private string _portText = "";
    private string _hkToggle = "";
    private string _hkQr = "";
    private string _hkEmergency = "";
    private string _statusText = "";
    private string _networkText = "";
    private bool _runAtStartup;
    private bool _enableLogging;
    private bool _logTextContent;

    public SettingsViewModel(AppServices services, MainViewModel main)
    {
        _s = services;
        _main = main;

        _portText = services.Settings.Port.ToString();
        _hkToggle = services.Settings.HotkeyToggleStream;
        _hkQr = services.Settings.HotkeyShowQr;
        _hkEmergency = services.Settings.HotkeyEmergencyStop;
        _runAtStartup = StartupHelper.IsEnabled();
        _enableLogging = services.Settings.EnableLogging;
        _logTextContent = services.Settings.LogTextContent;

        SaveCommand = new RelayCommand(_ => _ = SaveAsync());
        OpenLogsCommand = new RelayCommand(_ => OpenLogs());
        SetLangArCommand = new RelayCommand(_ => Loc.Set(Loc.Arabic));
        SetLangEnCommand = new RelayCommand(_ => Loc.Set(Loc.English));

        _networkText = BuildNetworkText();
        Loc.LanguageChanged += OnLanguageChanged;
    }

    public ICommand SaveCommand { get; }
    public ICommand OpenLogsCommand { get; }
    public ICommand SetLangArCommand { get; }
    public ICommand SetLangEnCommand { get; }

    public string PortText { get => _portText; set => Set(ref _portText, value); }
    public string HkToggle { get => _hkToggle; set => Set(ref _hkToggle, value); }
    public string HkQr { get => _hkQr; set => Set(ref _hkQr, value); }
    public string HkEmergency { get => _hkEmergency; set => Set(ref _hkEmergency, value); }

    public bool RunAtStartup { get => _runAtStartup; set => Set(ref _runAtStartup, value); }
    public bool EnableLogging { get => _enableLogging; set => Set(ref _enableLogging, value); }
    public bool LogTextContent { get => _logTextContent; set => Set(ref _logTextContent, value); }

    public bool LangIsAr => Loc.Current == Loc.Arabic;
    public bool LangIsEn => Loc.Current == Loc.English;

    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

    public string NetworkText { get => _networkText; private set => Set(ref _networkText, value); }

    private async Task SaveAsync()
    {
        if (!int.TryParse(PortText.Trim(), out var port) || port is < 1024 or > 65535)
        {
            StatusText = Loc.Get("SetPortInvalid");
            return;
        }

        if (HotkeyGesture.Parse(HkToggle) is null) { StatusText = Loc.Format("SetHotkeyBad", Loc.Get("SetHkToggle")); return; }
        if (HotkeyGesture.Parse(HkQr) is null) { StatusText = Loc.Format("SetHotkeyBad", Loc.Get("SetHkQr")); return; }
        if (HotkeyGesture.Parse(HkEmergency) is null) { StatusText = Loc.Format("SetHotkeyBad", Loc.Get("SetHkEmergency")); return; }

        var probe = new AppSettings
        {
            HotkeyToggleStream = HkToggle,
            HotkeyShowQr = HkQr,
            HotkeyEmergencyStop = HkEmergency,
        };
        if (AppSettings.ValidateHotkeys(probe).Count > 0)
        {
            StatusText = Loc.Get("SetHotkeyDup");
            return;
        }

        var portChanged = port != _s.Settings.Port && _s.Server.IsRunning;
        _s.Settings.Port = port;
        _s.Settings.HotkeyToggleStream = HkToggle;
        _s.Settings.HotkeyShowQr = HkQr;
        _s.Settings.HotkeyEmergencyStop = HkEmergency;
        _s.Settings.RunAtStartup = RunAtStartup;
        _s.Settings.EnableLogging = EnableLogging;
        _s.Settings.LogTextContent = LogTextContent;
        _s.Settings.Save();

        StartupHelper.Set(RunAtStartup);
        _s.Log.Enabled = EnableLogging;
        _main.ReRegisterHotkeys();

        if (portChanged)
        {
            await _s.Server.StopAsync();
            var started = await _s.Server.StartAsync(port, _s.Connections);
            StatusText = started
                ? Loc.Format("SetRestartDone", _s.Server.ActualPort)
                : Loc.Get("ErrPortRange");
            _s.Log.Info($"Server restarted on port {_s.Server.ActualPort}");
        }
        else
        {
            StatusText = Loc.Get("SetSaved");
        }

        NetworkText = BuildNetworkText();
    }

    private string BuildNetworkText()
    {
        var ips = LocalIpFinder.GetCandidates();
        var lines = ips.Count == 0
            ? "—"
            : string.Join("\n", ips.Select(c => $"{c.Address}  ({c.InterfaceName}{(c.IsPreferred ? " ✓" : "")})"));
        var port = _s.Server.IsRunning ? _s.Server.ActualPort : _s.Settings.Port;
        return $"{lines}\n{Loc.Get("DiagPort")}: {port}";
    }

    private void OpenLogs()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", _s.Log.Folder) { UseShellExecute = true });
        }
        catch { /* explorer blocked — ignore */ }
    }

    private void OnLanguageChanged()
    {
        Raise(nameof(LangIsAr));
        Raise(nameof(LangIsEn));
        Raise(string.Empty);
        NetworkText = BuildNetworkText();
    }
}
