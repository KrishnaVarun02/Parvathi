using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using Parvathi.Windows.Views;
using Forms = System.Windows.Forms;

namespace Parvathi.Windows;

public partial class App : System.Windows.Application
{
    private Mutex? instanceMutex;
    private EventWaitHandle? activation;
    private Forms.NotifyIcon? tray;
    private AssistantCoordinator? coordinator;
    private MainWindow? dashboard;
    private VoicePanel? panel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length >= 2 && e.Args[0] == "--self-check") { SelfCheck(e.Args[1]); return; }
        bool preview = e.Args.Length >= 2 && e.Args[0] == "--render-preview";
        if (!preview) {
            instanceMutex = new Mutex(true, @"Local\Parvathi.Windows", out bool first);
            if (!first) {
                try { using var existing = EventWaitHandle.OpenExisting(@"Local\Parvathi.Activate"); existing.Set(); } catch { }
                Shutdown(); return;
            }
        }
        try {
            coordinator = new AssistantCoordinator(Dispatcher);
            dashboard = new MainWindow(coordinator);
            MainWindow = dashboard;
            panel = new VoicePanel(coordinator);
            coordinator.ShowPanel += () => panel.ShowWithoutActivation();
            CreateTray();
            if (!preview) {
                activation = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Parvathi.Activate");
                new Thread(() => {
                    try { while (activation.WaitOne()) Dispatcher.BeginInvoke(ShowDashboard); } catch (ObjectDisposedException) { }
                }) { IsBackground = true, Name = "Parvathi activation" }.Start();
            }
            if (!e.Args.Contains("--tray")) dashboard.Show();
            if (preview) {
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () => {
                    dashboard.UpdateLayout();
                    var bitmap = new RenderTargetBitmap((int)dashboard.ActualWidth, (int)dashboard.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(dashboard);
                    var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                    using (var stream = File.Create(e.Args[1])) png.Save(stream);
                    ExitApp();
                });
            }
        } catch (Exception ex) {
            System.Windows.MessageBox.Show("Parvathi could not start: " + ex.Message, "Parvathi", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void CreateTray()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Parvathi.ico");
        tray = new Forms.NotifyIcon { Icon = new System.Drawing.Icon(iconPath), Text = "Parvathi", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Parvathi", null, (_, _) => ShowDashboard());
        menu.Items.Add("Dictate", null, (_, _) => StartFromTray(InteractionMode.Dictation));
        menu.Items.Add("Voice command", null, (_, _) => StartFromTray(InteractionMode.Command));
        menu.Items.Add("Ask Parvathi", null, (_, _) => StartFromTray(InteractionMode.Assistant));
        menu.Items.Add("Rewrite selection", null, (_, _) => StartFromTray(InteractionMode.Rewrite));
        menu.Items.Add("Explain selection", null, (_, _) => StartFromTray(InteractionMode.Explain));
        menu.Items.Add("Finish recording", null, (_, _) => _ = coordinator!.FinishRecordingAsync());
        menu.Items.Add("Stop everything", null, (_, _) => coordinator!.Cancel());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Settings", null, (_, _) => { ShowDashboard(); dashboard!.ShowSettings(); });
        menu.Items.Add("Exit", null, (_, _) => ExitApp());
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => ShowDashboard();
    }

    private void StartFromTray(InteractionMode mode) => Dispatcher.BeginInvoke(
        System.Windows.Threading.DispatcherPriority.ApplicationIdle,
        new Action(() => coordinator!.ToggleRecording(mode)));

    private void ShowDashboard() { dashboard?.Show(); if (dashboard != null) { dashboard.WindowState = WindowState.Normal; dashboard.Activate(); } }
    public void ExitApp() { if (dashboard != null) dashboard.AllowClose = true; Shutdown(); }
    protected override void OnExit(ExitEventArgs e)
    {
        coordinator?.Dispose(); panel?.Close(); tray?.Dispose(); activation?.Dispose();
        if (instanceMutex != null) { try { instanceMutex.ReleaseMutex(); } catch (ApplicationException) { } instanceMutex.Dispose(); }
        base.OnExit(e);
    }

    private void SelfCheck(string output)
    {
        // Package evidence only: no user settings, microphone, text insertion, or provider requests.
        try {
            var handle = NativeLibrary.Load("libvosk", Assembly.GetExecutingAssembly(), DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories);
            NativeLibrary.GetExport(handle, "vosk_model_new"); NativeLibrary.Free(handle);
            var result = new { application = "Parvathi", version = "0.2.0", architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                os = RuntimeInformation.OSDescription, nativeRecognitionLibrary = "loaded", runtime = RuntimeInformation.FrameworkDescription,
                microphoneAndInsertion = "not tested", credentials = "not accessed" };
            File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            Shutdown(0);
        } catch (Exception ex) { File.WriteAllText(output, JsonSerializer.Serialize(new { error = ex.Message })); Shutdown(1); }
    }
}
