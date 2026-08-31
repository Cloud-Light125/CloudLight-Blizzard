$ErrorActionPreference = 'Stop'
$finder = Join-Path $PSScriptRoot 'Find-Python.ps1'
if (-not (Test-Path -LiteralPath $finder -PathType Leaf)) {
    throw '找不到 build-support\Find-Python.ps1。'
}
. $finder

$pythonInfo = Find-CloudLightPython
if ([string]::IsNullOrWhiteSpace($pythonInfo.Path) -or
    [string]::IsNullOrWhiteSpace($pythonInfo.Version) -or
    [string]::IsNullOrWhiteSpace($pythonInfo.OpenSsl)) {
    throw 'Python discovery 未返回完整的可用运行时信息。'
}

Write-Host 'Python discovery selftest: PASS' -ForegroundColor Green
Write-Host "Python: $($pythonInfo.Version)"
Write-Host "Source: $($pythonInfo.Source)"
Write-Host "Path: $($pythonInfo.Path)"
Write-Host "SSL: $($pythonInfo.OpenSsl)"
