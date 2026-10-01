import Foundation

/// Serializes request ownership on the main actor, and prevents replaying an action.
/// Call check after each suspension point and claimAction immediately before a side effect.
@MainActor
public final class RequestGate {
    private var current: UUID?
    private var actionClaimed = false

    public init() {}

    @discardableResult
    public func begin() -> UUID {
        let id = UUID()
        current = id
        actionClaimed = false
        return id
    }

    public func cancel() {
        current = nil
        actionClaimed = false
    }

    public func isCurrent(_ id: UUID) -> Bool { current == id }

    public func check(_ id: UUID) throws {
        guard current == id, !Task.isCancelled else { throw CancellationError() }
    }

    public func claimAction(_ id: UUID) throws {
        try check(id)
        guard !actionClaimed else {
            throw PipelineError("This request has already been executed. Record a new command to run it again.")
        }
        actionClaimed = true
    }

    public func finish(_ id: UUID) {
        // A stale completion must never cancel a newer request.
        guard current == id else { return }
        current = nil
        actionClaimed = false
    }
}
