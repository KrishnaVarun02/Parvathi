import Foundation
import ParvathiCore

/// Device services are actor-isolated adapters; the pipeline depends on these contracts.
@MainActor
protocol SpeechRecognizing: AnyObject {
    var onPartial: ((String) -> Void)? { get set }
    var onLevel: ((Double) -> Void)? { get set }
    var onFailure: ((Error) -> Void)? { get set }
    var onFinal: (() -> Void)? { get set }
    func start(localeIdentifier: String, onDeviceOnly: Bool) async throws
    func stop() async throws -> String
    func cancel()
}

@MainActor
protocol SpeechSynthesizing: AnyObject {
    func speak(_ text: String, voiceIdentifier: String?, onFinished: @escaping () -> Void)
    func stop()
}

@MainActor
protocol SystemAutomating: AnyObject {
    func discoverApplications() -> [AppDescriptor]
    func execute(_ action: CommandAction, target: FocusSnapshot?) async throws -> ActionResult
}

extension SpeechRecognitionService: SpeechRecognizing {}
extension SpeechOutput: SpeechSynthesizing {}
extension SystemAutomation: SystemAutomating {}
