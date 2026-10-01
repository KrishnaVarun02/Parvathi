using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using Parvathi.Core;

namespace Parvathi.Windows.Services;

/// <summary>Captures an explicit editable target, then addresses that same target directly.
/// No keyboard injection, clipboard replacement, foreground restoration, or blind paste fallback.</summary>
public sealed class FocusService : IFocusService, IDisposable
{
    private const int MaxDocumentLength = 100_000;
    private const int MaxInsertionLength = 20_000;
    private readonly NativeMethods.WinEventCallback _callback;
    private readonly List<nint> _hooks = [];
    private readonly SemaphoreSlim _insertLock = new(1, 1);
    private long _revision;
    private nint _trackedWindow;
    private uint _trackedProcess;
    private bool _disposed;

    private sealed record TargetState(AutomationElement Element, nint EditHandle,
        string NativeText, int NativeSelectionStart, int NativeSelectionEnd, long ProcessStartTicks, string? InsertionError);

    /// <remarks>Construct/dispose on a thread with a Windows message loop; call Capture on an MTA worker.</remarks>
    public FocusService()
    {
        _callback = OnWinEvent;
        foreach (var eventType in new[] { NativeMethods.EventForeground, NativeMethods.EventFocus,
                     NativeMethods.EventValueChange, NativeMethods.EventTextSelectionChange })
        {
            var handle = NativeMethods.SetWinEventHook(eventType, eventType, nint.Zero, _callback, 0, 0, 0);
            if (handle == nint.Zero)
            {
                Dispose();
                throw new PipelineException("Windows could not monitor focus safely. Restart Parvathi in your unlocked desktop session.");
            }
            _hooks.Add(handle);
        }
    }

    private void OnWinEvent(nint hook, uint eventType, nint hwnd, int objectId,
        int childId, uint eventThread, uint eventTime)
    {
        if (eventType is NativeMethods.EventForeground or NativeMethods.EventFocus)
        {
            Interlocked.Increment(ref _revision);
            return;
        }
        if (hwnd == nint.Zero || _trackedWindow == nint.Zero) return;
        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        // Reject even an edit changed and then restored during a request, where events are exposed.
        if (processId == _trackedProcess && NativeMethods.GetAncestor(hwnd, 2) == _trackedWindow)
            Interlocked.Increment(ref _revision);
    }

