using Parvathi.Core;
using Xunit;

namespace Parvathi.Core.Tests;

public sealed class RequestSafetyTests
{
    [Fact]
    public void CancellationPreventsSideEffectClaim()
    {
        var gate = new RequestGate(); var id = gate.Begin(); gate.Cancel();
        Assert.Throws<OperationCanceledException>(() => gate.ClaimAction(id));
    }

    [Fact]
    public void CancelledTokenPreventsSideEffectClaim()
    {
        var gate = new RequestGate(); var id = gate.Begin();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => gate.ClaimAction(id, cancellation.Token));
    }

    [Fact]
    public void LateResponseAndLateFinishDoNotAffectNewRequest()
    {
        var gate = new RequestGate(); var old = gate.Begin(); var next = gate.Begin();
        gate.Finish(old);
        Assert.Throws<OperationCanceledException>(() => gate.Check(old));
        Assert.True(gate.IsCurrent(next));
        gate.ClaimAction(next);
        Assert.Throws<PipelineException>(() => gate.ClaimAction(next));
    }

    [Fact]
    public async Task RacingRetriesClaimOnlyOneAction()
    {
        var gate = new RequestGate(); var id = gate.Begin(); var claims = 0;
        await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => Task.Run(() =>
        {
            try { gate.ClaimAction(id); Interlocked.Increment(ref claims); }
            catch (PipelineException) { }
        })));
        Assert.Equal(1, claims);
    }

    private static FocusSnapshot Snapshot => new(12, 123, "Notepad", "edit.1", "Original text", 2, 4, 7);
    public static IEnumerable<object[]> ChangedDestinations()
    {
        yield return [Snapshot with { WindowHandle = 13 }];
        yield return [Snapshot with { ProcessId = 124 }];
        yield return [Snapshot with { ElementId = "edit.2" }];
        yield return [Snapshot with { Text = "Changed text" }];
        yield return [Snapshot with { SelectionStart = 3 }];
        yield return [Snapshot with { SelectionLength = 0 }];
        yield return [Snapshot with { Revision = 8 }];
    }

    [Theory]
    [MemberData(nameof(ChangedDestinations))]
    public void ChangesToDestinationAreRejected(FocusSnapshot current)
        => Assert.Throws<PipelineException>(() => FocusGuard.Validate(Snapshot, current));

    [Fact]
    public void UnchangedDestinationPassesAndUnknownDestinationDoesNot()
    {
        FocusGuard.Validate(Snapshot, Snapshot);
        Assert.Throws<PipelineException>(() => FocusGuard.Validate(Snapshot with { ElementId = "" }, Snapshot));
        Assert.Throws<PipelineException>(() => FocusGuard.Validate(Snapshot with { SelectionStart = int.MaxValue }, Snapshot));
    }

    [Fact]
    public void FocusVerificationObservesCancellation()
        => Assert.Throws<OperationCanceledException>(() => FocusGuard.Validate(Snapshot, Snapshot, new CancellationToken(true)));

    [Theory]
    [InlineData(42u, 42u, true)]
    [InlineData(42u, 43u, false)]
    [InlineData(0u, 0u, false)]
    public void ClipboardRestorationOnlyOwnsUnchangedClipboard(uint written, uint current, bool expected)
        => Assert.Equal(expected, ClipboardRestorationPolicy.ShouldRestore(written, current));
}
