using System.Windows.Input;
using LiveTypeBridge.App.Localization;
using LiveTypeBridge.App.Mvvm;
using LiveTypeBridge.App.Services;
using LiveTypeBridge.Core.Diagnostics;
using LiveTypeBridge.Core.Networking;

namespace LiveTypeBridge.App.ViewModels;

/// <summary>IP, port, server state, last error, firewall state and the local self-test.</summary>
public sealed class DiagnosticsViewModel : ObservableObject
{
    private readonly AppServices _s;
    private readonly MainViewModel _main;
    private FirewallStatus _fw = FirewallStatus.Unknown;
    private DateTime _fwCheckedUtc = DateTime.MinValue;
    private string _selfTestOutput = "";
    private bool _isTesting;

    public DiagnosticsViewModel(AppServices services, MainViewModel main)
    {
        _s = services;
        _main = main;
        RefreshCommand = new RelayCommand(_ => _ = RefreshAsync());
        RunSelfTestCommand = new RelayCommand(_ => _ = RunSelfTestAsync(), _ => !IsTesting);
        Loc.LanguageChanged += () => Raise(string.Empty);
        _ = RefreshAsync();
    }

    public ICommand RefreshCommand { get; }
    public ICommand RunSelfTestCommand { get; }

    public string IpText { get; private set; } = "";
    public string PortText { get; private set; } = "";
    public string WsText { get; private set; } = "";
    public string UptimeText { get; private set; } = "";
    public string FirewallText { get; private set; } = "";

    public string LastErrorText =>
        string.IsNullOrWhiteSpace(_main.LastErrorText) ? Loc.Get("DiagNoErr") : _main.LastErrorText;

    public bool IsTesting
    {
        get => _isTesting;
        private set => Set(ref _isTesting, value);
    }

    public string SelfTestOutput
    {
        get => _selfTestOutput;
        private set => Set(ref _selfTestOutput, value);
    }

    public void NotifyError() => Raise(nameof(LastErrorText));

    public async Task RefreshAsync()
    {
        var ips = LocalIpFinder.GetCandidates();
        IpText = ips.Count == 0
            ? "—"
            : string.Join("\n", ips.Select(c => $"{c.Address}  ({c.InterfaceName})"));

        PortText = _s.Server.IsRunning
            ? $"{_s.Server.ActualPort} ({_s.Settings.Port})"
            : _s.Settings.Port.ToString();

        WsText = _s.Server.IsRunning ? Loc.Get("WsRunning") : Loc.Get("WsStopped");
        UptimeText = _s.Server.IsRunning ? _s.Server.Uptime.ToString(@"hh\:mm\:ss") : "—";

        if ((DateTime.UtcNow - _fwCheckedUtc).TotalSeconds > 30)
        {
            _fw = await Task.Run(FirewallChecker.Query);
            _fwCheckedUtc = DateTime.UtcNow;
        }
        FirewallText = _fw switch
        {
            FirewallStatus.Allowed => Loc.Get("FwAllowed"),
            FirewallStatus.NoRule => Loc.Get("FwNoRule"),
            _ => Loc.Get("FwUnknown"),
        };

        Raise(string.Empty);
    }

    private async Task RunSelfTestAsync()
    {
        if (IsTesting) return;
        IsTesting = true;
        SelfTestOutput = Loc.Get("DiagRunning");
        try
        {
            var result = await _s.SelfTest.RunAsync();
            SelfTestOutput = string.Join(Environment.NewLine, result.Lines);
            _s.Log.Info($"Self test finished: {(result.Passed ? "PASS" : "FAIL")}");
        }
        catch (Exception ex)
        {
            SelfTestOutput = $"FAIL  self test crashed — {ex.Message}";
            _s.Log.Error($"Self test crashed: {ex}");
        }
        finally
        {
            IsTesting = false;
            await RefreshAsync();
        }
    }
}
