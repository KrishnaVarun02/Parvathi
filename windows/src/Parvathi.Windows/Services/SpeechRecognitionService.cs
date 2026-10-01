using System.IO;
using System.Text;
using System.Text.Json;
using NAudio.Wave;
using Parvathi.Core;
using Vosk;

namespace Parvathi.Windows.Services;

public sealed record MicrophoneInfo(int Id, string Name);

/// <summary>Live 16 kHz mono PCM to Vosk. Audio remains in short-lived memory buffers.</summary>
public sealed class SpeechRecognitionService : IRecognitionService, IDisposable
{
    private readonly object gate = new();
    private Session? current;
    private long generation;
    private bool disposed;
    public event Action<string>? Partial;
    public event Action<double>? Level;
    public event Action<Exception>? Failed;
    public bool IsRecording { get { lock (gate) return current is { Active: true, StopRequested: false }; } }

    public static IReadOnlyList<MicrophoneInfo> GetMicrophones()
    {
        var devices = new List<MicrophoneInfo> { new(-1, "System default microphone") };
        for (var i = 0; i < WaveInEvent.DeviceCount; i++) devices.Add(new(i, WaveInEvent.GetCapabilities(i).ProductName));
        return devices;
    }

    public async Task StartAsync(RecognitionOptions options, CancellationToken cancellationToken)
    {
        Cancel();
        long expected;
        lock (gate) { ObjectDisposedException.ThrowIf(disposed, this); expected = generation; }
        cancellationToken.ThrowIfCancellationRequested();
        if (WaveInEvent.DeviceCount == 0) throw new PipelineException(MicrophoneHelp("No microphone was found."));
        if (options.DeviceNumber < -1 || options.DeviceNumber >= WaveInEvent.DeviceCount)
            throw new PipelineException("The selected microphone is disconnected. Select an available microphone in Settings.");
        var path = string.IsNullOrWhiteSpace(options.ModelPath) ? ModelManager.DefaultModelPath : options.ModelPath;
        await ModelManager.ValidateInstalledAsync(path, cancellationToken).ConfigureAwait(false);
        Session? session = null;
        try
        {
            // Native model construction is not interruptible. Generation fencing prevents it
            // from starting a microphone if Stop was requested while loading.
            session = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                Vosk.Vosk.SetLogLevel(-1);
                var model = new Model(path);
                try { return new Session(model, new VoskRecognizer(model, 16000), options.DeviceNumber); }
                catch { model.Dispose(); throw; }
            }, cancellationToken).ConfigureAwait(false);
            lock (gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (disposed || expected != generation) throw new OperationCanceledException("Recording was cancelled before the microphone started.", cancellationToken);
                current = session;
                session.Wave.DataAvailable += (_, e) => OnData(session, e);
                session.Wave.RecordingStopped += (_, e) => OnStopped(session, e);
                session.Active = true;
                lock (session.WaveGate)
                {
                    session.Wave.StartRecording();
                    session.Started = true;
                }
            }
            var registration = cancellationToken.Register(() => CancelSession(session));
            lock (session.Gate)
            {
                if (session.Cleaned) registration.Dispose();
                else session.Registration = registration;
            }
        }
        catch (OperationCanceledException) { if (session is not null) CancelSession(session); throw; }
        catch (Exception ex)
        {
            if (session is not null) CancelSession(session);
            if (ex is DllNotFoundException or BadImageFormatException or TypeInitializationException)
                throw new PipelineException("The offline speech runtime could not load. Install the Windows x64 build and keep every extracted file together. " + ex.Message);
            throw new PipelineException(MicrophoneHelp("Could not start recording: " + ex.Message));
        }
    }

    public async Task<string> StopAsync(CancellationToken cancellationToken)
    {
        Session session;
        lock (gate)
        {
            session = current ?? throw new PipelineException("There is no active recording. Start dictation and speak first.");
            session.StopRequested = true;
        }
        try
        {
            lock (session.WaveGate) { if (!session.Cancelled && !session.Cleaned) session.Wave.StopRecording(); }
            await session.Stopped.Task.WaitAsync(TimeSpan.FromSeconds(4), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            string transcript;
            lock (session.Gate)
            {
                if (session.Cancelled || session.Cleaned) throw new OperationCanceledException(cancellationToken);
                if (session.Error is not null) throw new PipelineException(MicrophoneHelp("Recording stopped: " + session.Error.Message));
                Append(session.Text, ReadText(session.Recognizer.FinalResult(), "text"));
                transcript = session.Text.ToString().Trim();
                session.Active = false;
            }
            lock (gate) { if (ReferenceEquals(current, session)) current = null; }
            Cleanup(session);
            Level?.Invoke(0);
            return transcript;
        }
        catch (TimeoutException)
        {
            CancelSession(session);
            throw new PipelineException("The microphone did not stop within four seconds. Reconnect it and try again. No text was inserted.");
        }
        catch { CancelSession(session); throw; }
    }

    public void Cancel()
    {
        Session? session;
        lock (gate) { generation++; session = current; current = null; }
        if (session is not null) CancelSession(session);
    }

    private void CancelSession(Session session)
    {
        lock (gate) { if (ReferenceEquals(current, session)) { current = null; generation++; } }
        // Stopping microphone capture must not wait for a native decoder call.
        // Decoder disposal is deferred until WinMM finishes its callback thread.
        session.Cancelled = true;
        session.Active = false;
        session.StopRequested = true;
        lock (session.WaveGate)
        {
            if (session.Cleaned) return;
            try { session.Wave.StopRecording(); }
            catch (Exception ex) { session.Error ??= ex; }
            if (!session.Started) session.Stopped.TrySetResult();
        }
        // Dispose buffers only once the WinMM capture thread reports stopped.
        // This prevents a disposed-buffer race with the last DataAvailable callback.
        _ = session.Stopped.Task.ContinueWith(_ => Cleanup(session), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void OnData(Session session, WaveInEventArgs e)
    {
        try
        {
            string preview;
            double level;
            lock (session.Gate)
            {
                if (!session.Active || session.Cleaned || session.Cancelled) return;
                double squares = 0;
                for (var i = 0; i + 1 < e.BytesRecorded; i += 2)
                {
                    var sample = BitConverter.ToInt16(e.Buffer, i) / 32768d;
                    squares += sample * sample;
                }
                level = e.BytesRecorded > 1 ? Math.Clamp(Math.Sqrt(squares / (e.BytesRecorded / 2)) * 5, 0, 1) : 0;
                if (session.Recognizer.AcceptWaveform(e.Buffer, e.BytesRecorded))
                {
                    Append(session.Text, ReadText(session.Recognizer.Result(), "text"));
                    preview = session.Text.ToString().Trim();
                }
                else preview = (session.Text + " " + ReadText(session.Recognizer.PartialResult(), "partial")).Trim();
            }
            lock (gate)
            {
                if (!ReferenceEquals(current, session) || !session.Active) return;
                Level?.Invoke(level);
                Partial?.Invoke(preview);
            }
        }
        catch (Exception ex)
        {
            lock (gate)
            {
                if (!ReferenceEquals(current, session)) return;
                CancelSession(session);
                Failed?.Invoke(new PipelineException("Offline transcription failed. Check the model installation and microphone. " + ex.Message));
            }
        }
        finally { Array.Clear(e.Buffer, 0, e.BytesRecorded); }
    }

    private void OnStopped(Session session, StoppedEventArgs e)
    {
        var unexpected = false;
        lock (session.Gate)
        {
            session.Error ??= e.Exception;
            unexpected = session.Active && !session.StopRequested;
            session.Stopped.TrySetResult();
        }
        if (unexpected)
        {
            lock (gate)
            {
                if (!ReferenceEquals(current, session)) return;
                CancelSession(session);
                Failed?.Invoke(new PipelineException(MicrophoneHelp(e.Exception?.Message ?? "The microphone stopped unexpectedly.")));
            }
        }
    }

    private static void Cleanup(Session session)
    {
        CancellationTokenRegistration registration;
        lock (session.Gate)
        {
            if (session.Cleaned) return;
            session.Cleaned = true;
            session.Active = false;
            registration = session.Registration;
            lock (session.WaveGate) session.Wave.Dispose();
            session.Recognizer.Dispose();
            session.Model.Dispose();
            session.Text.Clear();
        }
        registration.Unregister();
    }
    private static string ReadText(string json, string property)
    {
        using var value = JsonDocument.Parse(json);
        return value.RootElement.TryGetProperty(property, out var text) ? text.GetString() ?? "" : "";
    }
    private static void Append(StringBuilder builder, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (builder.Length > 0) builder.Append(' ');
        builder.Append(text.Trim());
    }
    private static string MicrophoneHelp(string reason) => reason + " Open Windows Settings > Privacy & security > Microphone; enable Microphone access and Let desktop apps access your microphone. Check Settings > System > Sound > Input and close apps using the microphone exclusively.";
    public void Dispose() { lock (gate) { if (disposed) return; disposed = true; } Cancel(); }

    private sealed class Session(Model model, VoskRecognizer recognizer, int deviceNumber)
    {
        public readonly object Gate = new();
        public readonly object WaveGate = new();
        public readonly Model Model = model;
        public readonly VoskRecognizer Recognizer = recognizer;
        public readonly WaveInEvent Wave = new() { DeviceNumber = deviceNumber, WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 60, NumberOfBuffers = 3 };
        public readonly StringBuilder Text = new();
        public readonly TaskCompletionSource Stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationTokenRegistration Registration;
        public volatile bool Active, Started, StopRequested, Cancelled, Cleaned;
        public Exception? Error;
    }
}
