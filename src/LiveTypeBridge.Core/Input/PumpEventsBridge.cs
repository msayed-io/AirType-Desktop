using LiveTypeBridge.Core.Networking;
using LiveTypeBridge.Core.Pairing;
using LiveTypeBridge.Core.Protocol;

namespace LiveTypeBridge.Core.Input;

/// <summary>
/// Adapts pump callbacks to the app: acks travel over the bound socket (ordered),
/// UI notifications travel to whoever subscribed (the WPF layer marshals to its thread).
/// </summary>
public sealed class PumpEventsBridge : IPumpEvents
{
    private readonly PairingSession _session;
    private readonly PhoneConnectionManager _manager;
    private readonly Action<PumpMode>? _modeChanged;
    private readonly Action<PumpStats>? _statsUpdated;
    private readonly Action<string>? _injectorWarning;
    private readonly Action<int, int>? _resetNeeded;

    public PumpEventsBridge(
        PairingSession session,
        PhoneConnectionManager manager,
        Action<PumpMode>? modeChanged = null,
        Action<PumpStats>? statsUpdated = null,
        Action<string>? injectorWarning = null,
        Action<int, int>? resetNeeded = null)
    {
        _session = session;
        _manager = manager;
        _modeChanged = modeChanged;
        _statsUpdated = statsUpdated;
        _injectorWarning = injectorWarning;
        _resetNeeded = resetNeeded;
    }

    public Task SendToPhoneAsync(Envelope envelope) => _manager.SendToSessionAsync(_session, envelope);

    public void OnResetNeeded(int appliedLength, int expectedNextRevision)
        => _ = OnResetNeededAsync(appliedLength, expectedNextRevision);

    public async Task OnResetNeededAsync(int appliedLength, int expectedNextRevision)
    {
        _resetNeeded?.Invoke(appliedLength, expectedNextRevision);
        // Tell the phone exactly where our mirror stands so it can reconcile safely.
        await SendToPhoneAsync(new Envelope(
            Type: Envelope.Types.ResetStream,
            BaselineRevision: expectedNextRevision - 1,
            AppliedLength: appliedLength,
            Reason: Envelope.Reasons.LostSync));
    }

    public void OnModeChanged(PumpMode mode) => _modeChanged?.Invoke(mode);
    public void OnStats(PumpStats stats) => _statsUpdated?.Invoke(stats);
    public void OnInjectorWarning(string code) => _injectorWarning?.Invoke(code);
}
