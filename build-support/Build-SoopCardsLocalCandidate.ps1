param(
    [Parameter(Mandatory)][ValidateSet('Freeze','Publish','Validate','Package','Extract')][string]$Stage,
    [Parameter(Mandatory)][string]$CandidateRoot,
    [string]$SoopCorePath = '',
    [string]$AppRoot = '',
    [string]$ValidationName = 'app'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$CandidateRoot = [IO.Path]::GetFullPath($CandidateRoot)
$python = Join-Path $repo '.worker-build-venv\Scripts\python.exe'
$validation = Join-Path $CandidateRoot 'validation'
Set-Location -LiteralPath $repo
if ($Stage -eq 'Freeze') {
    if ((& git status --porcelain | Out-String).Trim()) { throw 'Commit sources before building.' }
    if ((Test-Path -LiteralPath $CandidateRoot) -and @(Get-ChildItem -LiteralPath $CandidateRoot -Force).Count) { throw 'Use a new candidate path; previous candidates are preserved.' }
    if (-not $SoopCorePath) { throw 'Specify the independently verified local SOOP Core path.' }
    New-Item -ItemType Directory -Path $validation,(Join-Path $CandidateRoot 'manifests') -Force | Out-Null
    $previousCore = $env:CLOUDLIGHT_SOOP_CORE
    try {
        $env:CLOUDLIGHT_SOOP_CORE = [IO.Path]::GetFullPath($SoopCorePath)
        & (Join-Path $repo 'Integrations\Drops\build-workers.ps1') -BuildPurpose Local -SoopCorePath $SoopCorePath -ArtifactsRoot (Join-Path $CandidateRoot 'workers') *> (Join-Path $validation 'freeze.log')
        if ($LASTEXITCODE -ne 0) { throw 'Worker freeze/regression failed.' }
    } finally { $env:CLOUDLIGHT_SOOP_CORE = $previousCore }
    foreach ($step in @('prepare','licenses','extracted-tests')) {
        & $python (Join-Path $PSScriptRoot 'Verify-FinalPayload.py') $step --repo $repo --candidate $CandidateRoot *> (Join-Path $validation "$step.log")
        if ($LASTEXITCODE -ne 0) { throw "Frozen payload $step failed." }
    }
    & $python (Join-Path $PSScriptRoot 'Verify-FrozenSoop.py') --repo $repo --candidate $CandidateRoot *> (Join-Path $validation 'soop-binary-extracted-tests.log')
    if ($LASTEXITCODE -ne 0) { throw 'Frozen SOOP regression failed.' }
    Write-Output 'PASS all four Workers rebuilt from this clean commit; LOCAL-ONLY; frozen Twitch regression and exact bytecode verified.'
} elseif ($Stage -eq 'Extract') {
    $setup = Join-Path $CandidateRoot 'installer\CloudLight-Blizzard-3.0.0-win-x64-Setup.exe'
    $extracted = Join-Path $CandidateRoot 'installer-payload-verified'
    if (Test-Path -LiteralPath $extracted) { throw 'Extraction directory must be new.' }
    $previousLayer = $env:__COMPAT_LAYER
    try {
        $env:__COMPAT_LAYER = 'RunAsInvoker'
        foreach ($mode in @('PAYLOADVERIFY','SETTINGSPRESERVETEST')) {
            $log = Join-Path $validation "installer-$mode.log"
            $arguments = @("/$mode",'/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOCLOSEAPPLICATIONS',('/LOG="'+$log+'"'))
            if ($mode -eq 'PAYLOADVERIFY') { $arguments += '/PAYLOADOUT="'+$extracted+'"' }
            $process = Start-Process -FilePath $setup -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
            $content = Get-Content -Raw -LiteralPath $log
            if ($mode -eq 'PAYLOADVERIFY') {
                if (-not $content.Contains('PAYLOAD_VERIFY PASS') -or -not $content.Contains('PAYLOAD_EXTRACT PASS')) { throw 'Full payload extraction failed.' }
            } elseif (-not $content.Contains('ALL_PRESERVATION_CHECKS PASS')) { throw 'Isolated settings retention test failed.' }
            Write-Output "PASS $mode; setup exit=$($process.ExitCode); intentionally aborted before system installation."
        }
    } finally { $env:__COMPAT_LAYER = $previousLayer }
    $expected = @(Get-Content -Raw -LiteralPath (Join-Path $CandidateRoot 'manifests\payload-manifest.json') | ConvertFrom-Json)
    if (@(Get-ChildItem -LiteralPath $extracted -Recurse -File).Count -ne $expected.Count) { throw 'Extracted file count differs.' }
    foreach ($file in $expected) {
        if ((Get-FileHash -LiteralPath (Join-Path $extracted $file.path)).Hash.ToLowerInvariant() -ne $file.sha256) { throw "Extracted hash mismatch: $($file.path)" }
    }
    Write-Output "PASS $($expected.Count) exact compressed payload files."
} else {
    & (Join-Path $PSScriptRoot 'Build-FinalLocalCandidate.ps1') -Stage $Stage -CandidateRoot $CandidateRoot -AppRoot $AppRoot -ValidationName $ValidationName
    if ($LASTEXITCODE -ne 0) { throw "Local candidate $Stage failed." }
    if ($Stage -eq 'Package') {
        $commit = (& git rev-parse HEAD | Out-String).Trim()
        $records = Get-Content -Raw -LiteralPath (Join-Path $CandidateRoot 'manifests\workers.json') | ConvertFrom-Json
        $sums = @('app/CloudLight Blizzard.exe','app/CloudLight Blizzard.dll','workers/twitch/twitch-worker.exe','workers/soop/soop-worker.exe','workers/bilibili/bilibili-worker.exe','workers/youtube/youtube-worker.exe','installer/CloudLight-Blizzard-3.0.0-win-x64-Setup.exe')
        $hashes = [ordered]@{}
        foreach ($relative in $sums) { $hashes[$relative] = (Get-FileHash -LiteralPath (Join-Path $CandidateRoot $relative)).Hash.ToLowerInvariant() }
        $hashes.GetEnumerator() | ForEach-Object { "$($_.Value) *$($_.Key)" } | Set-Content -LiteralPath (Join-Path $CandidateRoot 'SHA256SUMS.txt') -Encoding utf8
        [ordered]@{version='3.0.0';distributionStatus='LOCAL-ONLY';buildCommit=$commit;workingTreeModified=$false;
            releaseTag=$null;releaseUrl=$null;publicDownloadUrl=$null;publicDistribution='BLOCKED: direct-upstream SOOP rights not confirmed';
            sourceManifestSha256=(Get-FileHash -LiteralPath (Join-Path $CandidateRoot 'manifests\source-manifest.json')).Hash.ToLowerInvariant();
            hashes=$hashes;workers=$records;realClaimRequests=0;realWatchRequests=0;systemInstall='NOT TESTED';
            autoUpdateRemote='BLOCKED: no public 3.0.0 release';sameVersionBuildUpgrade=$false} | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath (Join-Path $CandidateRoot 'candidate.json') -Encoding utf8
    }
}
