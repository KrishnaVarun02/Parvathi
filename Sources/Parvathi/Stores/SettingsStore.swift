import Foundation
import Combine
import Carbon.HIToolbox
import ParvathiCore

struct UserSettings: Codable {
    var locale = "en-US"
    var onDeviceOnly = true
    var style = TranscriptionStyle.verbatim
    var handsFree = false
    var spokenResponses = true
    var voiceIdentifier = ""
    var keepHistory = false
    var provider = AIConfiguration.Provider.openAI
    var baseURL = "https://api.openai.com/v1"
    var model = "gpt-6-astra"
    var dictationKey: UInt32 = 49 // Space
    var commandKey: UInt32 = 8 // C
    var assistantKey: UInt32 = 0 // A
    var modifiers: UInt32 = UInt32(controlKey | optionKey)
}

@MainActor
final class SettingsStore: ObservableObject {
    @Published var value: UserSettings {
        didSet { if let data = try? JSONEncoder().encode(value) { UserDefaults.standard.set(data, forKey: "settings.v1") } }
    }
    init() {
        value = UserDefaults.standard.data(forKey: "settings.v1")
            .flatMap { try? JSONDecoder().decode(UserSettings.self, from: $0) } ?? UserSettings()
    }
    static let keychainAccount = "ai-provider-key"
    func makeProvider() throws -> any AIProvider {
        HTTPAIProvider(configuration: AIConfiguration(
            provider: value.provider, baseURL: value.baseURL,
            model: value.model, apiKey: value.provider == .openAI ? try KeychainStore.load(account: Self.keychainAccount) : nil))
    }
    func shortcutLabel(key: UInt32) -> String {
        let prefix = value.modifiers == UInt32(controlKey | optionKey) ? "⌃⌥" : "⌘⇧"
        return prefix + (Self.keys.first { $0.1 == key }?.0 ?? "?")
    }
    static let keys: [(String, UInt32)] = [("Space", 49), ("A", 0), ("C", 8), ("D", 2), ("F", 3), ("J", 38), ("P", 35), ("R", 15), ("V", 9)]
}

struct HistoryEntry: Codable, Identifiable {
    var id = UUID()
    let date: Date
    let mode: String
    let transcript: String
    let outcome: String
}

@MainActor
final class HistoryStore: ObservableObject {
    @Published private(set) var entries: [HistoryEntry] = []
    private var file: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("Parvathi/history.json")
    }
    init(enabled: Bool) {
        if enabled, let data = try? Data(contentsOf: file), let saved = try? JSONDecoder().decode([HistoryEntry].self, from: data) { entries = saved }
        if !enabled { clear() }
    }
    func add(mode: InteractionMode, transcript: String, outcome: String) {
        entries.insert(HistoryEntry(date: Date(), mode: mode.rawValue, transcript: transcript, outcome: outcome), at: 0)
        entries = Array(entries.prefix(100))
        do {
            try FileManager.default.createDirectory(at: file.deletingLastPathComponent(), withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
            let data = try JSONEncoder().encode(entries)
            try data.write(to: file, options: .atomic)
            try FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: file.path)
        } catch { /* Optional history never prevents the primary voice operation. */ }
    }
    func clear() { entries = []; try? FileManager.default.removeItem(at: file) }
}
