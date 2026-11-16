param(
    [Parameter(Mandatory)][string]$BaselineCandidate,
    [Parameter(Mandatory)][string]$OutputRoot,
    [Parameter(Mandatory)][string]$ReviewEvidence,
    [string]$Python = ''
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$taskRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskBaseline = (Resolve-Path -LiteralPath $BaselineCandidate).Path
$taskOutput = [IO.Path]::GetFullPath($OutputRoot)
$taskReview = (Resolve-Path -LiteralPath $ReviewEvidence).Path
if (Test-Path -LiteralPath $taskOutput) { throw 'Use a new candidate directory.' }
Push-Location $taskRepo
$taskOldResolver = $env:MSBuildEnableWorkloadResolver
try {
    $taskCommit = (& git rev-parse HEAD | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify source commit.' }
    if ((& git branch --show-current | Out-String).Trim() -ne 'ui/fluent-v2') { throw 'Use the independent ui/fluent-v2 branch.' }
    if ((& git status --porcelain --untracked-files=no | Out-String).Trim()) { throw 'Commit reviewed source changes first.' }
    $taskUntracked = @(& git ls-files --others --exclude-standard | Where-Object { $_ -notlike 'artifacts/*' })
    if ($taskUntracked.Count) { throw 'Review and commit new source files first.' }
    $taskMeta = Get-Content -LiteralPath (Join-Path $taskBaseline 'candidate.json') -Raw | ConvertFrom-Json
    & git diff --exit-code $taskMeta.commit $taskCommit -- Integrations/Drops/Shared Integrations/Drops/soop Integrations/Drops/bilibili Integrations/Drops/youtube Integrations/Drops/build-constraints.txt
    if ($LASTEXITCODE -ne 0) { throw 'Inherited worker sources changed; reuse is unsafe.' }
    $taskResults = Get-Content -LiteralPath (Join-Path $taskReview 'matrix-results.txt')
    $taskDpiLine = $taskResults | Where-Object { $_ -match '^physical-monitor-dpi=' } | Select-Object -Last 1
    $taskPhysicalDpi = if ($taskDpiLine -match '^physical-monitor-dpi=(\d+)/') { [int]$Matches[1] } else { 0 }
    if ($taskResults -notcontains 'binding-errors=0' -or @($taskResults | Where-Object { $_ -match '^FAIL' }).Count) { throw 'Visual/layout review contains failures.' }
    $taskWorkers = Join-Path $taskOutput 'workers'
    $taskValidation = Join-Path $taskOutput 'validation'
    $taskApp = Join-Path $taskOutput 'app'
    New-Item -ItemType Directory -Path $taskValidation -Force | Out-Null
    & (Join-Path $taskRepo 'Integrations/Drops/build-workers.ps1') -Python $Python -Platforms twitch -BuildPurpose Local -ArtifactsRoot $taskWorkers *> (Join-Path $taskValidation 'twitch-build.log')
    if ($LASTEXITCODE -ne 0) { throw 'Twitch worker build/embedded compatibility checks failed.' }
    $taskManifestFile = Join-Path $taskWorkers 'build-manifest.json'
    $taskManifest = Get-Content -LiteralPath $taskManifestFile -Raw | ConvertFrom-Json -AsHashtable
    Copy-Item -LiteralPath $taskManifestFile -Destination (Join-Path $taskWorkers 'twitch-build-manifest.json')
    Copy-Item -LiteralPath (Join-Path $taskBaseline 'workers/build-manifest.json') -Destination (Join-Path $taskWorkers 'inherited-build-manifest.json')
    $taskBuilds = [ordered]@{ twitch = @{commit=$taskCommit;sha256=$taskManifest.workers.twitch;rebuilt=$true;manifest='twitch-build-manifest.json'} }
    foreach ($taskPlatform in @('soop','bilibili','youtube')) {
        $taskOriginal = Join-Path $taskBaseline "workers/$taskPlatform/$taskPlatform-worker.exe"
        $taskHash = (Get-FileHash -LiteralPath $taskOriginal -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($taskHash -ne $taskMeta.workers.$taskPlatform) { throw "Inherited worker hash mismatch: $taskPlatform" }
        Copy-Item -LiteralPath (Join-Path $taskBaseline "workers/$taskPlatform") -Destination (Join-Path $taskWorkers $taskPlatform) -Recurse
        $taskManifest.workers[$taskPlatform] = $taskHash
        $taskBuilds[$taskPlatform] = @{commit=$taskMeta.commit;sha256=$taskHash;rebuilt=$false;manifest='inherited-build-manifest.json'}
    }
    $taskManifest['workerBuilds']=$taskBuilds
    $taskManifest['scope']='UI v2: Twitch read-only metadata adapter rebuilt; SOOP/Bilibili/YouTube inherited unchanged'
    $taskManifest | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $taskManifestFile -Encoding utf8
    $env:MSBuildEnableWorkloadResolver='false'
    & dotnet publish 'CloudLight Blizzard.csproj' -c Release --no-restore -o $taskApp "-p:BuildCommit=$taskCommit" "-p:BuildTimestamp=$([DateTimeOffset]::UtcNow.ToString('o'))" '-p:DropsBuildPurpose=Maintenance' "-p:DropsArtifactsRoot=$taskWorkers" *> (Join-Path $taskValidation 'publish.log')
    if ($LASTEXITCODE -ne 0) { throw 'WPF Release publish failed.' }
    foreach ($taskPlatform in $taskBuilds.Keys) {
        $taskPackaged = Join-Path $taskApp "_internal/drops/$taskPlatform/$taskPlatform-worker.exe"
        if ((Get-FileHash -LiteralPath $taskPackaged).Hash.ToLowerInvariant() -ne $taskBuilds[$taskPlatform].sha256) { throw "Packaged worker mismatch: $taskPlatform" }
    }
    $taskDll = Join-Path $taskApp 'CloudLight Blizzard.dll'
    & dotnet $taskDll --feature-selftest (Join-Path $taskValidation 'feature') *> (Join-Path $taskValidation 'feature-console.log')
    if ($LASTEXITCODE -ne 0) { throw 'FeatureSelfTest failed.' }
    & dotnet $taskDll --ui-selftest (Join-Path $taskValidation 'ui.txt') *> (Join-Path $taskValidation 'ui-console.log')
    $taskUiExit=$LASTEXITCODE
    if ($taskUiExit -notin @(0,2)) { throw 'UI/Host/Dispatcher regression failed.' }
    & dotnet $taskDll --ui-v2-selftest (Join-Path $taskValidation 'images') *> (Join-Path $taskValidation 'images-console.log')
    $taskImageExit=$LASTEXITCODE
    if ($taskImageExit -notin @(0,2)) { throw 'Image/projection regression failed.' }
    & dotnet $taskDll --ui-review-test (Join-Path $taskValidation 'presentation') *> (Join-Path $taskValidation 'presentation-console.log')
    if ($LASTEXITCODE -ne 0) { throw 'Packaged WPF layout/binding/draft/launch-exit regression failed.' }
    $taskReceipt = [ordered]@{
        commit=$taskCommit;branch='ui/fluent-v2';sourceState='clean';app=$taskApp
        workers=$taskManifest.workers;workerBuilds=$taskBuilds;releaseBuild='PASS';feature='PASS'
        ui=$(if($taskUiExit -eq 2){'BLOCKED'}else{'PASS'});images=$(if($taskImageExit -eq 2){'BLOCKED'}else{'PASS'})
        presentation='PASS';physicalMonitorDpi=$taskPhysicalDpi;equivalentDpi=@(100,125,150)
        officialRewardImage='BLOCKED: no verified authenticated Core snapshot';realClaim='NOT RUN'
        appDllSha256=(Get-FileHash -LiteralPath $taskDll).Hash.ToLowerInvariant()
        purpose='Local UI v2 review candidate; no installer, public Release, tag or stable branch replacement'
    }
    if ((& git status --porcelain --untracked-files=no | Out-String).Trim() -or (& git rev-parse HEAD | Out-String).Trim() -ne $taskCommit) { throw 'Source changed during candidate build.' }
    $taskReceipt | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $taskOutput 'candidate.json') -Encoding utf8
    $taskReceipt | ConvertTo-Json -Depth 20
}
finally { $env:MSBuildEnableWorkloadResolver=$taskOldResolver; Pop-Location }
