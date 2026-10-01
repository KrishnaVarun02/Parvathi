import AppKit
import CoreAudio
import ParvathiCore

@MainActor
final class SystemAutomation {
    private let focus: FocusService

    init(focus: FocusService) { self.focus = focus }

    /// Discover bundles on disk rather than trusting a model's application names.
    func discoverApplications() -> [AppDescriptor] {
        let roots = [URL(fileURLWithPath: "/Applications"), URL(fileURLWithPath: "/System/Applications"),
                     FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Applications")]
        var found: [String: AppDescriptor] = [:]
        func add(_ url: URL) {
            let url = url.standardizedFileURL
            guard url.pathExtension.lowercased() == "app", let bundle = Bundle(url: url),
                  bundle.executableURL != nil else { return }
            let name = bundle.object(forInfoDictionaryKey: "CFBundleDisplayName") as? String
                ?? bundle.object(forInfoDictionaryKey: "CFBundleName") as? String
                ?? url.deletingPathExtension().lastPathComponent
            found[url.path] = AppDescriptor(id: bundle.bundleIdentifier ?? url.path, name: name, path: url.path)
        }
        for root in roots {
            guard let enumerator = FileManager.default.enumerator(at: root, includingPropertiesForKeys: [.isDirectoryKey],
                                                                  options: [.skipsHiddenFiles, .skipsPackageDescendants]) else { continue }
            for case let url as URL in enumerator { if url.pathExtension.lowercased() == "app" { add(url) } }
        }
        for running in NSWorkspace.shared.runningApplications { if let url = running.bundleURL { add(url) } }
        return found.values.sorted { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending }
    }

    func execute(_ action: CommandAction, target: FocusSnapshot?) async throws -> ActionResult {
        try Task.checkCancellation()
        switch action {
        case .openApplication(let query):
            let app = try ApplicationResolver.resolve(query: query, applications: discoverApplications())
            return try await openApplication(app)
        case .openWebsite(let url):
            return try openWebsite(url)
        case .searchWeb(let query):
            guard !query.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty, query.count <= 2_000 else {
                throw AutomationError.failed("The web search must contain between 1 and 2,000 characters.")
            }
            var components = URLComponents(string: "https://www.google.com/search")!
            components.queryItems = [URLQueryItem(name: "q", value: query)]
            guard let url = components.url else { throw AutomationError.failed("The search URL could not be created.") }
            return try openWebsite(url)
        case .setVolume(let percent): return try setVolume(percent)
        case .setMuted(let muted): return try setMuted(muted)
        case .typeText(let text):
            guard let target else { throw AutomationError.noTextTarget }
            guard !text.isEmpty, text.count <= 20_000 else {
                throw AutomationError.failed("Typing requires between 1 and 20,000 characters.")
            }
            return try await focus.insert(text: text, into: target)
        }
    }

    private func openApplication(_ descriptor: AppDescriptor) async throws -> ActionResult {
        try Task.checkCancellation()
        let url = URL(fileURLWithPath: descriptor.path)
        guard url.pathExtension.lowercased() == "app", Bundle(url: url)?.executableURL != nil else {
            throw AutomationError.failed("That application is no longer installed. Try its full application name.")
        }
        let running: NSRunningApplication
        if let existing = NSWorkspace.shared.runningApplications.first(where: { $0.bundleURL?.standardizedFileURL == url.standardizedFileURL }) {
            running = existing
        } else {
            let state = LaunchState()
            let configuration = NSWorkspace.OpenConfiguration()
            configuration.activates = false
            configuration.createsNewApplicationInstance = false
            // NSWorkspace's callback has no cancellation API. Launch in the background, then only
            // activate after our cancellable wait succeeds; late callbacks never activate windows.
            NSWorkspace.shared.openApplication(at: url, configuration: configuration) { application, error in
                Task { @MainActor in
                    if let application { state.result = .success(application) }
                    else { state.result = .failure(error ?? AutomationError.failed("macOS could not launch \(descriptor.name).")) }
                }
            }
            let deadline = ContinuousClock.now + .seconds(10)
            while state.result == nil {
                try Task.checkCancellation()
                guard ContinuousClock.now < deadline else {
                    throw AutomationError.failed("Opening \(descriptor.name) timed out. macOS may still finish launching it in the background; check before retrying.")
                }
                try await Task.sleep(for: .milliseconds(50))
            }
            running = try state.result!.get()
        }
        try Task.checkCancellation()
        guard running.activate(options: [.activateAllWindows]) else {
            return ActionResult(message: "\(descriptor.name) is running, but macOS did not confirm activation.", verified: false)
        }
        let deadline = ContinuousClock.now + .seconds(2)
        repeat {
            if NSWorkspace.shared.frontmostApplication?.processIdentifier == running.processIdentifier {
                return ActionResult(message: "Opened \(descriptor.name).", verified: true)
            }
            if Task.isCancelled { break }
            try? await Task.sleep(for: .milliseconds(50))
        } while ContinuousClock.now < deadline
        return ActionResult(message: "Activation was requested for \(descriptor.name), but foreground focus was not verified.", verified: false)
    }

    private func openWebsite(_ url: URL) throws -> ActionResult {
        try Task.checkCancellation()
        guard ["https", "http"].contains(url.scheme?.lowercased() ?? ""),
              let host = url.host, !host.isEmpty, url.user == nil, url.password == nil,
              url.absoluteString.count <= 8_192 else {
            throw AutomationError.failed("Only valid HTTP or HTTPS website URLs without embedded credentials are supported.")
        }
        guard NSWorkspace.shared.open(url) else {
            throw AutomationError.failed("macOS could not open the website. Check that a default browser is installed.")
        }
        return ActionResult(message: "Sent \(host) to your default browser. Page loading is not verified.", verified: false)
    }

    private func setVolume(_ percent: Int) throws -> ActionResult {
        guard (0...100).contains(percent) else { throw AutomationError.failed("Volume must be between 0 and 100 percent.") }
        let device = try defaultOutputDevice()
        let addresses = try writableAddresses(device: device, selector: kAudioDevicePropertyVolumeScalar)
        let scalar = Float32(percent) / 100
        for var address in addresses {
            try Task.checkCancellation()
            var value = scalar
            guard AudioObjectSetPropertyData(device, &address, 0, nil, UInt32(MemoryLayout<Float32>.size), &value) == noErr else {
                throw AutomationError.failed("The audio device rejected the volume change. Some channels may have changed; check the device volume.")
            }
        }
        guard try defaultOutputDevice() == device else {
            return ActionResult(message: "The audio output changed during the request; current volume is unverified.", verified: false)
        }
        let verified = addresses.allSatisfy { address in
            var address = address
            var value: Float32 = 0
            var size = UInt32(MemoryLayout<Float32>.size)
            return AudioObjectGetPropertyData(device, &address, 0, nil, &size, &value) == noErr && abs(value - scalar) <= 0.015
        }
        return ActionResult(message: verified ? "Volume set to \(percent) percent." : "Volume change was sent, but the device did not confirm \(percent) percent.", verified: verified)
    }

    private func setMuted(_ muted: Bool) throws -> ActionResult {
        let device = try defaultOutputDevice()
        let addresses = try writableAddresses(device: device, selector: kAudioDevicePropertyMute)
        for var address in addresses {
            try Task.checkCancellation()
            var value: UInt32 = muted ? 1 : 0
            guard AudioObjectSetPropertyData(device, &address, 0, nil, UInt32(MemoryLayout<UInt32>.size), &value) == noErr else {
                throw AutomationError.failed("The audio device rejected the mute change. Check its hardware volume controls.")
            }
        }
        guard try defaultOutputDevice() == device else {
            return ActionResult(message: "The audio output changed during the request; mute state is unverified.", verified: false)
        }
        let verified = addresses.allSatisfy { address in
            var address = address
            var value: UInt32 = 0
            var size = UInt32(MemoryLayout<UInt32>.size)
            return AudioObjectGetPropertyData(device, &address, 0, nil, &size, &value) == noErr && (value != 0) == muted
        }
        return ActionResult(message: verified ? (muted ? "System output muted." : "System output unmuted.") : "Mute change was sent, but the device state is unverified.", verified: verified)
    }

    private func defaultOutputDevice() throws -> AudioDeviceID {
        var address = AudioObjectPropertyAddress(mSelector: kAudioHardwarePropertyDefaultOutputDevice,
                                                 mScope: kAudioObjectPropertyScopeGlobal, mElement: kAudioObjectPropertyElementMain)
        var device = AudioDeviceID(0)
        var size = UInt32(MemoryLayout<AudioDeviceID>.size)
        guard AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject), &address, 0, nil, &size, &device) == noErr,
              device != kAudioObjectUnknown else { throw AutomationError.failed("No default audio output device is available.") }
        return device
    }

    private func writableAddresses(device: AudioDeviceID, selector: AudioObjectPropertySelector) throws -> [AudioObjectPropertyAddress] {
        func writable(_ element: AudioObjectPropertyElement) -> AudioObjectPropertyAddress? {
            var address = AudioObjectPropertyAddress(mSelector: selector, mScope: kAudioDevicePropertyScopeOutput, mElement: element)
            var settable = DarwinBoolean(false)
            guard AudioObjectHasProperty(device, &address), AudioObjectIsPropertySettable(device, &address, &settable) == noErr,
                  settable.boolValue else { return nil }
            return address
        }
        if let main = writable(kAudioObjectPropertyElementMain) { return [main] }
        let channels = (1...32).compactMap { writable(AudioObjectPropertyElement($0)) }
        guard !channels.isEmpty else {
            throw AutomationError.failed("This audio output does not expose software volume or mute controls. Use its hardware controls or select another output in System Settings → Sound.")
        }
        return channels
    }
}

@MainActor
private final class LaunchState {
    var result: Result<NSRunningApplication, Error>?
}
