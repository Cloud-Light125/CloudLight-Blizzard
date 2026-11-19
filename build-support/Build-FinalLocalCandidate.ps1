param(
    [ValidateSet('Publish','Validate','Package')][string]$Stage,
    [string]$CandidateRoot = '',
    [string]$RecordRoot = '',
    [string]$AppRoot = '',
    [string]$ValidationName = 'app',
    [ValidateSet('Local','Distribution')][string]$DropsBuildPurpose = 'Local'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $RecordRoot) { $RecordRoot = Join-Path $repo 'release\source-snapshots' }
if (-not $CandidateRoot) { $CandidateRoot = Join-Path $repo 'release\candidates\3.0.0-final-local' }
$CandidateRoot = [IO.Path]::GetFullPath($CandidateRoot)
$validation = Join-Path $CandidateRoot 'validation'
New-Item -ItemType Directory -Path $validation,(Join-Path $CandidateRoot 'manifests') -Force | Out-Null
if (-not $AppRoot) { $AppRoot = Join-Path $CandidateRoot 'app' }
$appDll = Join-Path $AppRoot 'CloudLight Blizzard.dll'
$python = Join-Path $repo '.worker-build-venv\Scripts\python.exe'
Set-Location -LiteralPath $repo

function Get-SourceManifest {
    $paths = @(& git -c core.quotepath=false ls-files --cached --others --exclude-standard) | Sort-Object -Unique
    if ($LASTEXITCODE -ne 0) { throw 'Source enumeration failed.' }
    @($paths | Where-Object { $_ -notmatch '^(release|artifacts|test-output|bin|obj)/' } | ForEach-Object {
        $file = Join-Path $repo $_
        if (Test-Path -LiteralPath $file -PathType Leaf) {
            [ordered]@{path=$_;length=(Get-Item -LiteralPath $file).Length;sha256=(Get-FileHash -LiteralPath $file).Hash.ToLowerInvariant()}
        }
    })
}
function Assert-FrozenSource {
    $saved = Get-Content -Raw -LiteralPath (Join-Path $CandidateRoot 'manifests\source-manifest.json') | ConvertFrom-Json
    $current = Get-SourceManifest
    if (($saved | ConvertTo-Json -Depth 8 -Compress) -ne ($current | ConvertTo-Json -Depth 8 -Compress)) { throw 'Source changed since publish; rebuild the affected payload.' }
}
function Invoke-AppTest([string]$Name, [string[]]$Arguments, [string]$ResultPath, [string]$Expected = '') {
    & dotnet $appDll @Arguments *> (Join-Path $validation "$ValidationName-$Name-console.log")
    if ($LASTEXITCODE -ne 0) { throw "$Name failed (exit $LASTEXITCODE)." }
    if (-not (Test-Path -LiteralPath $ResultPath)) { throw "$Name did not produce evidence." }
    $content = Get-Content -Raw -LiteralPath $ResultPath
    if ($content -match '(?m)^FAIL |OVERALL: FAIL' -or ($Expected -and -not $content.Contains($Expected))) { throw "$Name regression failed." }
    Write-Output "PASS $ValidationName/$Name"
}

