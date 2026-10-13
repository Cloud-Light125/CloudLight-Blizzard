param(
    [switch]$SkipDropsWorkers,
    [string]$Python = '',
    [ValidateSet('Local', 'Distribution')]
    [string]$DropsBuildPurpose = 'Distribution',
    [switch]$OmitUnlicensedSoopCore
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$soopAuthorization = Join-Path $root 'Integrations\Drops\soop\redistribution-authorization.json'
if ($DropsBuildPurpose -eq 'Distribution' -and -not $OmitUnlicensedSoopCore -and -not (Test-Path -LiteralPath $soopAuthorization)) {
    throw '完整 SOOP Distribution 构建需要匹配当前 Core 的明确再分发授权记录。'
}
$releaseVersion = '2.1.10'
$buildCommit = (& git -C $root rev-parse HEAD 2>$null | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($buildCommit)) {
    throw '无法读取当前 Git commit，拒绝生成没有来源标识的发布包。'
}
$buildTimestamp = [DateTimeOffset]::Now.ToString('yyyy-MM-ddTHH:mm:ss.fffzzz')
$buildSourceState = (& git -C $root status --porcelain --untracked-files=no 2>$null | Out-String).Trim()
$buildSourceState = if ([string]::IsNullOrWhiteSpace($buildSourceState)) { 'clean' } else { 'dirty' }
$msbuildProperties = @(
    "-p:BuildCommit=$buildCommit",
    "-p:BuildTimestamp=$buildTimestamp",
    '-p:BilibiliUiSchema=2',
    "-p:DropsBuildPurpose=$DropsBuildPurpose"
)
$dropsArtifacts = if ($OmitUnlicensedSoopCore) { Join-Path $root 'artifacts\drops-distribution' } else { Join-Path $root 'artifacts\drops' }
$msbuildProperties += @("-p:DropsArtifactsRoot=$dropsArtifacts", "-p:SoopAdapterOnly=$($OmitUnlicensedSoopCore.IsPresent.ToString().ToLowerInvariant())")
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
    if ($null -eq $dotnetParsedVersion -or $dotnetParsedVersion.Major -lt 8) {
        throw ".NET SDK 版本不受支持：$dotnetVersion。请安装 .NET 8 或更高版本 SDK。"
    }
    $isccCandidates = [System.Collections.Generic.List[string]]::new()
    $isccCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($isccCommand -and $isccCommand.Source) { $isccCandidates.Add($isccCommand.Source) }
    $innoRoots = @($env:LOCALAPPDATA, $env:ProgramFiles, ${env:ProgramFiles(x86)}) |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    foreach ($candidate in $innoRoots | ForEach-Object {
        Join-Path $_ 'Inno Setup 7\ISCC.exe'
        Join-Path $_ 'Inno Setup 6\ISCC.exe'
    }) {
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
    Write-Host "Build commit: $buildCommit"
    Write-Host "Build timestamp: $buildTimestamp"
    Write-Host "Source state: $buildSourceState"

    if (-not $iscc) {
        throw '未找到 Inno Setup 6/7（ISCC.exe）。请安装 Inno Setup，或将 ISCC.exe 加入 PATH 后重新运行 build.ps1。'
    }

    'bin', 'obj', 'publish', 'installer\out' | ForEach-Object {
        $buildDirectory = [IO.Path]::GetFullPath((Join-Path $root $_))
        if (-not $buildDirectory.StartsWith($root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw '构建清理路径超出项目目录，已停止。'
        }
        Remove-Item -LiteralPath $buildDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }

    if (-not $SkipDropsWorkers) {
        Write-Host '[1/9] Drops Workers' -ForegroundColor Cyan
        & (Join-Path $root 'Integrations\Drops\build-workers.ps1') -Python $pythonInfo.Path -BuildPurpose $DropsBuildPurpose -ArtifactsRoot $dropsArtifacts -SoopAdapterOnly:$OmitUnlicensedSoopCore
        if ($LASTEXITCODE -ne 0) { throw 'Drops Worker 构建失败。请检查上方 Python/SSL 环境摘要和 pip 错误。' }
    }
    else { Write-Host '[1/9] Drops Workers skipped' -ForegroundColor Yellow }

    # Skipping compilation may reuse binaries only when their recorded runtime
    # source and commit still match this candidate. Never silently publish old
    # Workers alongside a new WPF assembly.
    $workerManifestPath = Join-Path $dropsArtifacts 'build-manifest.json'
    if (-not (Test-Path -LiteralPath $workerManifestPath)) { throw '缺少 Worker 构建清单，请重新打包。' }
    $workerManifest = Get-Content -LiteralPath $workerManifestPath -Raw | ConvertFrom-Json
    if ($workerManifest.commit -ne $buildCommit -or $workerManifest.purpose -ne $DropsBuildPurpose) {
        throw 'Worker 提交或构建用途与主程序不一致，请重新打包。'
    }
    $provenancePython = Join-Path $root '.worker-build-venv\Scripts\python.exe'
    $currentManifestPath = Join-Path $root 'obj\current-drops-source.json'
    $currentManifestArgs = @('--repo', $root, '--purpose', $DropsBuildPurpose, '--output', $currentManifestPath)
    if ($workerManifest.soopCore) { $currentManifestArgs += @('--core', $workerManifest.soopCore.path) }
    if ($DropsBuildPurpose -eq 'Distribution' -and $workerManifest.soopCore) { $currentManifestArgs += @('--soop-authorization', $soopAuthorization) }
    & $provenancePython (Join-Path $root 'Integrations\Drops\Shared\build_provenance.py') @currentManifestArgs
    if ($LASTEXITCODE -ne 0) { throw '当前 Drops 来源检查失败。' }
    $currentManifest = Get-Content -LiteralPath $currentManifestPath -Raw | ConvertFrom-Json
    if ($DropsBuildPurpose -eq 'Distribution' -and $workerManifest.soopCore) {
        & (Join-Path $root 'build-support\Verify-SoopDistribution.ps1') -Manifest $workerManifestPath -Authorization $soopAuthorization
    }
    if ($currentManifest.adapterSources.sha256 -ne $workerManifest.adapterSources.sha256 -or
        ($workerManifest.soopCore -and $currentManifest.soopCore.sha256 -ne $workerManifest.soopCore.sha256)) {
        throw 'Worker 与当前 Drops 源码不一致，请重新打包。'
    }
    foreach ($platformName in @('twitch', 'soop', 'youtube', 'bilibili')) {
        $workerPath = Join-Path $dropsArtifacts "$platformName\$platformName-worker.exe"
        if (-not (Test-Path -LiteralPath $workerPath) -or
            (Get-FileHash -LiteralPath $workerPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $workerManifest.workers.$platformName) {
            throw "$platformName Worker 摘要不匹配，请重新打包。"
        }
    }

    Write-Host '[2/9] dotnet restore' -ForegroundColor Cyan
    & dotnet restore 'CloudLight Blizzard.csproj' -r win-x64 -v minimal @msbuildProperties
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore 失败。请检查 .NET 8 SDK 和 NuGet 网络。' }

    Write-Host '[3/9] dotnet build (Release)' -ForegroundColor Cyan
    & dotnet build 'CloudLight Blizzard.csproj' -c Release --no-restore -v minimal @msbuildProperties
    if ($LASTEXITCODE -ne 0) { throw 'Release 构建失败。' }

    Write-Host '[4/9] dotnet publish' -ForegroundColor Cyan
    & dotnet publish 'CloudLight Blizzard.csproj' -c Release -r win-x64 --self-contained false -o publish --no-restore -v minimal @msbuildProperties
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish 失败。' }
    $publishedExe = Join-Path $root 'publish\CloudLight Blizzard.exe'
    $publishedDll = Join-Path $root 'publish\CloudLight Blizzard.dll'
    if (-not (Test-Path -LiteralPath $publishedExe -PathType Leaf)) { throw 'publish 输出缺少 CloudLight Blizzard.exe。' }
    if (-not (Test-Path -LiteralPath $publishedDll -PathType Leaf)) { throw 'publish 输出缺少 CloudLight Blizzard.dll。' }
    & (Join-Path $root 'Integrations\Drops\test-worker-ssl.ps1') -Root (Join-Path $root 'publish\_internal\drops') -SoopAdapterOnly:$OmitUnlicensedSoopCore
    if ($LASTEXITCODE -ne 0) { throw '发布目录 Drops Worker SSL 自检失败。' }
    Remove-Item publish\*.pdb -ErrorAction SilentlyContinue

    Write-Host '[5/9] publish selftest' -ForegroundColor Cyan
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

    Write-Host '[6/9] WPF navigation integration selftest' -ForegroundColor Cyan
    $uiSelftestRoot = Join-Path $root 'obj\publish-ui-selftest'
    Remove-Item -LiteralPath $uiSelftestRoot -Recurse -Force -ErrorAction SilentlyContinue
    & dotnet $publishedDll '--ui-selftest' (Join-Path $uiSelftestRoot 'ui-navigation-selftest.txt')
    if ($LASTEXITCODE -ne 0) { throw '发布目录 WPF navigation integration selftest 进程失败。' }
    $uiSelftestReport = Join-Path $uiSelftestRoot 'ui-navigation-selftest.txt'
    if (-not (Test-Path -LiteralPath $uiSelftestReport -PathType Leaf)) {
        throw '发布目录 WPF navigation integration selftest 未生成报告。'
    }
    if (-not (Get-Content -LiteralPath $uiSelftestReport -Raw).Contains('UI navigation integration selftest: PASS')) {
        throw '发布目录 WPF navigation integration selftest 未通过，请查看 obj\publish-ui-selftest\ui-navigation-selftest.txt。'
    }

    Write-Host '[7/9] Inno Setup' -ForegroundColor Cyan
    $installerScript = Get-Content -LiteralPath (Join-Path $root 'installer\app.iss') -Raw
    if ($installerScript -notmatch '(?m)^Filename:.*Flags:.*\brunasoriginaluser\b') {
        throw '安装脚本必须以原登录用户身份启动应用，避免更新后读取管理员账户的设置目录。'
    }
    if ($installerScript -notmatch 'BackupUserSettings' -or
        $installerScript -notmatch 'RestoreUserSettings') {
        throw '安装脚本必须在关闭旧程序前备份并在安装后恢复用户设置。'
    }
    if ($installerScript -notmatch '(?m)^CloseApplications=force$' -or
        $installerScript -notmatch '(?m)^RestartApplications=no$') {
        throw '安装脚本必须可靠关闭占用程序文件的旧进程，并禁止 Restart Manager 重复启动应用。'
    }
    $publishDir = (Resolve-Path (Join-Path $root 'publish')).Path
    & $iscc ("/DPublishDir=$publishDir") (Join-Path $root 'installer\app.iss') | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup 构建失败。' }
    $setup = Get-Item (Join-Path $root "installer\out\CloudLight-Blizzard-$releaseVersion-win-x64-Setup.exe") -ErrorAction SilentlyContinue
    if (-not $setup) { throw 'Inno Setup 未生成安装包。' }
    if ($setup.Length -le 0) { throw '生成的安装包大小为 0。' }
    $setupProductVersion = ([string]$setup.VersionInfo.ProductVersion).Trim()
    $setupFileVersion = ([string]$setup.VersionInfo.FileVersion).Trim()
    $setupProductVersionRaw = ([string]$setup.VersionInfo.ProductVersionRaw).Trim()
    $setupFileVersionRaw = ([string]$setup.VersionInfo.FileVersionRaw).Trim()
    if ($setupProductVersionRaw -ne "${releaseVersion}.0" -or $setupFileVersionRaw -ne "${releaseVersion}.0") {
        throw "安装包版本元数据不正确（ProductVersion=$setupProductVersion/$setupProductVersionRaw，FileVersion=$setupFileVersion/$setupFileVersionRaw）。"
    }
    Write-Host "Installer ProductVersion: $setupProductVersion (raw $setupProductVersionRaw)"
    Write-Host "Installer FileVersion: $setupFileVersion (raw $setupFileVersionRaw)"
    Write-Host '[8/9] Installer payload validation' -ForegroundColor Cyan
    $publishedDllHash = (Get-FileHash -LiteralPath $publishedDll -Algorithm SHA256).Hash.ToLowerInvariant()
    $payloadValidationRoot = Join-Path $root 'obj\installer-payload-selftest'
    Remove-Item -LiteralPath $payloadValidationRoot -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $payloadValidationRoot -Force | Out-Null
    $payloadLog = Join-Path $payloadValidationRoot 'inno-payload.log'
    $payloadInstallFallback = Join-Path $payloadValidationRoot 'fallback-install'
    $payloadArgs = @(
        '/PAYLOADVERIFY', '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/NOCANCEL',
        '/NOCLOSEAPPLICATIONS', '/NOICONS',
        ('/DIR="' + $payloadInstallFallback + '"'), ('/LOG="' + $payloadLog + '"')
    )
    $payloadProcess = Start-Process -FilePath $setup.FullName -ArgumentList $payloadArgs -Wait -PassThru
    if (-not (Test-Path -LiteralPath $payloadLog -PathType Leaf)) {
        throw "Inno payload validation 未生成日志（exit $($payloadProcess.ExitCode)）。"
    }
    $payloadLogText = Get-Content -LiteralPath $payloadLog -Raw
    $publishedExeHash = (Get-FileHash -LiteralPath $publishedExe -Algorithm SHA256).Hash.ToLowerInvariant()
    $expectedPayloadExeLine = "PAYLOAD_VERIFY EXE_SHA256=$publishedExeHash"
    $expectedPayloadLine = "PAYLOAD_VERIFY DLL_SHA256=$publishedDllHash"
    if (-not $payloadLogText.Contains($expectedPayloadExeLine) -or
        -not $payloadLogText.Contains($expectedPayloadLine) -or
        $payloadLogText.Contains('PAYLOAD_VERIFY FAIL')) {
        throw "Installer payload 与 publish EXE/DLL 不一致（exe=$publishedExeHash，dll=$publishedDllHash，exit=$($payloadProcess.ExitCode)）。"
    }
    Write-Host '[9/9] Installer settings preservation validation' -ForegroundColor Cyan
    $settingsTestLog = Join-Path $payloadValidationRoot 'inno-settings-preserve.log'
    $settingsTestArgs = @(
        '/SETTINGSPRESERVETEST', '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/NOCANCEL',
        '/NOCLOSEAPPLICATIONS', '/NOICONS',
        ('/DIR="' + $payloadInstallFallback + '"'), ('/LOG="' + $settingsTestLog + '"')
    )
    $settingsTestProcess = Start-Process -FilePath $setup.FullName -ArgumentList $settingsTestArgs -Wait -PassThru
    if (-not (Test-Path -LiteralPath $settingsTestLog -PathType Leaf)) {
        throw "Inno settings preservation validation 未生成日志（exit $($settingsTestProcess.ExitCode)）。"
    }
    $settingsTestLogText = Get-Content -LiteralPath $settingsTestLog -Raw
    if (-not $settingsTestLogText.Contains('SETTINGS_PRESERVE_TEST PASS') -or
        $settingsTestLogText.Contains('SETTINGS_PRESERVE_TEST FAIL') -or
        $settingsTestLogText.Contains('Type Mismatch')) {
        throw "Installer 设置保留自检失败（exit=$($settingsTestProcess.ExitCode)）。"
    }
    $sha = (Get-FileHash -LiteralPath $setup.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host "Publish: $publishedExe ($((Get-Item $publishedExe).Length) bytes)" -ForegroundColor Green
    Write-Host "Publish DLL SHA256: $publishedDllHash"
    Write-Host "Installer payload DLL SHA256: $publishedDllHash"
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
