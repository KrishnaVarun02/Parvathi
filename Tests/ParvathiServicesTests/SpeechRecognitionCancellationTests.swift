import Foundation
import Testing
@testable import Parvathi

struct SpeechRecognitionCancellationTests {
    @Test @MainActor
    func preCanceledStartDoesNotTouchMicrophoneOrResetAnotherSession() async {
        let service = SpeechRecognitionService()
        var audioCallbacks = 0
        service.onLevel = { _ in audioCallbacks += 1 }

        // This task cannot run until the current main-actor test yields. Cancel it
        // first, reproducing a superseded recording queued by a rapid shortcut.
        let task = Task { @MainActor in
            try await service.start(localeIdentifier: "en-US", onDeviceOnly: true)
        }
        task.cancel()

        await #expect(throws: CancellationError.self) { try await task.value }
        // cancel() emits a zero audio level. No callback proves the canceled start
        // returned before resetting any existing service state or asking permission.
        #expect(audioCallbacks == 0)
    }
}
