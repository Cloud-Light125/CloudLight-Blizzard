param([switch]$SkipDropsWorkers)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Push-Location $root
try {
    'bin', 'obj', 'publish', 'installer\out' | ForEach-Object {
        Remove-Item -Recurse -Force $_ -ErrorAction SilentlyContinue
    }

    if (-not $SkipDropsWorkers) {
        Write-Host '[1/3] Drops Workers' -ForegroundColor Cyan
        & (Join-Path $root 'Integrations\Drops\build-workers.ps1')
        if ($LASTEXITCODE -ne 0) { throw 'Drops Worker build failed.' }
    }

    Write-Host '[2/3] dotnet publish' -ForegroundColor Cyan
    & dotnet publish 'CloudLight Blizzard.csproj' -c Release -r win-x64 --self-contained false -o publish -v minimal
    if ($LASTEXITCODE -ne 0) {
        throw 'dotnet publish failed.'
    }
    & (Join-Path $root 'Integrations\Drops\test-worker-ssl.ps1') -Root (Join-Path $root 'publish\_internal\drops')
    Remove-Item publish\*.pdb -ErrorAction SilentlyContinue

    $iscc = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'
    if (Test-Path $iscc) {
        Write-Host '[3/3] Inno Setup' -ForegroundColor Cyan
        & $iscc installer\app.iss | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw 'ISCC build failed.'
        }
        $setup = Get-ChildItem installer\out\*.exe | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        $sha = (Get-FileHash $setup.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        Write-Host "Setup: $($setup.FullName)" -ForegroundColor Green
        Write-Host "SHA256: $sha"
        Write-Host "Setup size: $([math]::Round($setup.Length / 1MB, 2)) MB"
    }
    else {
        Write-Host '[3/3] Inno Setup not found; publish output is ready.' -ForegroundColor Yellow
    }
}
finally {
    Pop-Location
}
