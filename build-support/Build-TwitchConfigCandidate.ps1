param(
    [Parameter(Mandatory)][string]$BaselineCandidate,
    [Parameter(Mandatory)][string]$OutputRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$BaselineCandidate = (Resolve-Path -LiteralPath $BaselineCandidate).Path
Push-Location $repo
$oldWorkload = $env:MSBuildEnableWorkloadResolver
$oldCompatibility = $env:__COMPAT_LAYER
try {
    $commit = (& git rev-parse HEAD | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or (& git status --porcelain | Out-String).Trim()) { throw 'Use a completely clean checkout.' }
    if ((Test-Path -LiteralPath $OutputRoot) -and @(Get-ChildItem -LiteralPath $OutputRoot -Force).Count) { throw 'Output must be new; previous candidates are never overwritten.' }
    $baseline = Get-Content -Raw -LiteralPath (Join-Path $BaselineCandidate 'candidate.json') | ConvertFrom-Json
    # Inherited binaries are allowed only when their entire platform + shared
    # Worker sources are unchanged. Their original identity is recorded below.
    & git diff --exit-code $baseline.commit $commit -- Integrations/Drops/Shared Integrations/Drops/soop Integrations/Drops/bilibili Integrations/Drops/youtube Integrations/Drops/build-constraints.txt
    if ($LASTEXITCODE -ne 0) { throw 'An inherited Worker source changed; rebuild that component separately.' }
    $workers = Join-Path $OutputRoot 'workers'
    $validation = Join-Path $OutputRoot 'validation'
    $app = Join-Path $OutputRoot 'app'
    New-Item -ItemType Directory -Path $validation -Force | Out-Null
    & (Join-Path $repo 'Integrations\Drops\build-workers.ps1') -Platforms twitch -BuildPurpose Local -ArtifactsRoot $workers *> (Join-Path $validation 'twitch-build.log')
    if ($LASTEXITCODE -ne 0) { throw 'Twitch freeze or embedded SSL/protocol/source checks failed.' }
    $manifestFile = Join-Path $workers 'build-manifest.json'
    $manifest = Get-Content -Raw -LiteralPath $manifestFile | ConvertFrom-Json -AsHashtable
    Copy-Item -LiteralPath $manifestFile -Destination (Join-Path $workers 'twitch-build-manifest.json')
    Copy-Item -LiteralPath (Join-Path $BaselineCandidate 'workers\build-manifest.json') -Destination (Join-Path $workers 'inherited-build-manifest.json')
    $builds = [ordered]@{ twitch = @{commit=$commit;sha256=$manifest.workers.twitch;rebuilt=$true;manifest='twitch-build-manifest.json'} }
    foreach ($platform in @('soop','bilibili','youtube')) {
        $oldFile = Join-Path $BaselineCandidate "workers\$platform\$platform-worker.exe"
        $hash = (Get-FileHash -LiteralPath $oldFile -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($hash -ne $baseline.workers.$platform) { throw "Baseline hash mismatch: $platform" }
        Copy-Item -LiteralPath (Join-Path $BaselineCandidate "workers\$platform") -Destination (Join-Path $workers $platform) -Recurse
        $manifest.workers[$platform] = $hash
        $builds[$platform] = @{commit=$baseline.commit;sha256=$hash;rebuilt=$false;manifest='inherited-build-manifest.json'}
    }
    $manifest['workerBuilds'] = $builds
    $manifest['scope'] = 'Twitch rebuilt; other Workers inherited unchanged with original provenance'
    $manifest | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $manifestFile -Encoding utf8
    $python = Join-Path $repo '.worker-build-venv\Scripts\python.exe'
    & $python (Join-Path $repo 'Integrations\Drops\twitch\tests\frozen_config_smoke.py') --worker (Join-Path $workers 'twitch\twitch-worker.exe') --output (Join-Path $validation 'frozen-config') *> (Join-Path $validation 'frozen-config.log')
    if ($LASTEXITCODE -ne 0) { throw 'Frozen Twitch configuration regression failed.' }
    $env:MSBuildEnableWorkloadResolver = 'false'
    & dotnet publish 'CloudLight Blizzard.csproj' -c Release -r win-x64 --self-contained false -o $app "-p:BuildCommit=$commit" "-p:BuildTimestamp=$([DateTimeOffset]::Now.ToString('o'))" '-p:DropsBuildPurpose=Local' "-p:DropsArtifactsRoot=$workers" *> (Join-Path $validation 'publish.log')
    if ($LASTEXITCODE -ne 0) { throw 'Release publish failed.' }
    $dll = Join-Path $app 'CloudLight Blizzard.dll'
    & dotnet $dll --ui-selftest (Join-Path $validation 'ui.txt') *> (Join-Path $validation 'ui-console.log')
    $uiExit = $LASTEXITCODE
    $ui = Get-Content -Raw -LiteralPath (Join-Path $validation 'ui.txt')
    if ($uiExit -notin @(0,2) -or $ui -match '(?m)^FAIL ') { throw 'WPF regression failed.' }
    $uiStatus = if ($uiExit -eq 2 -or $ui -match '(?m)^BLOCKED ') { 'BLOCKED' } else { 'PASS' }
    $inno = @($env:ProgramFiles, ${env:ProgramFiles(x86)}, $env:LOCALAPPDATA) | Where-Object { $_ } | ForEach-Object {
        Join-Path $_ 'Inno Setup 7\ISCC.exe'
        Join-Path $_ 'Inno Setup 6\ISCC.exe'
    } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $inno) { throw 'Inno Setup is required.' }
    $installerRoot = Join-Path $OutputRoot 'installer'
    & $inno "/O$installerRoot" "/DPublishDir=$app" (Join-Path $repo 'installer\app.iss') *> (Join-Path $validation 'installer-build.log')
    if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
    $installers = @(Get-ChildItem -LiteralPath $installerRoot -File -Filter '*Setup.exe')
    if ($installers.Count -ne 1) { throw 'Expected exactly one installer.' }
    $installer = $installers[0]
    $env:__COMPAT_LAYER = 'RunAsInvoker'
    foreach ($mode in @('PAYLOADVERIFY','SETTINGSPRESERVETEST')) {
        $log = Join-Path $validation "installer-$mode.log"
        $process = Start-Process -FilePath $installer.FullName -ArgumentList @("/$mode",'/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOCLOSEAPPLICATIONS',('/LOG="'+$log+'"')) -WindowStyle Hidden -Wait -PassThru
        $content = Get-Content -Raw -LiteralPath $log
        if ($mode -eq 'PAYLOADVERIFY') {
            foreach ($extension in @('exe','dll')) {
                $hash = (Get-FileHash -LiteralPath (Join-Path $app "CloudLight Blizzard.$extension")).Hash.ToLowerInvariant()
                if (-not $content.Contains("PAYLOAD_VERIFY $($extension.ToUpperInvariant())_SHA256=$hash")) { throw 'Installer payload identity differs.' }
            }
        } elseif (-not $content.Contains('SETTINGS_PRESERVE_TEST PASS')) { throw 'Settings preservation test failed.' }
    }
    if ((& git status --porcelain | Out-String).Trim() -or (& git rev-parse HEAD | Out-String).Trim() -ne $commit) { throw 'Source changed during build.' }
    $metadata = [ordered]@{version='2.1.10';commit=$commit;sourceState='clean';purpose='Local';scope='Twitch';uiAcceptance=$uiStatus;
        frozenConfiguration='PASS';installedToSystem=$false;officialClaimTests='NOT RUN';workers=$manifest.workers;workerBuilds=$builds;
        appDllSha256=(Get-FileHash -LiteralPath $dll).Hash.ToLowerInvariant();installerSha256=(Get-FileHash -LiteralPath $installer.FullName).Hash.ToLowerInvariant()}
    $metadata | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $OutputRoot 'candidate.json') -Encoding utf8
    "TWITCH CANDIDATE BUILD PASS; UI=$uiStatus; commit=$commit"
} finally {
    $env:MSBuildEnableWorkloadResolver = $oldWorkload
    $env:__COMPAT_LAYER = $oldCompatibility
    Pop-Location
}
