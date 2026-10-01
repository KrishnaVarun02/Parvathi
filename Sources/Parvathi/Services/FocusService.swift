import AppKit
import ApplicationServices

struct ActionResult {
    let message: String
    let verified: Bool
}

enum AutomationError: LocalizedError {
    case accessibilityPermission, noTextTarget, protectedField, targetChanged, clipboardUnavailable
    case failed(String)

    var errorDescription: String? {
        switch self {
        case .accessibilityPermission:
            return "Allow Parvathi in System Settings → Privacy & Security → Accessibility, then try again."
        case .noTextTarget:
            return "Click an editable text field in another app before using the dictation shortcut. This editor must expose its text and cursor through macOS Accessibility."
        case .protectedField:
            return "Parvathi does not read or insert text in password or protected fields."
        case .targetChanged:
            return "The app, field, cursor, or text changed while you were speaking. Nothing was inserted. Click the intended field and try again."
        case .clipboardUnavailable:
            return "The clipboard could not be safely preserved. Your text is available in Parvathi to copy manually."
        case .failed(let message): return message
        }
    }
}

/// A short-lived, in-memory snapshot. Its text is never written to history or logs.
struct FocusSnapshot {
    let pid: pid_t
    let appName: String
    let element: AXUIElement
    let range: CFRange
    let value: String
    fileprivate let activationRevision: UInt64
}

/// The narrow AppKit/Accessibility boundary for inserting into another process.
@MainActor
final class FocusService {
    private var activationRevision: UInt64 = 0
    private var activationObserver: NSObjectProtocol?

    init() {
        activationObserver = NSWorkspace.shared.notificationCenter.addObserver(
            forName: NSWorkspace.didActivateApplicationNotification, object: nil, queue: .main
        ) { [weak self] _ in
            MainActor.assumeIsolated { self?.activationRevision &+= 1 }
        }
    }

    deinit {
        if let activationObserver { NSWorkspace.shared.notificationCenter.removeObserver(activationObserver) }
    }

    func capture() throws -> FocusSnapshot {
        try Task.checkCancellation()
        guard AXIsProcessTrusted() else { throw AutomationError.accessibilityPermission }
        guard let app = NSWorkspace.shared.frontmostApplication,
              app.processIdentifier != ProcessInfo.processInfo.processIdentifier else {
            throw AutomationError.noTextTarget
        }
        let element = try focusedElement(pid: app.processIdentifier)
        try rejectProtected(element)
        // Requiring both makes it possible to prove that the insertion point has not moved.
        guard let value = stringAttribute(element, kAXValueAttribute), let range = selectedRange(element),
              valid(range, for: value) else { throw AutomationError.noTextTarget }
        let role = stringAttribute(element, kAXRoleAttribute) ?? ""
        guard [kAXTextFieldRole, kAXTextAreaRole, kAXComboBoxRole].contains(role) else {
            throw AutomationError.noTextTarget
        }
        return FocusSnapshot(pid: app.processIdentifier, appName: app.localizedName ?? "the previous app",
                             element: element, range: range, value: value, activationRevision: activationRevision)
    }

    func selectedText(in snapshot: FocusSnapshot) throws -> String {
        try validate(snapshot)
        guard snapshot.range.length > 0 else {
            throw AutomationError.failed("Select the text you want to rewrite or explain, then use the shortcut again.")
        }
        return (snapshot.value as NSString).substring(with: NSRange(location: snapshot.range.location, length: snapshot.range.length))
    }

    func insert(text: String, into snapshot: FocusSnapshot) async throws -> ActionResult {
        guard !text.isEmpty else { throw AutomationError.failed("There is no text to insert.") }
        try validate(snapshot)
        let expected = (snapshot.value as NSString).replacingCharacters(
            in: NSRange(location: snapshot.range.location, length: snapshot.range.length), with: text)
        var writable = DarwinBoolean(false)
        let canWrite = AXUIElementIsAttributeSettable(snapshot.element, kAXSelectedTextAttribute as CFString, &writable)
        if canWrite == .success, writable.boolValue {
            try validate(snapshot)
            let result = AXUIElementSetAttributeValue(snapshot.element, kAXSelectedTextAttribute as CFString, text as CFString)
            if result == .success {
                return await verifyInsertion(expected: expected, snapshot: snapshot, method: "Accessibility")
            }
            // An IPC timeout can mean the receiving app already changed. Retrying could duplicate text.
            guard result == .attributeUnsupported || result == .notImplemented else {
                throw AutomationError.failed("The editor did not acknowledge insertion (Accessibility error \(result.rawValue)). Check the field before trying again; Parvathi did not retry.")
            }
        }
        return try await paste(text: text, expected: expected, into: snapshot)
    }

