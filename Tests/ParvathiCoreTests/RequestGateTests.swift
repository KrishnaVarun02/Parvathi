import Foundation
import Testing
@testable import ParvathiCore

struct RequestGateTests {
    @Test func testCancellationPreventsPendingAction() async {
        await MainActor.run {
            let gate = RequestGate()
            let request = gate.begin()
            gate.cancel()
            #expect(!gate.isCurrent(request))
            #expect(throws: CancellationError.self) { try gate.check(request) }
            #expect(throws: CancellationError.self) { try gate.claimAction(request) }
        }
    }

    @Test func testSupersededRequestCannotActOrFinishNewRequest() async throws {
        try await MainActor.run {
            let gate = RequestGate()
            let first = gate.begin()
            let second = gate.begin()
            #expect(throws: (any Error).self) { try gate.claimAction(first) }
            gate.finish(first)
            #expect(gate.isCurrent(second))
            try gate.claimAction(second)
        }
    }

    @Test func testRetriesCannotExecuteAnActionTwice() async throws {
        try await MainActor.run {
            let gate = RequestGate()
            let request = gate.begin()
            try gate.claimAction(request)
            let error = #expect(throws: PipelineError.self) { try gate.claimAction(request) }
            #expect(error?.localizedDescription.contains("already been executed") == true)
            // A new explicit recording is an independent request and can act once.
            let next = gate.begin()
            try gate.claimAction(next)
            gate.finish(next)
            #expect(throws: (any Error).self) { try gate.claimAction(next) }
        }
    }

    @Test func testStructuredTaskCancellationPreventsAnActionEvenIfGateIsStillCurrent() async {
        let task = Task { @MainActor in
            let gate = RequestGate()
            let request = gate.begin()
            do { try await Task.sleep(nanoseconds: 5_000_000_000) } catch {}
            #expect(gate.isCurrent(request))
            #expect(throws: CancellationError.self) { try gate.claimAction(request) }
        }
        task.cancel()
        await task.value
    }
}
