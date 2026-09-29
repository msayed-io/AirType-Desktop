using System.Text;

namespace LiveTypeBridge.Core.Protocol;

public enum EditDecision
{
    Applied,
    Duplicate,
    Old,
    Gap,
    Invalid,
}

/// <summary>Result of trying to apply one text_edit operation.</summary>
/// <param name="Decision">What the engine decided.</param>
/// <param name="Backspaces">Backspace presses to emit (only when Applied).</param>
/// <param name="InsertText">Sanitized text to inject (only when Applied).</param>
/// <param name="AppliedRevision">Revision this state is now at.</param>
/// <param name="ExpectedNextRevision">When Gap: the revision the phone must resend from.</param>
public sealed record EditOutcome(
    EditDecision Decision,
    int Backspaces,
    string InsertText,
    int AppliedRevision,
    int? ExpectedNextRevision);

/// <summary>
/// Server-side mirror of the streamed text. It tracks only what THIS program typed
/// (never a window handle, never UI state). Revisions must arrive strictly in order
/// (1, 2, 3, ...); anything out of order forces a reset instead of risking wrong text.
/// </summary>
public sealed class StreamState
{
    private int _lastRevision;          // 0 = nothing applied yet; phone revisions start at 1
    private int _appliedLength;         // UTF-16 code units currently typed by the stream

    public int LastRevision => Volatile.Read(ref _lastRevision);
    public int AppliedLength => Volatile.Read(ref _appliedLength);

    public EditOutcome Apply(int revision, int deleteCount, string? insertText)
    {
        if (revision <= 0 || deleteCount < 0)
            return new EditOutcome(EditDecision.Invalid, 0, "", _lastRevision, null);

        if (revision == _lastRevision)
            return new EditOutcome(EditDecision.Duplicate, 0, "", _lastRevision, null);

        if (revision < _lastRevision)
            return new EditOutcome(EditDecision.Old, 0, "", _lastRevision, null);

        if (revision > _lastRevision + 1)
            return new EditOutcome(EditDecision.Gap, 0, "", _lastRevision, _lastRevision + 1);

        var text = Sanitize(insertText);
        var deletes = Math.Min(deleteCount, _appliedLength); // cannot delete more than we typed

        _lastRevision = revision;
        _appliedLength = _appliedLength - deletes + text.Length;

        return new EditOutcome(EditDecision.Applied, deletes, text, revision, null);
    }

    /// <summary>
    /// Adopt a phone-side reset: the phone restarts its op stream at
    /// <paramref name="baselineRevision"/> and takes responsibility for reconciling the
    /// already-typed text (it was told our appliedLength in the reset request).
    /// </summary>
    public void ResetTo(int baselineRevision, int appliedLength)
    {
        _lastRevision = Math.Max(0, baselineRevision);
        _appliedLength = Math.Max(0, appliedLength);
    }

    /// <summary>Normalize text for injection: \r\n -> \n, strip control chars except \n and \t.</summary>
    public static string Sanitize(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c == '\r') continue;
            if (c == '\n' || c == '\t') { sb.Append(c); continue; }
            if (char.IsControl(c)) continue;
            sb.Append(c);
        }
        return sb.ToString();
    }
}
