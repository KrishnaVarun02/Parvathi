using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using Parvathi.Core;

namespace Parvathi.Windows.Services;

public sealed class SystemAutomation(IFocusService focus) : ISystemAutomation
{
    private sealed record DiscoveredApp(AppDescriptor Descriptor, string? Executable, bool CanFocusByExecutable = true);
    private readonly object _catalogLock = new();
    private readonly SemaphoreSlim _executionLock = new(1, 1);
    private Dictionary<string, DiscoveredApp> _catalog = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<AppDescriptor> DiscoverApplications()
    {
        var apps = new Dictionary<string, DiscoveredApp>(StringComparer.OrdinalIgnoreCase);
        ReadAppPaths(apps);
        ReadStartMenu(apps);
        ReadPackagedApplications(apps);
        lock (_catalogLock) _catalog = apps;
        return apps.Values.Select(app => app.Descriptor).OrderBy(app => app.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<ActionResult> ExecuteAsync(CommandAction action, FocusSnapshot? snapshot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CommandRouter.Validate(action);
        if (action is TypeText type)
        {
            if (snapshot is null) throw new PipelineException("Focus the destination text field before starting a Type command.");
            return await focus.InsertAsync(type.Text, snapshot, cancellationToken);
        }
        if (!await _executionLock.WaitAsync(0, cancellationToken))
            throw new PipelineException("A previous Windows action is still returning. Inspect its outcome and wait before issuing another action.");
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // Run shell discovery and COM audio calls away from the window/message-loop thread.
        var worker = Task.Run(async () =>
        {
            try
            {
                linked.Token.ThrowIfCancellationRequested();
                return action switch
                {
                    OpenApplication open => await OpenApplicationAsync(open.Query, linked.Token),
                    OpenWebsite website => OpenUrl(website.Url, linked.Token),
                    SearchWeb search => OpenUrl(new Uri("https://www.google.com/search?q=" + Uri.EscapeDataString(search.Query)), linked.Token),
                    SetVolume volume => SetVolume(volume.Percent, linked.Token),
                    SetMuted muted => SetMuted(muted.Muted, linked.Token),
                    _ => throw new PipelineException("This action is not in the supported Windows tool registry.")
                };
            }
            catch (OperationCanceledException) { throw; }
            catch (PipelineException) { throw; }
            catch (Exception error) when (error is System.ComponentModel.Win32Exception or COMException
                       or UnauthorizedAccessException or InvalidOperationException or IOException)
            {
                throw new PipelineException("Windows could not complete or verify this action. Check that the application or sound output is available, then inspect its state before trying again. " + error.Message);
            }
            finally { _executionLock.Release(); linked.Dispose(); }
        }, CancellationToken.None);
        try { return await worker.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken); }
        catch (TimeoutException)
        {
            try { linked.Cancel(); } catch (ObjectDisposedException) { }
            _ = worker.ContinueWith(task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
            return new ActionResult("Windows timed out. The action is unverified; inspect its state before retrying. No automatic retry was made.", false);
        }
        catch (OperationCanceledException)
        {
            try { linked.Cancel(); } catch (ObjectDisposedException) { }
            _ = worker.ContinueWith(task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
            throw;
        }
    }

    private async Task<ActionResult> OpenApplicationAsync(string query, CancellationToken cancellationToken)
    {
        var available = DiscoverApplications();
        cancellationToken.ThrowIfCancellationRequested();
        var descriptor = ApplicationResolver.Resolve(query, available);
        DiscoveredApp app;
        lock (_catalogLock)
        {
            if (!_catalog.TryGetValue(descriptor.Id, out var current))
                throw new PipelineException("The installed application list changed during this request. Refresh it and try again.");
            app = current;
        }

        if (app.Executable is not null && app.CanFocusByExecutable)
        {
            foreach (var running in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(app.Executable)))
            {
                using (running)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        if (running.MainWindowHandle != nint.Zero &&
                            string.Equals(running.MainModule?.FileName, app.Executable, StringComparison.OrdinalIgnoreCase))
                            return await FocusProcessAsync(running.Id, descriptor.Name, cancellationToken);
                    }
                    catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
                    { /* An inaccessible process is not proof of a matching application. */ }
                }
            }
        }

        int? processId = null;
        cancellationToken.ThrowIfCancellationRequested();
        if (descriptor.IsPackaged)
        {
            // Activation uses an AppUserModelID discovered from the AppsFolder, never a spoken shell command.
            var activation = (IApplicationActivationManager)new ApplicationActivationManager();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = activation.ActivateApplication(descriptor.LaunchTarget, null, 0, out var launchedId);
                Marshal.ThrowExceptionForHR(result);
                if (launchedId > 0) processId = checked((int)launchedId);
            }
            finally { Marshal.FinalReleaseComObject(activation); }
        }
        else
        {
            // The only paths admitted to the catalog are existing .exe files and installed .lnk files
            // resolving to executables. Spoken text never becomes a command line or executable path.
            if (!File.Exists(descriptor.LaunchTarget))
                throw new PipelineException($"{descriptor.Name} is no longer installed at its discovered location. Refresh the application list.");
            cancellationToken.ThrowIfCancellationRequested();
            using var launched = Process.Start(new ProcessStartInfo(descriptor.LaunchTarget) { UseShellExecute = true });
            if (launched is not null) processId = launched.Id;
        }
        // A shortcut with arguments can represent a specific browser profile, web app, or document.
        // An executable/PID match alone cannot prove that particular target was opened.
        if (processId is not null && app.CanFocusByExecutable)
            return await FocusProcessAsync(processId.Value, descriptor.Name, cancellationToken);
        return new ActionResult($"Windows accepted the launch request for {descriptor.Name}. The application window could not be verified; check your taskbar.", false);
    }

    private static async Task<ActionResult> FocusProcessAsync(int processId, string name, CancellationToken cancellationToken)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(3))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var process = Process.GetProcessById(processId);
                process.Refresh();
                var window = process.MainWindowHandle;
                if (window != nint.Zero)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (NativeMethods.IsIconic(window)) NativeMethods.ShowWindowAsync(window, NativeMethods.SwRestore);
                    cancellationToken.ThrowIfCancellationRequested();
                    NativeMethods.SetForegroundWindow(window);
                    await Task.Delay(120, cancellationToken);
                    var foreground = NativeMethods.GetForegroundWindow();
                    NativeMethods.GetWindowThreadProcessId(foreground, out var foregroundProcessId);
                    return foreground == window && foregroundProcessId == processId
                        ? new ActionResult($"{name} is open and in the foreground.", true)
                        : new ActionResult($"{name} has a window, but Windows did not confirm foreground focus. Select it from the taskbar.", false);
                }
                if (process.HasExited) break;
            }
            catch (ArgumentException) { break; }
            catch (InvalidOperationException) { break; }
            await Task.Delay(150, cancellationToken);
        }
        return new ActionResult($"Requested {name}. Its foreground window could not be verified; check your taskbar before retrying.", false);
    }

    private static ActionResult OpenUrl(Uri uri, CancellationToken cancellationToken)
    {
        CommandRouter.Validate(new OpenWebsite(uri));
        cancellationToken.ThrowIfCancellationRequested();
        using var process = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        return new ActionResult($"Requested {uri.Host} in your default browser. Page loading has not been verified.", false);
    }

    private static ActionResult SetVolume(int percent, CancellationToken cancellationToken)
    {
        if (percent is < 0 or > 100) throw new PipelineException("Volume must be from 0 to 100 percent.");
        using var enumerator = new MMDeviceEnumerator();
        using var output = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        cancellationToken.ThrowIfCancellationRequested();
        output.AudioEndpointVolume.MasterVolumeLevelScalar = percent / 100f;
        var actual = (int)Math.Round(output.AudioEndpointVolume.MasterVolumeLevelScalar * 100);
        return Math.Abs(actual - percent) <= 1
            ? new ActionResult($"System volume is {actual} percent{(output.AudioEndpointVolume.Mute ? "; sound is still muted" : "")}.", true)
            : new ActionResult($"Requested {percent} percent volume, but Windows reports {actual} percent. The requested value is unverified.", false);
    }

    private static ActionResult SetMuted(bool muted, CancellationToken cancellationToken)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var output = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        cancellationToken.ThrowIfCancellationRequested();
        output.AudioEndpointVolume.Mute = muted;
        var verified = output.AudioEndpointVolume.Mute == muted;
        return new ActionResult(verified ? $"System sound is {(muted ? "muted" : "unmuted")}." : "Windows did not confirm the requested mute state.", verified);
    }

    private static void ReadAppPaths(Dictionary<string, DiscoveredApp> apps)
    {
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var key = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths");
                if (key is null) continue;
                foreach (var name in key.GetSubKeyNames().Take(2000))
                {
                    using var entry = key.OpenSubKey(name);
                    var path = (entry?.GetValue(null) as string)?.Trim().Trim('"');
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    path = Environment.ExpandEnvironmentVariables(path);
                    if (!IsExistingExecutable(path)) continue;
                    var id = "exe:" + Path.GetFullPath(path);
                    var displayName = FileVersionInfo.GetVersionInfo(path).FileDescription;
                    if (string.IsNullOrWhiteSpace(displayName)) displayName = Path.GetFileNameWithoutExtension(path);
                    apps.TryAdd(id, new DiscoveredApp(new AppDescriptor(id, displayName, path), path));
                }
            }
            catch (Exception error) when (error is UnauthorizedAccessException or IOException or System.Security.SecurityException or System.ComponentModel.Win32Exception)
            { /* A denied registry view should not hide applications available from another source. */ }
        }
    }

    private static void ReadStartMenu(Dictionary<string, DiscoveredApp> apps)
    {
        object? shell = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return;
            shell = Activator.CreateInstance(shellType);
            if (shell is null) return;
            foreach (var location in new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms })
            {
                var folder = Environment.GetFolderPath(location);
                if (!Directory.Exists(folder)) continue;
                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
                foreach (var link in Directory.EnumerateFiles(folder, "*.lnk", options).Take(2000))
                {
                    object? shortcut = null;
                    try
                    {
                        shortcut = ((dynamic)shell).CreateShortcut(link);
                        string executable = ((dynamic)shortcut).TargetPath;
                        string arguments = ((dynamic)shortcut).Arguments;
                        if (!IsExistingExecutable(executable)) continue;
                        // Avoid exposing uninstall/repair shortcuts as ordinary applications.
                        var name = Path.GetFileNameWithoutExtension(link);
                        if (name.Contains("uninstall", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("remove ", StringComparison.OrdinalIgnoreCase)) continue;
                        var id = string.IsNullOrWhiteSpace(arguments) ? "exe:" + Path.GetFullPath(executable) : "link:" + Path.GetFullPath(link);
                        // Prefer the actual Start menu label over version-resource descriptions.
                        apps[id] = new DiscoveredApp(new AppDescriptor(id, name, link), executable,
                            CanFocusByExecutable: string.IsNullOrWhiteSpace(arguments));
                    }
                    catch (Exception error) when (error is COMException or IOException or UnauthorizedAccessException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
                    { }
                    finally { ReleaseCom(shortcut); }
                }
            }
        }
        catch (Exception error) when (error is COMException or IOException or UnauthorizedAccessException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        { }
        finally { ReleaseCom(shell); }
    }

    private static void ReadPackagedApplications(Dictionary<string, DiscoveredApp> apps)
    {
        object? shell = null;
        object? folder = null;
        object? items = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null) return;
            shell = Activator.CreateInstance(shellType);
            if (shell is null) return;
            folder = ((dynamic)shell).NameSpace("shell:AppsFolder");
            if (folder is null) return;
            items = ((dynamic)folder).Items();
            var count = Math.Min((int)((dynamic)items).Count, 2000);
            for (var index = 0; index < count; index++)
            {
                object? item = null;
                try
                {
                    item = ((dynamic)items).Item(index);
                    string? appId = ((dynamic)item).ExtendedProperty("System.AppUserModel.ID") as string;
                    string name = ((dynamic)item).Name;
                    if (string.IsNullOrWhiteSpace(appId) || !appId.Contains('!') || string.IsNullOrWhiteSpace(name)) continue;
                    var id = "package:" + appId;
                    apps.TryAdd(id, new DiscoveredApp(new AppDescriptor(id, name, appId, true), null));
                }
                catch (Exception error) when (error is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
                finally { ReleaseCom(item); }
            }
        }
        catch (Exception error) when (error is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
        finally { ReleaseCom(items); ReleaseCom(folder); ReleaseCom(shell); }
    }

    private static bool IsExistingExecutable(string path) =>
        !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) &&
        string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path);

    private static void ReleaseCom(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }

    [ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
    private class ApplicationActivationManager { }

    [ComImport, Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string? arguments, uint options, out uint processId);
        [PreserveSig]
        int ActivateForFile([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId, nint itemArray,
            [MarshalAs(UnmanagedType.LPWStr)] string verb, out uint processId);
        [PreserveSig]
        int ActivateForProtocol([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId, nint itemArray, out uint processId);
    }
}
