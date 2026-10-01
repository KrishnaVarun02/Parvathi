using System.Speech.Synthesis;
using Parvathi.Core;

namespace Parvathi.Windows.Services;

/// <summary>Installed Windows SAPI voices; synthesis does not send text to a cloud service.</summary>
public sealed class SpeechOutput : ISpeechOutput, IDisposable
{
    private readonly object gate = new();
    private readonly SemaphoreSlim serial = new(1, 1);
    private SpeechSynthesizer? synthesizer;
    private string? defaultVoice;
    private TaskCompletionSource? pending;
    private Prompt? activePrompt;
    private long generation;
    private bool disposed;

    public IReadOnlyList<string> Voices
    {
        get
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                try { return GetSynthesizer().GetInstalledVoices().Where(v => v.Enabled).Select(v => v.VoiceInfo.Name).ToArray(); }
                catch (Exception ex) { throw new PipelineException("Windows speech voices are unavailable. Add a voice in Windows Settings > Time & language > Speech. " + ex.Message); }
            }
        }
    }

    public async Task SpeakAsync(string text, string? voice, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        long expected;
        lock (gate) { ObjectDisposedException.ThrowIf(disposed, this); expected = generation; }
        await serial.WaitAsync(cancellationToken).ConfigureAwait(false);
        TaskCompletionSource? completion = null;
        SpeechSynthesizer? engine = null;
        EventHandler<SpeakCompletedEventArgs>? handler = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            lock (gate)
            {
                timeout.Token.ThrowIfCancellationRequested();
                if (disposed || expected != generation) throw new OperationCanceledException(cancellationToken);
                engine = GetSynthesizer();
                if (string.IsNullOrWhiteSpace(voice)) voice = defaultVoice;
                if (!string.IsNullOrWhiteSpace(voice))
                {
                    if (!engine.GetInstalledVoices().Any(v => v.Enabled && v.VoiceInfo.Name == voice))
                        throw new PipelineException("The selected Windows voice is no longer installed. Choose another voice in Settings.");
                    engine.SelectVoice(voice);
                }
                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var ownCompletion = completion;
                var ownPrompt = new Prompt(text.Length <= 1200 ? text : text[..1200]);
                handler = (_, args) =>
                {
                    if (!ReferenceEquals(args.Prompt, ownPrompt)) return;
                    if (args.Error is not null) ownCompletion.TrySetException(args.Error);
                    else if (args.Cancelled) ownCompletion.TrySetCanceled();
                    else ownCompletion.TrySetResult();
                };
                engine.SpeakCompleted += handler;
                pending = completion;
                // Short speech is an intentional product constraint; full response stays visible.
                activePrompt = ownPrompt;
                engine.SpeakAsync(ownPrompt);
            }
            using var registration = timeout.Token.Register(() => CancelPending(completion));
            try { await completion.Task.WaitAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
            { throw new PipelineException("Speech stopped after its 60-second limit. The response is still available as text."); }
        }
        catch (OperationCanceledException) { throw; }
        catch (PipelineException) { throw; }
        catch (Exception ex) { throw new PipelineException("Windows could not speak the response. Check your output device and installed speech voices. " + ex.Message); }
        finally
        {
            lock (gate)
            {
                if (engine is not null && handler is not null) engine.SpeakCompleted -= handler;
                if (ReferenceEquals(pending, completion)) { pending = null; activePrompt = null; }
            }
            serial.Release();
        }
    }

    public void Stop()
    {
        lock (gate)
        {
            generation++;
            if (pending is not null) CancelPending(pending);
        }
    }
    private void CancelPending(TaskCompletionSource? expected)
    {
        lock (gate)
        {
            if (expected is null || !ReferenceEquals(pending, expected)) return;
            expected.TrySetCanceled();
            if (activePrompt is not null)
            {
                try { synthesizer?.SpeakAsyncCancel(activePrompt); }
                catch (InvalidOperationException) { /* The prompt may have just finished. */ }
            }
        }
    }
    private SpeechSynthesizer GetSynthesizer()
    {
        if (synthesizer is not null) return synthesizer;
        var created = new SpeechSynthesizer();
        try { created.SetOutputToDefaultAudioDevice(); defaultVoice = created.Voice.Name; synthesizer = created; return created; }
        catch { created.Dispose(); throw; }
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            Stop();
            disposed = true;
            synthesizer?.Dispose();
            synthesizer = null;
        }
    }
}
