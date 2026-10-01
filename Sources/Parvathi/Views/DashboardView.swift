import SwiftUI
import AppKit
import ApplicationServices
import AVFoundation
import ParvathiCore

struct DashboardView: View {
    @ObservedObject var controller: AssistantController
    @ObservedObject var settings: SettingsStore
    @ViewState<String> private var section = "Studio"
    var body: some View {
        HStack(spacing: 0) {
            VStack(alignment: .leading, spacing: 25) {
                HStack(spacing: 10) {
                    Image(systemName: "waveform.circle.fill").font(.system(size: 27)).foregroundStyle(Palette.cyan)
                    Text("Parvathi").font(.system(size: 21, weight: .semibold, design: .rounded))
                }.padding(.bottom, 20)
                ForEach([("Studio", "waveform"), ("Commands", "command"), ("History", "clock.arrow.circlepath")], id: \.0) { name, icon in
                    Button { section = name } label: {
                        Label(name, systemImage: icon).frame(maxWidth: .infinity, alignment: .leading).padding(12)
                            .background(section == name ? Palette.cyan.opacity(0.1) : .clear, in: RoundedRectangle(cornerRadius: 9))
                            .foregroundStyle(section == name ? Palette.cyan : Palette.muted)
                    }.buttonStyle(.plain)
                }
                Spacer()
                Text("YOUR VOICE.\nA LITTLE MORE POSSIBLE.").font(.system(size: 10, weight: .semibold)).tracking(1.2).foregroundStyle(Palette.muted).lineSpacing(5)
                SettingsLink { Label("Settings", systemImage: "slider.horizontal.3") }.buttonStyle(.plain).foregroundStyle(Palette.muted)
                Text("macOS · v0.1.0").font(.caption2).foregroundStyle(Palette.muted.opacity(0.7))
            }.padding(24).frame(width: 210).background(Palette.surface.opacity(0.7))
            Divider().overlay(Color.white.opacity(0.04))
            Group {
                switch section {
                case "Commands": CommandLibraryView()
                case "History": HistoryView(history: controller.history, settings: settings, clear: controller.clearSession)
                default: StudioView(controller: controller, settings: settings)
                }
            }.frame(maxWidth: .infinity, maxHeight: .infinity)
        }.background(Palette.background).tint(Palette.cyan)
    }
}

private struct StudioView: View {
    @ObservedObject var controller: AssistantController
    @ObservedObject var settings: SettingsStore
    @ViewState<String> private var input = ""
    @ViewState<InteractionMode> private var composerMode = .command
    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 22) {
                HStack {
                    Text("VOICE WORKSPACE").font(.system(size: 10, weight: .bold)).tracking(2.3).foregroundStyle(Palette.muted)
                    Spacer()
                    Label(controller.isRecording ? "Mic active" : "Mic off", systemImage: controller.isRecording ? "mic.fill" : "mic.slash")
                        .font(.caption).foregroundStyle(controller.isRecording ? Palette.cyan : Palette.muted)
                }
                VStack(alignment: .leading, spacing: 8) {
                    Text("Less typing.\nMore flow.").font(.system(size: 39, weight: .semibold, design: .rounded)).lineSpacing(2)
                    Text("Speak into your apps, move around your Mac,\nor think out loud with Parvathi.").foregroundStyle(Palette.muted).lineSpacing(4)
                }
                VStack(spacing: 12) {
                    HStack {
                        Text(controller.phase.rawValue.uppercased()).font(.system(size: 10, weight: .bold)).tracking(2).foregroundStyle(Palette.cyan)
                        Spacer()
                        if let verified = controller.verified {
                            Label(verified ? "Verified" : "Unverified", systemImage: verified ? "checkmark.circle" : "questionmark.circle")
                                .font(.caption).foregroundStyle(verified ? Palette.cyan : .orange)
                        }
                    }
                    WaveformView(level: controller.level, active: controller.isRecording)
                    if !controller.transcript.isEmpty {
                        Text(controller.transcript).font(.body).textSelection(.enabled).frame(maxWidth: .infinity, alignment: .leading)
                    }
                    Text(controller.feedback).font(.system(size: 13)).foregroundStyle(controller.hasError ? .orange : Palette.muted)
                        .textSelection(.enabled).frame(maxWidth: .infinity, alignment: .leading)
                    if controller.busy {
                        HStack {
                            if controller.isRecording { Button("Finish recording") { controller.finishRecording() } }
                            Button("Stop · Escape", role: .cancel) { controller.cancel() }
                            Spacer()
                        }
                    }
                }.padding(20).background(Palette.surface, in: RoundedRectangle(cornerRadius: 16))
                HStack(spacing: 12) {
                    ShortcutCard(title: "Dictate", detail: settings.value.handsFree ? "Press to start / finish" : "Hold to speak", key: settings.shortcutLabel(key: settings.value.dictationKey))
                    ShortcutCard(title: "Command", detail: "Press to start / finish", key: settings.shortcutLabel(key: settings.value.commandKey))
                    ShortcutCard(title: "Ask", detail: "Press to start / finish", key: settings.shortcutLabel(key: settings.value.assistantKey))
                }
                VStack(alignment: .leading, spacing: 10) {
                    Text("TRY IT WITH TEXT").font(.system(size: 10, weight: .bold)).tracking(1.6).foregroundStyle(Palette.muted)
                    HStack {
                        Picker("Mode", selection: $composerMode) { Text("Command").tag(InteractionMode.command); Text("Ask").tag(InteractionMode.assistant) }.labelsHidden().frame(width: 115)
                        TextField(composerMode == .command ? "Open Chrome" : "What can you help me think through?", text: $input).textFieldStyle(.plain)
                            .onSubmit { send() }
                        Button(action: send) { Image(systemName: "arrow.up") }.buttonStyle(.borderedProminent).disabled(input.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || controller.busy).accessibilityLabel("Submit request")
                    }.padding(12).background(Palette.surface, in: RoundedRectangle(cornerRadius: 10))
                }
                PermissionHintView()
            }.padding(32)
        }
    }
    private func send() { guard !input.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty, !controller.busy else { return }; controller.submit(input, mode: composerMode); input = "" }
}

