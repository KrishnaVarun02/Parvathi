#Requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Path)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $env:WINDOWS_SIGNING_CERT_BASE64) { throw 'Signing was requested but WINDOWS_SIGNING_CERT_BASE64 is missing.' }
$certificateFile = Join-Path ([IO.Path]::GetTempPath()) ('parvathi-sign-' + [Guid]::NewGuid().ToString('N') + '.pfx')
$imported = @()
$existingThumbprints = @(Get-ChildItem Cert:\CurrentUser\My | ForEach-Object Thumbprint)
try {
    [IO.File]::WriteAllBytes($certificateFile, [Convert]::FromBase64String($env:WINDOWS_SIGNING_CERT_BASE64))
    $password = if ([string]::IsNullOrEmpty($env:WINDOWS_SIGNING_PASSWORD)) { [Security.SecureString]::new() } else {
        ConvertTo-SecureString -String $env:WINDOWS_SIGNING_PASSWORD -AsPlainText -Force
    }
    $imported = @(Import-PfxCertificate -FilePath $certificateFile -CertStoreLocation Cert:\CurrentUser\My -Password $password)
    $certificate = $imported | Where-Object HasPrivateKey | Select-Object -First 1
    if (-not $certificate) { throw 'The configured signing certificate has no private key.' }
    $sdkTools = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
    $signTool = Get-ChildItem $sdkTools -Filter signtool.exe -Recurse | Where-Object FullName -Match '[\\/]x64[\\/]signtool\.exe$' | Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $signTool) { throw 'Install the Windows SDK signing tools or use a Windows hosted runner.' }
    $timestamp = if ($env:WINDOWS_SIGNING_TIMESTAMP_URL) { $env:WINDOWS_SIGNING_TIMESTAMP_URL } else { 'http://timestamp.digicert.com' }
    & $signTool.FullName sign /fd SHA256 /sha1 $certificate.Thumbprint /tr $timestamp /td SHA256 $Path
    if ($LASTEXITCODE -ne 0) { throw 'Authenticode signing failed.' }
    if ((Get-AuthenticodeSignature $Path).Status -ne 'Valid') { throw 'The signed executable did not pass Authenticode verification.' }
} finally {
    Remove-Item $certificateFile -Force -ErrorAction SilentlyContinue
    foreach ($certificate in $imported) {
        if ($existingThumbprints -notcontains $certificate.Thumbprint) {
            Remove-Item ('Cert:\CurrentUser\My\' + $certificate.Thumbprint) -DeleteKey -Force -ErrorAction SilentlyContinue
        }
    }
}
