$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Push-Location $root
try {
    'bin', 'obj', 'publish', 'installer\out' | ForEach-Object {
        Remove-Item -Recurse -Force $_ -ErrorAction SilentlyContinue
    }

    Write-Host '[1/2] dotnet publish' -ForegroundColor Cyan
    & dotnet publish BnetSwitch.csproj -c Release -r win-x64 --self-contained false -o publish -v minimal
    if ($LASTEXITCODE -ne 0) {
        throw 'dotnet publish failed.'
    }
    Remove-Item publish\*.pdb -ErrorAction SilentlyContinue

    $iscc = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'
    if (Test-Path $iscc) {
        Write-Host '[2/2] Inno Setup' -ForegroundColor Cyan
        & $iscc installer\app.iss | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw 'ISCC build failed.'
        }
        $setup = Get-ChildItem installer\out\*.exe | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        $sha = (Get-FileHash $setup.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        Write-Host "Setup: $($setup.FullName)" -ForegroundColor Green
        Write-Host "SHA256: $sha"
    }
    else {
        Write-Host '[2/2] Inno Setup not found; publish output is ready.' -ForegroundColor Yellow
    }
}
finally {
    Pop-Location
}
