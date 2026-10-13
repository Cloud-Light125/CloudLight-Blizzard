param(
    [Parameter(Mandatory)][string]$Manifest,
    [Parameter(Mandatory)][string]$Authorization
)
$ErrorActionPreference = 'Stop'
$record = Get-Content -LiteralPath $Authorization -Raw -Encoding UTF8 | ConvertFrom-Json
$build = Get-Content -LiteralPath $Manifest -Raw -Encoding UTF8 | ConvertFrom-Json
$recordHash = (Get-FileHash -LiteralPath $Authorization -Algorithm SHA256).Hash.ToLowerInvariant()
if ($record.schema -ne 1 -or $record.repository -ne 'Cloud-Light125/CloudLight-Blizzard' -or
    $record.binaryRedistribution -ne $true -or [string]::IsNullOrWhiteSpace($record.declaredBy) -or
    $build.purpose -ne 'Distribution' -or -not $build.soopCore -or
    $build.soopCore.redistributionAuthorized -ne $true -or
    $build.soopCore.commit -ne $record.coreCommit -or $build.soopCore.sha256 -ne $record.coreSha256 -or
    $build.soopCore.authorization.recordSha256 -ne $recordHash) {
    throw 'SOOP Distribution provenance does not match the publisher authorization scope. Rebuild with an authorized Core.'
}
$artifactRoot = Split-Path ([IO.Path]::GetFullPath($Manifest)) -Parent
foreach ($platform in @('twitch', 'soop', 'youtube', 'bilibili')) {
    $workerFile = Join-Path $artifactRoot "$platform\$platform-worker.exe"
    if (-not (Test-Path -LiteralPath $workerFile) -or
        (Get-FileHash -LiteralPath $workerFile -Algorithm SHA256).Hash.ToLowerInvariant() -ne $build.workers.$platform) {
        throw "Distribution $platform Worker hash does not match the build manifest."
    }
}
Write-Output 'SOOP Distribution authorization and Worker hashes: PASS'
