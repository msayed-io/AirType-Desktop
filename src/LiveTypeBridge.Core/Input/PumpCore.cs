using System.Diagnostics;
using System.Threading.Channels;
using LiveTypeBridge.Core.Protocol;

namespace LiveTypeBridge.Core.Input;

public enum PumpMode
{
    Active,            // streaming into the focused field
    PausedProtect,     // paused because the user typed/moved locally (protect the text)
    PausedManual,      // paused by the user (button / hotkey)
    EmergencyStopped,  // emergency hotkey: nothing is injected at all
    Resync,            // revision gap: waiting for the phone to reset_stream
    Disposed,
}

public sealed record PumpStats(
    long OpsApplied, long OpsIgnored, long CharsTyped, long Backspaces,
    double LastInjectMs, double AvgInjectMs, DateTime? LastAppliedUtc);

/// <summary>Bridge between the pump and the rest of the app (acks, UI, phone).</summary>
public interface IPumpEvents
{
    /// <summary>Sends an ack/reset envelope to the phone. Awaited by the pump so frames stay ordered.</summary>
    Task SendToPhoneAsync(Envelope envelope);

    /// <summary>Revision gap detected: asks the phone to reset_stream and reconcile. Awaited so ordering is deterministic.</summary>
    Task OnResetNeededAsync(int appliedLength, int expectedNextRevision);

    /// <summary>Revision gap detected (fire-and-forget variant for the overflow path).</summary>
    void OnResetNeeded(int appliedLength, int expectedNextRevision);

    /// <summary>Pump mode changed (pause/resume/emergency/resync).</summary>
    void OnModeChanged(PumpMode mode);

    /// <summary>Statistics refresh after an applied op.</summary>
    void OnStats(PumpStats stats);

    /// <summary>Injection was refused (usually an elevated foreground app).</summary>
    void OnInjectorWarning(string code);
}

/// <summary>
/// Serializes incoming text_edit ops and injects them strictly in revision order.
/// All decisions (duplicate / old / gap / applied) come from <see cref="StreamState"/>;
/// on any gap the pump refuses to type and asks the phone for a reset instead of
/// guessing. Never holds a focus handle: every injection lands in whatever field is
/// focused at injection time.
/// </summary>
public sealed class PumpCore : IDisposable
{
    private const int BufferCapacity = 256; // ops buffered while paused

    private readonly ITextInjector _injector;
    private readonly IPumpEvents _events;
    private readonly StreamState _state = new();
    private readonly Channel<Envelope> _inbox;
    private readonly List<Envelope> _pausedBuffer = new();
    private readonly Task _loop;
    private readonly object _modeGate = new();

    private PumpMode _mode = PumpMode.Active;
    private long _opsApplied, _opsIgnored, _charsTyped, _backspaces;
    private double _lastInjectMs, _injectMsSum;
    private long _injectCount;
    private DateTime? _lastAppliedUtc;
    private volatile bool _disposed;

    public PumpCore(ITextInjector injector, IPumpEvents events)
    {
        _injector = injector;
        _events = events;
        _inbox = Channel.CreateBounded<Envelope>(new BoundedChannelOptions(BufferCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait, // TryWrite returns false when full -> overflow path
        });
        _loop = Task.Run(ProcessLoopAsync);
    }

    public PumpMode Mode => _mode;
    public int LastRevision => _state.LastRevision;
    public int AppliedLength => _state.AppliedLength;

    // ---- Public control surface -------------------------------------------

    public void HandleEdit(Envelope edit) => Offer(edit);

    public void HandleReset(Envelope reset)
    {
        // Routed through the inbox so all state mutations stay on the pump loop thread.
        Offer(reset);
    }

    public void PauseProtect()
    {
        lock (_modeGate) { if (_mode == PumpMode.Active) SetMode(PumpMode.PausedProtect); else if (_mode == PumpMode.Resync) SetMode(PumpMode.PausedProtect); }
    }

    public void PauseManual()
    {
        lock (_modeGate)
        {
            if (_mode != PumpMode.Disposed) SetMode(PumpMode.PausedManual);
        }
    }

    public void EmergencyStop()
    {
        lock (_modeGate)
        {
            if (_mode != PumpMode.Disposed) SetMode(PumpMode.EmergencyStopped);
        }
    }

    /// <summary>Resumes from any paused state; buffered ops are applied in order.</summary>
    public void Resume()
    {
        if (_disposed) return;
        Envelope[] replay;
        lock (_modeGate)
        {
            if (_mode is PumpMode.Active or PumpMode.Disposed) return;
            SetMode(PumpMode.Active);
            replay = _pausedBuffer.ToArray();
            _pausedBuffer.Clear();
        }
        foreach (var env in replay) Offer(env);
    }

    private void Offer(Envelope env)
    {
        if (_disposed) return;
        if (!_inbox.Writer.TryWrite(env))
        {
            // Channel full: the phone is far ahead of injection. Protect the text:
            // pause and ask for a resync rather than silently dropping ops.
            lock (_modeGate) SetMode(PumpMode.PausedProtect);
            _events.OnResetNeeded(_state.AppliedLength, _state.LastRevision + 1);
        }
    }

