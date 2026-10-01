import SwiftUI
import AVFoundation
import Speech
import ApplicationServices
import Carbon.HIToolbox
import ParvathiCore

struct PreferencesView: View {
    @ObservedObject var settings: SettingsStore
    @ObservedObject var controller: AssistantController
    @ViewState<String> private var key = ""
    @ViewState<String> private var keyMessage = ""
    @ViewState<String> private var permissionMessage = ""
    var body: some View {
        TabView {
            Form {
                Section("Microphone & recognition") {
                    HStack { Text("Microphone"); Spacer(); Text("macOS default input").foregroundStyle(.secondary); Button("Choose…") { openSettings("x-apple.systempreferences:com.apple.Sound-Settings.extension") } }
                    Text("Choose the input device in macOS Sound settings before recording. Parvathi follows the system default.").font(.caption).foregroundStyle(.secondary)
                    TextField("Recognition locale", text: $settings.value.locale)
                    Toggle("Require on-device recognition", isOn: $settings.value.onDeviceOnly)
                    Text("On: audio stays in Apple's on-device recognizer; unavailable languages fail with guidance. Off: recognition may send audio to Apple.").font(.caption).foregroundStyle(.secondary)
                    Picker("Transcription", selection: $settings.value.style) { Text("Verbatim").tag(TranscriptionStyle.verbatim); Text("Polished · requires AI provider").tag(TranscriptionStyle.polished) }
                }
                Section("Voice") {
                    Toggle("Speak short responses", isOn: $settings.value.spokenResponses).onChange(of: settings.value.spokenResponses) { _, _ in controller.muteChanged() }
                    Picker("Voice", selection: $settings.value.voiceIdentifier) {
                        Text("System default").tag("")
                        ForEach(AVSpeechSynthesisVoice.speechVoices(), id: \.identifier) { voice in Text("\(voice.name) · \(voice.language)").tag(voice.identifier) }
                    }
                }
                Section("Permissions") {
                    HStack {
                        Button("Enable Accessibility") {
                            let options = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
                            let granted = AXIsProcessTrustedWithOptions(options)
                            permissionMessage = granted ? "Accessibility is enabled." : "Enable Parvathi in Privacy & Security → Accessibility. Relaunch if macOS requests it."
                            if !granted { openSettings("x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility") }
                        }
                        Button("Request speech access") { requestSpeech() }
                    }
                    if !permissionMessage.isEmpty { Text(permissionMessage).font(.caption).foregroundStyle(.secondary) }
                    Text("Global shortcuts are registered with macOS. Accessibility is needed to inspect and insert into another application's text field.").font(.caption).foregroundStyle(.secondary)
                }
            }.formStyle(.grouped).tabItem { Label("Voice", systemImage: "mic") }

            Form {
                Section("Global shortcuts") {
                    Picker("Modifiers", selection: $settings.value.modifiers) {
                        Text("Control + Option").tag(UInt32(controlKey | optionKey))
                        Text("Command + Shift").tag(UInt32(cmdKey | shiftKey))
                    }
                    keyPicker("Dictation", selection: $settings.value.dictationKey)
                    keyPicker("Command", selection: $settings.value.commandKey)
                    keyPicker("Assistant", selection: $settings.value.assistantKey)
                    Toggle("Hands-free dictation (press to start / finish)", isOn: $settings.value.handsFree)
                    Text("Command and Assistant always toggle. Rewrite and Explain are available in the menu bar. Escape cancels the active recording, request, or spoken response.").font(.caption).foregroundStyle(.secondary)
                    Button("Apply shortcuts") { controller.configureShortcuts() }
                    Text(controller.shortcutStatus).font(.caption).foregroundStyle(.secondary)
                }
                Section("Getting the right destination") {
                    Text("Start dictation while your cursor is in another app's text field. Keep the same field and selection focused until insertion finishes. If focus changes, Parvathi stops insertion and keeps the transcript visible.")
                    Text("For a rewrite, select text first, choose Rewrite selection from the menu bar, say your instruction, then click Finish on the floating panel.")
                }.font(.callout)
            }.formStyle(.grouped).tabItem { Label("Shortcuts", systemImage: "command") }

            Form {
                Section("Reasoning provider") {
                    Picker("Provider", selection: $settings.value.provider) {
                        Text("OpenAI").tag(AIConfiguration.Provider.openAI)
                        Text("Ollama · local").tag(AIConfiguration.Provider.ollama)
                    }.onChange(of: settings.value.provider) { _, provider in
                        settings.value.baseURL = provider == .openAI ? "https://api.openai.com/v1" : "http://localhost:11434"
                        settings.value.model = provider == .openAI ? "gpt-6-astra" : ""
                    }
                    TextField("API base URL", text: $settings.value.baseURL)
                    TextField("Model ID", text: $settings.value.model)
                    Text("Ollama needs a running server and an installed model name. HTTPS is required for remote providers; plain HTTP is allowed only for localhost.").font(.caption).foregroundStyle(.secondary)
                }
                Section("Credentials · macOS Keychain") {
                    SecureField("API key", text: $key)
                    HStack {
                        Button("Save key") {
                            do { guard !key.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { throw PipelineError("Enter an API key first.") }; try KeychainStore.save(key.trimmingCharacters(in: .whitespacesAndNewlines), account: SettingsStore.keychainAccount); key = ""; keyMessage = "Saved securely to Keychain." }
                            catch { keyMessage = error.localizedDescription }
                        }
                        Button("Delete key") { do { try KeychainStore.delete(account: SettingsStore.keychainAccount); key = ""; keyMessage = "Key deleted." } catch { keyMessage = error.localizedDescription } }
                    }
                    Text(keyMessage.isEmpty ? "A key is needed for OpenAI. No credentials are needed for Apple dictation or local computer commands." : keyMessage).font(.caption).foregroundStyle(.secondary)
                }
                Section("What leaves this Mac") {
                    Text("Assistant prompts, recent conversation turns, polished transcripts, and explicitly selected rewrite/explanation text go to the configured AI provider. No screenshots or raw audio are sent to the reasoning provider. OpenAI requests use store: false; provider data policies still apply.").font(.callout)
                }
            }.formStyle(.grouped).tabItem { Label("Provider", systemImage: "cpu") }

            Form {
                Section("Privacy by default") {
                    Toggle("Save local transcript history", isOn: $settings.value.keepHistory).onChange(of: settings.value.keepHistory) { _, enabled in if !enabled { controller.history.clear() } }
                    Text("History is off by default. Enabling it saves up to 100 completed requests in ~/Library/Application Support/Parvathi/history.json. Raw audio is never written to disk. Settings are saved in UserDefaults; secrets are stored in Keychain.")
                    Button("Delete history & clear conversation") { controller.clearSession() }
                }
                Section("Boundaries") {
                    Text("Dictation is always text. Command mode uses a fixed set of validated actions. Selected text is reference material, never permission to execute a command. Parvathi has no screen capture, wake word, messaging, purchase, deletion, or arbitrary shell capabilities.")
                    Text("Web search opens your browser. The assistant does not read search results and has no live retrieval; its conversational answers must not be treated as current information.")
                    Text("macOS controls Microphone, Speech Recognition and Accessibility access. You can revoke them at any time in System Settings.")
                }
            }.font(.callout).formStyle(.grouped).tabItem { Label("Privacy", systemImage: "hand.raised") }
        }.padding(8).tint(Palette.cyan)
    }

    private func keyPicker(_ label: String, selection: Binding<UInt32>) -> some View {
        Picker(label, selection: selection) { ForEach(SettingsStore.keys, id: \.1) { name, code in Text(name).tag(code) } }
    }
    private func openSettings(_ url: String) { if let url = URL(string: url) { NSWorkspace.shared.open(url) } }
    private func requestSpeech() {
        AVCaptureDevice.requestAccess(for: .audio) { microphone in
            SFSpeechRecognizer.requestAuthorization { status in
                Task { @MainActor in
                    permissionMessage = microphone && status == .authorized ? "Microphone and Speech Recognition are enabled." : "Enable Parvathi under Privacy & Security → Microphone and Speech Recognition in System Settings."
                }
            }
        }
    }
}
