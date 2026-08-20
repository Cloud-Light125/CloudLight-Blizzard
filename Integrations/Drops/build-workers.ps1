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

$twitchCore = Join-Path $dropsRoot "twitch\core"
if ($Platforms -contains "twitch") {
    Build-Worker -Platform "twitch" -ExtraArgs @(
        "--paths", $twitchCore,
        "--add-data", "$twitchCore;core",
        "--hidden-import", "headless_gui",
        "--hidden-import", "gui",
        "--hidden-import", "settings",
        "--hidden-import", "twitch",
        "--exclude-module", "tkinter",
        "--exclude-module", "PIL"
    )
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
    Build-Worker -Platform "soop" -ExtraArgs $soopArgs
}

Get-ChildItem -LiteralPath $artifacts -Recurse -File | Select-Object FullName,Length