    private async Task ProcessLoopAsync()
    {
        var reader = _inbox.Reader;
        try
        {
            while (await reader.WaitToReadAsync().ConfigureAwait(false))
            {
                while (reader.TryRead(out var env))
                {
                    if (_disposed) return;
                    if (env.Type == Envelope.Types.ResetStream)
                        await HandleResetFromPhoneAsync(env);
                    else
                        await HandleEditInlineAsync(env);
                }
            }
        }
        catch (OperationCanceledException) { /* dispose */ }
    }

    private async Task HandleResetFromPhoneAsync(Envelope env)
    {
        _state.ResetTo(env.BaselineRevision ?? _state.LastRevision, env.AppliedLength ?? _state.AppliedLength);
        lock (_modeGate)
        {
            _pausedBuffer.Clear();
            if (_mode == PumpMode.Resync) SetMode(PumpMode.Active);
        }
        await _events.SendToPhoneAsync(new Envelope(
            Type: Envelope.Types.Ack,
            Applied: true,
            BaselineRevision: _state.LastRevision,
            AppliedLength: _state.AppliedLength,
            ReceivedAtUtc: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Reason: "reset"));
    }

    private async Task HandleEditInlineAsync(Envelope env)
    {
        PumpMode mode;
        lock (_modeGate) mode = _mode;

        if (mode == PumpMode.Disposed) return;

        if (mode is PumpMode.PausedProtect or PumpMode.PausedManual)
        {
            lock (_modeGate)
            {
                if (_pausedBuffer.Count < BufferCapacity) _pausedBuffer.Add(env);
                else _opsIgnored++;
            }
            await AckAsync(env, applied: false, Envelope.Reasons.Paused);
            return;
        }
        if (mode == PumpMode.EmergencyStopped)
        {
            _opsIgnored++;
            await AckAsync(env, applied: false, Envelope.Reasons.Paused);
            return;
        }
        if (mode == PumpMode.Resync)
        {
            _opsIgnored++;
            await AckAsync(env, applied: false, Envelope.Reasons.Resync);
            return;
        }

        var revision = env.Revision ?? -1;
        var outcome = _state.Apply(revision, env.DeleteCount ?? 0, env.InsertText);

        switch (outcome.Decision)
        {
            case EditDecision.Duplicate:
            case EditDecision.Old:
            case EditDecision.Invalid:
                _opsIgnored++;
                await AckAsync(env, applied: false,
                    outcome.Decision == EditDecision.Invalid ? "invalid" :
                    outcome.Decision == EditDecision.Duplicate ? Envelope.Reasons.Duplicate : Envelope.Reasons.Old);
                return;

            case EditDecision.Gap:
                lock (_modeGate) SetMode(PumpMode.Resync);
                await _events.OnResetNeededAsync(_state.AppliedLength, outcome.ExpectedNextRevision ?? (_state.LastRevision + 1));
                await AckAsync(env, applied: false, Envelope.Reasons.Resync);
                return;

            case EditDecision.Applied:
            default:
                var sw = Stopwatch.StartNew();
                var ok = _injector.Inject(outcome.Backspaces, outcome.InsertText, out var warning);
                sw.Stop();
                _lastInjectMs = sw.Elapsed.TotalMilliseconds;
                _injectMsSum += _lastInjectMs;
                _injectCount++;

                if (!ok)
                {
                    // Do not keep hammering a window that refuses input (UIPI): stop and warn.
                    lock (_modeGate) SetMode(PumpMode.PausedProtect);
                    _events.OnInjectorWarning(warning ?? "inject-failed");
                    await AckAsync(env, applied: false, "inject-refused");
                    return;
                }

                _opsApplied++;
                _charsTyped += outcome.InsertText.Length;
                _backspaces += outcome.Backspaces;
                _lastAppliedUtc = DateTime.UtcNow;
                _events.OnStats(new PumpStats(
                    _opsApplied, _opsIgnored, _charsTyped, _backspaces,
                    _lastInjectMs, _injectCount == 0 ? 0 : _injectMsSum / _injectCount, _lastAppliedUtc));

                await AckAsync(env, applied: true, null, outcome.Backspaces, outcome.InsertText.Length);
                return;
        }
    }

    private Task AckAsync(Envelope src, bool applied, string? reason, int backspaces = 0, int inserts = 0)
        => _events.SendToPhoneAsync(new Envelope(
            Type: Envelope.Types.Ack,
            Revision: src.Revision,
            Applied: applied,
            Reason: reason,
            Backspaces: backspaces,
            Inserts: inserts,
            ReceivedAtUtc: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

    private void SetMode(PumpMode mode)
    {
        // Caller must hold _modeGate.
        if (_mode == mode) return;
        _mode = mode;
        _events.OnModeChanged(mode);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_modeGate) _mode = PumpMode.Disposed;
        _inbox.Writer.TryComplete();
        try { _loop.Wait(TimeSpan.FromSeconds(2)); } catch { /* loop is exiting */ }
    }
}
