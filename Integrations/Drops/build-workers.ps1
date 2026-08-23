param(
    [string]$Python = "python",
    [string]$SoopCorePath = "",
    [ValidateSet("youtube", "soop", "twitch")]
    [string[]]$Platforms = @("youtube", "twitch", "soop")
)

$ErrorActionPreference = "Stop"
$env:PYTHONUTF8 = "1"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$dropsRoot = $PSScriptRoot
$venv = Join-Path $repoRoot ".worker-build-venv"
$artifacts = Join-Path $repoRoot "artifacts\drops"
$tuna = "https://mirrors.tuna.tsinghua.edu.cn/pypi/web/simple"

if (-not (Test-Path -LiteralPath (Join-Path $venv "Scripts\python.exe"))) {
    & $Python -m venv $venv
}
$buildPython = Join-Path $venv "Scripts\python.exe"
& $buildPython -m pip install -i $tuna --upgrade pip pyinstaller pefile
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

$tlsRuntimeArgs = @()
if ($Platforms.Count -gt 0) {
    # A venv created from Conda can import _ssl from the base interpreter while
    # PyInstaller only sees _ssl.pyd. Its dependent OpenSSL DLLs live under
    # <base_prefix>\Library\bin and are otherwise omitted from the one-file EXE.
    # Resolve and include those exact runtime DLLs instead of disabling TLS.
    $sslRuntimeJson = & $buildPython (Join-Path $dropsRoot "Shared\inspect_ssl_runtime.py")
    if ($LASTEXITCODE -ne 0) { throw "Build Python SSL runtime dependency inspection failed." }
    $sslRuntime = $sslRuntimeJson | ConvertFrom-Json
    $tlsRuntimeArgs = @("--hidden-import", "ssl", "--hidden-import", "_ssl")
    foreach ($dll in $sslRuntime.binaries) {
        $tlsRuntimeArgs += @("--add-binary", "$($dll.path);.")
        Write-Host "SSL dependency: $($sslRuntime.ssl_extension.name) -> $($dll.name) ($($dll.size) bytes)"
    }
    foreach ($dll in $sslRuntime.support_binaries) {
        $tlsRuntimeArgs += @("--add-binary", "$($dll.path);.")
        Write-Host "Conda runtime dependency: $($dll.name) ($($dll.size) bytes)"
    }
}

if ($Platforms -contains "youtube") {
    Build-Worker -Platform "youtube" -ExtraArgs (@(
        "--hidden-import", "yt_dlp",
        "--hidden-import", "requests",
        "--hidden-import", "websocket"
    ) + $tlsRuntimeArgs)
}

$twitchCore = Join-Path $dropsRoot "twitch\core"
if ($Platforms -contains "twitch") {
    $twitchBuildArgs = @(
        "--paths", $twitchCore,
        # Do not copy the developer tree wholesale: it can contain __pycache__
        # produced by a different local Python version. Include only runtime
        # source/resources that Twitch core actually needs.
        "--add-data", "$twitchCore\*.py;core",
        "--add-data", "$twitchCore\lang;core\lang",
        "--add-data", "$twitchCore\LICENSE;core",
        "--add-data", "$twitchCore\PATCHED_BUILD.md;core",
        "--hidden-import", "headless_gui",
        "--hidden-import", "gui",
        "--hidden-import", "settings",
        "--hidden-import", "twitch",
        "--exclude-module", "tkinter",
        "--exclude-module", "PIL"
    ) + $tlsRuntimeArgs
    Build-Worker -Platform "twitch" -ExtraArgs $twitchBuildArgs
    Copy-Item -LiteralPath (Join-Path $twitchCore "LICENSE") -Destination (Join-Path $artifacts "twitch\TwitchDropsMiner-MIT.txt") -Force

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

foreach ($platform in $Platforms) {
    $worker = Join-Path $artifacts "$platform\$platform-worker.exe"
    $archive = & $buildPython -m PyInstaller.utils.cliutils.archive_viewer -l $worker
    if ($LASTEXITCODE -ne 0 -or
        -not ($archive -match '_ssl\.pyd') -or
        -not ($archive -match 'libssl-3-x64\.dll') -or
        -not ($archive -match 'libcrypto-3-x64\.dll')) {
        throw "$platform Worker archive does not contain _ssl.pyd and its exact OpenSSL DLL dependencies."
    }
    Write-Host "$platform Worker archive SSL contents: PASS"
}

& (Join-Path $dropsRoot "test-worker-ssl.ps1") -Root $artifacts -Platforms $Platforms

Get-ChildItem -LiteralPath $artifacts -Recurse -File | Select-Object FullName,Length
