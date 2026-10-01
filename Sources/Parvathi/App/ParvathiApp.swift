import SwiftUI
import AppKit
import ParvathiCore

@main
struct ParvathiApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) var delegate
    @StateObject private var settings: SettingsStore
    @StateObject private var controller: AssistantController
    @ViewState<VoicePanelController?> private var panel = nil
    @Environment(\.openWindow) private var openWindow

    init() {
        let settings = SettingsStore()
        let controller = AssistantController(settings: settings)
        _settings = StateObject(wrappedValue: settings)
        _controller = StateObject(wrappedValue: controller)
        if let argument = CommandLine.arguments.firstIndex(of: "--render-preview"), CommandLine.arguments.indices.contains(argument + 1) {
            let path = CommandLine.arguments[argument + 1]
            DispatchQueue.main.async {
                PreviewExporter.export(controller: controller, settings: settings, path: path) { success in exit(success ? 0 : 1) }
            }
        }
    }
    var body: some Scene {
        WindowGroup("Parvathi", id: "main") {
            DashboardView(controller: controller, settings: settings)
                .preferredColorScheme(.dark)
                .frame(minWidth: 820, minHeight: 610)
                .onAppear {
                    guard panel == nil else { return }
                    let voicePanel = VoicePanelController(controller: controller)
                    panel = voicePanel
                    controller.showPanel = { voicePanel.show() }
                    controller.configureShortcuts()
                }
        }
        .defaultSize(width: 920, height: 680)
        .windowResizability(.contentMinSize)
        .commands {
            CommandGroup(replacing: .newItem) {}
            CommandMenu("Voice") {
                Button("Stop everything") { controller.cancel() }.keyboardShortcut(.escape, modifiers: [])
                Button("Clear session") { controller.clearSession() }
            }
        }
        MenuBarExtra("Parvathi", systemImage: controller.isRecording ? "mic.fill" : "waveform.circle") {
            Text(controller.isRecording ? "Microphone active" : "Parvathi · \(controller.phase.rawValue.capitalized)")
            Divider()
            Button(controller.isRecording ? "Finish recording" : "Dictate") { controller.toggleRecording(mode: .dictation) }
            Button("Voice command") { controller.toggleRecording(mode: .command) }
            Button("Ask Parvathi") { controller.toggleRecording(mode: .assistant) }
            Button("Rewrite selection") { controller.toggleRecording(mode: .rewrite) }
            Button("Explain selection") { controller.toggleRecording(mode: .explain) }
            Button("Stop everything") { controller.cancel() }.keyboardShortcut(.escape, modifiers: [])
            Divider()
            Toggle("Spoken responses", isOn: $settings.value.spokenResponses)
                .onChange(of: settings.value.spokenResponses) { _, _ in controller.muteChanged() }
            Button("Open Parvathi") { openWindow(id: "main"); NSApp.activate(ignoringOtherApps: true) }
            SettingsLink { Text("Settings…") }
            Divider()
            Button("Quit Parvathi") { controller.cancel(silent: true); NSApp.terminate(nil) }.keyboardShortcut("q")
        }
        Settings {
            PreferencesView(settings: settings, controller: controller)
                .preferredColorScheme(.dark)
                .frame(width: 600, height: 570)
        }
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)
        NSApp.activate(ignoringOtherApps: true)
    }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }
}
