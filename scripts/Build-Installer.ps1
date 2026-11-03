[CmdletBinding()]
param(
    [string]$FeeScreenSimRoot,
    [string]$AdditionalPackageSource,
    [string]$InnoCompiler
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'Installer-Common.ps1')
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'Die Setup-Erstellung benötigt Windows, das .NET-8-SDK und Inno Setup 6.'
}
# Fail before publishing if the compiler is unavailable. Empty ProgramFiles
# variables must not make Join-Path fail, even with an explicit compiler path.
$compiler = Resolve-VibnInnoCompiler -ExplicitPath $InnoCompiler

& (Join-Path $PSScriptRoot 'Publish-Portable.ps1') `
    -FeeScreenSimRoot $FeeScreenSimRoot `
    -AdditionalPackageSource $AdditionalPackageSource `
    -SkipArchive
if ($LASTEXITCODE -ne 0) { throw 'Portable Veröffentlichung fehlgeschlagen.' }

Assert-VibnPublishOutput -PublishRoot (Join-Path $repositoryRoot 'artifacts/publish/VIBN_Tools-win-x64')

Push-Location $repositoryRoot
try {
    [xml]$buildProperties = Get-Content -LiteralPath 'Directory.Build.props'
    $productVersion = [string]($buildProperties.Project.PropertyGroup.VibnToolsVersion | Select-Object -First 1)
    if ([string]::IsNullOrWhiteSpace($productVersion)) {
        throw 'VibnToolsVersion fehlt in Directory.Build.props.'
    }
    $setupPath = Join-Path $repositoryRoot 'artifacts/installer/VIBN_Tools_Setup.exe'
    if (Test-Path -LiteralPath $setupPath) { Remove-Item -LiteralPath $setupPath -Force }
    & $compiler "/DMyAppVersion=$productVersion" 'installer\VIBN_Tools.iss'
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup ist mit Exitcode $LASTEXITCODE fehlgeschlagen." }
    if (-not (Test-Path -LiteralPath $setupPath -PathType Leaf)) {
        throw 'Inno Setup meldete Erfolg, hat aber keine neue Setup-Datei erzeugt.'
    }
}
finally {
    Pop-Location
}

Write-Host (Join-Path $repositoryRoot 'artifacts\installer\VIBN_Tools_Setup.exe')
