param(
    [Parameter(Mandatory)][string]$BaselineCandidate,
    [Parameter(Mandatory)][string]$OutputRoot
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$taskRepo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskBaseline=(Resolve-Path -LiteralPath $BaselineCandidate).Path
$taskOutput=[IO.Path]::GetFullPath($OutputRoot)
if(Test-Path -LiteralPath $taskOutput){throw 'Use a new candidate directory.'}
Push-Location $taskRepo
$taskResolver=$env:MSBuildEnableWorkloadResolver
try {
    $taskCommit=(& git rev-parse HEAD | Out-String).Trim()
    if($LASTEXITCODE -ne 0){throw 'Cannot identify commit.'}
    if((& git branch --show-current | Out-String).Trim() -ne 'ui/fluent-v2'){throw 'Use ui/fluent-v2.'}
    if((& git status --porcelain --untracked-files=no | Out-String).Trim()){throw 'Commit reviewed source changes first.'}
    $taskMeta=Get-Content -LiteralPath (Join-Path $taskBaseline 'candidate.json') -Raw | ConvertFrom-Json
    & git diff --exit-code $taskMeta.commit $taskCommit -- Integrations/Drops
    if($LASTEXITCODE -ne 0){throw 'Worker sources changed; inherited binaries cannot be reused.'}
    $taskValidation=Join-Path $taskOutput 'validation'
    $taskWorkers=Join-Path $taskOutput 'workers'
    $taskApp=Join-Path $taskOutput 'app'
    New-Item -ItemType Directory -Path $taskValidation -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $taskBaseline 'workers') -Destination $taskWorkers -Recurse
    $taskBuilds=[ordered]@{}
    foreach($taskPlatform in @('twitch','soop','bilibili','youtube')){
        $taskWorker=Join-Path $taskWorkers "$taskPlatform/$taskPlatform-worker.exe"
        $taskHash=(Get-FileHash -LiteralPath $taskWorker -Algorithm SHA256).Hash.ToLowerInvariant()
        $taskWorkerMeta=$taskMeta.workerBuilds.$taskPlatform
        $taskExpectedHash=if($taskMeta.PSObject.Properties.Name -contains 'workers'){$taskMeta.workers.$taskPlatform}else{$taskWorkerMeta.sha256}
        $taskWorkerCommit=if($taskWorkerMeta.PSObject.Properties.Name -contains 'sourceCommit'){$taskWorkerMeta.sourceCommit}else{$taskWorkerMeta.commit}
        if($taskHash -ne $taskExpectedHash){throw "Worker hash mismatch: $taskPlatform"}
        $taskBuilds[$taskPlatform]=[ordered]@{sha256=$taskHash;sourceCommit=$taskWorkerCommit;rebuilt=$false}
    }
    $env:MSBuildEnableWorkloadResolver='false'
    & dotnet publish 'CloudLight Blizzard.csproj' -c Release --no-restore -o $taskApp "-p:BuildCommit=$taskCommit" "-p:BuildTimestamp=$([DateTimeOffset]::UtcNow.ToString('o'))" '-p:DropsBuildPurpose=Maintenance' "-p:DropsArtifactsRoot=$taskWorkers" *> (Join-Path $taskValidation 'publish.log')
    if($LASTEXITCODE -ne 0){throw 'WPF Release publish failed.'}
    foreach($taskPlatform in $taskBuilds.Keys){
        if((Get-FileHash -LiteralPath (Join-Path $taskApp "_internal/drops/$taskPlatform/$taskPlatform-worker.exe") -Algorithm SHA256).Hash.ToLowerInvariant() -ne $taskBuilds[$taskPlatform].sha256){throw "Packaged Worker mismatch: $taskPlatform"}
    }
    $taskDll=Join-Path $taskApp 'CloudLight Blizzard.dll'
    & dotnet $taskDll --ui-v2-selftest (Join-Path $taskValidation 'images') *> (Join-Path $taskValidation 'images-console.log')
    if($LASTEXITCODE -ne 0){throw 'Image/state/control tests failed.'}
    & dotnet $taskDll --ui-selftest (Join-Path $taskValidation 'ui-host.txt') *> (Join-Path $taskValidation 'ui-console.log')
    $taskUiExit=$LASTEXITCODE
    if($taskUiExit -notin @(0,2)){throw 'UI/Host regression failed.'}
    & dotnet $taskDll --feature-selftest (Join-Path $taskValidation 'feature') *> (Join-Path $taskValidation 'feature-console.log')
    if($LASTEXITCODE -ne 0){throw 'Feature tests failed.'}
    $taskDensity=Join-Path $taskValidation 'density'
    New-Item -ItemType Directory -Path $taskDensity | Out-Null
    Set-Content -LiteralPath (Join-Path $taskDensity 'density-test.flag') -Value 'SIMULATED data, actual WPF at current physical DPI' -Encoding utf8
    & dotnet $taskDll --ui-review-test $taskDensity *> (Join-Path $taskValidation 'density-console.log')
    if($LASTEXITCODE -ne 0){throw 'Reward density/layout/binding regression failed.'}
    & dotnet $taskDll --ui-review-test (Join-Path $taskValidation 'layouts') *> (Join-Path $taskValidation 'layout-console.log')
    if($LASTEXITCODE -ne 0){throw 'Full layout/binding/draft regression failed.'}
    if((& git status --porcelain --untracked-files=no | Out-String).Trim() -or (& git rev-parse HEAD | Out-String).Trim() -ne $taskCommit){throw 'Source changed during build.'}
    $taskResult=[ordered]@{
        commit=$taskCommit;branch='ui/fluent-v2';app=$taskApp;workerBuilds=$taskBuilds
        appDllSha256=(Get-FileHash -LiteralPath $taskDll -Algorithm SHA256).Hash.ToLowerInvariant()
        releaseBuild='PASS';images='PASS';feature='PASS';density='PASS';layouts='PASS'
        uiHost=$(if($taskUiExit -eq 0){'PASS'}else{'BLOCKED: inspect validation/ui-host.txt'})
        realAccountEvidence='docs/ui-redesign-v2/real-twitch-e2e.md (separate from simulated component/layout tests)'
        originalUserDataModified=$false;workerSourcesChanged=$false
        purpose='Local review candidate; no installer, Release, tag or main merge'
    }
    $taskResult | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $taskOutput 'candidate.json') -Encoding utf8
    $taskResult | ConvertTo-Json -Depth 12
} finally {$env:MSBuildEnableWorkloadResolver=$taskResolver;Pop-Location}
