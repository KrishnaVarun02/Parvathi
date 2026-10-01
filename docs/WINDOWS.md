# Parvathi for Windows

Parvathi's Windows implementation is a native WPF application for Windows 11 x64. It has a Start menu entry, system tray controls, a floating voice panel, real offline speech recognition, bounded Windows actions, and optional AI conversation. The macOS source and CI remain independent.

## Install and first run

1. Visit the public GitHub repository; no account is required to download the application.
2. Open [Windows release v0.2.0-windows.1](https://github.com/KrishnaVarun02/Parvathi/releases/tag/v0.2.0-windows.1), expand Assets, and download `Parvathi-Setup-0.2.0-win-x64.exe`.
3. Run the installer for your Windows user. Administrative privileges are not required. Choose whether to add a desktop shortcut, then launch **Parvathi** from Start.
4. In Settings, choose the offline model download button. This explicitly connects to `alphacephei.com` and downloads **41,205,931 bytes** (39.3 MiB); installed model data is approximately 67.6 MiB. Progress is visible; **Cancel download** cancels this operation. The downloaded archive must match the pinned SHA-256 before extraction.
5. Select your microphone. Windows Settings > Privacy & security > Microphone must allow microphone access and desktop apps. Also check Settings > System > Sound > Input.
6. Focus an editable text field in Notepad. Hold **Ctrl+Alt+Space**, speak a paragraph, and release. Keep the application, field, text, caret, and selection unchanged until insertion finishes. The floating panel reports verified, failed, or unverified results.

The installer and portable ZIP contain the .NET runtime and native libraries. No development tools, separate model server, or paid account are necessary for basic dictation. The model is a one-time explicit download; recognition runs locally afterward. The English model's vocabulary and accuracy vary with accent, names, background noise, and microphone quality. Verbatim output does not automatically restore punctuation or guarantee exact words.

For portable use, download `Parvathi-0.2.0-win-x64-portable.zip`, extract **all** files to a folder, and launch `Parvathi.exe`. Do not move only the EXE: the framework and native libraries must remain with it. Portable mode still saves settings, credentials, models, and optional history in the Windows user profile. It does not register an uninstaller or a Start menu shortcut.

This release is unsigned and marked as a prerelease. SmartScreen or an unknown-publisher warning may appear. Compare `Get-FileHash <downloaded-file> -Algorithm SHA256` with `SHA256SUMS.txt` and check that the asset came from the expected release. An unsigned executable is not a trusted or signed release. Do not disable Windows security to use it.

## Controls and supported actions

| Interaction | Default control | Behavior |
| --- | --- | --- |
| Dictation | Hold Ctrl+Alt+Space | Release to finalize and insert |
| Hands-free dictation | Enable in Settings, then Ctrl+Alt+Space | Press once to start, again to finish |
| Command | Ctrl+Alt+C | Press once to start, again to finish |
| Ask | Ctrl+Alt+A | Press once to start, again to finish |
| Rewrite / Explain selection | Tray menu | Select text first, speak an instruction, then Finish |
| Stop | Escape or Stop | Stop recording, pending voice/AI work, or speech |
| Exit | Tray > Exit | Close the process; closing the dashboard keeps the tray app running |

Settings offers microphone selection, voice selection, mute, reduced motion, mode, provider, shortcuts, optional history, and optional startup with Windows. Shortcut registration failures are reported instead of silently ignored. Startup is disabled by default; enabling it creates a per-user Run entry. If a portable installation is moved, reapply the startup setting from its new location.

Supported phrases in **Command** mode include:

- `Open Chrome` or `Switch to Visual Studio Code`.
- `Open website https://example.com`.
- `Search the web for vegetarian dinner recipes`.
- `Set the volume to 30 percent`, `Mute`, or `Unmute`.
- `Type: I'll join the meeting in five minutes.`

The searchable Commands tab lists supported syntax. App discovery reads Windows App Paths, Start menu shortcuts, and packaged applications from the AppsFolder. It resolves actual installed entries rather than manufacturing executable paths. Ambiguous names ask for a more specific application name. Browser opening reports that the request was accepted without claiming that the page loaded or that the assistant read it.

There is one action per request. The command parser produces typed values and validates their arguments. Dictation never passes through command parsing. There is no arbitrary PowerShell, CMD, generated shell execution, messaging, purchasing, file deletion, or sensitive-setting tool.

## Optional AI and speech

Verbatim dictation and supported computer actions work without AI credentials. Polished dictation, Ask, Rewrite, and Explain use the selected provider:

| Provider | Endpoint | Model | Credentials |
| --- | --- | --- | --- |
| OpenAI | `https://api.openai.com/v1` | An available model for your account | Enter and save the API key in Settings |
| Ollama | `http://localhost:11434` | A model already installed in your Ollama server | No OpenAI key is sent; authenticated Ollama is unsupported |

The model and endpoint are configurable. Ollama installation and model provisioning are separate from Parvathi. The app does not read `.env` files; use the interface. [settings.example.json](../windows/settings.example.json) documents nonsecret settings without creating or installing them. The OpenAI key is encrypted with Windows DPAPI scoped to the current user. A key copied from another Windows user or machine cannot simply be reused as an encrypted file. If you change the OpenAI-compatible endpoint, that configured service receives the OpenAI-provider credential; configure only an endpoint you intend to trust with it.

Speech output uses installed Windows SAPI voices. Add a voice through Windows Settings > Time & language > Speech if none is available. The response remains readable if synthesis fails. Spoken output is deliberately short, capped by the coordinator at 700 characters with a 60-second watchdog. Mute disables speech; Escape interrupts it. Windows speech generation does not send the response text to a Parvathi cloud speech service.

Conversation context is bounded and held for the current session. The app has no retrieval tool: answers do not have verified live web access. Opening a web search is distinct from reading those search results. Selected text is untrusted reference data; it never authorizes an operating-system action.

## Architecture and exact execution boundary

```text
windows/
  global.json / Directory.Build.props  SDK, language, and reproducible versions
  src/Parvathi.Core/                   Modes, typed actions, validation, provider HTTP, request gate
  src/Parvathi.Windows/                WPF application and tray lifecycle
    AssistantCoordinator.cs           Request ownership and visible pipeline state
    Services/                         Vosk/NAudio, SAPI, focus, Windows actions, DPAPI, settings
    Views/                            Dashboard, settings, command list, floating panel, waveform
  tests/                              Core and Windows adapter tests
  scripts/                            Build, signing, packaging, and package verification
  packaging/                          Per-user Inno Setup configuration
  licenses/                           Redistributed component notices
```

C# and WPF keep Windows accessibility, tray integration, global shortcuts, speech, and audio control close to their native APIs. A separate Windows project preserves the Mac implementation without pretending that Apple frameworks run on Windows. Replaceable `IRecognitionService`, `ISpeechOutput`, `IFocusService`, `ISystemAutomation`, and `IAIProvider` interfaces separate recognition, reasoning, execution, and speech.

The normal path is:

1. The shortcut selects an explicit mode; the coordinator captures the intended destination before showing the non-activating panel.
2. `ModelManager` validates the installed offline model. NAudio captures 16 kHz, 16-bit, mono PCM and sends in-memory buffers to Vosk. Actual input energy drives the waveform.
3. Vosk accumulates finalized segments and partial words; releasing the shortcut asks for the final result. Recording is capped at 55 seconds.
4. Dictation returns text; Command parses one typed action; Ask calls the provider; selected-text modes supply only the requested selection and instruction.
5. Before any insertion, the original process, window, focused element, text, selection, and focus revision must still match. Password fields, protected/terminal applications, elevated targets, and unverifiable controls are refused.
6. In supported editors, insertion addresses a native Edit/RichEdit control directly or uses Windows UI Automation ValuePattern. It does **not** replace the clipboard or send a blind paste shortcut. Clipboard preservation is therefore achieved by leaving it untouched.
7. The executor checks the observable result and reports verified, failed, or unverified feedback. A successful API dispatch is not by itself a verified outcome.

The UIA TextPattern must expose a stable single selection; a native edit handle or a compatible ValuePattern must support safe insertion. Some browser editors, rich document controls, remote desktops, and customized applications will be refused. The visible transcript can be copied manually. Do not run Parvathi as administrator to bypass integrity restrictions.

Cancellation invalidates request ownership, stops capture without waiting behind the native decoder, cancels HTTP work, and stops the active speech prompt. Loaded models and late callbacks are fenced by session identity. Native model construction itself cannot be interrupted, but a canceled load cannot start recording afterward. Canceled work cannot undo a side effect Windows already accepted. There is no automatic retry after uncertain execution, and duplicate prevention is scoped to the active in-memory request.

## Privacy and storage

| Information | Location / recipient | Default |
| --- | --- | --- |
| Audio | In-memory microphone buffers, local Vosk decoder | Never saved as raw audio |
| Offline model | `%LOCALAPPDATA%\Parvathi\models\vosk-model-small-en-us-0.15` | Explicit first download from `alphacephei.com`; audio is not uploaded |
| Settings | `%LOCALAPPDATA%\Parvathi\settings.json` | Nonsecret preferences |
| OpenAI credential | `%LOCALAPPDATA%\Parvathi\openai.credential.dpapi` | DPAPI CurrentUser encrypted; only supplied to OpenAI provider |
| Transcript history | `%LOCALAPPDATA%\Parvathi\history.json` | Disabled; if enabled, up to 100 bounded entries, not separately encrypted |
| AI text | Configured OpenAI/Ollama endpoint | Only invoked modes send prompts, bounded conversation, transcript, or explicit selection |
| Conversation | Process memory | Current session only; Clear session removes it |

The model archive is pinned to SHA-256 `30f26242c4eb449f948e42cb302dd7a686cb29a3423a8367f99ff41780942498`. Downloads have a ten-minute limit, exact byte-size verification, hash verification, bounded extraction, traversal/link rejection, and a staged installation. A manifest records installed file hashes, checked before recording. Repair downloads preserve an existing invalid folder with a `.previous-...` suffix rather than deleting an arbitrary selected path. No screenshot or screen-capture feature is present.

Remote AI endpoints require HTTPS; loopback Ollama may use HTTP. API redirects are rejected. OpenAI uses the Responses API with `store:false`; this is not a claim of zero provider retention. The provider's policies and account settings still apply. The assistant sends neither microphone audio nor screenshots to the AI provider.

Turn off history or select **Delete history & clear session** to remove local history. Remove the API key separately in Settings. Normal upgrades preserve profile data; ordinary uninstall removes application files and startup registration but leaves profile data so settings are not silently lost. Delete the profile folder only when you intend to remove models, preferences, history, and the encrypted key. Deleting the encrypted file is not a promise of forensic secure erasure.

## Build, package, and publish from source

End users skip this section. Development requires the SDK pinned in `windows/global.json`; installer creation uses the pinned Inno Setup package selected by the scripts. Run in a Windows PowerShell session from the repository root:

```powershell
pwsh ./windows/scripts/build.ps1
pwsh ./windows/scripts/install-inno.ps1
pwsh ./windows/scripts/package.ps1 -Version 0.2.0
```

Direct .NET commands, when needed:

```powershell
Set-Location windows
dotnet restore Parvathi.Windows.sln --locked-mode
dotnet test tests/Parvathi.Core.Tests/Parvathi.Core.Tests.csproj -c Release --no-restore
dotnet test tests/Parvathi.Windows.Tests/Parvathi.Windows.Tests.csproj -c Release --no-restore
dotnet publish src/Parvathi.Windows/Parvathi.Windows.csproj -c Release -r win-x64 --self-contained true
```

The SDK and NuGet dependency graph are pinned and lock files are committed. Packaging preserves all managed assemblies, Windows native libraries, assets, and license notices. Vosk's older NuGet target selects native binaries by build host, so the packaging scripts require a Windows host and verify the expected Windows DLLs. The release itself is built on Windows GitHub Actions runners.

`windows-ci.yml` builds, tests, and packages Windows independently of the preserved Mac CI. `windows-release.yml` publishes from a version tag or an explicit manual trigger. Assets come from the exact tagged commit. The release is created only after successful build, tests, package checks, and checksums; uploaded workflow artifacts alone are not the app's download channel.

The release assets are `Parvathi-Setup-0.2.0-win-x64.exe`, `Parvathi-0.2.0-win-x64-portable.zip`, and `SHA256SUMS.txt`, accompanied by release notes. Optional Authenticode signing uses `WINDOWS_SIGNING_CERT_BASE64` and `WINDOWS_SIGNING_PASSWORD` GitHub secrets, with the `WINDOWS_SIGNING_TIMESTAMP_URL` repository variable for the timestamp service. Without signing credentials, release notes identify an unsigned prerelease. The repository is public, and anyone can download the release assets without a GitHub account.

## Troubleshooting

| Symptom | Action |
| --- | --- |
| No microphone / access denied | Enable Windows microphone access and desktop-app access; select a connected input; close exclusive users of that microphone |
| Missing offline model | Click Download / repair model; allow the official host through your network policy; retry after a connection failure |
| Integrity verification failed | Retry the explicit model download to repair the installation; a changed official archive requires a reviewed application update |
| Shortcut unavailable | Choose a different combination in Settings; another app may already own it |
| Text was not inserted | Return to a supported editable field, keep its text/caret/focus unchanged, and try a new request; inspect the current field before retrying an unverified result |
| Protected or elevated target | Use an ordinary non-elevated editor; the app intentionally does not bypass Windows integrity restrictions |
| Native recognition library missing | Reinstall or extract the complete portable ZIP; do not move only the executable |
| Missing/rejected AI credential | Enter a valid key and available model in Settings, or use Verbatim/Command without AI |
| Network/provider error | Check the configured endpoint, model, account limits, and connectivity; Ollama must be running separately |
| No spoken reply | Check mute, output device, and installed Windows voices; the response remains visible as text |
| Model loaded after Escape | A native load may finish in the background, but canceled sessions are prevented from starting the microphone or inserting text |
| History/settings cannot be read | Use the named user-profile path in the error; rename a corrupt settings file to reset preferences, or clear history |
| ARM64 or Windows 10 | This release targets Windows 11 x64. Native ARM64 and other operating-system validation are future work |

## Verification ledger and manual acceptance

Automated evidence is tied to its source commit and GitHub Actions run. At commit `113f0bd`, [Windows CI run 36818128535](https://github.com/KrishnaVarun02/Parvathi/actions/runs/36818128535) passed **95 core tests and 23 Windows adapter tests**; **3 interactive tests were skipped**. The same run passed native recognition-library loading, portable-launch checks, and unsigned installer installation, reinstallation, and uninstallation smoke checks. The complete WPF application also cross-built with **zero warnings and zero errors** during development. [macOS CI run 36818128433](https://github.com/KrishnaVarun02/Parvathi/actions/runs/36818128433) passed the existing **33 tests**.

Core tests exercise mode isolation, parsing/validation, app-name ambiguity, request cancellation, duplicate/stale results, and controlled provider responses. Windows adapter and package smoke checks cover the native boundaries available to automation. These results are CI evidence for the named commit, not a claim that a final release is published or that interactive voice acceptance passed. Later release builds must run their own gates from the tagged source. Automated installation and process checks do not establish actual voice accuracy or interaction with a human desktop.

**No interactive Windows machine was available during implementation. Every live check below is Not run, not passed.** Record OS/build/architecture, commit/tag, microphone/output device, editor version, provider/model if used, steps, expected and observed result, and Pass/Fail/Not run. Do not capture API keys or private document content as evidence.

| Check | Expected evidence | Live status |
| --- | --- | --- |
| Clean install without developer tools | Start menu launch; no SDK/runtime dependency prompts | Not run |
| Portable extraction | Full folder launches `Parvathi.exe`; no console window | Not run |
| Paragraph into Notepad | Actual microphone transcription appears in intended field; result state is accurate | Not run |
| Dictate `open Chrome` | Those words become text; Chrome does not launch | Not run |
| Command `Open Chrome` | Installed Chrome opens/focuses; absent Chrome reports not found | Not run |
| Focus/caret/document change | Insertion refused; neither destination receives unintended text | Not run |
| Clipboard formats | Text/image/rich formats unchanged before and after safe direct insertion | Not run |
| Selected-text rewrite/explanation | Only requested selection is sent; rewrite revalidates destination | Not run |
| Escape while recording/loading/requesting/speaking | Active work stops or late completion is ignored; speech stops promptly | Not run |
| Permissions, missing model/key, offline network | Useful recovery text; no fabricated success | Not run |
| Volume/mute and URL/search | Real volume state read back; browser accepted/opened result honestly qualified | Not run |
| History, credentials, startup, multiple launches | Opt-in behavior, deletability, per-user settings, one app instance | Not run |
| Accessibility/high DPI/reduced motion | Keyboard and Narrator usability; clear contrast and labels at scaling levels | Not run |
| Upgrade and uninstall | App replacement safe; shortcuts/uninstall correct; settings retained as documented | Not run |

## Remaining limits

- English offline model and a deliberately narrow English command grammar; no wake word.
- Conservative text-field compatibility; no clipboard/paste fallback or keyboard injection.
- Windows 11 x64 only; no native ARM64 package or interactive Windows acceptance evidence yet.
- Unsigned prerelease unless signing secrets are supplied; no promise of SmartScreen reputation.
- No screen understanding, current-information retrieval, multi-step plans, messaging, purchases, deletion, or arbitrary scripts.
- AI and recognition can produce incorrect text. Important edits need review. Already-dispatched OS effects are not transactions and cannot be undone by cancellation.

## Official references

- [WPF overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/)
- [.NET self-contained deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/)
- [Windows UI Automation](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-overview)
- [RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)
- [Vosk C# example](https://github.com/alphacep/vosk-api/blob/master/csharp/demo/VoskDemo.cs)
- [Vosk model catalog and licenses](https://alphacephei.com/vosk/models)
- [NAudio WaveInEvent](https://github.com/naudio/NAudio/blob/release/2.x/NAudio.WinMM/WaveInEvent.cs)
- [Windows speech synthesis](https://learn.microsoft.com/en-us/dotnet/api/system.speech.synthesis.speechsynthesizer)
- [DPAPI ProtectedData](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.protecteddata)
- [GitHub Releases](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository)
- [Third-party license notices](../THIRD_PARTY_NOTICES.md)
