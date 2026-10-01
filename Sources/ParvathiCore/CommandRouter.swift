import Foundation

/// A deliberately small grammar. Language-model output never becomes executable code.
public enum CommandRouter {
    public static func route(_ text: String) throws -> CommandAction {
        let command = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !command.isEmpty else { throw PipelineError("Say one supported command, such as ‘Open Chrome’.") }

        // Text after Type is data, including words that look like another command.
        if let suppliedText = capture(#"^type(?:\s*:\s*|\s+)([\s\S]+)$"#, command) {
            guard suppliedText.utf8.count <= 50_000 else {
                throw PipelineError("That text is too long. Type a shorter passage.")
            }
            guard !suppliedText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
                throw PipelineError("What would you like me to type? Say ‘Type’, followed by your text.")
            }
            guard !suppliedText.unicodeScalars.contains(where: { CharacterSet.controlCharacters.contains($0) && $0 != "\n" && $0 != "\t" && $0 != "\r" }) else {
                throw PipelineError("The text contains unsupported control characters.")
            }
            return .typeText(suppliedText)
        }

        guard command.utf8.count <= 4_096 else { throw PipelineError("That command is too long. Say a shorter command.") }
        guard !command.unicodeScalars.contains(where: { CharacterSet.controlCharacters.contains($0) }) else {
            throw PipelineError("Say one command at a time.")
        }
        if capture(#"(?:;\s*|&&\s*|\|\|\s*|\bthen\s+|\band\s+)(open|switch|launch|search|set|mute|unmute|type|run|delete|send|buy)\b"#, command) != nil {
            throw PipelineError("Say one command at a time. Multi-step commands are not enabled.")
        }

        if command.range(of: #"^(?:mute|mute (?:the )?(?:system |computer )?(?:audio|sound|volume))[.!?]?$"#, options: [.regularExpression, .caseInsensitive]) != nil {
            return .setMuted(true)
        }
        if command.range(of: #"^(?:unmute|unmute (?:the )?(?:system |computer )?(?:audio|sound|volume))[.!?]?$"#, options: [.regularExpression, .caseInsensitive]) != nil {
            return .setMuted(false)
        }

        if let amount = capture(#"^(?:set\s+)?(?:the\s+)?(?:system\s+)?volume\s+(?:to\s+)?(.+?)(?:\s*percent|\s*%)?[.!?]?$"#, command) {
            guard let percent = parsePercentage(amount), (0...100).contains(percent) else {
                throw PipelineError("Choose a volume from 0 to 100 percent, such as ‘Set volume to 30 percent’.")
            }
            return .setVolume(percent)
        }

        if let query = capture(#"^search\s+(?:(?:the\s+)?web\s+)?for\s+(.+)$"#, command) {
            let query = query.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !query.isEmpty, query.utf8.count <= 2_000 else {
                throw PipelineError("Provide a search phrase shorter than 2,000 bytes.")
            }
            return .searchWeb(query)
        }

        if let address = capture(#"^(?:open\s+(?:the\s+)?(?:website|url)|go\s+to|visit)\s+(.+)$"#, command) {
            return .openWebsite(try validatedWebsite(address))
        }

        if let target = capture(#"^(?:open|launch|switch\s+to|focus)\s+(?:(?:the\s+)?app(?:lication)?\s+)?(.+)$"#, command) {
            let target = target.trimmingCharacters(in: .whitespacesAndNewlines)
            if looksLikeWebsite(target) { return .openWebsite(try validatedWebsite(target)) }
            let appName = target.trimmingCharacters(in: CharacterSet.whitespacesAndNewlines.union(CharacterSet(charactersIn: ".!?")))
            guard !appName.isEmpty, appName.count <= 160 else {
                throw PipelineError("Specify the name of one installed application.")
            }
            guard appName.range(of: #"(?:\band\b|\bthen\b|[;/\\|<>`$\n])"#, options: [.regularExpression, .caseInsensitive]) == nil else {
                throw PipelineError("Specify one application by name, such as ‘Open Chrome’.")
            }
            return .openApplication(appName)
        }

