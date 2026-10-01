import Testing
@testable import ParvathiCore

struct ApplicationResolverTests {
    private let chrome = AppDescriptor(id: "com.google.Chrome", name: "Google Chrome", path: "/Applications/Google Chrome.app")
    private let chromeBeta = AppDescriptor(id: "com.google.Chrome.beta", name: "Google Chrome Beta", path: "/Applications/Google Chrome Beta.app")
    private let code = AppDescriptor(id: "com.microsoft.VSCode", name: "Visual Studio Code", path: "/Applications/Visual Studio Code.app")

    @Test func testAliasesAndExactMatchesWinOverPartialMatches() throws {
        let apps = [chrome, chromeBeta, code]
        #expect(try ApplicationResolver.resolve(query: "Chrome", applications: apps) == chrome)
        #expect(try ApplicationResolver.resolve(query: "Google Chrome", applications: apps) == chrome)
        #expect(try ApplicationResolver.resolve(query: "VS Code", applications: apps) == code)
        #expect(try ApplicationResolver.resolve(query: "  VISUAL   STUDIO Code.app  ", applications: apps) == code)
        #expect(try ApplicationResolver.resolve(query: "com.microsoft.VSCode", applications: apps) == code)
    }

    @Test func testUniquePrefixAndSubstring() throws {
        #expect(try ApplicationResolver.resolve(query: "Visual", applications: [chrome, code]) == code)
        #expect(try ApplicationResolver.resolve(query: "Studio", applications: [chrome, code]) == code)
    }

    @Test func testAmbiguityListsCandidatesInsteadOfChoosingArbitrarily() {
        let error = #expect(throws: PipelineError.self) { try ApplicationResolver.resolve(query: "Google", applications: [chrome, chromeBeta]) }
        #expect(error?.localizedDescription.contains("Google Chrome") == true)
        #expect(error?.localizedDescription.contains("Google Chrome Beta") == true)
        #expect(error?.localizedDescription.contains("Which application") == true)
    }

    @Test func testMissingApplicationAndBlankQueryFail() {
        #expect(throws: (any Error).self) { try ApplicationResolver.resolve(query: "Photoshop", applications: [chrome]) }
        #expect(throws: (any Error).self) { try ApplicationResolver.resolve(query: "  ", applications: [chrome]) }
    }

    @Test func testDuplicateDiscoveryOfSamePathDoesNotCauseFalseAmbiguity() throws {
        #expect(try ApplicationResolver.resolve(query: "Chrome", applications: [chrome, chrome]) == chrome)
    }

    @Test func testDifferentCopiesRemainAmbiguous() {
        let otherChrome = AppDescriptor(id: chrome.id, name: chrome.name, path: "/Users/test/Applications/Google Chrome.app")
        #expect(throws: (any Error).self) { try ApplicationResolver.resolve(query: "Chrome", applications: [chrome, otherChrome]) }
    }
}
