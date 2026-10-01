# Third-party notices

The macOS Swift package has no third-party Swift dependencies. The Windows application redistributes the components below. Their licenses remain those of their respective copyright holders; this notice does not relicense them. Full license texts are included in `windows/licenses/` in source and the `licenses/` directory in Windows distribution packages.

## Windows managed components

| Component | Pinned version | License / source |
| --- | --- | --- |
| .NET runtime and Windows Desktop runtime | 10.0.12 | MIT and component notices; `dotnet-LICENSE.txt`, `dotnet-THIRD-PARTY-NOTICES.txt`, `WindowsDesktop-LICENSE.txt`; [dotnet/runtime](https://github.com/dotnet/runtime), [dotnet/wpf](https://github.com/dotnet/wpf) |
| System.Speech | 10.0.12 | Microsoft .NET MIT license and `System.Speech-THIRD-PARTY-NOTICES.txt`; [source](https://github.com/dotnet/runtime/tree/main/src/libraries/System.Speech) |
| System.Security.Cryptography.ProtectedData | 10.0.12 | Microsoft .NET MIT license and `ProtectedData-THIRD-PARTY-NOTICES.txt`; [source](https://github.com/dotnet/runtime/tree/main/src/libraries/System.Security.Cryptography.ProtectedData) |
| NAudio, NAudio.Core, NAudio.WinMM, NAudio.Wasapi, NAudio.Asio, NAudio.Midi, NAudio.WinForms | 2.2.1 | MIT, copyright Mark Heath; `NAudio-MIT.txt`; [source](https://github.com/naudio/NAudio/tree/v2.2.1) |
| Vosk C# wrapper and libvosk | 0.3.38 NuGet package | Apache License 2.0; `Apache-2.0.txt`; [Vosk source](https://github.com/alphacep/vosk-api), [published package](https://www.nuget.org/packages/Vosk/0.3.38) |

The committed `packages.lock.json` files record the resolved dependency graph and package hashes. Additional Microsoft runtime dependencies, where supplied by self-contained publishing, retain the notices shipped with the runtime. Tests and build tools have their own licenses and are not the application runtime.

## Native recognition runtime

The unmodified Windows x64 native files come from Vosk 0.3.38's NuGet package, `build/lib/win-x64/`. They must remain with the packaged application:

- `libvosk.dll`: Vosk recognition code and native dependencies. Vosk and Kaldi use Apache License 2.0; Kaldi's upstream combined notice is preserved as `Kaldi-COPYING.txt`. [Kaldi source and notices](https://github.com/kaldi-asr/kaldi).
- OpenFst components used by Kaldi: Apache License 2.0; see [OpenFst](https://www.openfst.org/) and the included Apache text.
- OpenBLAS and LAPACK code linked into the native recognition runtime: BSD-style licenses; `OpenBLAS-LICENSE.txt` and `LAPACK-LICENSE.txt` preserve their copyright, redistribution conditions, and disclaimers. [OpenBLAS source](https://github.com/OpenMathLib/OpenBLAS), [LAPACK source](https://github.com/Reference-LAPACK/lapack).
- `libgcc_s_seh-1.dll` and `libstdc++-6.dll`: GCC runtime libraries, GNU GPL version 3 with the GCC Runtime Library Exception version 3.1, where specified by upstream. Both complete texts are supplied in `GCC-GPL-3.0.txt` and `GCC-Runtime-Library-Exception-3.1.txt`. The exception's conditions govern independent modules; the GPL text alone must not be used to describe these binaries. [GCC source and release archives](https://gcc.gnu.org/releases.html), [runtime exception](https://www.gnu.org/licenses/gcc-exception-3.1.html).
- `libwinpthread-1.dll`: mingw-w64 winpthreads, including MIT-style mingw-w64 terms and the Lockless Inc. BSD-style notice. Both are preserved in `winpthreads-COPYING.txt`. [winpthreads source and notice](https://github.com/mingw-w64/mingw-w64/tree/master/mingw-w64-libraries/winpthreads).

Vosk's prebuilt package contains the native binaries; Parvathi does not claim to have independently rebuilt them or to know a single compiler version for all linked objects. Native package provenance is the pinned Vosk package and its lock-file content hash. Source references and notices are provided above; upgrading or replacing these binaries requires reviewing the corresponding upstream license set and provenance again.

## Downloaded English recognition model

The optional first-run download is `vosk-model-small-en-us-0.15`, published by Alpha Cephei and listed as Apache License 2.0 in the [official model catalog](https://alphacephei.com/vosk/models). It is not embedded in the installer. Its original README remains inside the installed model directory, and `Apache-2.0.txt` accompanies the application.

Official archive: `https://alphacephei.com/vosk/models/vosk-model-small-en-us-0.15.zip`

- Download size: 41,205,931 bytes.
- SHA-256: `30f26242c4eb449f948e42cb302dd7a686cb29a3423a8367f99ff41780942498`.
- Parvathi does not alter the recognition model files; it adds a separate integrity manifest.

## Build and documentation tooling

Inno Setup builds the installer; its compiler license and upstream terms remain those of [JRSoftware](https://jrsoftware.org/isinfo.php). ReportLab builds the interview PDF; Python/ReportLab are documentation tools, not a dependency of the Windows application. GitHub Actions components and test packages retain their own upstream licenses.

License files were obtained from the referenced upstream projects or the exact restored Microsoft runtime packages. Copyright notices in those files remain authoritative. No vendor endorsement is implied.
