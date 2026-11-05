[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../scripts/Installer-Common.ps1')
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('vibn-installer-' + [guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $temporary -Force | Out-Null
    $compiler = Join-Path $temporary 'Inno Setup 6/ISCC.exe'
    New-Item -ItemType Directory -Path (Split-Path $compiler) -Force | Out-Null
    Set-Content -LiteralPath $compiler -Value 'compiler fixture'
    if ((Resolve-VibnInnoCompiler -ExplicitPath $compiler -SearchRoots @()) -ne $compiler) {
        throw 'Explicit Inno compiler was not preserved.'
    }
    if ((Resolve-VibnInnoCompiler -SearchRoots @('', $temporary)) -ne $compiler) {
        throw 'Compiler discovery failed with an empty ProgramFiles candidate.'
    }
    $rejected = $false
    try { Resolve-VibnInnoCompiler -ExplicitPath (Join-Path $temporary 'missing.exe') | Out-Null }
    catch { $rejected = $true }
    if (-not $rejected) { throw 'A missing explicit compiler was accepted.' }
    $rejected = $false
    try { Assert-VibnPublishOutput -PublishRoot $temporary }
    catch { $rejected = $true }
    if (-not $rejected) { throw 'An incomplete publish directory was accepted.' }
    foreach ($relative in @('VIBN_Tools.exe', 'VIBN_Tools.deps.json', 'VIBN_Tools.runtimeconfig.json',
        'TiaBridge/VIBN_Tools.TiaBridge.exe', 'Content/LogicDefinitions/GrobStandard/Grob_Cylinder.xml')) {
        $file = Join-Path $temporary $relative
        New-Item -ItemType Directory -Path (Split-Path $file) -Force | Out-Null
        Set-Content -LiteralPath $file -Value 'publish fixture'
    }
    Assert-VibnPublishOutput -PublishRoot $temporary
    Remove-Item -LiteralPath (Join-Path $temporary 'TiaBridge/VIBN_Tools.TiaBridge.exe')
    $rejected = $false
    try { Assert-VibnPublishOutput -PublishRoot $temporary }
    catch { $rejected = $true }
    if (-not $rejected) { throw 'A publish without the TIA bridge was accepted.' }
    $repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
    foreach ($file in Get-ChildItem -Path (Join-Path $repository 'scripts') -Filter '*.ps1') {
        $parseErrors = $null
        $tokens = $null
        [void][Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$parseErrors)
        if ($parseErrors.Count -gt 0) { throw ($parseErrors | Out-String) }
    }
    Write-Host 'Installer compiler discovery, incomplete-publish rejection and script parsing passed.'
}
finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
}