        throw PipelineError("I couldn’t match that command. Try ‘Open Chrome’, ‘Search the web for …’, ‘Set volume to 30 percent’, ‘Mute’, or ‘Type: …’.")
    }

    /// Public so providers can validate structured URL arguments against the same boundary.
    public static func validatedWebsite(_ input: String) throws -> URL {
        let address = input.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !address.isEmpty, address.utf8.count <= 2_048,
              !address.unicodeScalars.contains(where: { CharacterSet.whitespacesAndNewlines.contains($0) || CharacterSet.controlCharacters.contains($0) }) else {
            throw PipelineError("Provide one website address without spaces, such as https://example.com.")
        }
        let hasScheme = address.range(of: #"^[A-Za-z][A-Za-z0-9+.-]*:"#, options: .regularExpression) != nil
        let value = hasScheme ? address : "https://" + address
        guard let components = URLComponents(string: value),
              let scheme = components.scheme?.lowercased(), ["https", "http"].contains(scheme),
              let host = components.host, !host.isEmpty,
              components.user == nil, components.password == nil,
              components.port.map({ (1...65_535).contains($0) }) ?? true,
              let url = components.url else {
            throw PipelineError("Only HTTP and HTTPS websites without embedded credentials are supported.")
        }
        guard !host.contains("\\"), !host.contains("%"), !host.contains(".."),
              host.unicodeScalars.allSatisfy({ CharacterSet.alphanumerics.contains($0) || ".-:[]".unicodeScalars.contains($0) }) else {
            throw PipelineError("That website address has an invalid hostname.")
        }
        return url
    }

    private static func looksLikeWebsite(_ target: String) -> Bool {
        if target.contains("://") || target.lowercased().hasPrefix("www.") { return true }
        // Speech recognizers add sentence punctuation. "Open Chrome." is an app,
        // while "Open example.com." still has a domain separator after trimming.
        let candidate = target.trimmingCharacters(in: CharacterSet(charactersIn: ".!?"))
        if candidate.lowercased().hasSuffix(".app") { return false }
        return !candidate.contains(" ") && candidate.contains(".")
    }

    private static func capture(_ pattern: String, _ text: String) -> String? {
        guard let regex = try? NSRegularExpression(pattern: pattern, options: .caseInsensitive),
              let match = regex.firstMatch(in: text, range: NSRange(text.startIndex..., in: text)),
              match.numberOfRanges > 1, let range = Range(match.range(at: 1), in: text) else { return nil }
        return String(text[range])
    }

    private static func parsePercentage(_ input: String) -> Int? {
        let normalized = input.lowercased().trimmingCharacters(in: .whitespacesAndNewlines)
        if normalized.range(of: #"^\d{1,3}$"#, options: .regularExpression) != nil { return Int(normalized) }
        let units: [String: Int] = ["zero": 0, "one": 1, "two": 2, "three": 3, "four": 4, "five": 5, "six": 6, "seven": 7, "eight": 8, "nine": 9, "ten": 10, "eleven": 11, "twelve": 12, "thirteen": 13, "fourteen": 14, "fifteen": 15, "sixteen": 16, "seventeen": 17, "eighteen": 18, "nineteen": 19]
        let tens: [String: Int] = ["twenty": 20, "thirty": 30, "forty": 40, "fifty": 50, "sixty": 60, "seventy": 70, "eighty": 80, "ninety": 90]
        if normalized == "one hundred" || normalized == "a hundred" || normalized == "hundred" { return 100 }
        if let value = units[normalized] ?? tens[normalized] { return value }
        let words = normalized.replacingOccurrences(of: "-", with: " ").split(whereSeparator: { $0.isWhitespace }).map(String.init)
        if words.count == 2, let ten = tens[words[0]], let unit = units[words[1]], (1...9).contains(unit) { return ten + unit }
        return nil
    }
}
