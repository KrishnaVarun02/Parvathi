using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;
using Parvathi.Core;

namespace Parvathi.Windows.Services;

public sealed class AppSettings
{
    public int MicrophoneDevice { get; set; } = -1;
    public string ModelPath { get; set; } = "";
    public bool HandsFree { get; set; }
    public bool SpeakResponses { get; set; } = true;
    public bool KeepHistory { get; set; }
    public bool ReducedMotion { get; set; }
    public bool StartWithWindows { get; set; }
    public string Voice { get; set; } = "";
    public TranscriptionStyle Style { get; set; } = TranscriptionStyle.Verbatim;
    public string Provider { get; set; } = "OpenAI";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string Model { get; set; } = "gpt-6-astra";
    public uint HotkeyModifiers { get; set; } = 3;
    public uint DictationKey { get; set; } = 0x20;
    public uint CommandKey { get; set; } = 0x43;
    public uint AssistantKey { get; set; } = 0x41;
}

public sealed class SettingsStore
{
    public static string DataDirectory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Parvathi");
    private readonly string path;
    public string DirectoryPath { get; }
    public SettingsStore(string? directory = null)
    {
        DirectoryPath = directory ?? DataDirectory;
        path = Path.Combine(DirectoryPath, "settings.json");
    }
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public AppSettings Load()
    {
        if (!File.Exists(path)) return new();
        try
        {
            if (new FileInfo(path).Length > 65536) throw new InvalidDataException("Settings file is too large.");
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) ?? throw new InvalidDataException("Settings are empty.");
            Validate(settings);
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { throw new InvalidOperationException($"Could not read settings. Rename {path} to reset settings, then restart Parvathi. {ex.Message}", ex); }
    }

    public void Save(AppSettings settings)
    {
        Validate(settings);
        AtomicWrite(path, JsonSerializer.Serialize(settings, Json));
    }

    private static void Validate(AppSettings value)
    {
        if (value.MicrophoneDevice < -1) throw new ArgumentException("Select a microphone device or the system default.");
        if (value.Provider is not ("OpenAI" or "Ollama")) throw new ArgumentException("Choose OpenAI or Ollama.");
        if (string.IsNullOrWhiteSpace(value.Model) || value.Model.Length > 200) throw new ArgumentException("Enter a provider model name (maximum 200 characters).");
        if (!Uri.TryCreate(value.BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("Enter an HTTP or HTTPS provider address without embedded credentials.");
        GlobalShortcuts.Validate(new ShortcutSettings(value.HotkeyModifiers, value.DictationKey, value.CommandKey, value.AssistantKey));
        if (!Enum.IsDefined(value.Style)) throw new ArgumentException("Choose verbatim or polished transcription.");
    }

    public static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true)
            ?? throw new InvalidOperationException("Windows did not allow the per-user startup setting. Check your organization's device policy.");
        if (!enabled) { key.DeleteValue("Parvathi", false); return; }
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable)) throw new InvalidOperationException("Cannot find this installation for startup. Reinstall Parvathi and try again.");
        key.SetValue("Parvathi", $"\"{executable}\" --tray", RegistryValueKind.String);
    }

    internal static void AtomicWrite(string destination, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, contents); File.Move(temporary, destination, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public sealed record HistoryEntry(DateTimeOffset Date, string Mode, string Transcript, string Outcome);

public sealed class HistoryStore
{
    private readonly string path;
    public HistoryStore(string? directory = null) => path = Path.Combine(directory ?? SettingsStore.DataDirectory, "history.json");
    private readonly object gate = new();
    public IReadOnlyList<HistoryEntry> Load()
    {
        lock (gate)
        {
            if (!File.Exists(path)) return [];
            if (new FileInfo(path).Length > 4_000_000) throw new InvalidDataException("History is too large. Use Clear history in Settings.");
            try { return (JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(path), SettingsStore.Json) ?? []).TakeLast(100).ToArray(); }
            catch (JsonException ex) { throw new InvalidDataException("History could not be read. Use Clear history in Settings.", ex); }
        }
    }
    // The controller calls Add only when KeepHistory is enabled. Audio is never persisted.
    public void Add(HistoryEntry entry)
    {
        lock (gate)
        {
            var bounded = entry with { Transcript = Truncate(entry.Transcript, 12000), Outcome = Truncate(entry.Outcome, 4000) };
            SettingsStore.AtomicWrite(path, JsonSerializer.Serialize(Load().Append(bounded).TakeLast(100), SettingsStore.Json));
        }
    }
    public void Clear() { lock (gate) { if (File.Exists(path)) File.Delete(path); } }
    private static string Truncate(string value, int maximum) => value.Length <= maximum ? value : value[..maximum];
}
