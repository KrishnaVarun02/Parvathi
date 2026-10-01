#Requires -Version 7.0
[CmdletBinding()]
param([ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.2.0')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'Package verification requires a disposable Windows runner or VM.' }
if (Test-Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{AC15950F-96F8-4590-B247-8D895555E7A0}_is1') {
    throw 'An existing Parvathi installation is registered for this user. Run installation smoke tests in a disposable runner or VM.'
}
if (Test-Path (Join-Path ([Environment]::GetFolderPath('Programs')) 'Parvathi.lnk')) {
    throw 'A Parvathi Start menu shortcut already exists. Use a clean runner or VM for installer smoke tests.'
}
$windowsRoot = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $windowsRoot 'artifacts'
$release = Join-Path $artifacts 'release'
$diagnostics = Join-Path $artifacts 'diagnostics'
$extracted = Join-Path $artifacts 'portable-check'
$install = Join-Path ([IO.Path]::GetTempPath()) ('Parvathi-package-check-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $diagnostics | Out-Null
function Invoke-CheckedProcess([string]$File, [string[]]$Arguments, [int]$Seconds = 60) {
    $process = Start-Process -FilePath $File -ArgumentList $Arguments -PassThru
    if (-not $process.WaitForExit($Seconds * 1000)) { Stop-Process -Id $process.Id -Force; throw "Process exceeded its $Seconds-second check: $File" }
    if ($process.ExitCode -ne 0) { throw "Package check failed with exit $($process.ExitCode): $File" }
}
function Assert-SelfCheck([string]$Executable, [string]$Output) {
    Invoke-CheckedProcess $Executable @('--self-check', ('"' + $Output + '"'))
    $result = Get-Content $Output -Raw | ConvertFrom-Json
    if ($result.nativeRecognitionLibrary -ne 'loaded' -or $result.architecture -ne 'X64') { throw 'Native runtime self-check failed.' }
}
foreach ($line in Get-Content (Join-Path $release 'SHA256SUMS.txt')) {
    if ($line -notmatch '^([a-f0-9]{64})  ([A-Za-z0-9.\-]+)$') { throw 'Malformed checksum manifest.' }
    if ((Get-FileHash (Join-Path $release $Matches[2]) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $Matches[1]) { throw 'Release checksum mismatch.' }
}
if (Test-Path $extracted) { Remove-Item $extracted -Recurse -Force }
Expand-Archive (Join-Path $release "Parvathi-$Version-win-x64-portable.zip") $extracted
foreach ($relative in @('Parvathi.exe','coreclr.dll','hostfxr.dll','PresentationFramework.dll','libvosk.dll','libgcc_s_seh-1.dll','libstdc++-6.dll','libwinpthread-1.dll','THIRD_PARTY_NOTICES.md','licenses','Assets/Parvathi.ico','WINDOWS-README.md')) {
    if (-not (Test-Path (Join-Path $extracted $relative))) { throw "Portable package is missing $relative" }
}
Assert-SelfCheck (Join-Path $extracted 'Parvathi.exe') (Join-Path $diagnostics 'portable-self-check.json')
Invoke-CheckedProcess (Join-Path $extracted 'Parvathi.exe') @('--render-preview', ('"' + (Join-Path $diagnostics 'windows-preview.png') + '"'))
if (-not (Test-Path (Join-Path $diagnostics 'windows-preview.png'))) { throw 'WPF preview rendering did not produce a PNG.' }
$installer = Join-Path $release "Parvathi-Setup-$Version-win-x64.exe"
$arguments = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/DIR="' + $install + '"'))
try {
    Invoke-CheckedProcess $installer ($arguments + ('/LOG="' + (Join-Path $diagnostics 'install.log') + '"')) 120
    Assert-SelfCheck (Join-Path $install 'Parvathi.exe') (Join-Path $diagnostics 'installed-self-check.json')
    # Reinstall the same exact release to exercise installer upgrade replacement without a running app.
    Invoke-CheckedProcess $installer ($arguments + ('/LOG="' + (Join-Path $diagnostics 'upgrade.log') + '"')) 120
    Assert-SelfCheck (Join-Path $install 'Parvathi.exe') (Join-Path $diagnostics 'upgraded-self-check.json')
    $uninstall = Join-Path $install 'unins000.exe'
    if (-not (Test-Path $uninstall)) { throw 'The installer did not register an uninstaller.' }
    Invoke-CheckedProcess $uninstall @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + (Join-Path $diagnostics 'uninstall.log') + '"')) 120
    if (Test-Path (Join-Path $install 'Parvathi.exe')) { throw 'Uninstall left the installed application executable.' }
    @{ portable = 'passed'; nativeLibrary = 'passed'; install = 'passed'; upgrade = 'passed'; uninstall = 'passed';
       preview = 'rendered'; microphone = 'not tested'; realEditorInsertion = 'not tested'; signing = (Get-AuthenticodeSignature $installer).Status.ToString() } |
       ConvertTo-Json | Set-Content (Join-Path $diagnostics 'package-verification.json') -Encoding utf8NoBOM
} finally {
    if (Test-Path (Join-Path $install 'unins000.exe')) {
        try { Invoke-CheckedProcess (Join-Path $install 'unins000.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') 120 } catch { Write-Warning 'Smoke-test uninstall cleanup failed; see diagnostics.' }
    }
}
Write-Host 'Portable runtime, WPF preview and installer lifecycle checks passed. Live voice/editor interactions remain unverified.'
