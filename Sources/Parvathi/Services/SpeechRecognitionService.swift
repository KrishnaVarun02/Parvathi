import AVFoundation
import Foundation
import Speech

/// Live buffers exist only in memory. The selected macOS default input device is used.
@MainActor
final class SpeechRecognitionService {
    var onPartial: ((String) -> Void)?
    var onLevel: ((Double) -> Void)?
    var onFailure: ((Error) -> Void)?
    var onFinal: (() -> Void)?

    private let engine = AVAudioEngine()
    private var recognizer: SFSpeechRecognizer?
    private var request: SFSpeechAudioBufferRecognitionRequest?
    private var recognitionTask: SFSpeechRecognitionTask?
    private var activeID: UUID?
    private var hasTap = false
    private var transcript = ""
    private var isFinal = false
    private var recognitionError: Error?
    private var finalContinuation: CheckedContinuation<String, Error>?
    private var finalTimeout: Task<Void, Never>?

    func start(localeIdentifier: String, onDeviceOnly: Bool) async throws {
        try Task.checkCancellation()
        cancel()
        let id = UUID()
        activeID = id
        do {
            try await authorize()
            try Task.checkCancellation()
            guard activeID == id else { throw CancellationError() }
            guard let speech = SFSpeechRecognizer(locale: Locale(identifier: localeIdentifier)) else {
                throw RecognitionError.unsupportedLocale(localeIdentifier)
            }
            guard speech.isAvailable else { throw RecognitionError.unavailable }
            if onDeviceOnly && !speech.supportsOnDeviceRecognition {
                throw RecognitionError.onDeviceUnavailable(localeIdentifier)
            }
            recognizer = speech
            let audioRequest = SFSpeechAudioBufferRecognitionRequest()
            audioRequest.shouldReportPartialResults = true
            audioRequest.requiresOnDeviceRecognition = onDeviceOnly
            audioRequest.taskHint = .dictation
            // Verbatim retains the recognizer's words. AI polishing is a separate opt-in step.
            audioRequest.addsPunctuation = false
            request = audioRequest

            let input = engine.inputNode
            let format = input.outputFormat(forBus: 0)
            guard format.sampleRate > 0, format.channelCount > 0 else {
                throw RecognitionError.noInput
            }
            input.installTap(onBus: 0, bufferSize: 1024, format: format) { [weak self] buffer, _ in
                audioRequest.append(buffer)
                var sum = 0.0
                let frames = Int(buffer.frameLength)
                if let channel = buffer.floatChannelData?[0], frames > 0 {
                    for i in 0..<frames { sum += Double(channel[i]) * Double(channel[i]) }
                }
                let rms = frames > 0 ? sqrt(sum / Double(frames)) : 0
                let level = min(1, max(0, (20 * log10(max(rms, 0.000_01)) + 60) / 60))
                Task { @MainActor [weak self] in
                    guard let self, self.activeID == id else { return }
                    self.onLevel?(level)
                }
            }
            hasTap = true
            recognitionTask = speech.recognitionTask(with: audioRequest) { [weak self] result, error in
                let text = result?.bestTranscription.formattedString
                let final = result?.isFinal ?? false
                Task { @MainActor [weak self] in
                    self?.receive(text: text, final: final, error: error, id: id)
                }
            }
            engine.prepare()
            try engine.start()
        } catch {
            if activeID == id { cancel() }
            throw error
        }
    }

    /// Final transcription is required: an incomplete result is never silently inserted.
    func stop() async throws -> String {
        try Task.checkCancellation()
        guard let id = activeID else { throw RecognitionError.notRecording }
        guard finalContinuation == nil else { throw RecognitionError.alreadyStopping }
        stopAudio()
        request?.endAudio()
        if let error = recognitionError {
            cancel()
            throw error
        }
        if isFinal {
            let value = transcript.trimmingCharacters(in: .whitespacesAndNewlines)
            cancel()
            guard !value.isEmpty else { throw RecognitionError.noSpeech }
            return value
        }
        return try await withTaskCancellationHandler {
            try await withCheckedThrowingContinuation { continuation in
                finalContinuation = continuation
                finalTimeout = Task { @MainActor [weak self] in
                    do { try await Task.sleep(for: .seconds(4)) } catch { return }
                    guard let self, self.activeID == id else { return }
                    self.finish(.failure(RecognitionError.finalizationTimeout))
                }
                if Task.isCancelled { cancel() }
            }
        } onCancel: {
            Task { @MainActor [weak self] in
                guard let self, self.activeID == id else { return }
                self.cancel()
            }
        }
    }

