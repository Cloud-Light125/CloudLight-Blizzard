param(
    [switch]$SkipDropsWorkers,
    [string]$Python = ''
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$buildOriginalPath = $env:PATH
$buildOriginalCondaPrefix = $env:CONDA_PREFIX
$buildOriginalPythonHome = $env:PYTHONHOME
$buildOriginalPythonPath = $env:PYTHONPATH
$buildOriginalPythonUtf8 = $env:PYTHONUTF8
Push-Location $root
try {
    $pythonFinder = Join-Path $root 'build-support\Find-Python.ps1'
    if (-not (Test-Path -LiteralPath $pythonFinder)) {
        throw '缺少 build-support\Find-Python.ps1，无法检测构建环境。'
    }
    . $pythonFinder
    $pythonInfo = Find-CloudLightPython -ExplicitPath $Python
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw '.NET SDK 不可用。请安装 .NET 8 SDK，并在新的 PowerShell 窗口中确认 dotnet --version。'
    }
    $dotnetVersion = (& dotnet --version 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($dotnetVersion)) {
        throw '.NET SDK 不可用。请安装 .NET 8 SDK，并在新的 PowerShell 窗口中确认 dotnet --version。'
    }
    $dotnetParsedVersion = $null
    try { $dotnetParsedVersion = [version]$dotnetVersion } catch { }
    if ($null -eq $dotnetParsedVersion -or $dotnetParsedVersion.Major -ne 8) {
        throw ".NET SDK 版本不受支持：$dotnetVersion。请安装 .NET 8 SDK。"
    }
    $isccCandidates = [System.Collections.Generic.List[string]]::new()
    $isccCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($isccCommand -and $isccCommand.Source) { $isccCandidates.Add($isccCommand.Source) }
    $innoRoots = @($env:LOCALAPPDATA, $env:ProgramFiles, ${env:ProgramFiles(x86)}) |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    foreach ($candidate in $innoRoots | ForEach-Object { Join-Path $_ 'Inno Setup 6\ISCC.exe' }) {
        if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Leaf) -and
            -not $isccCandidates.Contains($candidate)) { $isccCandidates.Add($candidate) }
    }
    $iscc = $isccCandidates | Select-Object -First 1
    Write-Host '=== CloudLight Blizzard build environment ===' -ForegroundColor Cyan
    Write-Host ".NET SDK: $dotnetVersion"
    Write-Host "Python: $($pythonInfo.Version)"
    Write-Host "Source: $($pythonInfo.Source)"
    Write-Host "Path: $($pythonInfo.Path)"
    Write-Host "Conda: $(if ($pythonInfo.Source -match 'Conda|conda') { 'detected' } else { 'not selected' })"
    Write-Host "Inno Setup: $(if ($iscc) { $iscc } else { 'not found' })"
    Write-Host 'Architecture: x64'
    Write-Host 'Publish RID: win-x64'

    if (-not $iscc) {
        throw '未找到 Inno Setup 6（ISCC.exe）。请安装 Inno Setup 6，或将 ISCC.exe 加入 PATH 后重新运行 build.ps1。'
    }

    'bin', 'obj', 'publish', 'installer\out' | ForEach-Object {
        Remove-Item -Recurse -Force $_ -ErrorAction SilentlyContinue
    }

    if (-not $SkipDropsWorkers) {
        Write-Host '[1/6] Drops Workers' -ForegroundColor Cyan
        & (Join-Path $root 'Integrations\Drops\build-workers.ps1') -Python $pythonInfo.Path
        if ($LASTEXITCODE -ne 0) { throw 'Drops Worker 构建失败。请检查上方 Python/SSL 环境摘要和 pip 错误。' }
    }
    else { Write-Host '[1/6] Drops Workers skipped' -ForegroundColor Yellow }

    Write-Host '[2/6] dotnet restore' -ForegroundColor Cyan
    & dotnet restore 'CloudLight Blizzard.csproj' -r win-x64 -v minimal
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore 失败。请检查 .NET 8 SDK 和 NuGet 网络。' }

    Write-Host '[3/6] dotnet build (Release)' -ForegroundColor Cyan
    & dotnet build 'CloudLight Blizzard.csproj' -c Release --no-restore -v minimal
    if ($LASTEXITCODE -ne 0) { throw 'Release 构建失败。' }

    Write-Host '[4/6] dotnet publish' -ForegroundColor Cyan
    & dotnet publish 'CloudLight Blizzard.csproj' -c Release -r win-x64 --self-contained false -o publish --no-restore -v minimal
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish 失败。' }
    $publishedExe = Join-Path $root 'publish\CloudLight Blizzard.exe'
    $publishedDll = Join-Path $root 'publish\CloudLight Blizzard.dll'
    if (-not (Test-Path -LiteralPath $publishedExe -PathType Leaf)) { throw 'publish 输出缺少 CloudLight Blizzard.exe。' }
    if (-not (Test-Path -LiteralPath $publishedDll -PathType Leaf)) { throw 'publish 输出缺少 CloudLight Blizzard.dll。' }
    if (-not $SkipDropsWorkers) {
        & (Join-Path $root 'Integrations\Drops\test-worker-ssl.ps1') -Root (Join-Path $root 'publish\_internal\drops')
        if ($LASTEXITCODE -ne 0) { throw '发布目录 Drops Worker SSL 自检失败。' }
    }
    Remove-Item publish\*.pdb -ErrorAction SilentlyContinue

    Write-Host '[5/6] publish selftest' -ForegroundColor Cyan
    $selftestRoot = Join-Path $root 'obj\publish-selftest'
    Remove-Item -LiteralPath $selftestRoot -Recurse -Force -ErrorAction SilentlyContinue
    # WinExe apphosts do not provide a stable synchronous console/process
    # boundary under every PowerShell host. Run the published assembly with
    # the already validated SDK runtime so the report is deterministic.
    & dotnet $publishedDll '--feature-selftest' $selftestRoot
    if ($LASTEXITCODE -ne 0) { throw '发布目录 FeatureSelfTest 进程失败。' }
    $selftestReport = Join-Path $selftestRoot 'feature-selftest.txt'
    if (-not (Test-Path -LiteralPath $selftestReport -PathType Leaf)) {
        throw '发布目录 FeatureSelfTest 未生成报告。'
    }
    if (-not (Get-Content -LiteralPath $selftestReport -Raw).Contains('OVERALL: PASS')) {
        throw '发布目录 FeatureSelfTest 未通过，请查看 obj\publish-selftest\feature-selftest.txt。'
    }

    Write-Host '[6/6] Inno Setup + artifact validation' -ForegroundColor Cyan
    & $iscc installer\app.iss | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup 构建失败。' }
    $setup = Get-ChildItem installer\out\*.exe | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $setup) { throw 'Inno Setup 未生成安装包。' }
    if ($setup.Length -le 0) { throw '生成的安装包大小为 0。' }
    $sha = (Get-FileHash $setup.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host "Publish: $publishedExe ($((Get-Item $publishedExe).Length) bytes)" -ForegroundColor Green
    Write-Host "Setup: $($setup.FullName)" -ForegroundColor Green
    Write-Host "SHA256: $sha"
    Write-Host "Setup size: $([math]::Round($setup.Length / 1MB, 2)) MB"
    Write-Host 'BUILD: PASS' -ForegroundColor Green
}
catch {
    $buildFailure = if ($_.Exception -and $_.Exception.Message) { $_.Exception.Message } else { '未知构建错误。' }
    Write-Host "BUILD: FAIL - $buildFailure" -ForegroundColor Red
    exit 1
}
finally {
    $env:PATH = $buildOriginalPath
    if ($null -eq $buildOriginalCondaPrefix) { Remove-Item Env:CONDA_PREFIX -ErrorAction SilentlyContinue }
    else { $env:CONDA_PREFIX = $buildOriginalCondaPrefix }
    if ($null -eq $buildOriginalPythonHome) { Remove-Item Env:PYTHONHOME -ErrorAction SilentlyContinue }
    else { $env:PYTHONHOME = $buildOriginalPythonHome }
    if ($null -eq $buildOriginalPythonPath) { Remove-Item Env:PYTHONPATH -ErrorAction SilentlyContinue }
    else { $env:PYTHONPATH = $buildOriginalPythonPath }
    if ($null -eq $buildOriginalPythonUtf8) { Remove-Item Env:PYTHONUTF8 -ErrorAction SilentlyContinue }
    else { $env:PYTHONUTF8 = $buildOriginalPythonUtf8 }
    Pop-Location
}
