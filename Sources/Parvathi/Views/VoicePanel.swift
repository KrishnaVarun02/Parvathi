import SwiftUI
import AppKit
import ParvathiCore

enum Palette {
    static let background = Color(red: 0.055, green: 0.068, blue: 0.084)
    static let surface = Color(red: 0.087, green: 0.104, blue: 0.127)
    static let cyan = Color(red: 0.38, green: 0.89, blue: 0.91)
    static let muted = Color(red: 0.68, green: 0.73, blue: 0.78)
}

struct WaveformView: View {
    var level: Double
    var active: Bool
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    var body: some View {
        TimelineView(.animation(minimumInterval: 1.0 / 24, paused: !active || reduceMotion)) { context in
            HStack(spacing: 5) {
                ForEach(0..<29) { i in
                    let oscillation = active && !reduceMotion ? 0.4 + 0.6 * abs(sin(context.date.timeIntervalSinceReferenceDate * 6 + Double(i) * 0.7)) : 0.5
                    Capsule().fill(Palette.cyan.opacity(active ? 0.95 : 0.3))
                        .frame(width: 4, height: 4 + (active ? 5 + min(max(level, 0), 1) * 62 * oscillation : Double(i % 5) * 2))
                }
            }.frame(maxWidth: .infinity, minHeight: 72, maxHeight: 72)
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(active ? "Microphone recording; input level \(Int(level * 100)) percent" : "Microphone off")
    }
}

struct VoicePanelView: View {
    @ObservedObject var controller: AssistantController
    var close: () -> Void
    var body: some View {
        VStack(spacing: 12) {
            HStack {
                Circle().fill(controller.isRecording ? Palette.cyan : Palette.muted).frame(width: 7, height: 7)
                Text("PARVATHI").font(.system(size: 11, weight: .bold, design: .rounded)).tracking(2)
                Spacer()
                Text(controller.phase.rawValue.capitalized).font(.caption).foregroundStyle(Palette.muted)
                Button(action: close) { Image(systemName: "xmark").font(.caption) }.buttonStyle(.plain).accessibilityLabel("Hide voice panel")
            }
            WaveformView(level: controller.level, active: controller.isRecording)
            Text(controller.transcript.isEmpty ? controller.feedback : controller.transcript)
                .font(.system(size: 14)).lineLimit(3).frame(maxWidth: .infinity, alignment: .leading)
            if !controller.transcript.isEmpty {
                Text(controller.feedback).font(.caption).foregroundStyle(controller.hasError ? .orange : Palette.muted).lineLimit(3)
            }
            HStack {
                Text(controller.mode.rawValue.capitalized).font(.caption).foregroundStyle(Palette.cyan)
                Spacer()
                if controller.isRecording { Button("Finish") { controller.finishRecording() }.buttonStyle(.borderedProminent).tint(Palette.cyan) }
                Button("Stop · Esc") { controller.cancel() }.buttonStyle(.bordered).disabled(!controller.busy)
            }
        }
        .padding(20).background(Palette.surface).clipShape(RoundedRectangle(cornerRadius: 20))
        .overlay(RoundedRectangle(cornerRadius: 20).strokeBorder(Palette.cyan.opacity(0.2)))
        .preferredColorScheme(.dark)
    }
}

private final class NonactivatingVoicePanel: NSPanel {
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}

@MainActor
final class VoicePanelController {
    private let panel: NSPanel
    init(controller: AssistantController) {
        panel = NonactivatingVoicePanel(contentRect: NSRect(x: 0, y: 0, width: 400, height: 285), styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        panel.level = .floating; panel.isOpaque = false; panel.backgroundColor = .clear
        panel.hasShadow = true; panel.isMovableByWindowBackground = true
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        panel.hidesOnDeactivate = false
        panel.contentView = NSHostingView(rootView: VoicePanelView(controller: controller, close: { [weak panel] in panel?.orderOut(nil) }))
    }
    func show() {
        if !panel.isVisible, let frame = NSScreen.main?.visibleFrame {
            panel.setFrameOrigin(NSPoint(x: frame.midX - 200, y: frame.minY + 35))
        }
        panel.orderFrontRegardless()
    }
}
