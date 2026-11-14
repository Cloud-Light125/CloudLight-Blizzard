param(
    [Parameter(Mandatory)][string]$SoopCorePath,
    [string]$OutputRoot = '',
    [ValidateSet('Local', 'Distribution')][string]$BuildPurpose = 'Local'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$commit = (& git -C $repo rev-parse HEAD | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40}$') { throw 'Cannot identify source commit.' }
$dirty = (& git -C $repo status --porcelain --untracked-files=no | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $dirty) { throw 'Commit tracked changes before building the final candidate.' }
[xml]$project = Get-Content -LiteralPath (Join-Path $repo 'CloudLight Blizzard.csproj') -Raw
$version = [string]$project.Project.PropertyGroup[0].Version
if (-not $OutputRoot) { $OutputRoot = Join-Path $repo "release\candidates\$version-$($commit.Substring(0,12))-$BuildPurpose" }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
if ((Test-Path -LiteralPath $OutputRoot) -and @(Get-ChildItem -LiteralPath $OutputRoot -Force).Count) {
    throw 'Candidate directory must be new or empty. Existing artifacts are never removed.'
}
$SoopCorePath = (Resolve-Path -LiteralPath $SoopCorePath).Path
$inno = @($env:ProgramFiles, ${env:ProgramFiles(x86)}, $env:LOCALAPPDATA) |
    Where-Object { $_ } | ForEach-Object {
        Join-Path $_ 'Inno Setup 7\ISCC.exe'
        Join-Path $_ 'Inno Setup 6\ISCC.exe'
    } | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
if (-not $inno) { throw 'Inno Setup 6/7 is required to build the complete local installer candidate.' }
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$workers = Join-Path $OutputRoot 'workers'
$app = Join-Path $OutputRoot 'app'
$validation = Join-Path $OutputRoot 'validation'
New-Item -ItemType Directory -Path $validation -Force | Out-Null
$previousCore = $env:CLOUDLIGHT_SOOP_CORE
$previousWorkload = $env:MSBuildEnableWorkloadResolver
$previousCompatibility = $env:__COMPAT_LAYER
Push-Location $repo
try {
    $env:CLOUDLIGHT_SOOP_CORE = $SoopCorePath
    $env:MSBuildEnableWorkloadResolver = 'false'
    & (Join-Path $repo 'Integrations\Drops\build-workers.ps1') -SoopCorePath $SoopCorePath `
        -BuildPurpose $BuildPurpose -ArtifactsRoot $workers > (Join-Path $validation 'workers-build.log') 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'Worker build failed; inspect validation/workers-build.log.' }
    $properties = @("-p:BuildCommit=$commit", "-p:BuildTimestamp=$([DateTimeOffset]::Now.ToString('o'))",
        "-p:DropsBuildPurpose=$BuildPurpose", "-p:DropsArtifactsRoot=$workers")
    & dotnet publish 'CloudLight Blizzard.csproj' -c Release -r win-x64 --self-contained false -o $app @properties `
        > (Join-Path $validation 'publish.log') 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'WPF publish failed; inspect validation/publish.log.' }
    $auditPath = Join-Path $validation 'nuget-vulnerabilities.json'
    & dotnet list 'CloudLight Blizzard.csproj' package --vulnerable --include-transitive --format json > $auditPath
    if ($LASTEXITCODE -ne 0) { throw 'NuGet vulnerability audit could not be completed.' }
    $audit = Get-Content -LiteralPath $auditPath -Raw | ConvertFrom-Json -AsHashtable
    if (-not $audit.ContainsKey('projects') -or -not $audit.ContainsKey('sources') -or -not $audit.sources.Count) {
        throw 'NuGet vulnerability audit did not return a complete result.'
    }
    foreach ($auditProject in $audit.projects) {
        if (-not $auditProject.ContainsKey('frameworks')) { continue }
        foreach ($framework in $auditProject.frameworks) {
            foreach ($kind in @('topLevelPackages', 'transitivePackages')) {
                if (-not $framework.ContainsKey($kind)) { continue }
                foreach ($package in $framework[$kind]) {
                    if ($package.ContainsKey('vulnerabilities') -and $package.vulnerabilities.Count) {
                        throw "Known vulnerable dependency remains: $($package.id)"
                    }
                }
            }
        }
    }
    $dll = Join-Path $app 'CloudLight Blizzard.dll'
    foreach ($suite in @('all', 'account-safety', 'region-verified', 'update-safety', 'exit-safety', 'soop-timeout')) {
        & dotnet $dll --feature-selftest (Join-Path $validation $suite) $suite
        $report = Join-Path $validation "$suite\feature-selftest.txt"
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $report) -or
            -not (Get-Content -LiteralPath $report -Raw).Contains('OVERALL: PASS')) { throw "Component suite failed: $suite" }
    }
    $uiReport = Join-Path $validation 'ui.txt'
    & dotnet $dll --ui-selftest $uiReport
    $uiExit = $LASTEXITCODE
    if (-not (Test-Path -LiteralPath $uiReport)) { throw 'UI report was not generated.' }
    $ui = Get-Content -LiteralPath $uiReport -Raw
    if ($uiExit -notin @(0, 2) -or $ui -match '(?m)^FAIL ') { throw 'WPF UI regression failed.' }
    # A blocked clipboard/hardware check remains blocked in the delivered report.
    $uiStatus = if ($uiExit -eq 2 -or $ui -match '(?m)^BLOCKED ') { 'BLOCKED' } else { 'PASS' }
    $installerRoot = Join-Path $OutputRoot 'installer'
    & $inno "/O$installerRoot" "/DPublishDir=$app" (Join-Path $repo 'installer\app.iss') `
        > (Join-Path $validation 'installer-build.log') 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    $installer = Join-Path $installerRoot "CloudLight-Blizzard-$version-win-x64-Setup.exe"
    if (-not (Test-Path -LiteralPath $installer)) { throw 'Installer output is missing.' }
    # Test-only entry points abort before installation. Keep the child at the
    # caller's privilege level; these checks need no elevated system access.
    $env:__COMPAT_LAYER = 'RunAsInvoker'
    foreach ($mode in @('PAYLOADVERIFY', 'SETTINGSPRESERVETEST')) {
        $log = Join-Path $validation "installer-$mode.log"
        $arguments = @("/$mode", '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/NOCANCEL',
            '/NOCLOSEAPPLICATIONS', '/NOICONS', ('/DIR="' + (Join-Path $validation 'unused-install') + '"'), ('/LOG="' + $log + '"'))
        $process = Start-Process -FilePath $installer -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
        if (-not (Test-Path -LiteralPath $log)) { throw "Installer self-test did not produce a report: $mode" }
        $content = Get-Content -LiteralPath $log -Raw
        if ($mode -eq 'SETTINGSPRESERVETEST') {
            if (-not $content.Contains('SETTINGS_PRESERVE_TEST PASS') -or $content.Contains('SETTINGS_PRESERVE_TEST FAIL')) {
                throw 'Installer settings preservation self-test failed.'
            }
        } else {
            foreach ($name in @('EXE', 'DLL')) {
                $payload = Join-Path $app ("CloudLight Blizzard." + $name.ToLowerInvariant())
                $hash = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToLowerInvariant()
                if (-not $content.Contains("PAYLOAD_VERIFY ${name}_SHA256=$hash") -or $content.Contains('PAYLOAD_VERIFY FAIL')) {
                    throw "Installer payload mismatch: $name"
                }
            }
        }
    }
    $endCommit = (& git rev-parse HEAD | Out-String).Trim()
    $endDirty = (& git status --porcelain --untracked-files=no | Out-String).Trim()
    if ($endCommit -ne $commit -or $endDirty) { throw 'Source changed during candidate build.' }
    $manifest = Get-Content -LiteralPath (Join-Path $workers 'build-manifest.json') -Raw | ConvertFrom-Json
    $metadata = [ordered]@{ version=$version; commit=$commit; sourceState='clean'; purpose=$BuildPurpose;
        uiAcceptance=$uiStatus; componentAcceptance='PASS'; installedToSystem=$false;
        officialClaimTests='NOT RUN'; knownNugetVulnerabilities=0; workerSources=$manifest.adapterSources.sha256; workers=$manifest.workers;
        soopCoreSha256=$manifest.soopCore.sha256; soopCoreCommit=$manifest.soopCore.commit;
        appDllSha256=(Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash.ToLowerInvariant();
        installerSha256=(Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant() }
    $metadata | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $OutputRoot 'candidate.json') -Encoding utf8
    Write-Output "CANDIDATE BUILD PASS; UI=$uiStatus; commit=$commit; $OutputRoot"
} finally {
    Pop-Location
    $env:CLOUDLIGHT_SOOP_CORE = $previousCore
    $env:MSBuildEnableWorkloadResolver = $previousWorkload
    $env:__COMPAT_LAYER = $previousCompatibility
}
