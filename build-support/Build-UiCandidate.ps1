param(
    [Parameter(Mandatory)][string]$BaselineCandidate,
    [Parameter(Mandatory)][string]$OutputRoot
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$baseline = (Resolve-Path -LiteralPath $BaselineCandidate).Path
$output = [IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $output) { throw 'Use a new output directory; candidates are never overwritten.' }
Push-Location $repo
$previousResolver = $env:MSBuildEnableWorkloadResolver
try {
    $commit = (& git rev-parse HEAD | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot read source commit.' }
    if ((& git status --porcelain --untracked-files=no | Out-String).Trim()) { throw 'Commit tracked changes before building a candidate.' }
    $untracked = @(& git ls-files --others --exclude-standard | Where-Object { $_ -notlike 'artifacts/*' })
    if ($untracked.Count) { throw 'Untracked source files must be reviewed and committed first.' }
    $meta = Get-Content -Raw -LiteralPath (Join-Path $baseline 'candidate.json') | ConvertFrom-Json
    & git diff --exit-code $meta.commit HEAD -- Integrations/Drops
    if ($LASTEXITCODE -ne 0) { throw 'Worker sources differ from baseline; re-evaluate component builds.' }
    $workers = Join-Path $baseline 'workers'
    $hashes = [ordered]@{}
    foreach ($platform in @('soop','twitch','bilibili','youtube')) {
        $hash = (Get-FileHash -LiteralPath (Join-Path $workers "$platform/$platform-worker.exe") -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($hash -ne $meta.workers.$platform) { throw "Baseline Worker hash mismatch: $platform" }
        $hashes[$platform] = $hash
    }
    $app = Join-Path $output 'app'
    $validation = Join-Path $output 'validation'
    New-Item -ItemType Directory -Path $validation -Force | Out-Null
    $env:MSBuildEnableWorkloadResolver = 'false'
    & dotnet publish 'CloudLight Blizzard.csproj' -c Release --no-restore -o $app "-p:BuildCommit=$commit" "-p:BuildTimestamp=$([DateTimeOffset]::UtcNow.ToString('o'))" '-p:DropsBuildPurpose=Maintenance' "-p:DropsArtifactsRoot=$workers" *> (Join-Path $validation 'publish.log')
    if ($LASTEXITCODE -ne 0) { throw 'WPF Release publish failed.' }
    foreach ($platform in $hashes.Keys) {
        $packaged = Join-Path $app "_internal/drops/$platform/$platform-worker.exe"
        if ((Get-FileHash -LiteralPath $packaged -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hashes[$platform]) { throw "Packaged Worker mismatch: $platform" }
    }
    $dll = Join-Path $app 'CloudLight Blizzard.dll'
    & dotnet $dll --feature-selftest (Join-Path $validation 'feature') *> (Join-Path $validation 'feature-console.log')
    if ($LASTEXITCODE -ne 0) { throw 'FeatureSelfTest failed.' }
    & dotnet $dll --ui-selftest (Join-Path $validation 'ui.txt') *> (Join-Path $validation 'ui-console.log')
    $uiExit = $LASTEXITCODE
    if ($uiExit -notin @(0,2)) { throw 'UI/Host regression failed.' }
    & dotnet $dll --ui-review-test (Join-Path $validation 'presentation') *> (Join-Path $validation 'presentation-console.log')
    if ($LASTEXITCODE -ne 0) { throw 'Presentation/size/draft/launch-exit regression failed.' }
    $presentationLines = Get-Content -LiteralPath (Join-Path $validation 'presentation/matrix-results.txt')
    if (@($presentationLines | Where-Object { $_ -match '^FAIL' }).Count -or $presentationLines -notcontains 'binding-errors=0') { throw 'Presentation report contains a failure.' }
    $dpiLine = $presentationLines | Where-Object { $_ -match '^physical-monitor-dpi=' } | Select-Object -Last 1
    $physicalDpi = if ($dpiLine -match '^physical-monitor-dpi=(\d+)/') { [int]$Matches[1] } else { 0 }
    $receipt = [ordered]@{
        sourceCommit=$commit; baselineCommit=$meta.commit; baselineCandidate=$baseline
        app=$app; workers=$hashes; rebuiltWorkers=$false
        feature='PASS'; ui=$(if ($uiExit -eq 2) { 'BLOCKED' } else { 'PASS' }); presentation='PASS'
        physicalDpi=$physicalDpi; equivalentDpi=@(100,125,150); layouts=@($presentationLines | Where-Object { $_ -match '^layout ' }).Count
        purpose='Local WPF UI acceptance candidate; not an installer or public Release'
    }
    $receipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'candidate.json') -Encoding utf8
    $receipt | ConvertTo-Json -Depth 8
}
finally {
    $env:MSBuildEnableWorkloadResolver = $previousResolver
    Pop-Location
}