switch ($Stage) {
    'Publish' {
        if ((Get-Content -Raw -LiteralPath 'CloudLight Blizzard.csproj') -notmatch '<Version>3.0.0</Version>') { throw 'Version must remain 3.0.0.' }
        $baseCommit = (& git rev-parse HEAD | Out-String).Trim()
        if ((& git status --porcelain | Out-String).Trim()) { throw 'Commit all deliverable sources before publishing a candidate.' }
        if (Test-Path -LiteralPath $appDll) { throw 'Use a new candidate directory; existing payloads are preserved.' }
        $source = Get-SourceManifest
        $manifestPath = Join-Path $CandidateRoot 'manifests\source-manifest.json'
        $source | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8
        $sourceHash = (Get-FileHash -LiteralPath $manifestPath).Hash.ToLowerInvariant()
        $snapshot = Join-Path $RecordRoot ('source-snapshot-' + $sourceHash.Substring(0,12))
        New-Item -ItemType Directory -Path $snapshot -Force | Out-Null
        foreach ($file in $source) {
            $destination = Join-Path $snapshot $file.path
            New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
            Copy-Item -LiteralPath (Join-Path $repo $file.path) -Destination $destination
            if ((Get-FileHash -LiteralPath $destination).Hash.ToLowerInvariant() -ne $file.sha256) { throw 'Snapshot identity mismatch.' }
        }
        $env:MSBuildEnableWorkloadResolver = 'false'
        & dotnet publish 'CloudLight Blizzard.csproj' -c Release -r win-x64 --self-contained true -o $AppRoot "-p:BuildCommit=$baseCommit" "-p:BaseCommit=$baseCommit" '-p:WorkingTreeModified=false' "-p:SourceManifestSha256=$sourceHash" "-p:BuildTimestamp=$([DateTimeOffset]::Now.ToString('o'))" "-p:DropsBuildPurpose=$DropsBuildPurpose" "-p:DropsArtifactsRoot=$(Join-Path $CandidateRoot 'workers')" *> (Join-Path $validation 'publish.log')
        if ($LASTEXITCODE -ne 0) { throw 'Release publish failed.' }
        $noticeTarget = Join-Path $AppRoot 'THIRD_PARTY_LICENSES\Python'
        New-Item -ItemType Directory -Path $noticeTarget -Force | Out-Null
        Get-ChildItem -LiteralPath (Join-Path $CandidateRoot 'licenses\Python') | Copy-Item -Destination $noticeTarget -Recurse -Force
        Copy-Item -LiteralPath (Join-Path $CandidateRoot 'licenses\dependencies.json') -Destination (Join-Path $AppRoot 'THIRD_PARTY_LICENSES\Python-dependencies.json') -Force
        Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination (Join-Path $AppRoot 'LICENSE.txt') -Force
        Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $AppRoot 'BUILD-SOURCE.json') -Force
        Get-ChildItem -LiteralPath $AppRoot -Recurse -File -Filter '*.pdb' | Remove-Item -Force
        Assert-FrozenSource
        Write-Output "PASS Release publish; source=$sourceHash; snapshot=$snapshot"
    }
    'Validate' {
        Assert-FrozenSource
        foreach ($test in @('all','account-safety','region-verified','update-safety','exit-safety','twitch-audit','packaged-workers','soop-timeout')) {
            $root = Join-Path $validation "$ValidationName-$test"
            $arguments = @('--feature-selftest',$root)
            if ($test -ne 'all') { $arguments += $test }
            Invoke-AppTest $test $arguments (Join-Path $root 'feature-selftest.txt') 'OVERALL: PASS'
        }
        $uiRoot = Join-Path $validation "$ValidationName-ui-v2"
        Invoke-AppTest 'ui-v2' @('--ui-v2-selftest',$uiRoot) (Join-Path $uiRoot 'ui-v2-selftest.txt')
        $review = Join-Path $validation "$ValidationName-ui-review"
        Invoke-AppTest 'ui-review' @('--ui-review-test',$review) (Join-Path $review 'matrix-results.txt') 'binding-errors=0'
        Assert-FrozenSource
    }
    'Package' {
        Assert-FrozenSource
        $payload = @(Get-ChildItem -LiteralPath $AppRoot -Recurse -File | ForEach-Object {
            [ordered]@{path=[IO.Path]::GetRelativePath($AppRoot,$_.FullName).Replace('\','/');length=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()}
        } | Sort-Object { $_.path })
        if ($payload | Where-Object { $_.path -match '(?i)(cookies\.jar|claim-journal|approved-river|audit_jsonl|\.pdb$|\.py$|(^|/)(settings|session|inventory)\.json$)' }) { throw 'Unexpected private/development payload member.' }
        $payload | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $CandidateRoot 'manifests\payload-manifest.json') -Encoding utf8
        $inno = 'C:\Program Files\Inno Setup 7\ISCC.exe'
        & $inno "/O$(Join-Path $CandidateRoot 'installer')" "/DPublishDir=$AppRoot" (Join-Path $repo 'installer\app.iss') *> (Join-Path $validation 'installer-build.log')
        if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
        Assert-FrozenSource
        Write-Output 'PASS Setup.exe compiled from the verified Release payload'
    }
}
