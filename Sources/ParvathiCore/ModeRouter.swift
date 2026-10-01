import Foundation

/// A mode is an explicit user choice. Transcript content never changes that choice.
public enum RoutedInput: Equatable, Sendable {
    case dictation(String)
    case command(CommandAction)
    case assistant(String)
    case rewrite(String)
    case explain(String)
}

public enum ModeRouter {
    public static func route(_ text: String, mode: InteractionMode) throws -> RoutedInput {
        guard !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            throw PipelineError("No speech was recognized. Try recording again.")
        }
        guard text.utf8.count <= 100_000 else {
            throw PipelineError("The transcript is too long. Record a shorter passage.")
        }
        switch mode {
        case .dictation: return .dictation(text)
        case .command: return .command(try CommandRouter.route(text))
        case .assistant: return .assistant(text)
        case .rewrite: return .rewrite(text)
        case .explain: return .explain(text)
        }
    }
}
