using LiveTypeBridge.Core.Protocol;

namespace LiveTypeBridge.Core.Input;

/// <summary>Anything that can materialize a stream operation as real keyboard input.</summary>
public interface ITextInjector
{
    /// <summary>Injects backspaces then text. Returns false when Windows refused the input.</summary>
    /// <param name="warningCode">When injection fails: machine-readable hint (e.g. target-may-be-elevated).</param>
    bool Inject(int backspaceCount, string text, out string? warningCode);
}

/// <summary>
/// Turns one applied edit into a concrete SendInput plan. Pure logic — unit tested
/// without touching the real input system. UTF-16 code units map 1:1 to
/// KEYEVENTF_UNICODE events (an emoji = surrogate pair = 2 events).
/// </summary>
public static class InputPlanBuilder
{
    private const int MaxInputsPerOp = 8000;

    public static NativeInput.INPUT[] Build(int backspaceCount, string text)
    {
        var clean = StreamState.Sanitize(text);
        var total = Math.Clamp(backspaceCount, 0, 2000) + clean.Length;
        if (total == 0) return Array.Empty<NativeInput.INPUT>();
        if (total > MaxInputsPerOp) total = MaxInputsPerOp;

        var plan = new List<NativeInput.INPUT>(total);
        for (var i = 0; i < Math.Clamp(backspaceCount, 0, 2000) && plan.Count < MaxInputsPerOp; i++)
            plan.Add(NativeInput.Backspace());

        foreach (var c in clean)
        {
            if (plan.Count >= MaxInputsPerOp) break;
            plan.Add(c switch
            {
                '\n' => NativeInput.VirtualKey(NativeInput.VK_RETURN),
                '\t' => NativeInput.VirtualKey(NativeInput.VK_TAB),
                _ => NativeInput.UnicodeChar(c),
            });
        }
        return plan.ToArray();
    }
}

/// <summary>Real injector: types into whatever control currently owns keyboard focus.</summary>
public sealed class SendInputInjector : ITextInjector
{
    public bool Inject(int backspaceCount, string text, out string? warningCode)
    {
        warningCode = null;
        var plan = InputPlanBuilder.Build(backspaceCount, text);
        if (plan.Length == 0) return true;

        var accepted = NativeInput.SendBulk(plan);
        if (accepted >= plan.Length) return true;

        // SendInput silently drops input when the foreground app runs at a higher
        // integrity level (UIPI). We never try to bypass it — we surface it instead.
        warningCode = "target-may-be-elevated";
        return false;
    }
}