    private func paste(text: String, expected: String, into snapshot: FocusSnapshot) async throws -> ActionResult {
        try validate(snapshot)
        let board = NSPasteboard.general
        let initialChangeCount = board.changeCount
        var saved: [[NSPasteboard.PasteboardType: Data]] = []
        for item in board.pasteboardItems ?? [] {
            var representations: [NSPasteboard.PasteboardType: Data] = [:]
            for type in item.types {
                guard let data = item.data(forType: type) else { throw AutomationError.clipboardUnavailable }
                representations[type] = data
            }
            saved.append(representations)
        }
        guard board.changeCount == initialChangeCount else { throw AutomationError.clipboardUnavailable }
        guard let down = CGEvent(keyboardEventSource: nil, virtualKey: 9, keyDown: true),
              let up = CGEvent(keyboardEventSource: nil, virtualKey: 9, keyDown: false) else {
            throw AutomationError.failed("macOS could not create the paste keyboard event.")
        }
        try validate(snapshot)
        board.clearContents()
        guard board.setString(text, forType: .string) else {
            restore(saved, board: board, expectedChangeCount: board.changeCount)
            throw AutomationError.clipboardUnavailable
        }
        let ownedChangeCount = board.changeCount
        defer { restore(saved, board: board, expectedChangeCount: ownedChangeCount) }
        try validate(snapshot)
        down.flags = .maskCommand
        up.flags = .maskCommand
        // Post to the captured process, never to a newly foregrounded app.
        down.postToPid(snapshot.pid)
        up.postToPid(snapshot.pid)
        // A canceled Task.sleep returns immediately. Restoring immediately after dispatch could
        // cause the receiving app to paste the user's OLD clipboard instead of the transcript.
        // Allow one short, nonblocking delivery window even on cancellation; no new edit is sent.
        await withCheckedContinuation { continuation in
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.2) { continuation.resume() }
        }
        return await verifyInsertion(expected: expected, snapshot: snapshot, method: "Paste")
    }

    private func restore(_ saved: [[NSPasteboard.PasteboardType: Data]], board: NSPasteboard, expectedChangeCount: Int) {
        // Never overwrite something the user copied while the paste was in flight.
        guard board.changeCount == expectedChangeCount else { return }
        board.clearContents()
        let items = saved.map { representations in
            let item = NSPasteboardItem()
            for (type, data) in representations { item.setData(data, forType: type) }
            return item
        }
        if !items.isEmpty { board.writeObjects(items) }
    }

    private func verifyInsertion(expected: String, snapshot: FocusSnapshot, method: String) async -> ActionResult {
        // No retry after dispatch. Cancellation stops the wait, but cannot undo a dispatched edit.
        let deadline = ContinuousClock.now + .seconds(1)
        repeat {
            if stringAttribute(snapshot.element, kAXValueAttribute) == expected {
                return ActionResult(message: "Inserted into \(snapshot.appName).", verified: true)
            }
            if Task.isCancelled { break }
            try? await Task.sleep(for: .milliseconds(50))
        } while ContinuousClock.now < deadline
        return ActionResult(message: "\(method) was sent to \(snapshot.appName), but the resulting text could not be verified. Check the field before retrying.", verified: false)
    }

    private func validate(_ snapshot: FocusSnapshot) throws {
        try Task.checkCancellation()
        guard AXIsProcessTrusted() else { throw AutomationError.accessibilityPermission }
        guard activationRevision == snapshot.activationRevision,
              NSWorkspace.shared.frontmostApplication?.processIdentifier == snapshot.pid,
              let focused = try? focusedElement(pid: snapshot.pid), CFEqual(focused, snapshot.element) else {
            throw AutomationError.targetChanged
        }
        try rejectProtected(snapshot.element)
        guard let range = selectedRange(snapshot.element), range.location == snapshot.range.location,
              range.length == snapshot.range.length,
              stringAttribute(snapshot.element, kAXValueAttribute) == snapshot.value else {
            throw AutomationError.targetChanged
        }
    }

    private func focusedElement(pid: pid_t) throws -> AXUIElement {
        let app = AXUIElementCreateApplication(pid)
        AXUIElementSetMessagingTimeout(app, 0.2)
        var raw: CFTypeRef?
        guard AXUIElementCopyAttributeValue(app, kAXFocusedUIElementAttribute as CFString, &raw) == .success,
              let raw, CFGetTypeID(raw) == AXUIElementGetTypeID() else { throw AutomationError.noTextTarget }
        let element = unsafeBitCast(raw, to: AXUIElement.self)
        AXUIElementSetMessagingTimeout(element, 0.2)
        return element
    }

    private func rejectProtected(_ element: AXUIElement) throws {
        let subrole = stringAttribute(element, kAXSubroleAttribute) ?? ""
        let role = stringAttribute(element, kAXRoleAttribute) ?? ""
        if subrole.localizedCaseInsensitiveContains("secure") || role.localizedCaseInsensitiveContains("secure") {
            throw AutomationError.protectedField
        }
        for name in ["AXProtectedContent", "AXIsProtected"] {
            var raw: CFTypeRef?
            if AXUIElementCopyAttributeValue(element, name as CFString, &raw) == .success,
               let protected = raw as? Bool, protected { throw AutomationError.protectedField }
        }
    }

    private func stringAttribute(_ element: AXUIElement, _ name: String) -> String? {
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, name as CFString, &value) == .success else { return nil }
        return value as? String
    }

    private func selectedRange(_ element: AXUIElement) -> CFRange? {
        var raw: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, kAXSelectedTextRangeAttribute as CFString, &raw) == .success,
              let raw, CFGetTypeID(raw) == AXValueGetTypeID() else { return nil }
        let value = unsafeBitCast(raw, to: AXValue.self)
        var range = CFRange()
        guard AXValueGetType(value) == .cfRange, AXValueGetValue(value, .cfRange, &range) else { return nil }
        return range
    }

    private func valid(_ range: CFRange, for value: String) -> Bool {
        let length = (value as NSString).length
        return range.location >= 0 && range.length >= 0 && range.location <= length && range.length <= length - range.location
    }
}