    func cancel() {
        // Invalidate callbacks first; cancellation callbacks can arrive after a new session starts.
        activeID = nil
        stopAudio()
        request?.endAudio()
        recognitionTask?.cancel()
        recognitionTask = nil
        request = nil
        recognizer = nil
        transcript = ""
        isFinal = false
        recognitionError = nil
        finalTimeout?.cancel()
        finalTimeout = nil
        let continuation = finalContinuation
        finalContinuation = nil
        continuation?.resume(throwing: CancellationError())
    }

    private func receive(text: String?, final: Bool, error: Error?, id: UUID) {
        guard activeID == id, !isFinal else { return }
        if let text {
            transcript = text
            onPartial?(text)
        }
        if final {
            isFinal = true
            stopAudio()
            request?.endAudio()
            if finalContinuation != nil {
                let value = transcript.trimmingCharacters(in: .whitespacesAndNewlines)
                finish(value.isEmpty ? .failure(RecognitionError.noSpeech) : .success(value))
            } else { onFinal?() }
        } else if let error {
            let usefulError = RecognitionError.engine(error.localizedDescription)
            recognitionError = usefulError
            stopAudio()
            if finalContinuation != nil { finish(.failure(usefulError)) }
            else { onFailure?(usefulError) }
        }
    }

    private func finish(_ result: Result<String, Error>) {
        let continuation = finalContinuation
        finalContinuation = nil
        cancel()
        continuation?.resume(with: result)
    }

    private func stopAudio() {
        engine.stop()
        if hasTap {
            engine.inputNode.removeTap(onBus: 0)
            hasTap = false
        }
        onLevel?(0)
    }

    private func authorize() async throws {
        let microphone = AVCaptureDevice.authorizationStatus(for: .audio)
        if microphone == .notDetermined {
            let granted = await AVCaptureDevice.requestAccess(for: .audio)
            guard granted else { throw RecognitionError.microphoneDenied }
        } else if microphone != .authorized { throw RecognitionError.microphoneDenied }
        try Task.checkCancellation()
        let speech: SFSpeechRecognizerAuthorizationStatus
        if SFSpeechRecognizer.authorizationStatus() == .notDetermined {
            speech = await withCheckedContinuation { continuation in
                SFSpeechRecognizer.requestAuthorization { continuation.resume(returning: $0) }
            }
        } else { speech = SFSpeechRecognizer.authorizationStatus() }
        guard speech == .authorized else { throw RecognitionError.speechDenied }
    }
}

enum RecognitionError: LocalizedError {
    case microphoneDenied, speechDenied, unavailable, noInput, noSpeech, notRecording, alreadyStopping
    case unsupportedLocale(String), onDeviceUnavailable(String), engine(String), finalizationTimeout

    var errorDescription: String? {
        switch self {
        case .microphoneDenied: return "Microphone access is off. Enable Parvathi in System Settings → Privacy & Security → Microphone, then restart Parvathi."
        case .speechDenied: return "Speech Recognition access is off. Enable Parvathi in System Settings → Privacy & Security → Speech Recognition, then restart Parvathi."
        case .unavailable: return "Apple Speech is unavailable. Check your connection or try again with an installed on-device language."
        case .noInput: return "No microphone is available. Choose an input in System Settings → Sound → Input, then retry."
        case .noSpeech: return "No speech was recognized. Check Sound → Input and speak again."
        case .notRecording: return "No recording is active. Start recording and speak again."
        case .alreadyStopping: return "The recording is already being transcribed."
        case .unsupportedLocale(let locale): return "Apple Speech does not support \(locale). Choose another recognition language."
        case .onDeviceUnavailable(let locale): return "On-device speech is unavailable for \(locale). Choose a supported language or explicitly turn off On-device only in Settings."
        case .engine(let message): return "Speech recognition failed: \(message). Check the microphone, speech permissions, and selected language, then retry."
        case .finalizationTimeout: return "Speech did not finish within 4 seconds. Nothing was inserted. Try a shorter recording or check your connection."
        }
    }
}
