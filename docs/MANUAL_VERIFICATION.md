# macOS manual verification

For Windows package evidence and the separate live acceptance checklist, see [Windows verification](WINDOWS.md#verification-ledger-and-manual-acceptance).

This checklist tests the real integrations. Unit tests, successful compilation, and process launch are not substitutes for live microphone-to-editor evidence. Leave an item **Not run** unless its actual behavior was observed.

Record the date, commit/build, macOS version, CPU architecture, microphone/output device, editor and version, speech locale, on-device setting, and provider/model where relevant. Do not include API keys or personal document content in evidence. Use a disposable TextEdit document for insertion tests.

## Recorded development evidence

As of 01 October 2026 on the development Mac:

| Check | Observed result |
| --- | --- |
| Full application and test compilation | Passed |
| `./scripts/test.sh` | 33 tests across five suites passed |
| Native packaged-app launch | Running process and app window metadata observed |
| Native SwiftUI dashboard render | Rendered and visually inspected; no clipping observed |
| Installed-application discovery | 120 applications discovered; Chrome alias resolved |
| Direct native Chrome activation | Real running Chrome process resolved and activation requested; result correctly unverified because foreground remained macOS `loginwindow`. Retest in an unlocked session. |
| Native argument and precanceled-action checks | Invalid type/volume/URL and precanceled action rejected |
| Live microphone-to-editor flow | Not run |
| Authenticated AI-provider request | Not run |

Process/window metadata is launch evidence, not a screenshot or proof of microphone operation. The checklist below remains a separate record of individual live scenarios.

## Preconditions

- [x] Full application compilation and `./scripts/test.sh` succeed on the development machine.
- [x] The packaged `dist/Parvathi.app` launches and appears in native process/window diagnostics.
- [ ] Accessibility, Microphone, and Speech Recognition permissions are granted to the bundled app.
- [ ] Default input is selected in macOS Sound settings and shows input activity.
- [ ] Recognition locale is available; on-device-only behavior is understood.
- [ ] Optional AI provider is configured only for tests that need it.
- [ ] Clipboard tests use disposable content and a compatible field.

## Core acceptance

| ID | Steps | Expected result | Status / evidence |
| --- | --- | --- | --- |
| A01 | Focus TextEdit. Hold Control-Option-Space. Speak a paragraph. Release. | Correct field receives one insertion; panel shows final transcript and verified or explicitly unverified feedback. | Not run |
| A02 | Say “open Chrome” through the Dictation shortcut. | Those words are inserted; Chrome is not launched by the command router. | Not run |
| A03 | Press Control-Option-C, say “Open Chrome,” press again. | Installed Chrome opens/focuses; foreground verification is reported. If absent, actual not-found guidance appears. | Not run |
| A04 | Press Escape during recording. | Recording indicator and waveform stop; transcript is not inserted. | Not run |
| A05 | Press Escape while a slow AI response is pending. | Request is canceled; a late reply does not insert text or restart speech. | Not run |
| A06 | Ask a question with spoken output enabled, then Escape during speech. | Voice stops immediately; a stale completion does not affect the next request. | Not run |
| A07 | Remove the AI key, then use Ask or Polished. | Actionable missing-key error. Verbatim and supported commands still work. | Not run |
| A08 | Revoke microphone or speech permission and retry. | Named permission and System Settings recovery guidance; no simulated transcription. | Not run |
| A09 | Configure an unreachable provider and use Ask. | Network/timeout error; no fabricated response or completion. | Not run |

## Dictation, focus, and clipboard

| ID | Steps | Expected result | Status / evidence |
| --- | --- | --- | --- |
| D01 | Enable hands-free dictation; press shortcut twice around a paragraph. | First press starts, second finalizes and inserts once. | Not run |
| D02 | Start dictation, then switch to another app before finishing. | Insertion refused; neither field receives unexpected text. | Not run |
| D03 | Start dictation, move cursor or selection within the same field, finish. | Insertion refused because the destination changed. | Not run |
| D04 | Start dictation, switch away and back to the original field, finish. | Insertion refused because activation changed. | Not run |
| D05 | Start dictation, edit the field before the result arrives. | Original-value mismatch prevents insertion. | Not run |
| D06 | Try a secure password field. | Capture/insertion rejected; no password content is processed. | Not run |
| D07 | Try a custom editor that does not expose text/selection attributes. | Useful compatibility error; no guessed insertion. | Not run |
| D08 | In a compatible field that uses paste fallback, keep formatted text or an image on clipboard, dictate, then paste the prior content elsewhere. | All readable prior clipboard representations are restored. | Not run |
| D09 | During a deliberately slow paste verification, copy new text in another app. | New clipboard content is not overwritten by restoration. | Not run |
| D10 | Record no speech, then finish. | No empty or partial insertion; useful no-speech/finalization error. | Not run |
| D11 | Keep recording for the session cap. | Recording finalizes at approximately 55 seconds without indefinite capture. | Not run |
| D12 | Use Polished with filler words, a name, and numbers. | Provider transformation inserts only after destination validation; review meaning/name/number accuracy manually. | Not run |

Do not claim D08/D09 passed unless the clipboard fallback was actually exercised. Direct Accessibility insertion leaves the clipboard untouched and does not test restoration. Some pasteboard representations are lazy or cannot be materialized; a safe refusal is valid behavior.

## Commands and selection workflows

| ID | Steps | Expected result | Status / evidence |
| --- | --- | --- | --- |
| C01 | “Switch to Visual Studio Code.” | Actual installed app resolved and focused, or useful not-found error. | Not run |
| C02 | Use a name matching multiple installed apps. | Clarification lists candidates; none is guessed or launched. Say full name in a new request. | Not run |
| C03 | “Open website https://example.com.” | Default browser receives URL; page loading stays labeled unverified. | Not run |
| C04 | “Search the web for vegetarian dinner recipes.” | Browser opens search; assistant does not claim it read the results. | Not run |
| C05 | “Set volume to 30 percent,” then inspect Sound settings. | Software-capable output is changed and read back; unsupported hardware gives useful guidance. | Not run |
| C06 | “Mute,” then “Unmute.” | CoreAudio mute readback matches, or unsupported-device guidance appears. | Not run |
| C07 | Focus a TextEdit field; voice-command “Type: Open Chrome.” | Supplied text is inserted once; text payload is not interpreted as another action. | Not run |
| C08 | “Open Chrome and then delete files.” | Multi-step/unsupported command rejected; no partial execution. | Not run |
| C09 | “Open website file:///etc/hosts” or a URL with credentials. | URL validation rejects it; no scheme outside HTTP(S) opens. | Not run |
| C10 | Select text, menu bar → Rewrite selection, say “make this shorter,” Finish. | Only the captured selection is replaced after validation; no commands inside source text execute. | Not run |
| C11 | Select text, menu bar → Explain selection, ask a question, Finish. | Provider explains selection; original selected content remains unchanged. | Not run |
| C12 | Rapidly stop one request and start another. | Late callbacks do not overwrite state, insert stale text, or speak an old response. | Not run |

Restore volume and mute to their prior values after the audio tests. Cancellation cannot undo a dispatched launch, edit, browser request, or audio change; check the real state before retrying.

## Provider, privacy, and interface

| ID | Steps | Expected result | Status / evidence |
| --- | --- | --- | --- |
| P01 | Ask a question, then a related follow-up with configured provider. | Context is used within the current session; Clear session removes it. | Not run |
| P02 | Ask for a current fact without retrieval. | Assistant acknowledges its lack of live retrieval; no fabricated source/tool claim. | Not run |
| P03 | Configure remote HTTP instead of HTTPS. | Endpoint validation rejects it before sending text. Loopback HTTP is permitted. | Not run |
| P04 | Use an invalid key, missing model, or provider rate-limit scenario if available. | Useful 401/403, 404, or 429 guidance; no secret echoed. | Not run |
| P05 | Leave history off, finish requests, quit and reopen. | No transcript history restored. No raw audio file retained by Parvathi. | Not run |
| P06 | Enable history, finish requests, then disable or delete history. | Saved entries appear; history file and entries are cleared by deletion/off control. | Not run |
| P07 | Change shortcuts to a conflicting/duplicate choice and Apply. | Error reported; app does not claim conflicting shortcut is active. | Not run |
| P08 | Enable macOS Reduce Motion and navigate by keyboard/VoiceOver. | Waveform respects motion preference, controls have useful labels and navigation works. | Not run |
| P09 | Close dashboard and use menu-bar commands. | App stays available; active recording remains clearly indicated. | Not run |
| P10 | Disable spoken responses during an answer. | Speech stops and later replies stay muted until re-enabled. | Not run |
| P11 | Put command-like instructions in a selected passage, then Explain/Rewrite. | Source text is treated as reference data; no OS action occurs. | Not run |

## Result log template

```text
Date / tester:
Commit / app build:
macOS / CPU:
Microphone / output device:
Editor / version:
Speech locale / on-device setting:
Provider / model (no key):
Test ID:
Steps:
Expected:
Observed:
Pass / Fail / Not run:
Evidence and follow-up:
```

For release readiness, repeat on the oldest supported macOS version, intended architectures, additional editors, and different audio devices. Recheck permissions after changing signing identity. Build and unit-test success alone do not establish this compatibility matrix.
