using Parvathi.Core;
using Parvathi.Windows.Services;
using Xunit;

namespace Parvathi.Windows.Tests;

public sealed class ShortcutAndAutomationTests
{
    [Theory]
    [InlineData(0u)]
    [InlineData(8u)]
    [InlineData(0x4000u)]
    [InlineData(uint.MaxValue)]
    public void RejectsUnmodifiedReservedOrUnknownModifierMasks(uint modifiers) =>
        Assert.Throws<ArgumentException>(() => GlobalShortcuts.Validate(new ShortcutSettings(Modifiers: modifiers)));

    [Theory]
    [InlineData(0x1Bu)] // Escape must always remain available for stop.
    [InlineData(0x7Bu)] // F12 is reserved by Windows for debuggers.
    [InlineData(0x09u)] // Tab should not suppress ordinary field navigation.
    [InlineData(0x10u)] // A modifier cannot also be the activation key.
    public void RejectsUnsupportedActivationKeys(uint key) =>
        Assert.Throws<ArgumentException>(() => GlobalShortcuts.Validate(new ShortcutSettings(DictationKey: key)));

    [Fact]
    public void RejectsDuplicateModeKeys() => Assert.Throws<ArgumentException>(() =>
        GlobalShortcuts.Validate(new ShortcutSettings(DictationKey: 0x44, CommandKey: 0x44)));

    [Theory]
    [InlineData(3u, 0x31u, 0x32u, 0x33u)]
    [InlineData(3u, 0x20u, 0x43u, 0x41u)]
    [InlineData(6u, 0x44u, 0x43u, 0x41u)]
    [InlineData(5u, 0x70u, 0x71u, 0x7Au)]
    public void AcceptsDistinctDocumentedShortcutCombinations(uint modifiers, uint dictation, uint command, uint assistant) =>
        GlobalShortcuts.Validate(new ShortcutSettings(modifiers, dictation, command, assistant));

    [Fact]
    public async Task CancellationBeforeExecutionNeverDispatchesTyping()
    {
        var focus = new RecordingFocus();
        var automation = new SystemAutomation(focus);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            automation.ExecuteAsync(new TypeText("hello"), Snapshot, cancelled.Token));
        Assert.Equal(0, focus.CallCount);
    }

    [Fact]
    public async Task MissingDestinationNeverDispatchesTyping()
    {
        var focus = new RecordingFocus();
        await Assert.ThrowsAsync<PipelineException>(() => new SystemAutomation(focus)
            .ExecuteAsync(new TypeText("hello"), null, CancellationToken.None));
        Assert.Equal(0, focus.CallCount);
    }

    [Fact]
    public async Task InvalidToolArgumentRejectedBeforeAnyNativeCall()
    {
        var focus = new RecordingFocus();
        await Assert.ThrowsAsync<PipelineException>(() => new SystemAutomation(focus)
            .ExecuteAsync(new SetVolume(101), null, CancellationToken.None));
        await Assert.ThrowsAsync<PipelineException>(() => new SystemAutomation(focus)
            .ExecuteAsync(new OpenWebsite(new Uri("file:///C:/Windows/System32/cmd.exe")), null, CancellationToken.None));
        Assert.Equal(0, focus.CallCount);
    }

    [Fact]
    public async Task TypingCarriesOriginalDestinationAndCancellationToFocusAdapter()
    {
        var focus = new RecordingFocus();
        using var source = new CancellationTokenSource();
        var result = await new SystemAutomation(focus).ExecuteAsync(new TypeText("open Chrome"), Snapshot, source.Token);
        Assert.Equal(1, focus.CallCount);
        Assert.Equal("open Chrome", focus.InsertedText);
        Assert.Same(Snapshot, focus.Destination);
        Assert.Equal(source.Token, focus.Token);
        Assert.False(result.Verified); // Preserve a native adapter's uncertain outcome.
    }

    [Fact]
    public async Task CancellationFromNativeTypingIsNotConvertedToSuccessfulFeedback()
    {
        var focus = new RecordingFocus { CancelOnInsert = true };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new SystemAutomation(focus)
            .ExecuteAsync(new TypeText("hello"), Snapshot, CancellationToken.None));
        Assert.Equal(1, focus.CallCount);
    }

    [Theory]
    [InlineData("one\r\ntwo\rthree\nfour", "one\ntwo\nthree\nfour")]
    [InlineData("names: Zoë, 30%, ₹5,000", "names: Zoë, 30%, ₹5,000")]
    public void WindowsTextComparisonNormalizesOnlyLineEndings(string input, string expected) =>
        Assert.Equal(expected, FocusService.Normalize(input));

    [Fact]
    public void VirtualParagraphMarkerIsRemovedOnlyWhenNativeValueProvesIt()
    {
        var document = "alpha\n";
        var selection = "alpha\n";
        Assert.True(FocusService.TryAlignDocumentEnd(ref document, ref selection, 0, "alpha"));
        Assert.Equal("alpha", document);
        Assert.Equal("alpha", selection);
    }

    [Fact]
    public void ActualTrailingNewlineIsPreserved()
    {
        var document = "alpha\n";
        var selection = "alpha\n";
        Assert.True(FocusService.TryAlignDocumentEnd(ref document, ref selection, 0, "alpha\n"));
        Assert.Equal("alpha\n", document);
        Assert.Equal("alpha\n", selection);
    }

    [Fact]
    public void InconsistentNativeTextCannotBeTrimmedIntoAMatch()
    {
        var document = "alpha\n\n";
        var selection = "";
        Assert.False(FocusService.TryAlignDocumentEnd(ref document, ref selection, 0, "alpha"));
        Assert.Equal("alpha\n\n", document);
    }

    private static FocusSnapshot Snapshot { get; } = new(new nint(100), 123, "test editor", "1.2.3", "before", 6, 0, 1);

    private sealed class RecordingFocus : IFocusService
    {
        public int CallCount { get; private set; }
        public string? InsertedText { get; private set; }
        public FocusSnapshot? Destination { get; private set; }
        public CancellationToken Token { get; private set; }
        public bool CancelOnInsert { get; init; }
        public FocusSnapshot Capture() => throw new InvalidOperationException("Typing must retain its previously captured destination.");
        public string SelectedText(FocusSnapshot snapshot) => throw new NotSupportedException();
        public Task<ActionResult> InsertAsync(string text, FocusSnapshot snapshot, CancellationToken cancellationToken)
        {
            CallCount++;
            InsertedText = text;
            Destination = snapshot;
            Token = cancellationToken;
            if (CancelOnInsert) throw new OperationCanceledException();
            return Task.FromResult(new ActionResult("Unverified native response", false));
        }
    }
}
