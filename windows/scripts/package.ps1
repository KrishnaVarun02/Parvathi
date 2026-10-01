#Requires -Version 7.0
[CmdletBinding()]
param([ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.2.0', [string]$InnoCompiler)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'Package the Windows application on Windows.' }
$windowsRoot = Split-Path $PSScriptRoot -Parent
$publish = Join-Path $windowsRoot 'artifacts/publish'
$release = Join-Path $windowsRoot 'artifacts/release'
if (-not (Test-Path (Join-Path $publish 'build-info.json'))) { throw 'Run windows/scripts/build.ps1 first.' }
$info = Get-Content (Join-Path $publish 'build-info.json') -Raw | ConvertFrom-Json
if ($info.version -ne $Version) { throw 'The requested package version differs from the published binary.' }
New-Item -ItemType Directory -Force -Path $release | Out-Null
if (-not $InnoCompiler) { $InnoCompiler = & (Join-Path $PSScriptRoot 'install-inno.ps1') }
if (-not (Test-Path $InnoCompiler)) { throw 'The specified Inno compiler does not exist.' }
$compilerVersion = & $InnoCompiler --version
if ($LASTEXITCODE -ne 0 -or ($compilerVersion -join ' ') -notmatch '7\.1\.0') { throw 'Packaging requires Inno Setup 7.1.0.' }
$signed = -not [string]::IsNullOrWhiteSpace($env:WINDOWS_SIGNING_CERT_BASE64)
if ($signed) { & (Join-Path $PSScriptRoot 'sign.ps1') -Path (Join-Path $publish 'Parvathi.exe') }
$signing = if ($signed) { 'Authenticode signed and verified' } else { 'Unsigned prerelease; SmartScreen may warn about an unknown publisher' }
$compilerArguments = @("/DAppVersion=$Version", "/DPublishDir=$publish", "/DArtifactDir=$release", '--no-ide-signtools')
if ($signed) {
    $signScript = Join-Path $PSScriptRoot 'sign.ps1'
    $compilerArguments += '/DSignBuild=1'
    $compilerArguments += ('--signtool=parvathi=pwsh.exe -NoProfile -File $q' + $signScript + '$q -Path $f')
}
$compilerArguments += (Join-Path $windowsRoot 'packaging/Parvathi.iss')
& $InnoCompiler @compilerArguments
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
$installer = Join-Path $release "Parvathi-Setup-$Version-win-x64.exe"
if ($signed -and (Get-AuthenticodeSignature $installer).Status -ne 'Valid') { throw 'Installer signature validation failed.' }
$portable = Join-Path $release "Parvathi-$Version-win-x64-portable.zip"
if (Test-Path $portable) { Remove-Item $portable -Force }
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $portable -CompressionLevel Optimal
$notes = @"
Parvathi $Version for Windows — prerelease

Windows 11 x64 only. Source commit: $($info.commit)
Signing: $signing.

Download Parvathi-Setup-$Version-win-x64.exe, run it, and launch Parvathi from the Start menu. The portable ZIP can be extracted into a folder and launched through Parvathi.exe; keep all files together. The application includes its .NET desktop runtime and needs no developer tools.

At first launch, explicitly download the approximately 41 MB offline English model from Settings. Local dictation and supported computer commands need no AI key. Optional OpenAI or separately installed Ollama supplies conversation, explanations and polished text. Text leaves the device when a remote provider is selected. API credentials use Windows user-scoped protected storage.

Use the Dictate shortcut in a supported editable field. Command mode is separate. Keep the captured field unchanged until insertion; Escape stops active work. Unsupported controls and elevated applications are rejected. The assistant reports unverified outcomes honestly.

Automated release gates cover Core tests, Windows adapter tests, package contents, native recognition-library loading, portable launch, installer installation/upgrade/uninstallation and WPF preview rendering. These checks do not prove microphone capture, recognition accuracy, real Notepad insertion, global shortcuts on an interactive desktop, authenticated provider calls, or speech output; those require manual Windows acceptance testing. See docs/WINDOWS.md and docs/MANUAL_VERIFICATION.md in the repository.

This repository is private. Downloaders need repository access. No repository visibility change is required.

SHA256SUMS.txt contains the installer and ZIP digests. Keep Windows security enabled. For unsigned builds, the publisher identity is not verified by Authenticode and SmartScreen may show a warning.
"@
$notes | Set-Content (Join-Path $release 'release-notes.md') -Encoding utf8NoBOM
@{ version = $Version; commit = $info.commit; signed = $signed; signing = $signing; architecture = 'win-x64' } |
    ConvertTo-Json | Set-Content (Join-Path $release 'release-metadata.json') -Encoding utf8NoBOM
@($installer, $portable) | ForEach-Object { ((Get-FileHash $_ -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + (Split-Path $_ -Leaf)) } |
    Set-Content (Join-Path $release 'SHA256SUMS.txt') -Encoding ascii
Write-Host "Created installer and portable ZIP in $release ($signing)."
