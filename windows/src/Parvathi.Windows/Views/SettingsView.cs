using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Input;
using Parvathi.Windows.Services;

namespace Parvathi.Windows.Views;

public sealed class SettingsView : UserControl
{
    private readonly AssistantCoordinator coordinator;
    private readonly StackPanel form = new() { MaxWidth = 730, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) };
    private readonly TextBlock modelStatus = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar progress = new() { Height = 5, Minimum = 0, Maximum = 100, Margin = new Thickness(0, 10, 0, 10) };
    private readonly ComboBox microphone = new(), voice = new(), style = new(), provider = new(), modifiers = new();
    private readonly ComboBox dictateKey = new(), commandKey = new(), askKey = new();
    private readonly TextBox endpoint = new(), model = new();
    private readonly PasswordBox apiKey = new();
    private readonly CheckBox handsFree = new() { Content = "Hands-free dictation (press shortcut again to finish)" };
    private readonly CheckBox speak = new() { Content = "Speak short responses aloud" };
    private readonly CheckBox history = new() { Content = "Keep up to 100 transcripts on this computer" };
    private readonly CheckBox motion = new() { Content = "Reduce waveform motion" };
    private readonly CheckBox startup = new() { Content = "Start in the tray when I sign in to Windows" };
    private CancellationTokenSource? download;

    public SettingsView(AssistantCoordinator coordinator)
    {
        this.coordinator = coordinator;
        Content = form;
        var settings = coordinator.Settings;
        Heading("Offline speech recognition");
        Description("Download the English Vosk model once: 39.3 MiB download, about 67.6 MiB installed. Audio is processed on this device and is never saved. Basic dictation needs no paid account or model server.");
        modelStatus.Text = coordinator.Models.IsInstalled ? "English model installed. Integrity is checked before recording." : "Speech model not installed.";
        form.Children.Add(modelStatus); form.Children.Add(progress);
        var modelButtons = new StackPanel { Orientation = Orientation.Horizontal };
        Button(modelButtons, "Download / repair model", async () => await DownloadModelAsync());
        Button(modelButtons, "Cancel download", () => download?.Cancel());
        form.Children.Add(modelButtons);
        Field("Microphone", microphone);
        microphone.DisplayMemberPath = "Name"; microphone.SelectedValuePath = "Id";
        try { microphone.ItemsSource = SpeechRecognitionService.GetMicrophones(); microphone.SelectedValue = settings.MicrophoneDevice; }
        catch (Exception ex) { status.Text = ex.Message; }
        if (microphone.SelectedIndex < 0 && microphone.Items.Count > 0) microphone.SelectedIndex = 0;
        Button(form, "Open Windows microphone permissions", () => Process.Start(new ProcessStartInfo("ms-settings:privacy-microphone") { UseShellExecute = true }));
        Description("In Windows Settings, allow microphone access and access for desktop apps. A cyan waveform and Listening state appear only while the microphone is recording.");
        Field("Transcription style", style); style.ItemsSource = Enum.GetValues<TranscriptionStyle>(); style.SelectedItem = settings.Style;
        Description("Verbatim keeps the recognizer's words. Polished sends the transcript to your selected AI provider to remove fillers and add punctuation.");
        Heading("Global shortcuts");
        Description("Focus the destination in another app before using a shortcut. Dictation is separate from Command. Commands and Ask toggle on/off; dictation supports holding the shortcut. Escape stops active work.");
        Field("Shortcut modifiers", modifiers);
        modifiers.ItemsSource = new[] { new ModifierChoice("Ctrl + Alt", 3), new ModifierChoice("Ctrl + Shift", 6), new ModifierChoice("Alt + Shift", 5), new ModifierChoice("Ctrl + Alt + Shift", 7) };
        modifiers.DisplayMemberPath = "Name"; modifiers.SelectedValuePath = "Value"; modifiers.SelectedValue = settings.HotkeyModifiers;
        if (modifiers.SelectedIndex < 0) modifiers.SelectedIndex = 0;
        ConfigureKey("Dictation key", dictateKey, settings.DictationKey);
        ConfigureKey("Command key", commandKey, settings.CommandKey);
        ConfigureKey("Ask key", askKey, settings.AssistantKey);
        form.Children.Add(handsFree); handsFree.IsChecked = settings.HandsFree;
        Heading("Responses and AI provider");
        form.Children.Add(speak); speak.IsChecked = settings.SpeakResponses;
        Button(form, "Stop speech now", () => coordinator.Cancel());
        Field("Windows voice", voice);
        try { voice.ItemsSource = new[] { "System default" }.Concat(coordinator.Speech.Voices).ToArray(); }
        catch (Exception ex) { voice.ItemsSource = new[] { "System default" }; status.Text = "Windows voices unavailable: " + ex.Message; }
        voice.SelectedItem = string.IsNullOrEmpty(settings.Voice) ? "System default" : settings.Voice;
        if (voice.SelectedIndex < 0) voice.SelectedIndex = 0;
        Field("AI provider", provider); provider.ItemsSource = new[] { "OpenAI", "Ollama" }; provider.SelectedItem = settings.Provider;
        Field("Provider address", endpoint); endpoint.Text = settings.BaseUrl;
        Field("Model name", model); model.Text = settings.Model;
        provider.SelectionChanged += (_, _) => {
            bool cloud = (string?)provider.SelectedItem == "OpenAI";
            endpoint.Text = cloud ? "https://api.openai.com/v1" : "http://localhost:11434";
            model.Text = cloud ? "gpt-6-astra" : "llama3.2";
        };
        Description("AI requests send text and bounded conversation context to the configured provider address. OpenAI uses api.openai.com by default and may incur API charges; changing its address also changes where its key is sent. Choose a model available to your account. Ollama requires a separately installed server and model and never receives an OpenAI key. Neither provider receives microphone audio here. Web search only opens your browser; the assistant cannot read current web results.");
        Field("OpenAI API key (stored securely for this Windows user)", apiKey);
        Description("Leave this field empty to keep your saved key. This field never displays an existing key. The key is protected with Windows DPAPI, separate from settings.");
        var credentialButtons = new StackPanel { Orientation = Orientation.Horizontal };
        Button(credentialButtons, "Save OpenAI key", () => { new CredentialStore().Save(apiKey.Password); apiKey.Clear(); status.Text = "OpenAI key saved securely."; });
        Button(credentialButtons, "Remove saved key", () => { new CredentialStore().Delete(); apiKey.Clear(); status.Text = "Saved OpenAI key removed."; });
        form.Children.Add(credentialButtons);
        Heading("Privacy and preferences");
        form.Children.Add(history); history.IsChecked = settings.KeepHistory;
        form.Children.Add(motion); motion.IsChecked = settings.ReducedMotion;
        form.Children.Add(startup); startup.IsChecked = settings.StartWithWindows;
        Description("History is off by default. Disabling it deletes saved transcripts. Current-session conversation stays in memory until cleared or the app exits. Raw audio and screenshots are never retained. Settings, downloaded model, optional history and encrypted credentials live in %LOCALAPPDATA%\\Parvathi; uninstall preserves this folder.");
        Button(form, "Clear history and conversation", () => { coordinator.ClearHistory(); status.Text = "Saved history and conversation cleared."; });
        Button(form, "Save settings", Save);
        form.Children.Add(status);
        Description("Insertion supports verifiable plain editable controls. Password fields, elevated apps and controls whose text or selection cannot be verified are refused. Keep the original field focused while recording. Use the transcript for manual copying when an app is unsupported; Parvathi never changes your clipboard automatically.");
    }

    private async Task DownloadModelAsync()
    {
        if (download != null) return;
        download = new CancellationTokenSource();
        coordinator.Cancel();
        modelStatus.Text = "Downloading and verifying the English model…";
        try {
            await coordinator.Models.DownloadAsync(new Progress<double>(value => progress.Value = value * 100), download.Token);
            progress.Value = 100; modelStatus.Text = "English model installed and integrity verified. Focus another app and use the Dictation shortcut.";
        } catch (OperationCanceledException) { modelStatus.Text = "Download canceled. You can try again when ready."; }
        catch (Exception ex) { modelStatus.Text = ex.Message; }
        finally { download.Dispose(); download = null; }
    }

    private void Save()
    {
        coordinator.SaveSettings(new AppSettings {
            MicrophoneDevice = microphone.SelectedValue is int id ? id : -1,
            Style = style.SelectedItem is TranscriptionStyle selectedStyle ? selectedStyle : TranscriptionStyle.Verbatim,
            HandsFree = handsFree.IsChecked == true, SpeakResponses = speak.IsChecked == true,
            KeepHistory = history.IsChecked == true, ReducedMotion = motion.IsChecked == true, StartWithWindows = startup.IsChecked == true,
            Voice = (string?)voice.SelectedItem == "System default" ? "" : (string?)voice.SelectedItem ?? "",
            Provider = (string?)provider.SelectedItem ?? "OpenAI", BaseUrl = endpoint.Text.Trim(), Model = model.Text.Trim(),
            HotkeyModifiers = modifiers.SelectedValue is uint mask ? mask : 3,
            DictationKey = (uint)dictateKey.SelectedValue, CommandKey = (uint)commandKey.SelectedValue, AssistantKey = (uint)askKey.SelectedValue
        });
        status.Text = coordinator.HasError ? coordinator.Feedback : "Settings saved. Your global shortcuts are ready.";
    }

    private void ConfigureKey(string label, ComboBox combo, uint selected)
    {
        Field(label, combo);
        var keys = new[] { new KeyChoice("Space", 0x20) }
            .Concat(Enumerable.Range(0x41, 26).Select(k => new KeyChoice(((char)k).ToString(), (uint)k)))
            .Concat(Enumerable.Range(1, 11).Select(n => new KeyChoice("F" + n, (uint)(0x6f + n)))).ToArray();
        combo.ItemsSource = keys; combo.DisplayMemberPath = "Name"; combo.SelectedValuePath = "Value"; combo.SelectedValue = selected;
        if (combo.SelectedIndex < 0) combo.SelectedIndex = 0;
    }
    private void Field(string label, Control control)
    {
        var caption = new Label { Content = label, Target = control, Padding = new Thickness(0, 12, 0, 5) };
        AutomationProperties.SetName(control, label);
        control.MinWidth = 320; control.HorizontalAlignment = HorizontalAlignment.Left;
        form.Children.Add(caption); form.Children.Add(control);
    }
    private void Heading(string value) => form.Children.Add(new TextBlock { Text = value, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 22, 0, 8) });
    private void Description(string value) => form.Children.Add(new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 10), MaxWidth = 700 });
    private void Button(Panel parent, string label, Action action)
    {
        var button = new Button { Content = label, Margin = new Thickness(0, 8, 8, 4), HorizontalAlignment = HorizontalAlignment.Left };
        button.Click += (_, _) => { try { action(); } catch (Exception ex) { status.Text = ex.Message; } };
        parent.Children.Add(button);
    }
    private sealed record ModifierChoice(string Name, uint Value);
    private sealed record KeyChoice(string Name, uint Value);
}
