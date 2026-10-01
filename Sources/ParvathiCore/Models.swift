import Foundation

public enum InteractionMode: String, CaseIterable, Codable, Sendable {
    case dictation, command, assistant, rewrite, explain

    public var title: String { rawValue.capitalized }
}

public enum TranscriptionStyle: String, CaseIterable, Codable, Sendable {
    case verbatim, polished
}

public enum AssistantPhase: String, CaseIterable, Codable, Sendable {
    case idle, listening, transcribing, thinking, executing, speaking
}

public enum CommandAction: Equatable, Sendable {
    case openApplication(String)
    case openWebsite(URL)
    case searchWeb(String)
    case setVolume(Int)
    case setMuted(Bool)
    case typeText(String)
}

public struct PipelineError: LocalizedError, Equatable, Sendable {
    public let message: String
    public init(_ message: String) { self.message = message }
    public var errorDescription: String? { message }
}

public struct AppDescriptor: Identifiable, Codable, Equatable, Sendable {
    public let id: String
    public let name: String
    public let path: String

    public init(id: String, name: String, path: String) {
        self.id = id
        self.name = name
        self.path = path
    }
}

public struct CommandExample: Identifiable, Equatable, Sendable {
    public var id: String { phrase }
    public let title: String
    public let phrase: String
    public let details: String

    public init(title: String, phrase: String, details: String) {
        self.title = title
        self.phrase = phrase
        self.details = details
    }
}

public enum CommandCatalog {
    public static let examples: [CommandExample] = [
        .init(title: "Open an application", phrase: "Open Chrome", details: "Opens or focuses an installed application. Ambiguous names need clarification."),
        .init(title: "Switch applications", phrase: "Switch to Visual Studio Code", details: "Focuses the named application, launching it if necessary."),
        .init(title: "Open a website", phrase: "Open website https://example.com", details: "Opens an HTTP or HTTPS address in your default browser."),
        .init(title: "Search the web", phrase: "Search the web for vegetarian dinner recipes", details: "Opens a search in your browser. Search results are not automatically read by the assistant."),
        .init(title: "Set volume", phrase: "Set the volume to 30 percent", details: "Sets the system output volume from zero to one hundred percent."),
        .init(title: "Mute sound", phrase: "Mute", details: "Mutes system audio. Say unmute to restore sound."),
        .init(title: "Type supplied text", phrase: "Type: I’ll join the meeting in five minutes.", details: "Inserts the supplied text into the previously focused text field."),
    ]
}
