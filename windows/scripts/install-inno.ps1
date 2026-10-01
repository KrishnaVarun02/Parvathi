#Requires -Version 7.0
[CmdletBinding()]
param([string]$Destination = (Join-Path ([IO.Path]::GetTempPath()) 'parvathi-inno-7.1.0'))
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'Inno Setup requires Windows.' }
$download = Join-Path ([IO.Path]::GetTempPath()) ('parvathi-inno-' + [Guid]::NewGuid().ToString('N') + '.exe')
try {
    Invoke-WebRequest 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_0/innosetup-7.1.0-x64.exe' -OutFile $download
    $expected = '0362a383ed217d4c4239b5933866dd96d3eb2102737da92f80f6057a4b40df2f'
    if ((Get-FileHash $download -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'Inno Setup checksum mismatch.' }
    $signature = Get-AuthenticodeSignature $download
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Pyrsys B\.V\.') { throw 'Inno Setup publisher signature is not valid.' }
    $process = Start-Process -FilePath $download -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/CURRENTUSER',('/DIR="' + $Destination + '"')) -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Inno Setup installation failed: $($process.ExitCode)" }
    $compiler = Join-Path $Destination 'ISCC.exe'
    if (-not (Test-Path $compiler)) { throw 'Inno Setup compiler was not installed.' }
    Write-Output $compiler
} finally { Remove-Item $download -Force -ErrorAction SilentlyContinue }