    public FocusSnapshot Capture()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try { return CaptureCore(); }
        catch (PipelineException) { throw; }
        catch (Exception error) when (error is ElementNotAvailableException or InvalidOperationException
                   or UnauthorizedAccessException or System.ComponentModel.Win32Exception or COMException)
        {
            throw new PipelineException("Parvathi cannot safely read this text field. Focus an editable field in Notepad or another accessible app, keep it unchanged, and try again. Elevated, protected, or inaccessible fields are unsupported.");
        }
    }

    private FocusSnapshot CaptureCore()
    {
        var revision = Interlocked.Read(ref _revision);
        var window = NativeMethods.GetForegroundWindow();
        if (window == nint.Zero) throw new PipelineException("No foreground application is available. Unlock Windows and focus a text field first.");
        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        if (processId == 0 || processId == Environment.ProcessId)
            throw new PipelineException("Focus a text field in another application, then use the global voice shortcut.");
        RejectElevatedProcess(processId);
        using var process = Process.GetProcessById(checked((int)processId));
        var appName = process.ProcessName;
        if (IsProtectedApplication(appName))
            throw new PipelineException("Typing into terminals, sign-in screens, or system security tools is not supported. Choose an ordinary text editor.");

        var element = AutomationElement.FocusedElement;
        if (element is null || element.Current.ProcessId != processId || !element.Current.HasKeyboardFocus)
            throw new PipelineException("The focused text field could not be verified. Click directly in the field and try again.");
        if (element.Current.IsPassword)
            throw new PipelineException("Password and protected fields cannot receive dictation or selected-text operations.");
        if (!element.Current.IsEnabled)
            throw new PipelineException("The selected field is disabled. Choose an editable text field.");
        if (element.Current.ControlType != ControlType.Edit && element.Current.ControlType != ControlType.Document)
            throw new PipelineException("This control does not expose an editable text field. Choose Notepad or an accessible text input.");
        if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var rawTextPattern))
            throw new PipelineException("This app does not expose its caret or selection to Windows accessibility. Automatic insertion is disabled here; use an accessible text editor.");
        var textPattern = (TextPattern)rawTextPattern;
        var isReadOnly = textPattern.DocumentRange.GetAttributeValue(TextPattern.IsReadOnlyAttribute) is bool readOnly && readOnly;
        var selections = textPattern.GetSelection();
        if (selections.Length != 1)
            throw new PipelineException("Parvathi needs one caret or one continuous text selection. Multiple selections are unsupported.");
        var document = textPattern.DocumentRange;
        var text = ReadRange(document);
        var beforeSelection = document.Clone();
        beforeSelection.MoveEndpointByRange(TextPatternRangeEndpoint.End, selections[0], TextPatternRangeEndpoint.Start);
        var prefix = ReadRange(beforeSelection);
        var selected = ReadRange(selections[0]);
        var selectionStart = prefix.Length;
        if (selectionStart > text.Length || selectionStart + selected.Length > text.Length ||
            text.Substring(selectionStart, selected.Length) != selected)
            throw new PipelineException("This application returned inconsistent text-selection information. Automatic insertion was refused.");
        var runtimeId = element.GetRuntimeId();
        if (runtimeId.Length == 0) throw new PipelineException("This application does not provide a stable text-field identity.");

        var editHandle = FindNativeEditHandle(element, window, processId);
        string nativeText = "";
        int nativeStart = 0, nativeEnd = 0;
        string? insertionError = isReadOnly ? "The selected text is read-only. Explanations are available, but insertion needs an editable field." : null;
        if (editHandle != nint.Zero)
        {
            (nativeText, nativeStart, nativeEnd) = ReadNativeEdit(editHandle);
            if (!TryAlignDocumentEnd(ref text, ref selected, selectionStart, Normalize(nativeText)))
                insertionError = "The editor's native text differs from its accessibility text. Automatic insertion is disabled for this control.";
        }
        else if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out var rawValuePattern))
        {
            insertionError = "This editor cannot be addressed safely without blind keyboard input. Selected text can be explained, but insertion requires Notepad or a field supporting Windows ValuePattern.";
        }
        else
        {
            var valuePattern = (ValuePattern)rawValuePattern;
            if (valuePattern.Current.IsReadOnly || !TryAlignDocumentEnd(ref text, ref selected,
                    selectionStart, Normalize(valuePattern.Current.Value)))
                insertionError = "The field's value and accessibility text do not agree, or the field is read-only. Automatic insertion was refused.";
        }
        if (window != NativeMethods.GetForegroundWindow() || revision != Interlocked.Read(ref _revision))
            throw new PipelineException("Focus changed while the destination was being captured. Focus the text field and try again.");
        _trackedWindow = window;
        _trackedProcess = processId;
        return new FocusSnapshot(window, checked((int)processId), appName, string.Join(".", runtimeId),
            text, selectionStart, selected.Length, revision,
            new TargetState(element, editHandle, nativeText, nativeStart, nativeEnd, process.StartTime.ToUniversalTime().Ticks, insertionError));
    }

    public string SelectedText(FocusSnapshot snapshot)
    {
        if (snapshot.SelectionLength == 0)
            throw new PipelineException("Select the text you want to rewrite or explain before using the voice shortcut.");
        if (snapshot.SelectionStart < 0 || snapshot.SelectionLength < 0 ||
            (long)snapshot.SelectionStart + snapshot.SelectionLength > snapshot.Text.Length)
            throw new PipelineException("The text selection is invalid. Select it again and retry.");
        return snapshot.Text.Substring(snapshot.SelectionStart, snapshot.SelectionLength);
    }

    public async Task<ActionResult> InsertAsync(string text, FocusSnapshot snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0 || text.Length > MaxInsertionLength || text.Contains('\0'))
            throw new PipelineException("Text insertion needs between 1 and 20,000 characters and cannot contain a null character.");
        EnsureCanInsert(snapshot);
        var target = (TargetState)snapshot.NativeState!;
        // A timed-out provider must finish before another insertion is admitted. No retry can duplicate it.
        if (!await _insertLock.WaitAsync(0, cancellationToken))
            throw new PipelineException("A previous editor call is still returning. Check the destination and wait before starting another insertion.");
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var worker = Task.Run(() =>
        {
            try { return InsertCore(text, snapshot, target, linked.Token); }
            finally { _insertLock.Release(); linked.Dispose(); }
        }, CancellationToken.None);
        try { return await worker.WaitAsync(TimeSpan.FromSeconds(4), cancellationToken); }
        catch (TimeoutException)
        {
            try { linked.Cancel(); } catch (ObjectDisposedException) { }
            _ = worker.ContinueWith(task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
            return new ActionResult("The editor timed out. Insertion is unverified; inspect the field before retrying. Parvathi will not retry automatically.", false);
        }
        catch (OperationCanceledException)
        {
            try { linked.Cancel(); } catch (ObjectDisposedException) { }
            _ = worker.ContinueWith(task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
            throw;
        }
    }

    private ActionResult InsertCore(string text, FocusSnapshot snapshot, TargetState original, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = Capture();
        EnsureCanInsert(current);
        FocusGuard.Validate(snapshot, current, cancellationToken);
        if (current.NativeState is not TargetState now || now.ProcessStartTicks != original.ProcessStartTicks ||
            now.EditHandle != original.EditHandle || now.NativeText != original.NativeText ||
            now.NativeSelectionStart != original.NativeSelectionStart || now.NativeSelectionEnd != original.NativeSelectionEnd)
            throw new PipelineException("The original text destination changed. Nothing was inserted. Capture the field again.");
        var normalized = Normalize(text);
        var expected = snapshot.Text[..snapshot.SelectionStart] + normalized +
                       snapshot.Text[(snapshot.SelectionStart + snapshot.SelectionLength)..];
        // Final validation follows potentially slow accessibility calls, immediately before the addressed mutation.
        cancellationToken.ThrowIfCancellationRequested();
        if (NativeMethods.GetForegroundWindow() != snapshot.WindowHandle ||
            Interlocked.Read(ref _revision) != snapshot.Revision)
            throw new PipelineException("Focus or text changed while you spoke. Nothing was inserted. Focus the intended field and try again.");
        try
        {
            if (original.EditHandle != nint.Zero)
            {
                var buffer = Marshal.StringToHGlobalUni(normalized.Replace("\n", "\r\n", StringComparison.Ordinal));
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (NativeMethods.SendMessageTimeout(original.EditHandle, NativeMethods.EmReplaceSel, 1, buffer,
                            NativeMethods.SmtoAbortIfHung | NativeMethods.SmtoErrorOnExit, 1000, out _) == nint.Zero)
                        return Unverified("Windows did not acknowledge insertion before the timeout.");
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            else
            {
                if (!original.Element.TryGetCurrentPattern(ValuePattern.Pattern, out var rawPattern))
                    throw new PipelineException("The target no longer supports safe text replacement.");
                cancellationToken.ThrowIfCancellationRequested();
                if (NativeMethods.GetForegroundWindow() != snapshot.WindowHandle ||
                    Interlocked.Read(ref _revision) != snapshot.Revision)
                    throw new PipelineException("The destination changed before insertion. Nothing was inserted.");
                ((ValuePattern)rawPattern).SetValue(expected);
            }
            // Read the same target, never whichever window may now be foreground.
            string actual;
            if (original.EditHandle != nint.Zero) actual = Normalize(ReadNativeEdit(original.EditHandle).Text);
            else actual = Normalize(((ValuePattern)original.Element.GetCurrentPattern(ValuePattern.Pattern)).Current.Value);
            return actual == expected
                ? new ActionResult($"Inserted {text.Length} characters into {snapshot.AppName}; text verified. Clipboard unchanged.", true)
                : Unverified("The editor's resulting text differs from the expected text.");
        }
        catch (OperationCanceledException) { throw; }
        catch (PipelineException) { throw; }
        catch (Exception error) when (error is InvalidOperationException or ElementNotAvailableException
                   or COMException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return Unverified("The editor stopped exposing its text during insertion.");
        }
    }

    private static ActionResult Unverified(string reason) => new(
        reason + " Insertion is unverified; inspect the field before retrying. No automatic retry was made.", false);

    public void EnsureCanInsert(FocusSnapshot snapshot)
    {
        if (snapshot.NativeState is not TargetState target)
            throw new PipelineException("The destination was not captured by this Windows session. Capture a fresh text field.");
        if (target.InsertionError is not null) throw new PipelineException(target.InsertionError);
    }

    private static string ReadRange(TextPatternRange range)
    {
        var text = range.GetText(MaxDocumentLength + 1);
        if (text.Length > MaxDocumentLength)
            throw new PipelineException("This text field is larger than the safe 100,000-character capture limit. Use a smaller field or document.");
        return Normalize(text);
    }

    internal static string Normalize(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    internal static bool TryAlignDocumentEnd(ref string document, ref string selection, int start, string actualValue)
    {
        if (document == actualValue) return true;
        // Some RichEdit/WPF providers expose a virtual final paragraph terminator. Remove it only
        // when the addressed editor independently proves the complete underlying value and bounds.
        if (document != actualValue + "\n" || start > actualValue.Length) return false;
        var length = Math.Min(selection.Length, actualValue.Length - start);
        if (length < 0 || document.Substring(start, length) != selection[..length]) return false;
        document = actualValue;
        selection = selection[..length];
        return true;
    }

    private static nint FindNativeEditHandle(AutomationElement element, nint root, uint processId)
    {
        var handle = new nint(element.Current.NativeWindowHandle);
        if (handle == nint.Zero || NativeMethods.GetAncestor(handle, 2) != root) return nint.Zero;
        NativeMethods.GetWindowThreadProcessId(handle, out var owner);
        if (owner != processId) return nint.Zero;
        var name = new StringBuilder(256);
        NativeMethods.GetClassName(handle, name, name.Capacity);
        var className = name.ToString();
        return className.Equals("Edit", StringComparison.OrdinalIgnoreCase) ||
               className.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase) ? handle : nint.Zero;
    }

    private static (string Text, int Start, int End) ReadNativeEdit(nint handle)
    {
        if (!NativeMethods.IsWindow(handle)) throw new PipelineException("The captured editor has closed.");
        var flags = NativeMethods.SmtoAbortIfHung | NativeMethods.SmtoErrorOnExit;
        if (NativeMethods.SendMessageTimeout(handle, NativeMethods.WmGetTextLength, 0, nint.Zero, flags, 500, out var size) == nint.Zero ||
            size > MaxDocumentLength)
            throw new PipelineException("The editor is unresponsive or its text is too large to capture safely.");
        var buffer = Marshal.AllocHGlobal(checked(((int)size + 1) * 2));
        var startPointer = Marshal.AllocHGlobal(4);
        var endPointer = Marshal.AllocHGlobal(4);
        try
        {
            Marshal.WriteInt16(buffer, 0);
            Marshal.WriteInt32(startPointer, -1);
            Marshal.WriteInt32(endPointer, -1);
            if (NativeMethods.SendMessageTimeout(handle, NativeMethods.WmGetText, size + 1, buffer, flags, 500, out _) == nint.Zero ||
                NativeMethods.SendMessageTimeout(handle, NativeMethods.EmGetSel, (nuint)startPointer, endPointer, flags, 500, out _) == nint.Zero)
                throw new PipelineException("Windows could not read this editor's text and caret safely.");
            var start = Marshal.ReadInt32(startPointer);
            var end = Marshal.ReadInt32(endPointer);
            if (start < 0 || end < start || end > MaxDocumentLength)
                throw new PipelineException("The editor did not expose a valid selection.");
            return (Marshal.PtrToStringUni(buffer) ?? "", start, end);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
            Marshal.FreeHGlobal(startPointer);
            Marshal.FreeHGlobal(endPointer);
        }
    }

    private static void RejectElevatedProcess(uint processId)
    {
        var process = NativeMethods.OpenProcess(0x1000, false, processId);
        if (process == nint.Zero) throw new PipelineException("Windows denied access to this application. Use a normal, non-administrator text editor.");
        nint token = nint.Zero;
        try
        {
            if (!NativeMethods.OpenProcessToken(process, 0x0008, out token) ||
                !NativeMethods.GetTokenInformation(token, 20, out var elevated, sizeof(int), out _))
                throw new PipelineException("The application's Windows security level could not be verified. Automatic insertion was refused.");
            if (elevated != 0)
                throw new PipelineException("This application is running as administrator. Restart it normally to use dictation; Parvathi does not elevate privileges.");
        }
        finally
        {
            if (token != nint.Zero) NativeMethods.CloseHandle(token);
            NativeMethods.CloseHandle(process);
        }
    }

    private static bool IsProtectedApplication(string name) => new[]
    {
        "cmd", "powershell", "pwsh", "WindowsTerminal", "conhost", "wsl", "ssh", "regedit", "LogonUI", "CredentialUIBroker", "consent"
    }.Contains(name, StringComparer.OrdinalIgnoreCase);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var hook in _hooks) NativeMethods.UnhookWinEvent(hook);
        _hooks.Clear();
        GC.KeepAlive(_callback);
        // Do not dispose the semaphore while a timed-out provider is still unwinding.
    }
}
