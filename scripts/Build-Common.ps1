function Resolve-FeeScreenSimRoot {
    [CmdletBinding()]
    param([string]$ExplicitRoot)

    $repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
    $candidates = [Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($ExplicitRoot)) { $candidates.Add($ExplicitRoot) }
    if (-not [string]::IsNullOrWhiteSpace($env:FEE_SCREEN_SIM_ROOT)) { $candidates.Add($env:FEE_SCREEN_SIM_ROOT) }
    $candidates.Add((Join-Path $repositoryRoot 'external\fe-screen-sim'))

    $installationRoots = @(
        (Join-Path $env:ProgramFiles 'fe.screen-sim V5')
        if (-not [string]::IsNullOrWhiteSpace(${env:ProgramFiles(x86)})) {
            Join-Path ${env:ProgramFiles(x86)} 'fe.screen-sim V5'
        }
    ) | Select-Object -Unique
    foreach ($installationRoot in $installationRoots) {
        if (-not (Test-Path -LiteralPath $installationRoot -PathType Container)) { continue }
        Get-ChildItem -LiteralPath $installationRoot -Directory |
            Sort-Object {
                $match = [regex]::Match($_.Name, '\d+(?:\.\d+){1,3}')
                $parsed = [version]'0.0'
                if ($match.Success -and [version]::TryParse($match.Value, [ref]$parsed)) { $parsed } else { [version]'0.0' }
            } -Descending |
            ForEach-Object { $candidates.Add($_.FullName) }
    }

    $checked = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($candidate in $candidates) {
        if ([string]::IsNullOrWhiteSpace($candidate)) { continue }
        $absoluteCandidate = [IO.Path]::GetFullPath($candidate)
        if (-not $checked.Add($absoluteCandidate)) { continue }

        $marker = Join-Path $absoluteCandidate 'Bin\FS.SDK.dll'
        if (Test-Path -LiteralPath $marker -PathType Leaf) {
            $resolved = (Resolve-Path -LiteralPath $absoluteCandidate).Path
            Write-Host "FEE SDK erkannt: Version $([IO.Path]::GetFileName($resolved)) unter '$resolved'."
            return $resolved
        }

        if (Test-Path -LiteralPath $absoluteCandidate -PathType Container) {
            Write-Warning "FEE-Installation '$absoluteCandidate' wird übersprungen: Bin\FS.SDK.dll fehlt."
        }
    }

    throw 'Keine vollständige fe.screen-sim-SDK-Installation gefunden. Unvollständige Versionen wurden übersprungen. FEE_SCREEN_SIM_ROOT setzen oder external\fe-screen-sim bereitstellen.'
}

function Assert-LastExitCode {
    param([string]$Operation)
    if ($LASTEXITCODE -ne 0) { throw "$Operation ist mit Exitcode $LASTEXITCODE fehlgeschlagen." }
}

function Get-FeeRuntimeClosure {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$FeeRoot)

    $repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
    $binRoot = Join-Path $FeeRoot 'Bin'
    $pluginAssembly = Join-Path $binRoot 'Plugins\ReadingUnitPlugin\ReadingUnitPlugin.dll'
    $availableFiles = @(Get-ChildItem -LiteralPath $binRoot -Filter 'FS.*.dll' -File)
    if ($availableFiles.Count -eq 0) {
        throw "Im FEE-SDK '$FeeRoot' wurden keine FS.*-Runtime-Assemblies gefunden."
    }
    if (-not (Test-Path -LiteralPath $pluginAssembly -PathType Leaf)) {
        throw "Im FEE-SDK '$FeeRoot' fehlt Plugins\ReadingUnitPlugin\ReadingUnitPlugin.dll."
    }

    $available = [Collections.Generic.Dictionary[string, IO.FileInfo]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($assemblyFile in $availableFiles) {
        $available[[IO.Path]::GetFileNameWithoutExtension($assemblyFile.Name)] = $assemblyFile
    }

    # The project references are the closure roots. Starting from them avoids
    # packaging unrelated FS tools merely because they share the same Bin dir.
    [xml]$project = Get-Content -LiteralPath (Join-Path $repositoryRoot 'VIBN_Tools.csproj') -Raw
    $rootNames = @($project.SelectNodes('//Reference') |
        ForEach-Object { [string]$_.Include } |
        Where-Object { $_.StartsWith('FS.', [StringComparison]::OrdinalIgnoreCase) } |
        Sort-Object -Unique)
    $queue = [Collections.Generic.Queue[IO.FileInfo]]::new()
    $selected = [Collections.Generic.Dictionary[string, IO.FileInfo]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($rootName in $rootNames) {
        if (-not $available.ContainsKey($rootName)) {
            throw "Die im Projekt referenzierte FEE-Assembly '$rootName.dll' fehlt unter '$binRoot'."
        }
        $rootFile = $available[$rootName]
        $selected[$rootName] = $rootFile
        $queue.Enqueue($rootFile)
    }
    $queue.Enqueue((Get-Item -LiteralPath $pluginAssembly))

    $inspected = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $missing = [Collections.Generic.SortedSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    while ($queue.Count -gt 0) {
        $assemblyFile = $queue.Dequeue()
        if (-not $inspected.Add($assemblyFile.FullName)) { continue }
        try {
            $assembly = [Reflection.Assembly]::LoadFile($assemblyFile.FullName)
            foreach ($reference in $assembly.GetReferencedAssemblies()) {
                if (-not $reference.Name.StartsWith('FS.', [StringComparison]::OrdinalIgnoreCase)) { continue }
                if (-not $available.ContainsKey($reference.Name)) {
                    [void]$missing.Add($reference.Name)
                    continue
                }
                $dependencyFile = $available[$reference.Name]
                if (-not $selected.ContainsKey($reference.Name)) {
                    $selected[$reference.Name] = $dependencyFile
                    $queue.Enqueue($dependencyFile)
                }
            }
        }
        catch {
            throw "Benötigte FEE-Assembly konnte nicht geprüft werden: $($assemblyFile.FullName). $($_.Exception.Message)"
        }
    }

    if ($missing.Count -gt 0) {
        throw "Das FEE-SDK ist nur für den Build, nicht für ein lauffähiges Deployment vollständig. Fehlend: $($missing -join ', ')."
    }

    return @($selected.Values | Sort-Object Name)
}

function Assert-FeeRuntimeClosure {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$FeeRoot)

    [void]@(Get-FeeRuntimeClosure -FeeRoot $FeeRoot)
}
