Set-StrictMode -Version Latest

function Resolve-CloudLightCommandPath {
    param([Parameter(Mandatory)][string]$Name)

    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($null -eq $command) { return $null }
    if ($command.Source -and (Test-Path -LiteralPath $command.Source)) {
        return (Resolve-Path -LiteralPath $command.Source).Path
    }
    if ($command.Path -and (Test-Path -LiteralPath $command.Path)) {
        return (Resolve-Path -LiteralPath $command.Path).Path
    }
    return $null
}

function Test-CloudLightPythonRuntime {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    try {
        $versionText = (& $Path --version 2>&1 | Out-String).Trim()
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($versionText)) { return $null }

        $probe = @"
import platform, ssl, sys
import _ssl
print(sys.executable)
print(platform.python_version())
print(ssl.OPENSSL_VERSION)
"@
        $runtimeText = (& $Path -c $probe 2>&1 | Out-String).Trim()
        if ($LASTEXITCODE -ne 0) { return $null }
        $lines = @($runtimeText -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if ($lines.Count -lt 3 -or $lines[2] -notmatch '^OpenSSL\s') { return $null }

        [pscustomobject]@{
            Path = (Resolve-Path -LiteralPath $Path).Path
            Version = $lines[1].Trim()
            OpenSsl = $lines[2].Trim()
        }
    }
    catch { return $null }
}

function Add-CloudLightPythonCandidate {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[object]]$List,
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.HashSet[string]]$Seen,
        [string]$Path,
        [Parameter(Mandatory)][string]$Source
    )

    if ([string]::IsNullOrWhiteSpace($Path)) { return }
    try {
        $resolved = if (Test-Path -LiteralPath $Path -PathType Leaf) {
            (Resolve-Path -LiteralPath $Path).Path
        } else { $null }
        if ($resolved -and $Seen.Add($resolved.ToLowerInvariant())) {
            $List.Add([pscustomobject]@{ Path = $resolved; Source = $Source })
        }
    }
    catch { }
}

function Get-CloudLightCondaExecutables {
    $paths = [System.Collections.Generic.List[string]]::new()
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    Add-CloudLightPath -List $paths -Seen $seen -Path $env:CONDA_EXE
    Add-CloudLightPath -List $paths -Seen $seen -Path (Resolve-CloudLightCommandPath -Name 'conda')

    $roots = @(
        $env:USERPROFILE,
        $env:LOCALAPPDATA,
        $env:ProgramData
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    foreach ($root in $roots) {
        Add-CloudLightPath -List $paths -Seen $seen -Path (Join-Path $root 'miniconda3\Scripts\conda.exe')
        Add-CloudLightPath -List $paths -Seen $seen -Path (Join-Path $root 'anaconda3\Scripts\conda.exe')
    }
    return $paths
}

function Add-CloudLightPath {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$List,
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.HashSet[string]]$Seen,
        [string]$Path
    )
    if ([string]::IsNullOrWhiteSpace($Path)) { return }
    try {
        if ((Test-Path -LiteralPath $Path -PathType Leaf) -and
            $Seen.Add((Resolve-Path -LiteralPath $Path).Path.ToLowerInvariant())) {
            $List.Add((Resolve-Path -LiteralPath $Path).Path)
        }
    }
    catch { }
}

function Get-CloudLightCondaPythonCandidates {
    $candidates = [System.Collections.Generic.List[object]]::new()
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

    if (-not [string]::IsNullOrWhiteSpace($env:CONDA_PREFIX)) {
        Add-CloudLightPythonCandidate -List $candidates -Seen $seen `
            -Path (Join-Path $env:CONDA_PREFIX 'python.exe') -Source 'Activated Conda'
    }

    foreach ($conda in @(Get-CloudLightCondaExecutables)) {
        try {
            $base = (& $conda info --base 2>$null | Select-Object -Last 1).ToString().Trim()
            if ($base) {
                Add-CloudLightPythonCandidate -List $candidates -Seen $seen `
                    -Path (Join-Path $base 'python.exe') -Source 'Conda base'
            }

            $envJson = (& $conda env list --json 2>$null | Out-String).Trim()
            if ($LASTEXITCODE -eq 0 -and $envJson) {
                $envInfo = $envJson | ConvertFrom-Json
                foreach ($prefix in @($envInfo.envs)) {
                    Add-CloudLightPythonCandidate -List $candidates -Seen $seen `
                        -Path (Join-Path $prefix 'python.exe') -Source 'Conda environment'
                }
            }

            if (-not [string]::IsNullOrWhiteSpace($env:CONDA_DEFAULT_ENV)) {
                $executable = (& $conda run -n $env:CONDA_DEFAULT_ENV python -c 'import sys; print(sys.executable)' 2>$null | Select-Object -Last 1).ToString().Trim()
                Add-CloudLightPythonCandidate -List $candidates -Seen $seen `
                    -Path $executable -Source 'conda run'
            }
        }
        catch { }
    }
    return $candidates
}

function Find-CloudLightPython {
    param([string]$ExplicitPath = '')

    $candidates = [System.Collections.Generic.List[object]]::new()
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        $explicit = $ExplicitPath.Trim().Trim('"')
        if (-not (Test-Path -LiteralPath $explicit -PathType Leaf)) {
            $commandPath = Resolve-CloudLightCommandPath -Name $explicit
            if ($commandPath) { $explicit = $commandPath }
        }
        Add-CloudLightPythonCandidate -List $candidates -Seen $seen -Path $explicit -Source 'Explicit -Python'
    }

    foreach ($candidate in @(Get-CloudLightCondaPythonCandidates)) {
        if ($candidate.Path -and $seen.Add($candidate.Path.ToLowerInvariant())) {
            $candidates.Add($candidate)
        }
    }

    $py = Resolve-CloudLightCommandPath -Name 'py.exe'
    if ($py) {
        try {
            $pyPython = (& $py -3 -c 'import sys; print(sys.executable)' 2>$null | Select-Object -Last 1).ToString().Trim()
            Add-CloudLightPythonCandidate -List $candidates -Seen $seen -Path $pyPython -Source 'py.exe'
        }
        catch { }
    }

    Add-CloudLightPythonCandidate -List $candidates -Seen $seen `
        -Path (Resolve-CloudLightCommandPath -Name 'python.exe') -Source 'PATH'

    $userPythonRoots = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Python'),
        (Join-Path $env:USERPROFILE 'AppData\Local\Programs\Python')
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Container) }
    foreach ($root in $userPythonRoots) {
        foreach ($directory in @(Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue |
                    Sort-Object Name -Descending)) {
            Add-CloudLightPythonCandidate -List $candidates -Seen $seen `
                -Path (Join-Path $directory.FullName 'python.exe') -Source '用户级 Python'
        }
    }

    foreach ($candidate in $candidates) {
        $runtime = Test-CloudLightPythonRuntime -Path $candidate.Path
        if ($runtime) {
            return [pscustomobject]@{
                Path = $runtime.Path
                Version = $runtime.Version
                OpenSsl = $runtime.OpenSsl
                Source = $candidate.Source
            }
        }
    }

    throw '未找到可用的 Python：请安装 Python 3.10+（建议使用 Conda 或 python.org 版本），并确认可执行 --version、import ssl。'
}
