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
    private const int MaxLogicalInputsPerOp = 8000;
    private const int MaxNativeInputsPerOp = MaxLogicalInputsPerOp * 2;

    public static NativeInput.INPUT[] Build(int backspaceCount, string text)
    {
        var clean = StreamState.Sanitize(text);
        var logicalCount = Math.Clamp(backspaceCount, 0, 2000) + clean.Length;
        if (logicalCount == 0) return Array.Empty<NativeInput.INPUT>();

        // Every keyboard action must have a matching key-up event. Leaving synthetic
        // keys down makes Windows/app controls observe a stuck key and can trigger
        // repeat/composition corruption. Keep the existing native-event safety cap.
        var plan = new List<NativeInput.INPUT>(Math.Min(logicalCount * 2, MaxNativeInputsPerOp));
        for (var i = 0; i < Math.Clamp(backspaceCount, 0, 2000) && plan.Count + 2 <= MaxNativeInputsPerOp; i++)
        {
            plan.Add(NativeInput.Backspace());
            plan.Add(NativeInput.Backspace(keyUp: true));
        }

        foreach (var c in clean)
        {
            if (plan.Count + 2 > MaxNativeInputsPerOp) break;
            switch (c)
            {
                case '\n':
                    plan.Add(NativeInput.VirtualKey(NativeInput.VK_RETURN));
                    plan.Add(NativeInput.VirtualKey(NativeInput.VK_RETURN, keyUp: true));
                    break;
                case '\t':
                    plan.Add(NativeInput.VirtualKey(NativeInput.VK_TAB));
                    plan.Add(NativeInput.VirtualKey(NativeInput.VK_TAB, keyUp: true));
                    break;
                default:
                    plan.Add(NativeInput.UnicodeChar(c));
                    plan.Add(NativeInput.UnicodeChar(c, keyUp: true));
                    break;
            }
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
