using System.ComponentModel;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Parvathi.Windows.Services;

public enum ShortcutKind { Dictation, Command, Assistant }
public sealed record ShortcutSettings(uint Modifiers = 0x0003, uint DictationKey = 0x20,
    uint CommandKey = 0x43, uint AssistantKey = 0x41);

/// <summary>RegisterHotKey supplies key-down; only the three configured keys are sampled for key-up.
/// No global keyboard hook is installed and no unrelated key contents are collected.</summary>
public sealed class GlobalShortcuts : IDisposable
{
    private const int FirstId = 700;
    private const int EscapeId = 704;
    private readonly HwndSource _source;
    private readonly DispatcherTimer _releaseTimer;
    private readonly Action<ShortcutKind, bool> _onEvent;
    private readonly Action _onEscape;
    private readonly Dictionary<int, (ShortcutKind Kind, uint Key)> _registered = [];
    private readonly HashSet<int> _pressed = [];
    private ShortcutSettings? _settings;
    private bool _escapeRegistered;
    private bool _active;
    private bool _escapeWasDown;
    private bool _disposed;

    public GlobalShortcuts(Action<ShortcutKind, bool> onEvent, Action onEscape)
    {
        _onEvent = onEvent;
        _onEscape = onEscape;
        // A message-only window: never visible, never takes keyboard focus.
        _source = new HwndSource(new HwndSourceParameters("Parvathi shortcuts")
        {
            ParentWindow = new nint(-3), Width = 0, Height = 0, WindowStyle = 0
        });
        _source.AddHook(WindowProc);
        _releaseTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _releaseTimer.Tick += ReleaseTick;
    }

    public void Configure(ShortcutSettings settings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Validate(settings);
        var previous = _settings;
        RemoveModeKeys();
        try
        {
            RegisterModes(settings);
            _settings = settings;
        }
        catch
        {
            RemoveModeKeys();
            if (previous is not null)
            {
                try { RegisterModes(previous); } catch { RemoveModeKeys(); }
            }
            throw;
        }
    }

    public static void Validate(ShortcutSettings settings)
    {
        if (settings.Modifiers == 0 || (settings.Modifiers & ~0x0007u) != 0)
            throw new ArgumentException("Use a combination of Ctrl, Alt, or Shift; Windows-key shortcuts are reserved by Windows.");
        var keys = new[] { settings.DictationKey, settings.CommandKey, settings.AssistantKey };
        if (keys.Distinct().Count() != keys.Length || keys.Any(key => !IsSupportedKey(key)))
            throw new ArgumentException("Choose three different letter, number, Space, or F1–F11 keys for the global shortcuts.");
    }

    private static bool IsSupportedKey(uint key) => key is 0x20 or >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A or >= 0x70 and <= 0x7A;

    private void RegisterModes(ShortcutSettings settings)
    {
        var keys = new[] { settings.DictationKey, settings.CommandKey, settings.AssistantKey };
        for (var index = 0; index < keys.Length; index++)
        {
            var id = FirstId + index;
            if (!NativeMethods.RegisterHotKey(_source.Handle, id, settings.Modifiers | NativeMethods.ModNoRepeat, keys[index]))
                throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error(),
                    "A Parvathi shortcut is already used by Windows or another application. Choose different keys in Settings.");
            _registered.Add(id, ((ShortcutKind)index, keys[index]));
        }
    }

    public void SetActive(bool active)
    {
        if (_disposed || _active == active) return;
        _active = active;
        _escapeWasDown = (NativeMethods.GetAsyncKeyState((int)NativeMethods.VkEscape) & 0x8000) != 0;
        if (active)
        {
            _escapeRegistered = NativeMethods.RegisterHotKey(_source.Handle, EscapeId,
                NativeMethods.ModNoRepeat, NativeMethods.VkEscape);
            // Sample Escape while active too, so it works while shortcut modifiers are held.
            _releaseTimer.Start();
        }
        else
        {
            if (_escapeRegistered) NativeMethods.UnregisterHotKey(_source.Handle, EscapeId);
            _escapeRegistered = false;
            if (_pressed.Count == 0) _releaseTimer.Stop();
        }
    }

    private nint WindowProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != NativeMethods.WmHotkey) return nint.Zero;
        var id = wParam.ToInt32();
        if (id == EscapeId)
        {
            handled = true;
            _escapeWasDown = true;
            if (_active) _onEscape();
        }
        else if (_registered.TryGetValue(id, out var shortcut))
        {
            handled = true;
            if (_pressed.Add(id))
            {
                _releaseTimer.Start();
                _onEvent(shortcut.Kind, true);
            }
        }
        return nint.Zero;
    }

    private void ReleaseTick(object? sender, EventArgs e)
    {
        foreach (var id in _pressed.ToArray())
        {
            if (!_registered.TryGetValue(id, out var shortcut) ||
                (NativeMethods.GetAsyncKeyState((int)shortcut.Key) & 0x8000) == 0)
            {
                _pressed.Remove(id);
                if (_registered.ContainsKey(id)) _onEvent(shortcut.Kind, false);
            }
        }
        if (_active)
        {
            var down = (NativeMethods.GetAsyncKeyState((int)NativeMethods.VkEscape) & 0x8000) != 0;
            if (down && !_escapeWasDown) _onEscape();
            _escapeWasDown = down;
        }
        if (!_active && _pressed.Count == 0) _releaseTimer.Stop();
    }

    private void RemoveModeKeys()
    {
        foreach (var id in _registered.Keys) NativeMethods.UnregisterHotKey(_source.Handle, id);
        _registered.Clear();
        _pressed.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        SetActive(false);
        _disposed = true;
        _releaseTimer.Stop();
        _releaseTimer.Tick -= ReleaseTick;
        RemoveModeKeys();
        _source.RemoveHook(WindowProc);
        _source.Dispose();
    }
}
