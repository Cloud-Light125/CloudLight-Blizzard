param(
    [string]$Python = "",
    [string]$SoopCorePath = "",
    [ValidateSet("youtube", "soop", "twitch", "bilibili")]
    [string[]]$Platforms = @("youtube", "twitch", "soop", "bilibili")
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$env:PYTHONUTF8 = "1"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$dropsRoot = $PSScriptRoot
$venv = Join-Path $repoRoot ".worker-build-venv"
$artifacts = Join-Path $repoRoot "artifacts\drops"
$tuna = "https://mirrors.tuna.tsinghua.edu.cn/pypi/web/simple"
$pythonFinder = Join-Path $repoRoot 'build-support\Find-Python.ps1'
if (-not (Test-Path -LiteralPath $pythonFinder)) {
    throw '找不到构建环境发现器 build-support\Find-Python.ps1。请确认仓库完整。'
}
. $pythonFinder

$pythonInfo = Find-CloudLightPython -ExplicitPath $Python
Write-Host "Python: $($pythonInfo.Version)" -ForegroundColor Green
Write-Host "Source: $($pythonInfo.Source)"
Write-Host "Path: $($pythonInfo.Path)"
Write-Host "SSL: $($pythonInfo.OpenSsl)"

$venvPython = Join-Path $venv "Scripts\python.exe"
$venvConfig = Join-Path $venv 'pyvenv.cfg'
$managedMarker = Join-Path $venv '.cloudlight-managed'
$rebuildVenv = -not (Test-Path -LiteralPath $venvPython -PathType Leaf)
if (-not $rebuildVenv -and -not (Test-Path -LiteralPath $venvConfig -PathType Leaf)) { $rebuildVenv = $true }
if (-not $rebuildVenv) {
    $cfgText = Get-Content -LiteralPath $venvConfig -Raw -ErrorAction SilentlyContinue
    $baseExecutable = [regex]::Match($cfgText, '(?im)^executable\s*=\s*(.+?)\s*$').Groups[1].Value.Trim()
    if ([string]::IsNullOrWhiteSpace($baseExecutable) -or
        -not (Test-Path -LiteralPath $baseExecutable -PathType Leaf) -or
        -not (Test-CloudLightPythonRuntime -Path $venvPython)) {
        $rebuildVenv = $true
    }
}
if ($rebuildVenv -and (Test-Path -LiteralPath $venv)) {
    if (-not (Test-Path -LiteralPath $managedMarker -PathType Leaf)) {
        throw '检测到失效的 .worker-build-venv，但该目录没有 CloudLight Blizzard 创建标记。为避免删除用户环境，脚本未自动删除；请手动移走该目录后重试。'
    }
    Remove-Item -LiteralPath $venv -Recurse -Force
    Write-Host '已删除失效的 .worker-build-venv，将使用当前可用 Python 重建。' -ForegroundColor Yellow
}
if ($rebuildVenv) {
    & $pythonInfo.Path -m venv $venv
    if ($LASTEXITCODE -ne 0) { throw '创建 .worker-build-venv 失败。请检查 Python venv/ensurepip 组件。' }
    Set-Content -LiteralPath $managedMarker -Value 'CloudLight Blizzard Drops worker build environment.' -Encoding UTF8
}
$venvRuntime = Test-CloudLightPythonRuntime -Path $venvPython
if (-not $venvRuntime) { throw '新建的 .worker-build-venv 无法加载 Python SSL 运行库。请重新安装带 SSL 的 Python/Conda。' }

$buildPython = $venvPython
& $buildPython -m pip install -i $tuna --upgrade pip pyinstaller pefile
if ($LASTEXITCODE -ne 0) { throw '安装 Drops Worker 构建工具失败。请检查 Python、SSL 和清华 PyPI 镜像。' }
& $buildPython -m pip install -i $tuna -r (Join-Path $dropsRoot "youtube\requirements.txt")
if ($LASTEXITCODE -ne 0) { throw '安装 YouTube Worker 依赖失败。' }
& $buildPython -m pip install -i $tuna -r (Join-Path $dropsRoot "soop\requirements.txt")
if ($LASTEXITCODE -ne 0) { throw '安装 SOOP Worker 依赖失败。' }
& $buildPython -m pip install -i $tuna -r (Join-Path $dropsRoot "twitch\requirements.txt")
if ($LASTEXITCODE -ne 0) { throw '安装 Twitch Worker 依赖失败。' }
if ($Platforms -contains "bilibili") {
    & $buildPython -m pip install -i $tuna -r (Join-Path $dropsRoot "bilibili\requirements.txt")
    if ($LASTEXITCODE -ne 0) { throw '安装 Bilibili Worker 依赖失败。' }
}

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

if ($Platforms -contains "bilibili") {
    $bilibiliVendor = Join-Path $dropsRoot "bilibili\vendor"
    $bilibiliArgs = @(
        "--paths", $bilibiliVendor,
        "--collect-submodules", "bilibili_drops_miner",
        "--hidden-import", "httpx",
        "--hidden-import", "httpcore",
        "--hidden-import", "h11",
        "--hidden-import", "anyio",
        "--hidden-import", "qrcode",
        "--hidden-import", "PIL"
    ) + $tlsRuntimeArgs
    Build-Worker -Platform "bilibili" -ExtraArgs $bilibiliArgs
    Copy-Item -LiteralPath (Join-Path $dropsRoot "bilibili\LICENSE") -Destination (Join-Path $artifacts "bilibili\BiliBiliDropsMiner-MIT.txt") -Force

    Write-Host "Bilibili Worker Python contract tests" -ForegroundColor Cyan
    & $buildPython -m unittest discover -s (Join-Path $dropsRoot "bilibili\tests") -p "test*.py" -v
    if ($LASTEXITCODE -ne 0) { throw "Bilibili Worker Python contract tests failed." }
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
