#Requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][ValidatePattern('^v\d+\.\d+\.\d+-windows\.\d+$')][string]$Tag,
      [Parameter(Mandatory)][string]$AssetDirectory,
      [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{40}$')][string]$ExpectedCommit)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$metadata = Get-Content (Join-Path $AssetDirectory 'release-metadata.json') -Raw | ConvertFrom-Json
if ($metadata.commit -ne $ExpectedCommit -or $Tag -notmatch ('^v' + [Regex]::Escape($metadata.version) + '-windows\.\d+$')) { throw 'Tag, version and built source commit do not match.' }
$tagCommit = (& git rev-parse "refs/tags/$Tag^{commit}").Trim()
if ($LASTEXITCODE -ne 0 -or $tagCommit -ne $ExpectedCommit) { throw 'The release tag does not point to the built commit.' }
foreach ($line in Get-Content (Join-Path $AssetDirectory 'SHA256SUMS.txt')) {
    if ($line -notmatch '^([a-f0-9]{64})  ([A-Za-z0-9.\-]+)$') { throw 'Invalid checksum manifest.' }
    if ((Get-FileHash (Join-Path $AssetDirectory $Matches[2]) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $Matches[1]) { throw 'Release asset checksum mismatch.' }
}
& gh release view $Tag --json tagName 2>$null | Out-Null
if ($LASTEXITCODE -eq 0) { throw "Release $Tag already exists. Existing releases are never overwritten; inspect it or use a new tag." }
$names = @("Parvathi-Setup-$($metadata.version)-win-x64.exe", "Parvathi-$($metadata.version)-win-x64-portable.zip", 'SHA256SUMS.txt', 'release-notes.md', 'release-metadata.json')
$assets = @($names | ForEach-Object {
    $path = Join-Path $AssetDirectory $_
    if (-not (Test-Path $path) -or (Get-Item $path).Length -le 0) { throw "Missing or empty release asset: $_" }
    $path
})
# A failed upload can leave only a draft. Publication happens after names and sizes match.
& gh release create $Tag @assets --verify-tag --target $ExpectedCommit --draft --prerelease --title "Parvathi $($metadata.version) for Windows (prerelease)" --notes-file (Join-Path $AssetDirectory 'release-notes.md')
if ($LASTEXITCODE -ne 0) { throw 'Creating the draft release or uploading assets failed.' }
$remote = & gh release view $Tag --json assets | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Could not verify uploaded draft assets.' }
foreach ($asset in $assets) {
    $file = Get-Item $asset
    $match = @($remote.assets | Where-Object name -EQ $file.Name)
    if ($match.Count -ne 1 -or $match[0].size -ne $file.Length) { throw "Uploaded asset validation failed: $($file.Name). The release remains a draft." }
}
& gh release edit $Tag --draft=false --prerelease --latest=false
if ($LASTEXITCODE -ne 0) { throw 'Publishing the verified prerelease failed.' }
& gh release view $Tag --json url --jq '.url'
