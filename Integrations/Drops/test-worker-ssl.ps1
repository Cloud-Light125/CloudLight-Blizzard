param(
    [Parameter(Mandatory = $true)]
    [string]$Root,
    [string]$TestRoot,
    [switch]$SoopAdapterOnly,
    [ValidateSet("youtube", "soop", "twitch", "bilibili")]
    [string[]]$Platforms = @("youtube", "twitch", "soop", "bilibili")
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Get-CloudLightJsonProperty {
    param(
        [AllowNull()][object]$Object,
        [Parameter(Mandatory = $true)][string]$Name
    )
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}
$resolvedRoot = (Resolve-Path -LiteralPath $Root).Path
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if ([string]::IsNullOrWhiteSpace($TestRoot)) {
    $TestRoot = Join-Path $repoRoot "obj\drops-workers\ssl-selftest"
}
$resolvedTestRoot = [System.IO.Path]::GetFullPath($TestRoot)
New-Item -ItemType Directory -Force -Path $resolvedTestRoot | Out-Null

# Only alter this script's child-process environment. The test must not borrow
# Python, Conda, OpenSSL, or certificate configuration from the developer machine.
$originalPath = $env:PATH
$env:PATH = (($originalPath -split ';') | Where-Object {
    $_ -and $_ -notmatch '(?i)(python|conda|miniconda|anaconda|openssl|\.worker-build-venv)'
}) -join ';'
$isolatedVariables = @(
    "PYTHONHOME", "PYTHONPATH", "CLOUDLIGHT_DROPS_PYTHON",
    "CLOUDLIGHT_TWITCH_CORE", "CLOUDLIGHT_SOOP_CORE",
    "SSL_CERT_FILE", "SSL_CERT_DIR", "OPENSSL_CONF", "OPENSSL_MODULES"
)
$originalVariables = @{}
foreach ($name in $isolatedVariables) {
    $originalVariables[$name] = [Environment]::GetEnvironmentVariable($name, "Process")
    [Environment]::SetEnvironmentVariable($name, $null, "Process")
}

try {
    foreach ($platform in $Platforms) {
        $worker = Join-Path $resolvedRoot "$platform\$platform-worker.exe"
        if (-not (Test-Path -LiteralPath $worker)) {
            throw "Packaged $platform Worker was not found: $worker"
        }
        $platformRoot = Join-Path $resolvedTestRoot $platform
        New-Item -ItemType Directory -Force -Path $platformRoot | Out-Null
        $request = '{"id":"ssl-selftest","command":"ssl_check","payload":{}}'
        Push-Location $platformRoot
        try {
            $output = $request | & $worker --data-dir (Join-Path $platformRoot "data") --log-file (Join-Path $platformRoot "worker.log")
        }
        finally {
            Pop-Location
        }
        if ($LASTEXITCODE -ne 0) {
            throw "Packaged $platform Worker SSL self-test failed to start (exit $LASTEXITCODE)."
        }
        $response = $output | ForEach-Object { try { $_ | ConvertFrom-Json } catch { $null } } |
            Where-Object { $_ -and $_.PSObject.Properties['id'] -and $_.id -eq "ssl-selftest" } |
            Select-Object -Last 1
        $responseResult = Get-CloudLightJsonProperty -Object $response -Name 'result'
        $responseOk = [bool](Get-CloudLightJsonProperty -Object $response -Name 'ok')
        $contextCreated = [bool](Get-CloudLightJsonProperty -Object $responseResult -Name 'contextCreated')
        $available = [bool](Get-CloudLightJsonProperty -Object $responseResult -Name 'available')
        $frozen = [bool](Get-CloudLightJsonProperty -Object $responseResult -Name 'frozen')
        if (-not $responseOk -or -not $contextCreated -or
            -not $available -or -not $frozen) {
            $diagnostic = ($output | ForEach-Object { [string]$_ }) -join " | "
            throw "Packaged $platform Worker cannot import ssl/_ssl and create a default SSL context. Output: $diagnostic"
        }
        $sslModule = [string](Get-CloudLightJsonProperty -Object $responseResult -Name 'sslModule')
        if ($sslModule -match '(?i)(miniconda|anaconda|\.worker-build-venv|\\code\\CloudLight Blizzard)') {
            throw "Packaged $platform Worker borrowed _ssl from the developer environment: $sslModule"
        }
        $python = [string](Get-CloudLightJsonProperty -Object $responseResult -Name 'python')
        $openssl = [string](Get-CloudLightJsonProperty -Object $responseResult -Name 'openssl')
        Write-Host "Packaged $platform SSL self-test: PASS (Python $python; $openssl; frozen=$frozen)" -ForegroundColor Green
        Write-Host "  _ssl module: $sslModule"
        $provenanceRequest = '{"id":"provenance-selftest","command":"provenance_check","payload":{}}'
        $provenanceOutput = $provenanceRequest | & $worker --data-dir (Join-Path $platformRoot 'provenance-data') --log-file (Join-Path $platformRoot 'provenance.log')
        if ($LASTEXITCODE -ne 0) { throw "$platform provenance check failed." }
        $provenanceResponse = $provenanceOutput | ForEach-Object { try { $_ | ConvertFrom-Json } catch { $null } } |
            Where-Object { $_ -and $_.PSObject.Properties['id'] -and $_.id -eq 'provenance-selftest' } | Select-Object -Last 1
        if (-not $provenanceResponse.ok -or $provenanceResponse.result.protocol -ne 1 -or -not $provenanceResponse.result.frozen -or -not $provenanceResponse.result.commit) {
            throw "$platform embedded build manifest missing or invalid."
        }
        $manifest = Get-Content (Join-Path $resolvedRoot 'build-manifest.json') -Raw | ConvertFrom-Json
        if ($provenanceResponse.result.adapterSha256 -ne $manifest.adapterSources.sha256 -or
            $provenanceResponse.result.commit -ne $manifest.commit) { throw "$platform Worker belongs to another source build." }
        if ($platform -eq 'soop' -and -not $SoopAdapterOnly -and -not $provenanceResponse.result.core.matches) {
            throw 'Bundled SOOP source digest does not match its build manifest.'
        }
        Write-Host "Packaged $platform provenance: PASS (commit=$($provenanceResponse.result.commit); purpose=$($provenanceResponse.result.purpose))"

        if ($platform -eq "twitch") {
            $compatRequest = '{"id":"compat-selftest","command":"compatibility_check","payload":{}}'
            $compatOutput = $compatRequest | & $worker --data-dir (Join-Path $platformRoot "compat-data") --log-file (Join-Path $platformRoot "compat-worker.log")
            if ($LASTEXITCODE -ne 0) {
                throw "Packaged Twitch Worker compatibility self-test failed to start (exit $LASTEXITCODE)."
            }
            $compatResponse = $compatOutput | ForEach-Object { try { $_ | ConvertFrom-Json } catch { $null } } |
                Where-Object { $_ -and $_.PSObject.Properties['id'] -and $_.id -eq "compat-selftest" } |
                Select-Object -Last 1
            $compatResult = Get-CloudLightJsonProperty -Object $compatResponse -Name 'result'
            $upstreamCommit = [string](Get-CloudLightJsonProperty -Object $compatResult -Name 'upstreamCommit')
            $inventoryHash = [string](Get-CloudLightJsonProperty -Object $compatResult -Name 'inventorySha256')
            $campaignsHash = [string](Get-CloudLightJsonProperty -Object $compatResult -Name 'campaignsSha256')
            $webUserAgent = [string](Get-CloudLightJsonProperty -Object $compatResult -Name 'webUserAgent')
            if (-not [bool](Get-CloudLightJsonProperty -Object $compatResponse -Name 'ok') -or
                $upstreamCommit -ne '22d0c6134f9291d1e904012c465504a22bd3f97c' -or
                $inventoryHash -ne '8337eb8541b314040b0edde0c09c5c7a2783ba1960aa9edfbf3bac16d0fec404' -or
                $campaignsHash -ne 'c16bb890cc8ce7647a96ee69cd313d423a378a3dedadf630a1017cde18975feb' -or
                $webUserAgent -notmatch 'Chrome/153' -or
                (Get-CloudLightJsonProperty -Object $compatResult -Name 'watchPathUpstreamCommit') -ne 'f693b0ca41301ac2282f3978713479efebe0490e' -or
                (Get-CloudLightJsonProperty -Object $compatResult -Name 'watchTransport') -ne 'playlist' -or
                (Get-CloudLightJsonProperty -Object $compatResult -Name 'watchIntervalSeconds') -ne 20) {
                $diagnostic = ($compatOutput | ForEach-Object { [string]$_ }) -join " | "
                throw "Packaged Twitch Worker contains stale GQL/Client/watch-path compatibility data. Output: $diagnostic"
            }
            Write-Host "Packaged twitch GQL/Client/playlist-watch compatibility self-test: PASS" -ForegroundColor Green
        }

        if ($platform -eq "soop") {
            $coreRequest = '{"id":"core-selftest","command":"load_state","payload":{}}'
            $coreOutput = $coreRequest | & $worker --data-dir (Join-Path $platformRoot "core-data") --log-file (Join-Path $platformRoot "core-worker.log")
            if ($LASTEXITCODE -ne 0) {
                throw "Packaged SOOP Worker Core self-test failed to start (exit $LASTEXITCODE)."
            }
            $coreResponse = $coreOutput | ForEach-Object { try { $_ | ConvertFrom-Json } catch { $null } } |
                Where-Object { $_ -and $_.PSObject.Properties['id'] -and $_.id -eq "core-selftest" } |
                Select-Object -Last 1
            $coreResult = Get-CloudLightJsonProperty -Object $coreResponse -Name 'result'
            $coreAvailable = [bool](Get-CloudLightJsonProperty -Object $coreResult -Name 'coreAvailable')
            if ($SoopAdapterOnly) {
                if ($coreAvailable -or (Get-CloudLightJsonProperty -Object $provenanceResponse.result -Name 'core')) {
                    throw 'Distribution adapter unexpectedly bundled SOOP Core.'
                }
                Write-Host 'Packaged SOOP adapter-only self-test: PASS (Core not bundled; capability unavailable is explicit)'
                continue
            }
            if (-not $coreAvailable) {
                $coreError = [string](Get-CloudLightJsonProperty -Object $coreResult -Name 'coreError')
                throw "Packaged SOOP Worker cannot load SOOP Core. Output: $coreError"
            }
            $coreCapabilities = Get-CloudLightJsonProperty -Object $coreResult -Name 'coreCapabilities'
            if (-not [bool](Get-CloudLightJsonProperty -Object $coreCapabilities -Name 'forceRefreshAccount') -or
                -not [bool](Get-CloudLightJsonProperty -Object $coreCapabilities -Name 'forceRefresh')) {
                throw 'Packaged SOOP Core is too old: force_refresh_account / force_refresh are required.'
            }
            Write-Host "Packaged soop Core import self-test: PASS" -ForegroundColor Green
        }
    }
}
finally {
    $env:PATH = $originalPath
    foreach ($name in $isolatedVariables) {
        [Environment]::SetEnvironmentVariable($name, $originalVariables[$name], "Process")
    }
}
