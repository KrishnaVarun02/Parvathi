namespace Parvathi.Core;

public enum InteractionMode { Dictation, Command, Assistant, Rewrite, Explain }
public enum AssistantPhase { Idle, Listening, Transcribing, Thinking, Executing, Speaking }
public enum TranscriptionStyle { Verbatim, Polished }

public abstract record CommandAction;
public sealed record OpenApplication(string Query) : CommandAction;
public sealed record OpenWebsite(Uri Url) : CommandAction;
public sealed record SearchWeb(string Query) : CommandAction;
public sealed record SetVolume(int Percent) : CommandAction;
public sealed record SetMuted(bool Muted) : CommandAction;
public sealed record TypeText(string Text) : CommandAction;

public sealed class PipelineException(string message) : Exception(message);
public sealed record AppDescriptor(string Id, string Name, string LaunchTarget, bool IsPackaged = false);
public sealed record ActionResult(string Message, bool Verified);
public sealed record FocusSnapshot(nint WindowHandle, int ProcessId, string AppName,
    string ElementId, string Text, int SelectionStart, int SelectionLength, long Revision,
    object? NativeState = null);
public sealed record RecognitionOptions(int DeviceNumber, string ModelPath);
public sealed record ConversationTurn(string Role, string Content);
public sealed record AIConfiguration(string Provider, string BaseUrl, string Model, string? ApiKey = null);

public interface IFocusService
{
    FocusSnapshot Capture();
    string SelectedText(FocusSnapshot snapshot);
    Task<ActionResult> InsertAsync(string text, FocusSnapshot snapshot, CancellationToken cancellationToken);
}

public interface ISystemAutomation
{
    IReadOnlyList<AppDescriptor> DiscoverApplications();
    Task<ActionResult> ExecuteAsync(CommandAction action, FocusSnapshot? snapshot, CancellationToken cancellationToken);
}

public interface IRecognitionService
{
    event Action<string>? Partial;
    event Action<double>? Level;
    event Action<Exception>? Failed;
    bool IsRecording { get; }
    Task StartAsync(RecognitionOptions options, CancellationToken cancellationToken);
    Task<string> StopAsync(CancellationToken cancellationToken);
    void Cancel();
}

public interface ISpeechOutput
{
    Task SpeakAsync(string text, string? voice, CancellationToken cancellationToken);
    void Stop();
    IReadOnlyList<string> Voices { get; }
}

public interface IAIProvider
{
    Task<string> RespondAsync(string prompt, IReadOnlyList<ConversationTurn> context, CancellationToken cancellationToken);
    Task<string> TransformAsync(string text, string instruction, CancellationToken cancellationToken);
}

public sealed record CommandExample(string Title, string Phrase, string Details);
public static class CommandCatalog
{
    public static IReadOnlyList<CommandExample> Examples { get; } = new[]
    {
        new CommandExample("Open an application", "Open Chrome", "Opens or focuses an installed application. Ambiguous names require clarification."),
        new CommandExample("Switch applications", "Switch to Visual Studio Code", "Focuses the named application, launching it when needed."),
        new CommandExample("Open a website", "Open website https://example.com", "Opens an HTTP or HTTPS website in your default browser."),
        new CommandExample("Search the web", "Search the web for vegetarian dinner recipes", "Opens browser search results. The assistant does not read the results."),
        new CommandExample("Set volume", "Set the volume to 30 percent", "Sets system output volume from zero to one hundred percent."),
        new CommandExample("Mute sound", "Mute", "Mutes system sound. Say unmute to restore sound."),
        new CommandExample("Type supplied text", "Type: I’ll join the meeting in five minutes.", "Types into the previously focused field after checking the destination.")
    };
}
