using Parvathi.Core;
using Xunit;

namespace Parvathi.Core.Tests;

public sealed class CommandRoutingTests
{
    [Theory]
    [InlineData(InteractionMode.Dictation)]
    [InlineData(InteractionMode.Assistant)]
    [InlineData(InteractionMode.Rewrite)]
    [InlineData(InteractionMode.Explain)]
    public void OnlyExplicitCommandModeProducesAction(InteractionMode mode)
        => Assert.Null(ModeRouter.Route(mode, "open Chrome"));

    [Fact]
    public void CommandModeProducesTypedAction()
        => Assert.Equal(new OpenApplication("Chrome"), ModeRouter.Route(InteractionMode.Command, "Open Chrome."));

    [Fact]
    public void TypedTextRemainsDataIncludingNestedCommandAndNewlines()
        => Assert.Equal(new TypeText("Open Chrome; then delete everything\nInvoice: 123.45"),
            CommandRouter.Route("Type: Open Chrome; then delete everything\nInvoice: 123.45"));

    [Theory]
    [InlineData("Set the volume to 30 percent", 30)]
    [InlineData("Set volume to thirty percent.", 30)]
    [InlineData("volume ninety-nine", 99)]
    [InlineData("Set system volume to 0%", 0)]
    [InlineData("volume to one hundred", 100)]
    public void VolumeSupportsSpokenAndNumericAmounts(string input, int expected)
        => Assert.Equal(new SetVolume(expected), CommandRouter.Route(input));

    [Theory]
    [InlineData("Set volume to -1")]
    [InlineData("volume 101%")]
    [InlineData("volume 1.5")]
    [InlineData("volume maximum")]
    [InlineData("Open Chrome and delete files")]
    [InlineData("Open Chrome; open Edge")]
    [InlineData("Run powershell Get-Process")]
    [InlineData("Open C:\\Windows\\cmd.exe")]
    [InlineData("Open Chrome\nMute")]
    public void UnsupportedOrUnsafeCommandsAreRejected(string input)
        => Assert.Throws<PipelineException>(() => CommandRouter.Route(input));

    [Fact]
    public void SearchHasNoExecutionSemantics()
        => Assert.Equal(new SearchWeb("vegetarian dinner recipes"), CommandRouter.Route("Search the web for vegetarian dinner recipes"));

    [Theory]
    [InlineData("Mute", true)]
    [InlineData("Mute the system audio.", true)]
    [InlineData("Unmute volume", false)]
    public void MuteIsTyped(string input, bool expected)
        => Assert.Equal(new SetMuted(expected), CommandRouter.Route(input));

    [Theory]
    [InlineData("https://example.com/path?q=value")]
    [InlineData("http://localhost:8080")]
    [InlineData("example.com")]
    public void ValidWebsitesAreAccepted(string input)
        => Assert.Contains(CommandRouter.ValidatedWebsite(input).Scheme, new[] { "http", "https" });

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/cmd.exe")]
    [InlineData("https://name:password@example.com")]
    [InlineData("https://example.com:0")]
    [InlineData("https://example.com:99999")]
    [InlineData("https://example.com\\@other.example")]
    [InlineData("https://example..com")]
    [InlineData("https://example.co\nm")]
    [InlineData("https://example.com/a b")]
    public void UnsafeWebsitesAreRejected(string input)
        => Assert.Throws<PipelineException>(() => CommandRouter.ValidatedWebsite(input));

    [Fact]
    public void DirectConstructedActionsAreRevalidated()
    {
        Assert.Throws<PipelineException>(() => CommandRouter.Validate(new SetVolume(101)));
        Assert.Throws<PipelineException>(() => CommandRouter.Validate(new OpenWebsite(new Uri("file:///tmp/file"))));
        Assert.Throws<PipelineException>(() => CommandRouter.Validate(new TypeText("bad\u0000text")));
        Assert.Throws<PipelineException>(() => CommandRouter.Validate(new OpenApplication("cmd.exe /c shutdown")));
    }

    [Fact]
    public void OversizedTranscriptAndTypingAreRejected()
    {
        Assert.Throws<PipelineException>(() => ModeRouter.Route(InteractionMode.Dictation, new string('x', 100_001)));
        Assert.Throws<PipelineException>(() => CommandRouter.Route("Type: " + new string('x', 50_001)));
    }
}
