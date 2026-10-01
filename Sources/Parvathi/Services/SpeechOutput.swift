import AVFoundation
import Foundation

@MainActor
final class SpeechOutput: NSObject, AVSpeechSynthesizerDelegate {
    private let synthesizer = AVSpeechSynthesizer()
    private var utterance: AVSpeechUtterance?
    private var completion: (() -> Void)?
    private var deadline: Task<Void, Never>?

    override init() {
        super.init()
        synthesizer.delegate = self
    }

    func speak(_ text: String, voiceIdentifier: String?, onFinished: @escaping () -> Void) {
        stop()
        let value = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !value.isEmpty else { onFinished(); return }
        let next = AVSpeechUtterance(string: value)
        if let voiceIdentifier, !voiceIdentifier.isEmpty {
            next.voice = AVSpeechSynthesisVoice(identifier: voiceIdentifier)
        }
        next.rate = AVSpeechUtteranceDefaultSpeechRate
        utterance = next
        completion = onFinished
        synthesizer.speak(next)
        deadline = Task { [weak self, weak next] in
            do { try await Task.sleep(for: .seconds(60)) } catch { return }
            guard let self, let next, self.utterance === next else { return }
            self.synthesizer.stopSpeaking(at: .immediate)
            self.finished(next)
        }
    }

    func stop() {
        deadline?.cancel(); deadline = nil
        // Drop completion before the delegate sees cancellation of the previous utterance.
        utterance = nil
        completion = nil
        synthesizer.stopSpeaking(at: .immediate)
    }

    nonisolated func speechSynthesizer(_ synthesizer: AVSpeechSynthesizer, didFinish utterance: AVSpeechUtterance) {
        Task { @MainActor [weak self] in self?.finished(utterance) }
    }

    nonisolated func speechSynthesizer(_ synthesizer: AVSpeechSynthesizer, didCancel utterance: AVSpeechUtterance) {
        Task { @MainActor [weak self] in self?.finished(utterance) }
    }

    private func finished(_ finishedUtterance: AVSpeechUtterance) {
        guard utterance === finishedUtterance else { return }
        deadline?.cancel(); deadline = nil
        utterance = nil
        let action = completion
        completion = nil
        action?()
    }
}
