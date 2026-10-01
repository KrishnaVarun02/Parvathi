import Foundation

public enum ApplicationResolver {
    public static func resolve(query: String, applications: [AppDescriptor]) throws -> AppDescriptor {
        let needle = normalize(query)
        guard !needle.isEmpty, needle.count <= 160 else {
            throw PipelineError("Specify the name of an installed application.")
        }
        // The catalog may find the same bundle twice through distinct search roots.
        var seenPaths = Set<String>()
        let apps = applications.filter { seenPaths.insert($0.path).inserted }
        let exact = apps.filter { normalize($0.name) == needle || $0.id.lowercased() == query.lowercased() }
        if !exact.isEmpty { return try unique(exact, query: query) }

        let aliases: [String: [String]] = [
            "chrome": ["google chrome"],
            "google chrome": ["google chrome"],
            "code": ["visual studio code"],
            "vs code": ["visual studio code"],
            "vscode": ["visual studio code"],
            "visual studio": ["visual studio code", "visual studio"],
            "edge": ["microsoft edge"],
            "word": ["microsoft word"],
            "excel": ["microsoft excel"],
            "powerpoint": ["microsoft powerpoint"],
            "terminal": ["terminal"],
        ]
        if let names = aliases[needle] {
            let aliased = apps.filter { names.contains(normalize($0.name)) }
            if !aliased.isEmpty { return try unique(aliased, query: query) }
        }

        let prefixes = apps.filter { normalize($0.name).hasPrefix(needle) }
        if !prefixes.isEmpty { return try unique(prefixes, query: query) }
        let partials = apps.filter { normalize($0.name).contains(needle) }
        if !partials.isEmpty { return try unique(partials, query: query) }
        throw PipelineError("I couldn’t find an installed application named ‘\(query)’. Use the application’s full name. Parvathi scans standard Applications folders each time.")
    }

    private static func unique(_ matches: [AppDescriptor], query: String) throws -> AppDescriptor {
        guard matches.count == 1, let app = matches.first else {
            let choices = matches.sorted { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending }
                .prefix(6).map { $0.name }.joined(separator: ", ")
            throw PipelineError("‘\(query)’ matches several applications: \(choices). Which application do you mean? Say its full name.")
        }
        return app
    }

    private static func normalize(_ input: String) -> String {
        var name = input.trimmingCharacters(in: .whitespacesAndNewlines)
            .folding(options: [.caseInsensitive, .diacriticInsensitive], locale: Locale(identifier: "en_US_POSIX"))
        if name.hasSuffix(".app") { name.removeLast(4) }
        return name.split(whereSeparator: { $0.isWhitespace }).joined(separator: " ")
    }
}
