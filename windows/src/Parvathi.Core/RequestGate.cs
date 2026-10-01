namespace Parvathi.Core;

/// <summary>Owns request generations and permits at most one action per generation.</summary>
public sealed class RequestGate
{
    private readonly object sync = new();
    private long sequence;
    private long? current;
    private bool claimed;

    public long Begin() { lock (sync) { current = ++sequence; claimed = false; return current.Value; } }
    public void Cancel() { lock (sync) { current = null; claimed = false; } }
    public bool IsCurrent(long id) { lock (sync) return current == id; }
    public void Check(long id, CancellationToken cancellationToken = default)
    {
        lock (sync) CheckLocked(id, cancellationToken);
    }
    public void ClaimAction(long id, CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            CheckLocked(id, cancellationToken);
            if (claimed) throw new PipelineException("This request has already been executed. Record a new command to run it again.");
            claimed = true;
        }
    }
    public void Finish(long id)
    {
        lock (sync) { if (current != id) return; current = null; claimed = false; }
    }
    private void CheckLocked(long id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (current != id) throw new OperationCanceledException("This request was stopped or replaced.", cancellationToken);
    }
}

public static class FocusGuard
{
    public static void Validate(FocusSnapshot expected, FocusSnapshot current, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (expected.WindowHandle == 0 || expected.ProcessId <= 0 || string.IsNullOrWhiteSpace(expected.ElementId)
            || expected.SelectionStart < 0 || expected.SelectionLength < 0 || expected.SelectionStart > expected.Text.Length
            || expected.SelectionLength > expected.Text.Length - expected.SelectionStart)
            throw new PipelineException("The destination cannot be verified. Focus a supported editable field and record again.");
        if (expected.WindowHandle != current.WindowHandle || expected.ProcessId != current.ProcessId
            || expected.ElementId != current.ElementId || expected.Text != current.Text
            || expected.SelectionStart != current.SelectionStart || expected.SelectionLength != current.SelectionLength
            || expected.Revision != current.Revision)
            throw new PipelineException("The destination, text, or selection changed while you were recording. Nothing was inserted. Focus the intended field and record again.");
    }
}

public static class ClipboardRestorationPolicy
{
    // A sequence number of zero means Windows could not provide ownership evidence.
    // If another app/user writes the clipboard, preserve that newer content.
    public static bool ShouldRestore(uint ownedSequence, uint currentSequence)
        => ownedSequence != 0 && ownedSequence == currentSequence;
}
