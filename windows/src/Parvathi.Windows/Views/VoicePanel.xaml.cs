using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Parvathi.Windows.Views;

public partial class VoicePanel : Window
{
    private readonly AssistantCoordinator coordinator;
    public VoicePanel(AssistantCoordinator coordinator)
    {
        this.coordinator = coordinator; InitializeComponent(); DataContext = coordinator;
        SourceInitialized += (_, _) => {
            var handle = new WindowInteropHelper(this).Handle;
            SetWindowLongPtr(handle, -20, GetWindowLongPtr(handle, -20) | 0x08000000 | 0x80);
            HwndSource.FromHwnd(handle)?.AddHook((nint hwnd, int msg, nint wparam, nint lparam, ref bool handled) => { if (msg == 0x21) { handled = true; return 3; } return 0; });
        };
    }
    public void ShowWithoutActivation()
    {
        if (!IsVisible) { var area = SystemParameters.WorkArea; Left = area.Left + (area.Width - Width) / 2; Top = area.Bottom - Height - 24; }
        Show();
    }
    private async void Finish_Click(object sender, RoutedEventArgs e) => await coordinator.FinishRecordingAsync();
    private void Stop_Click(object sender, RoutedEventArgs e) => coordinator.Cancel();
    private void Hide_Click(object sender, RoutedEventArgs e) => Hide();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
}
