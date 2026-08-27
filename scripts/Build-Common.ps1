function Resolve-FeeScreenSimRoot {
    [CmdletBinding()]
    param([string]$ExplicitRoot)

    $candidates = [Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($ExplicitRoot)) { $candidates.Add($ExplicitRoot) }
    if (-not [string]::IsNullOrWhiteSpace($env:FEE_SCREEN_SIM_ROOT)) { $candidates.Add($env:FEE_SCREEN_SIM_ROOT) }

    $repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
    $candidates.Add((Join-Path $repositoryRoot 'external\fe-screen-sim'))

    $installationRoot = Join-Path $env:ProgramFiles 'fe.screen-sim V5'
    if (Test-Path -LiteralPath $installationRoot -PathType Container) {
        Get-ChildItem -LiteralPath $installationRoot -Directory |
            Sort-Object {
                $parsed = [version]'0.0'
                if ([version]::TryParse($_.Name, [ref]$parsed)) { $parsed } else { [version]'0.0' }
            } -Descending |
            ForEach-Object { $candidates.Add($_.FullName) }
    }

    foreach ($candidate in $candidates | Select-Object -Unique) {
        if (Test-Path -LiteralPath (Join-Path $candidate 'Bin\FS.SDK.dll') -PathType Leaf) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw 'Keine vollständige fe.screen-sim-SDK-Installation gefunden. FEE_SCREEN_SIM_ROOT setzen oder external\fe-screen-sim bereitstellen.'
}

function Assert-LastExitCode {
    param([string]$Operation)
    if ($LASTEXITCODE -ne 0) { throw "$Operation ist mit Exitcode $LASTEXITCODE fehlgeschlagen." }
}

function Assert-FeeRuntimeClosure {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$FeeRoot)

    $binRoot = Join-Path $FeeRoot 'Bin'
    $assemblyFiles = Get-ChildItem -LiteralPath $binRoot -Recurse -Filter '*.dll' -File
    $available = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($assemblyFile in $assemblyFiles) {
        [void]$available.Add([IO.Path]::GetFileNameWithoutExtension($assemblyFile.Name))
    }

    $missing = [Collections.Generic.SortedSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($assemblyFile in $assemblyFiles) {
        try {
            $assembly = [Reflection.Assembly]::LoadFile($assemblyFile.FullName)
            foreach ($reference in $assembly.GetReferencedAssemblies()) {
                if ($reference.Name.StartsWith('FS.', [StringComparison]::OrdinalIgnoreCase) -and
                    -not $available.Contains($reference.Name)) {
                    [void]$missing.Add($reference.Name)
                }
            }
        }
        catch {
            throw "FEE-Assembly konnte nicht geprüft werden: $($assemblyFile.FullName). $($_.Exception.Message)"
        }
    }

    if ($missing.Count -gt 0) {
        throw "Das FEE-SDK ist nur für den Build, nicht für ein lauffähiges Deployment vollständig. Fehlend: $($missing -join ', ')."
    }
}
