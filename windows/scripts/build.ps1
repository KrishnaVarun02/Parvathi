#Requires -Version 7.0
[CmdletBinding()]
param([ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.2.0', [switch]$SkipTests)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'Build this Windows release on Windows with PowerShell 7 and the SDK pinned in windows/global.json.' }
$windowsRoot = Split-Path $PSScriptRoot -Parent
$repositoryRoot = Split-Path $windowsRoot -Parent
$artifacts = Join-Path $windowsRoot 'artifacts'
$publish = Join-Path $artifacts 'publish'
$tests = Join-Path $artifacts 'test-results'
New-Item -ItemType Directory -Force -Path $artifacts, $tests | Out-Null
Push-Location $windowsRoot
try {
    & dotnet restore Parvathi.Windows.sln --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Locked dependency restore failed.' }
    if (-not $SkipTests) {
        & dotnet test tests/Parvathi.Core.Tests/Parvathi.Core.Tests.csproj --no-restore --configuration Release --logger 'trx;LogFileName=core.trx' --results-directory $tests
        if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
        & dotnet test tests/Parvathi.Windows.Tests/Parvathi.Windows.Tests.csproj --no-restore --configuration Release --logger 'trx;LogFileName=windows.trx' --results-directory $tests
        if ($LASTEXITCODE -ne 0) { throw 'Windows adapter tests failed.' }
    }
    if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
    & dotnet publish src/Parvathi.Windows/Parvathi.Windows.csproj --no-restore --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false "-p:Version=$Version" --output $publish
    if ($LASTEXITCODE -ne 0) { throw 'Self-contained Windows publish failed.' }
    # Vosk 0.3.38 chooses native assets by build HOST, not target RID. Always build on Windows;
    # independently assert every required DLL before any package can be published.
    foreach ($name in @('Parvathi.exe','Parvathi.dll','Parvathi.runtimeconfig.json','coreclr.dll','hostfxr.dll','hostpolicy.dll','System.Private.CoreLib.dll','PresentationFramework.dll','Vosk.dll','libvosk.dll','libgcc_s_seh-1.dll','libstdc++-6.dll','libwinpthread-1.dll','Assets/Parvathi.ico')) {
        if (-not (Test-Path (Join-Path $publish $name))) { throw "Required published file is missing: $name" }
    }
    Copy-Item (Join-Path $repositoryRoot 'THIRD_PARTY_NOTICES.md') $publish
    Copy-Item (Join-Path $windowsRoot 'licenses') (Join-Path $publish 'licenses') -Recurse
    Copy-Item (Join-Path $repositoryRoot 'docs/WINDOWS.md') (Join-Path $publish 'WINDOWS-README.md')
    Copy-Item (Join-Path $windowsRoot 'packaging/INSTALLATION.txt') $publish
    Get-ChildItem $publish -Filter '*.pdb' | Remove-Item
    $commit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Could not determine source commit.' }
    @{ version = $Version; commit = $commit; architecture = 'win-x64'; selfContained = $true;
       sdk = (& dotnet --version).Trim(); builtAt = [DateTimeOffset]::UtcNow.ToString('o');
       interactiveVoiceVerification = 'Not performed by automated packaging' } |
       ConvertTo-Json | Set-Content (Join-Path $publish 'build-info.json') -Encoding utf8NoBOM
    Write-Host "Published self-contained Windows application: $publish"
} finally { Pop-Location }
