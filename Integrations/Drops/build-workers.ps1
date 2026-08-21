param(
    [string]$Python = "python",
    [string]$SoopCorePath = "",
    [ValidateSet("youtube", "soop", "twitch")]
    [string[]]$Platforms = @("youtube", "twitch", "soop")
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$dropsRoot = $PSScriptRoot
$venv = Join-Path $repoRoot ".worker-build-venv"
$artifacts = Join-Path $repoRoot "artifacts\drops"
$tuna = "https://mirrors.tuna.tsinghua.edu.cn/pypi/web/simple"

if (-not (Test-Path -LiteralPath (Join-Path $venv "Scripts\python.exe"))) {
    & $Python -m venv $venv
}
$buildPython = Join-Path $venv "Scripts\python.exe"
& $buildPython -m pip install -i $tuna --upgrade pip pyinstaller
& $buildPython -m pip install -i $tuna -r (Join-Path $dropsRoot "youtube\requirements.txt")
& $buildPython -m pip install -i $tuna -r (Join-Path $dropsRoot "soop\requirements.txt")
& $buildPython -m pip install -i $tuna -r (Join-Path $dropsRoot "twitch\requirements.txt")

New-Item -ItemType Directory -Force -Path $artifacts | Out-Null

function Build-Worker {
    param([string]$Platform, [string[]]$ExtraArgs = @())
    $source = Join-Path $dropsRoot "$Platform\worker.py"
    $dist = Join-Path $artifacts $Platform
    $work = Join-Path $repoRoot "obj\drops-workers\$Platform"
    New-Item -ItemType Directory -Force -Path $dist,$work | Out-Null
    $arguments = @(
        "-m", "PyInstaller", "--noconfirm", "--clean", "--onefile",
        "--name", "$Platform-worker", "--distpath", $dist,
        "--workpath", $work, "--specpath", $work,
        "--paths", $dropsRoot, $source
    ) + $ExtraArgs
    & $buildPython @arguments
    if ($LASTEXITCODE -ne 0) { throw "$Platform Worker build failed." }
}

if ($Platforms -contains "youtube") {
    Build-Worker -Platform "youtube" -ExtraArgs @(
        "--hidden-import", "yt_dlp",
        "--hidden-import", "requests",
        "--hidden-import", "websocket"
    )
}

$tlsRuntimeArgs = @()
if (($Platforms -contains "twitch") -or ($Platforms -contains "soop")) {
    # A venv created from Conda can import _ssl from the base interpreter while
    # PyInstaller only sees _ssl.pyd. Its dependent OpenSSL DLLs live under
    # <base_prefix>\Library\bin and are otherwise omitted from the one-file EXE.
    # Resolve and include those exact runtime DLLs instead of disabling TLS.
    $sslRuntimeJson = & $buildPython -c "import json, ssl, _ssl, sys; ssl.create_default_context(); print(json.dumps({'base_prefix': sys.base_prefix, 'prefix': sys.prefix, 'ssl_extension': _ssl.__file__, 'openssl': ssl.OPENSSL_VERSION}))"
    if ($LASTEXITCODE -ne 0) { throw "Build Python SSL runtime is unavailable." }
    $sslRuntime = $sslRuntimeJson | ConvertFrom-Json
    $sslSearchRoots = @(
        (Join-Path $sslRuntime.base_prefix "Library\bin"),
        (Join-Path $sslRuntime.base_prefix "DLLs"),
        (Join-Path $sslRuntime.prefix "Library\bin"),
        (Split-Path -Parent $sslRuntime.ssl_extension)
    ) | Select-Object -Unique
    $opensslDlls = @($sslSearchRoots | Where-Object { Test-Path -LiteralPath $_ } |
        ForEach-Object { Get-ChildItem -LiteralPath $_ -File } |
        Where-Object { $_.Name -match '^lib(ssl|crypto).*\.dll$' } |
        Sort-Object Name -Unique)
    if (-not ($opensslDlls.Name -match '^libssl') -or -not ($opensslDlls.Name -match '^libcrypto')) {
        throw "Could not locate the OpenSSL runtime DLLs required by _ssl.pyd ($($sslRuntime.ssl_extension))."
    }
    $tlsRuntimeArgs = @("--hidden-import", "ssl", "--hidden-import", "_ssl")
    $condaSupportNames = @("libexpat.dll", "libmpdec-4.dll", "liblzma.dll", "ffi.dll")
    $condaSupportDlls = @($sslSearchRoots | Where-Object { Test-Path -LiteralPath $_ } |
        ForEach-Object { Get-ChildItem -LiteralPath $_ -File } |
        Where-Object { $condaSupportNames -contains $_.Name } |
        Sort-Object Name -Unique)
    foreach ($dll in @($opensslDlls) + @($condaSupportDlls)) {
        $tlsRuntimeArgs += @("--add-binary", "$($dll.FullName);.")
    }
}

$twitchCore = Join-Path $dropsRoot "twitch\core"
if ($Platforms -contains "twitch") {
    $twitchBuildArgs = @(
        "--paths", $twitchCore,
        "--add-data", "$twitchCore;core",
        "--hidden-import", "headless_gui",
        "--hidden-import", "gui",
        "--hidden-import", "settings",
        "--hidden-import", "twitch",
        "--exclude-module", "tkinter",
        "--exclude-module", "PIL"
    ) + $tlsRuntimeArgs
    Build-Worker -Platform "twitch" -ExtraArgs $twitchBuildArgs
    Copy-Item -LiteralPath (Join-Path $twitchCore "LICENSE") -Destination (Join-Path $artifacts "twitch\TwitchDropsMiner-MIT.txt") -Force

    $sslTestRoot = Join-Path $repoRoot "obj\drops-workers\ssl-selftest"
    New-Item -ItemType Directory -Force -Path $sslTestRoot | Out-Null
    $sslRequest = '{"id":"ssl-selftest","command":"ssl_check","payload":{}}'
    $sslOutput = $sslRequest | & (Join-Path $artifacts "twitch\twitch-worker.exe") --data-dir (Join-Path $sslTestRoot "data") --log-file (Join-Path $sslTestRoot "worker.log")
    if ($LASTEXITCODE -ne 0) { throw "Packaged Twitch Worker SSL self-test failed to start." }
    $sslResponse = $sslOutput | ForEach-Object { try { $_ | ConvertFrom-Json } catch { $null } } |
        Where-Object { $_.id -eq "ssl-selftest" } | Select-Object -Last 1
    if (-not $sslResponse.ok -or -not $sslResponse.result.contextCreated) {
        throw "Packaged Twitch Worker cannot create an SSL context."
    }
    Write-Host "Packaged Twitch SSL self-test: $($sslResponse.result.openssl)"
}

$soopArgs = @()
if ([string]::IsNullOrWhiteSpace($SoopCorePath)) {
    $SoopCorePath = Join-Path (Split-Path $repoRoot -Parent) "cloudlight soop drops miner"
}
if (Test-Path -LiteralPath (Join-Path $SoopCorePath "__init__.py")) {
    $resolvedSoop = (Resolve-Path -LiteralPath $SoopCorePath).Path
    $soopArgs = @(
        "--add-data", "$resolvedSoop\*.py;core",
        "--hidden-import", "aiohttp",
        "--hidden-import", "yarl"
    )
}
if ($Platforms -contains "soop") {
    Build-Worker -Platform "soop" -ExtraArgs ($soopArgs + $tlsRuntimeArgs)
}

Get-ChildItem -LiteralPath $artifacts -Recurse -File | Select-Object FullName,Length
