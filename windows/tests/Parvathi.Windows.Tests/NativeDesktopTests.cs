using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Parvathi.Core;
using Parvathi.Windows.Services;
using Xunit;

namespace Parvathi.Windows.Tests;

/// <summary>Opt in on an unlocked Windows desktop with PARVATHI_INTERACTIVE_TESTS=1.
/// These checks use a dedicated editor process and never a user's existing document.
/// They test insertion/focus/clipboard, not a microphone or the end-to-end voice pipeline.</summary>
public sealed class NativeDesktopFactAttribute : FactAttribute
{
    public NativeDesktopFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("PARVATHI_INTERACTIVE_TESTS") != "1")
            Skip = "Requires an explicitly opted-in, unlocked interactive Windows desktop.";
    }
}

[CollectionDefinition("Native desktop", DisableParallelization = true)]
public sealed class NativeDesktopCollection { }

[Collection("Native desktop")]
public sealed class NativeDesktopTests
{
    [NativeDesktopFact]
    [Trait("Category", "InteractiveDesktop")]
    public Task ReplacesActualExternalSelectionAndPreservesClipboard() => OnMessageThread(async focus =>
    {
        using var editor = await StartEditorAsync();
        var beforeClipboard = NativeMethods.GetClipboardSequenceNumber();
        var snapshot = await Task.Run(focus.Capture).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(editor.Id, snapshot.ProcessId);
        Assert.Equal("beta", focus.SelectedText(snapshot));
        focus.EnsureCanInsert(snapshot);
        var result = await focus.InsertAsync("dictated paragraph.", snapshot, CancellationToken.None);
        Assert.True(result.Verified, result.Message);
        Assert.Equal("alpha dictated paragraph. gamma", await ReadEditorAsync(editor));
        Assert.Equal(beforeClipboard, NativeMethods.GetClipboardSequenceNumber());
    });

    [NativeDesktopFact]
    [Trait("Category", "InteractiveDesktop")]
    public Task ChangedForegroundRefusesInsertionIntoEitherEditor() => OnMessageThread(async focus =>
    {
        using var original = await StartEditorAsync();
        var snapshot = await Task.Run(focus.Capture).WaitAsync(TimeSpan.FromSeconds(3));
        using var other = await StartEditorAsync();
        await Assert.ThrowsAsync<PipelineException>(() => focus.InsertAsync("must not appear", snapshot, CancellationToken.None));
        Assert.Equal("alpha beta gamma", await ReadEditorAsync(original));
        Assert.Equal("alpha beta gamma", await ReadEditorAsync(other));
    });

    [NativeDesktopFact]
    [Trait("Category", "InteractiveDesktop")]
    public Task CancelledInsertionLeavesActualEditorAndClipboardUnchanged() => OnMessageThread(async focus =>
    {
        using var editor = await StartEditorAsync();
        var snapshot = await Task.Run(focus.Capture).WaitAsync(TimeSpan.FromSeconds(3));
        var beforeClipboard = NativeMethods.GetClipboardSequenceNumber();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => focus.InsertAsync("must not appear", snapshot, cancellation.Token));
        Assert.Equal("alpha beta gamma", await ReadEditorAsync(editor));
        Assert.Equal(beforeClipboard, NativeMethods.GetClipboardSequenceNumber());
    });

    private static Task OnMessageThread(Func<FocusService, Task> test)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            _ = dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    using var focus = new FocusService();
                    await test(focus);
                    completion.TrySetResult();
                }
                catch (Exception error) { completion.TrySetException(error); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        }) { IsBackground = true, Name = "Parvathi native-test event loop" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private static async Task<EditorProcess> StartEditorAsync()
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "Parvathi.Windows.Tests.exe");
        if (!File.Exists(executable)) throw new InvalidOperationException("Build the Windows test project on Windows to produce its editor host executable.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        start.ArgumentList.Add("--native-editor");
        var process = Process.Start(start) ?? throw new InvalidOperationException("Test editor did not launch.");
        var owned = new EditorProcess(process);
        try
        {
            for (var attempt = 0; attempt < 50; attempt++)
            {
                process.Refresh();
                if (process.MainWindowHandle != nint.Zero)
                {
                    NativeMethods.SetForegroundWindow(process.MainWindowHandle);
                    await Task.Delay(250);
                    if (NativeMethods.GetForegroundWindow() == process.MainWindowHandle) return owned;
                }
                await Task.Delay(100);
            }
            throw new InvalidOperationException("Windows did not grant foreground focus to the test editor. This check requires an unlocked interactive desktop.");
        }
        catch { owned.Dispose(); throw; }
    }

    private static Task<string> ReadEditorAsync(EditorProcess editor) => Task.Run(() =>
    {
        var window = AutomationElement.FromHandle(editor.Window);
        var field = window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "ParvathiTestText"));
        return ((ValuePattern)field.GetCurrentPattern(ValuePattern.Pattern)).Current.Value;
    }).WaitAsync(TimeSpan.FromSeconds(3));

    private sealed class EditorProcess(Process process) : IDisposable
    {
        public int Id => process.Id;
        public nint Window => process.MainWindowHandle;
        public void Dispose()
        {
            // This process only belongs to this fixture and contains a disposable unsaved test field.
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            finally { process.Dispose(); }
        }
    }
}

public static class NativeTestEditor
{
    [STAThread]
    public static int Main(string[] arguments)
    {
        if (arguments.Length != 1 || arguments[0] != "--native-editor") return 2;
        var application = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        var field = new TextBox { Text = "alpha beta gamma", AcceptsReturn = true, FontSize = 22, Margin = new Thickness(20) };
        AutomationProperties.SetAutomationId(field, "ParvathiTestText");
        var window = new Window { Title = "Parvathi disposable native test editor", Width = 560, Height = 220, Content = field };
        window.Loaded += (_, _) => { window.Activate(); field.Focus(); field.Select(6, 4); };
        application.Run(window);
        return 0;
    }
}
