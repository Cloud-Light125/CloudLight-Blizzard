param(
    [Parameter(Mandatory)][string]$Manifest,
    [Parameter(Mandatory)][string]$Authorization
)
$ErrorActionPreference = 'Stop'
function Get-DistributionSha256([string]$Path) {
    $stream = [IO.File]::OpenRead([IO.Path]::GetFullPath($Path))
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
    finally { $algorithm.Dispose(); $stream.Dispose() }
}
$build = Get-Content -LiteralPath $Manifest -Raw -Encoding UTF8 | ConvertFrom-Json
if ($build.purpose -ne 'Distribution' -or -not $build.soopCore) {
    throw 'Full SOOP publish requires a Distribution Worker manifest.'
}
if ($env:CLOUDLIGHT_CONFIRM_SOOP_DISTRIBUTION -eq '1') {
    $repo = Join-Path $PSScriptRoot '..'
    $baseline = Get-Content -LiteralPath (Join-Path $repo 'Integrations/Drops/soop/core-baseline.json') -Raw | ConvertFrom-Json
    if ($build.soopCore.commit -ne $baseline.commit -or $build.soopCore.sha256 -ne $baseline.sha256 -or
        $build.soopCore.capabilityVersion -ne $baseline.capabilityVersion) {
        throw 'Distribution requires the exact pinned SOOP Core.'
    }
    $notice = Get-Content -LiteralPath (Join-Path $repo 'Assets/Licenses/SOOP-Brand-Source.txt') -Raw
    $brandHash = [regex]::Match($notice, '(?m)^SHA256: ([0-9a-f]{64})').Groups[1].Value
    if ((Get-DistributionSha256 (Join-Path $repo 'Assets/Brand/soop-official.ico')) -ne $brandHash) {
        throw 'Distribution SOOP mark does not match its source digest.'
    }
} else {
    $record = Get-Content -LiteralPath $Authorization -Raw -Encoding UTF8 | ConvertFrom-Json
    if (-not $record.upstreamRightsReview -or $record.upstreamRightsReview.status -ne 'confirmed' -or
        $record.upstreamRightsReview.allowsFrozenBinaries -ne $true -or
        $record.upstreamRightsReview.coversCoreCommit -ne $record.coreCommit -or
        -not $record.upstreamRightsReview.evidenceUrls -or -not $record.upstreamRightsReview.rightsHolders) {
        throw 'SOOP public distribution BLOCKED: direct-upstream redistribution evidence, including frozen binaries, is not confirmed.'
    }
    $recordHash = Get-DistributionSha256 $Authorization
    if ($record.schema -ne 1 -or $record.repository -ne 'Cloud-Light125/CloudLight-Blizzard' -or
        $record.binaryRedistribution -ne $true -or [string]::IsNullOrWhiteSpace($record.declaredBy) -or
        $build.purpose -ne 'Distribution' -or -not $build.soopCore -or
        $build.soopCore.redistributionAuthorized -ne $true -or
        $build.soopCore.commit -ne $record.coreCommit -or $build.soopCore.sha256 -ne $record.coreSha256 -or
        $build.soopCore.authorization.recordSha256 -ne $recordHash) {
        throw 'SOOP Distribution provenance does not match the publisher authorization scope. Rebuild with an authorized Core.'
    }
}
$artifactRoot = Split-Path ([IO.Path]::GetFullPath($Manifest)) -Parent
foreach ($platform in @('twitch', 'soop', 'youtube', 'bilibili')) {
    $workerFile = Join-Path $artifactRoot "$platform\$platform-worker.exe"
    if (-not (Test-Path -LiteralPath $workerFile) -or
        (Get-DistributionSha256 $workerFile) -ne $build.workers.$platform) {
        throw "Distribution $platform Worker hash does not match the build manifest."
    }
}
Write-Output 'SOOP Distribution provenance and Worker hashes: PASS'
