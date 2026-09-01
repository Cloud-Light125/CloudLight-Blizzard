param(
    [Parameter(Mandatory = $true)]
    [string]$InstallDirectory,
    [string]$PublishDirectory = (Join-Path $PSScriptRoot '..\publish')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$installRoot = (Resolve-Path -LiteralPath $InstallDirectory).Path
$publishRoot = (Resolve-Path -LiteralPath $PublishDirectory).Path
$markers = @(
    'BilibiliQrLoginButton',
    'BilibiliAccountCard',
    'BilibiliWatchModePicker',
    'BilibiliSessionsPerRoomText',
    'BilibiliTaskList',
    'BilibiliPanel'
)

function Get-FileRecord {
    param([Parameter(Mandatory = $true)][string]$Path)

    $item = Get-Item -LiteralPath $Path
    $version = $item.VersionInfo
    [pscustomobject]@{
        Path = $item.FullName
        SizeBytes = $item.Length
        LastWriteTime = $item.LastWriteTime.ToString('o')
        SHA256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToUpperInvariant()
        ProductVersion = ([string]$version.ProductVersion).Trim()
        FileVersion = ([string]$version.FileVersion).Trim()
    }
}

function Get-AssemblyMetadata {
    param([Parameter(Mandatory = $true)][string]$Path)

    $assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($Path))
    $metadata = @{}
    foreach ($attribute in $assembly.CustomAttributes |
            Where-Object { $_.AttributeType -eq [Reflection.AssemblyMetadataAttribute] }) {
        $metadata[[string]$attribute.ConstructorArguments[0].Value] =
            [string]$attribute.ConstructorArguments[1].Value
    }
    $informationalAttribute = $assembly.CustomAttributes |
        Where-Object { $_.AttributeType -eq [Reflection.AssemblyInformationalVersionAttribute] } |
        Select-Object -First 1
    [pscustomobject]@{
        ApplicationVersion = if ($null -eq $informationalAttribute) { 'unknown' } else {
            [string]$informationalAttribute.ConstructorArguments[0].Value
        }
        AssemblyVersion = $assembly.GetName().Version?.ToString()
        BuildCommit = if ($metadata.ContainsKey('BuildCommit')) { [string]$metadata['BuildCommit'] } else { 'unknown' }
        BuildTimestamp = if ($metadata.ContainsKey('BuildTimestamp')) { [string]$metadata['BuildTimestamp'] } else { 'unknown' }
        BilibiliUiSchema = if ($metadata.ContainsKey('BilibiliUiSchema')) { [string]$metadata['BilibiliUiSchema'] } else { 'unknown' }
    }
}

function Test-ByteSequence {
    param([byte[]]$Data, [byte[]]$Needle)

    if ($Needle.Length -eq 0 -or $Data.Length -lt $Needle.Length) { return $false }
    for ($i = 0; $i -le $Data.Length - $Needle.Length; $i++) {
        $match = $true
        for ($j = 0; $j -lt $Needle.Length; $j++) {
            if ($Data[$i + $j] -ne $Needle[$j]) { $match = $false; break }
        }
        if ($match) { return $true }
    }
    return $false
}

function Get-BilibiliBamlInspection {
    param([Parameter(Mandatory = $true)][string]$Path)

    $assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($Path))
    $resourceName = $assembly.GetManifestResourceNames() |
        Where-Object { $_ -like '*.g.resources' } | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($resourceName)) { throw "未找到 .g.resources：$Path" }

    $resourceStream = $assembly.GetManifestResourceStream($resourceName)
    $reader = [System.Resources.ResourceReader]::new($resourceStream)
    try {
        $entry = $reader.GetEnumerator() |
            Where-Object { $_.Key -eq 'views/pages/dropspage.baml' } | Select-Object -First 1
        if ($null -eq $entry) { throw "未找到 DropsPage BAML：$Path" }
        $bamlStream = [IO.Stream]$entry.Value
        $buffer = [IO.MemoryStream]::new()
        try { $bamlStream.CopyTo($buffer); $baml = $buffer.ToArray() }
        finally { $buffer.Dispose(); $bamlStream.Dispose() }
    }
    finally { $reader.Dispose(); $resourceStream.Dispose() }

    $present = @{}
    foreach ($marker in $markers) {
        $present[$marker] = (Test-ByteSequence $baml ([Text.Encoding]::UTF8.GetBytes($marker))) -or
            (Test-ByteSequence $baml ([Text.Encoding]::Unicode.GetBytes($marker)))
    }
    [pscustomobject]@{
        Resource = 'views/pages/dropspage.baml'
        BamlBytes = $baml.Length
        BamlSHA256 = (([Security.Cryptography.SHA256]::Create().ComputeHash($baml) |
                ForEach-Object { $_.ToString('x2') }) -join '')
        Markers = $present
    }
}