private struct ShortcutCard: View {
    var title: String; var detail: String; var key: String
    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack { Text(title).font(.system(size: 13, weight: .semibold)); Spacer(); Text(key).font(.system(size: 11, design: .monospaced)).foregroundStyle(Palette.cyan) }
            Text(detail).font(.system(size: 10)).foregroundStyle(Palette.muted)
        }.padding(13).frame(maxWidth: .infinity, alignment: .leading).background(Palette.surface, in: RoundedRectangle(cornerRadius: 10))
    }
}

struct PermissionHintView: View {
    var body: some View {
        VStack(alignment: .leading, spacing: 7) {
            Label("First dictation", systemImage: "sparkle").font(.system(size: 12, weight: .semibold))
            Text("Enable Accessibility in Settings, focus a text field in TextEdit, then use the Dictate shortcut. macOS will ask for Microphone and Speech Recognition access on first use.")
                .font(.caption).foregroundStyle(Palette.muted).lineSpacing(3)
        }
    }
}

private struct CommandLibraryView: View {
    @ViewState<String> private var query = ""
    var body: some View {
        VStack(alignment: .leading, spacing: 20) {
            Text("A few words. Real actions.").font(.system(size: 28, weight: .semibold, design: .rounded))
            Text("Use the Command shortcut. In Dictate mode, these phrases are always inserted as text.").foregroundStyle(Palette.muted)
            TextField("Search supported commands", text: $query).textFieldStyle(.roundedBorder)
            ScrollView {
                VStack(alignment: .leading, spacing: 12) {
                    ForEach(CommandCatalog.examples.filter { query.isEmpty || ($0.title + $0.phrase + $0.details).localizedCaseInsensitiveContains(query) }, id: \.phrase) { item in
                        VStack(alignment: .leading, spacing: 7) {
                            Text(item.title).font(.caption).foregroundStyle(Palette.cyan)
                            Text("“\(item.phrase)”").font(.system(size: 17, weight: .medium))
                            Text(item.details).font(.caption).foregroundStyle(Palette.muted)
                        }.padding(17).frame(maxWidth: .infinity, alignment: .leading).background(Palette.surface, in: RoundedRectangle(cornerRadius: 12))
                    }
                }
            }
            Text("One action per request. No arbitrary scripts, sending messages, purchases, file deletion, or screen capture.").font(.caption).foregroundStyle(Palette.muted)
        }.padding(32)
    }
}

private struct HistoryView: View {
    @ObservedObject var history: HistoryStore
    @ObservedObject var settings: SettingsStore
    var clear: () -> Void
    var body: some View {
        VStack(alignment: .leading, spacing: 20) {
            HStack { Text("Your recent words").font(.system(size: 28, weight: .semibold, design: .rounded)); Spacer(); Button("Delete & clear session", action: clear) }
            Toggle("Save transcript history on this Mac", isOn: $settings.value.keepHistory).onChange(of: settings.value.keepHistory) { _, enabled in if !enabled { history.clear() } }
            Text("Off by default. When enabled, the latest 100 completed requests are saved locally. Raw audio is never saved.").font(.caption).foregroundStyle(Palette.muted)
            if history.entries.isEmpty { Spacer(); ContentUnavailableView("Nothing saved", systemImage: "clock", description: Text("Your voice doesn't need a paper trail.")); Spacer() }
            else {
                List(history.entries) { entry in
                    VStack(alignment: .leading, spacing: 6) {
                        Text(entry.mode.capitalized + " · " + entry.date.formatted()).font(.caption).foregroundStyle(Palette.cyan)
                        Text(entry.transcript).textSelection(.enabled)
                        Text(entry.outcome).font(.caption).foregroundStyle(Palette.muted).textSelection(.enabled)
                    }.padding(.vertical, 8)
                }.scrollContentBackground(.hidden)
            }
        }.padding(32)
    }
}
