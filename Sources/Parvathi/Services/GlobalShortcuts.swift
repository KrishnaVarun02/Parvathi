import AppKit
import Carbon

enum ShortcutKind: UInt32, CaseIterable {
    case dictation = 1
    case command = 2
    case assistant = 3
}

/// Carbon registered shortcuts do not require a background keyboard logger.
/// Escape monitoring exists only while a request or spoken response is active.
@MainActor
final class GlobalShortcuts {
    var onEscape: (() -> Void)?
    private var handler: EventHandlerRef?
    private var hotKeys: [EventHotKeyRef] = []
    private var escapeHotKey: EventHotKeyRef?
    private var onEvent: ((ShortcutKind, Bool) -> Void)?
    private var globalEscapeMonitor: Any?
    private var localEscapeMonitor: Any?
    private var current: Configuration?
    private var pressed: Set<UInt32> = []
    private static let signature: OSType = 0x50565254

    private struct Configuration {
        let dictation: UInt32
        let command: UInt32
        let assistant: UInt32
        let modifiers: UInt32
    }

    func configure(dictationKey: UInt32, commandKey: UInt32, assistantKey: UInt32, modifiers: UInt32,
                   onEvent: @escaping (ShortcutKind, Bool) -> Void) throws {
        let keys = [dictationKey, commandKey, assistantKey]
        guard Set(keys).count == 3, keys.allSatisfy({ $0 < 128 && $0 != UInt32(kVK_Escape) }),
              modifiers & UInt32(cmdKey | optionKey | controlKey) != 0 else {
            throw AutomationError.failed("Choose three distinct shortcut keys, each with Command, Option, or Control. Escape is reserved for stopping.")
        }
        try installHandlerIfNeeded()
        let previous = current
        unregisterHotKeys()
        let configuration = Configuration(dictation: dictationKey, command: commandKey, assistant: assistantKey, modifiers: modifiers)
        do {
            try register(configuration)
            current = configuration
            self.onEvent = onEvent
        } catch {
            unregisterHotKeys()
            if let previous { try? register(previous) }
            throw error
        }
    }

    func setActive(_ active: Bool) {
        if active {
            // A temporary plain-Escape registration also works before Accessibility permission
            // is granted. It is removed as soon as the request ends; other keys are untouched.
            if escapeHotKey == nil, (try? installHandlerIfNeeded()) != nil {
                let id = EventHotKeyID(signature: Self.signature, id: 4)
                RegisterEventHotKey(UInt32(kVK_Escape), 0, id, GetApplicationEventTarget(), 0, &escapeHotKey)
            }
            if globalEscapeMonitor == nil {
                globalEscapeMonitor = NSEvent.addGlobalMonitorForEvents(matching: .keyDown) { [weak self] event in
                    guard event.keyCode == UInt16(kVK_Escape) else { return }
                    MainActor.assumeIsolated { self?.onEscape?() }
                }
            }
            if localEscapeMonitor == nil {
                localEscapeMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
                    guard event.keyCode == UInt16(kVK_Escape) else { return event }
                    MainActor.assumeIsolated { self?.onEscape?() }
                    return nil
                }
            }
        } else {
            if let escapeHotKey { UnregisterEventHotKey(escapeHotKey) }
            escapeHotKey = nil
            if let globalEscapeMonitor { NSEvent.removeMonitor(globalEscapeMonitor) }
            if let localEscapeMonitor { NSEvent.removeMonitor(localEscapeMonitor) }
            globalEscapeMonitor = nil
            localEscapeMonitor = nil
        }
    }

    func shutdown() {
        setActive(false)
        unregisterHotKeys()
        if let handler { RemoveEventHandler(handler) }
        handler = nil
    }

    private func installHandlerIfNeeded() throws {
        guard handler == nil else { return }
        var events = [EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed)),
                      EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyReleased))]
        let callback: EventHandlerUPP = { _, event, context in
            guard let event, let context else { return OSStatus(eventNotHandledErr) }
            var identifier = EventHotKeyID()
            let status = GetEventParameter(event, EventParamName(kEventParamDirectObject), EventParamType(typeEventHotKeyID),
                                           nil, MemoryLayout<EventHotKeyID>.size, nil, &identifier)
            guard status == noErr else { return status }
            let owner = Unmanaged<GlobalShortcuts>.fromOpaque(context).takeUnretainedValue()
            let isDown = GetEventKind(event) == UInt32(kEventHotKeyPressed)
            return MainActor.assumeIsolated {
                guard identifier.signature == GlobalShortcuts.signature else { return OSStatus(eventNotHandledErr) }
                if identifier.id == 4 {
                    if isDown { owner.onEscape?() }
                    return noErr
                }
                guard let kind = ShortcutKind(rawValue: identifier.id) else { return OSStatus(eventNotHandledErr) }
                if isDown {
                    guard owner.pressed.insert(identifier.id).inserted else { return noErr }
                } else {
                    owner.pressed.remove(identifier.id)
                }
                owner.onEvent?(kind, isDown)
                return noErr
            }
        }
        let status = InstallEventHandler(GetApplicationEventTarget(), callback, events.count, &events,
                                         Unmanaged.passUnretained(self).toOpaque(), &handler)
        guard status == noErr else {
            throw AutomationError.failed("macOS could not install the global shortcut handler (error \(status)). Relaunch Parvathi.")
        }
    }

    private func register(_ configuration: Configuration) throws {
        let definitions: [(ShortcutKind, UInt32)] = [(.dictation, configuration.dictation), (.command, configuration.command),
                                                   (.assistant, configuration.assistant)]
        for (kind, keyCode) in definitions {
            var hotKey: EventHotKeyRef?
            let id = EventHotKeyID(signature: Self.signature, id: kind.rawValue)
            let status = RegisterEventHotKey(keyCode, configuration.modifiers, id, GetApplicationEventTarget(), 0, &hotKey)
            guard status == noErr, let hotKey else {
                throw AutomationError.failed("The \(String(describing: kind)) shortcut is already in use or unavailable (error \(status)). Choose a different key combination in Settings.")
            }
            hotKeys.append(hotKey)
        }
    }

    private func unregisterHotKeys() {
        for hotKey in hotKeys { UnregisterEventHotKey(hotKey) }
        hotKeys.removeAll()
        pressed.removeAll()
    }
}
