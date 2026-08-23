param(
    [Parameter(Mandatory = $true)]
    [string]$Root,
    [string]$TestRoot,
    [ValidateSet("youtube", "soop", "twitch")]
    [string[]]$Platforms = @("youtube", "twitch", "soop")
)

$ErrorActionPreference = "Stop"
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
            Where-Object { $_.id -eq "ssl-selftest" } | Select-Object -Last 1
        if (-not $response.ok -or -not $response.result.contextCreated -or
            -not $response.result.available -or -not $response.result.frozen) {
            $diagnostic = ($output | ForEach-Object { [string]$_ }) -join " | "
            throw "Packaged $platform Worker cannot import ssl/_ssl and create a default SSL context. Output: $diagnostic"
        }
        $sslModule = [string]$response.result.sslModule
        if ($sslModule -match '(?i)(miniconda|anaconda|\.worker-build-venv|\\code\\CloudLight Blizzard)') {
            throw "Packaged $platform Worker borrowed _ssl from the developer environment: $sslModule"
        }
        Write-Host "Packaged $platform SSL self-test: PASS (Python $($response.result.python); $($response.result.openssl); frozen=$($response.result.frozen))" -ForegroundColor Green
        Write-Host "  _ssl module: $sslModule"
    }
}
finally {
    $env:PATH = $originalPath
    foreach ($name in $isolatedVariables) {
        [Environment]::SetEnvironmentVariable($name, $originalVariables[$name], "Process")
    }
}