$publishExe = Join-Path $publishRoot 'CloudLight Blizzard.exe'
$publishDll = Join-Path $publishRoot 'CloudLight Blizzard.dll'
$installExe = Join-Path $installRoot 'CloudLight Blizzard.exe'
$installDll = Join-Path $installRoot 'CloudLight Blizzard.dll'
foreach ($path in @($publishExe, $publishDll, $installExe, $installDll)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "缺少文件：$path" }
}

$publishFiles = @{
    EXE = Get-FileRecord $publishExe
    DLL = Get-FileRecord $publishDll
}
$installFiles = @{
    EXE = Get-FileRecord $installExe
    DLL = Get-FileRecord $installDll
}

Write-Host '=== Publish ==='
$publishFiles.Values | Format-Table Path,SizeBytes,SHA256,ProductVersion,FileVersion -AutoSize
Write-Host '=== Installed ==='
$installFiles.Values | Format-Table Path,SizeBytes,SHA256,ProductVersion,FileVersion -AutoSize

$publishMetadata = Get-AssemblyMetadata $publishDll
$installMetadata = Get-AssemblyMetadata $installDll
Write-Host '=== Build provenance ==='
[pscustomobject]@{Location='publish';ApplicationVersion=$publishMetadata.ApplicationVersion;AssemblyVersion=$publishMetadata.AssemblyVersion;BuildCommit=$publishMetadata.BuildCommit;BuildTimestamp=$publishMetadata.BuildTimestamp;BilibiliUiSchema=$publishMetadata.BilibiliUiSchema} | Format-Table -AutoSize
[pscustomobject]@{Location='installed';ApplicationVersion=$installMetadata.ApplicationVersion;AssemblyVersion=$installMetadata.AssemblyVersion;BuildCommit=$installMetadata.BuildCommit;BuildTimestamp=$installMetadata.BuildTimestamp;BilibiliUiSchema=$installMetadata.BilibiliUiSchema} | Format-Table -AutoSize

Write-Host '=== Bilibili BAML ==='
$publishBaml = Get-BilibiliBamlInspection $publishDll
$installBaml = Get-BilibiliBamlInspection $installDll
[pscustomobject]@{Location='publish';Resource=$publishBaml.Resource;BamlBytes=$publishBaml.BamlBytes;BamlSHA256=$publishBaml.BamlSHA256;Markers=($markers | ForEach-Object { "$_=$($publishBaml.Markers[$_])" }) -join ';'} | Format-Table -AutoSize
[pscustomobject]@{Location='installed';Resource=$installBaml.Resource;BamlBytes=$installBaml.BamlBytes;BamlSHA256=$installBaml.BamlSHA256;Markers=($markers | ForEach-Object { "$_=$($installBaml.Markers[$_])" }) -join ';'} | Format-Table -AutoSize

$failures = [System.Collections.Generic.List[string]]::new()
if ($publishFiles.EXE.SHA256 -ne $installFiles.EXE.SHA256) { $failures.Add('安装 EXE 与 publish EXE SHA256 不一致。') }
if ($publishFiles.DLL.SHA256 -ne $installFiles.DLL.SHA256) { $failures.Add('安装 DLL 与 publish DLL SHA256 不一致。') }
if ($publishMetadata.BuildCommit -ne $installMetadata.BuildCommit) { $failures.Add('安装 DLL 与 publish DLL 的 BuildCommit 不一致。') }
if ($publishMetadata.BuildTimestamp -ne $installMetadata.BuildTimestamp) { $failures.Add('安装 DLL 与 publish DLL 的 BuildTimestamp 不一致。') }
if ($installMetadata.BilibiliUiSchema -ne '2') { $failures.Add('安装 DLL 的 BilibiliUiSchema 不是 2。') }
foreach ($marker in $markers) {
    if (-not $publishBaml.Markers[$marker]) { $failures.Add("publish BAML 缺少 $marker。") }
    if (-not $installBaml.Markers[$marker]) { $failures.Add("安装 BAML 缺少 $marker。") }
}
if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host 'VERIFY INSTALLED BUILD: PASS' -ForegroundColor Green
