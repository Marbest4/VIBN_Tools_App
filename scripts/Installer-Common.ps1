function Resolve-VibnInnoCompiler {
    [CmdletBinding()]
    param([string]$ExplicitPath, [string[]]$SearchRoots = @(${env:ProgramFiles(x86)}, $env:ProgramFiles))

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        if (-not (Test-Path -LiteralPath $ExplicitPath -PathType Leaf)) {
            throw "InnoCompiler wurde nicht gefunden: $ExplicitPath"
        }
        return (Resolve-Path -LiteralPath $ExplicitPath).Path
    }
    foreach ($root in $SearchRoots) {
        if ([string]::IsNullOrWhiteSpace($root)) { continue }
        $candidate = Join-Path $root 'Inno Setup 6/ISCC.exe'
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return (Resolve-Path -LiteralPath $candidate).Path }
    }
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) { return $command.Source }
    throw 'Inno Setup 6 wurde nicht gefunden. InnoCompiler angeben oder Inno Setup 6 installieren.'
}

function Assert-VibnPublishOutput {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$PublishRoot)

    foreach ($relative in @('VIBN_Tools.exe', 'VIBN_Tools.deps.json', 'VIBN_Tools.runtimeconfig.json',
        'TiaBridge/VIBN_Tools.TiaBridge.exe', 'Content/LogicDefinitions/GrobStandard/Grob_Cylinder.xml')) {
        if (-not (Test-Path -LiteralPath (Join-Path $PublishRoot $relative) -PathType Leaf)) {
            throw "Unvollständige Veröffentlichung: $relative fehlt unter $PublishRoot. Kein Setup erzeugt."
        }
    }
}
