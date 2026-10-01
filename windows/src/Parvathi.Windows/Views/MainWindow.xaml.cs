using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Parvathi.Windows.Services;

namespace Parvathi.Windows.Views;

public partial class MainWindow : Window
{
    private readonly AssistantCoordinator coordinator;
    public bool AllowClose { get; set; }
    public MainWindow(AssistantCoordinator coordinator)
    {
        this.coordinator = coordinator;
        InitializeComponent(); DataContext = coordinator;
        CommandList.ItemsSource = CommandCatalog.Examples;
        SettingsHost.Children.Add(new SettingsView(coordinator));
        RefreshShortcutLabels();
        coordinator.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(coordinator.Settings)) RefreshShortcutLabels(); };
        Closing += (_, e) => { if (!AllowClose) { e.Cancel = true; Hide(); } };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && coordinator.Busy) { coordinator.Cancel(); e.Handled = true; } };
    }
    public void ShowSettings() => Tabs.SelectedItem = SettingsTab;
    private void RefreshShortcutLabels()
    {
        var mask = coordinator.Settings.HotkeyModifiers;
        string prefix = ((mask & 2) != 0 ? "Ctrl + " : "") + ((mask & 1) != 0 ? "Alt + " : "") + ((mask & 4) != 0 ? "Shift + " : "");
        static string Name(uint key) => KeyInterop.KeyFromVirtualKey((int)key).ToString();
        DictationShortcut.Text = prefix + Name(coordinator.Settings.DictationKey);
        CommandShortcut.Text = prefix + Name(coordinator.Settings.CommandKey);
        AssistantShortcut.Text = prefix + Name(coordinator.Settings.AssistantKey);
    }
    private async void Send_Click(object sender, RoutedEventArgs e) => await SendAsync();
    private async Task SendAsync()
    {
        if (coordinator.Busy || string.IsNullOrWhiteSpace(Composer.Text)) return;
        var text = Composer.Text; Composer.Clear();
        await coordinator.SubmitAsync(text, ComposerMode.SelectedIndex == 0 ? InteractionMode.Command : InteractionMode.Assistant);
    }
    private async void Composer_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; await SendAsync(); } }
    private async void Finish_Click(object sender, RoutedEventArgs e) => await coordinator.FinishRecordingAsync();
    private void Stop_Click(object sender, RoutedEventArgs e) => coordinator.Cancel();
    private void ClearHistory_Click(object sender, RoutedEventArgs e) { try { coordinator.ClearHistory(); HistoryList.ItemsSource = coordinator.History; } catch (Exception ex) { coordinator.ReportError(ex.Message); } }
    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (HistoryList != null && Tabs.SelectedIndex == 3) { try { HistoryList.ItemsSource = coordinator.History; } catch (Exception ex) { coordinator.ReportError(ex.Message); } } }
    private void CommandSearch_TextChanged(object sender, TextChangedEventArgs e) { if (CommandList != null) CommandList.ItemsSource = CommandCatalog.Examples.Where(x => (x.Title + x.Phrase + x.Details).Contains(CommandSearch.Text, StringComparison.OrdinalIgnoreCase)).ToArray(); }
}
