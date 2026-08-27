[CmdletBinding()]
param(
    [string]$FeeScreenSimRoot,
    [string]$AdditionalPackageSource,
    [ValidateSet('win-x64')]
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Build-Common.ps1')
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$feeRoot = Resolve-FeeScreenSimRoot -ExplicitRoot $FeeScreenSimRoot
Assert-FeeRuntimeClosure -FeeRoot $feeRoot
$artifactsRoot = Join-Path $repositoryRoot 'artifacts\publish'
$publishRoot = Join-Path $artifactsRoot "VIBN_Tools-$Runtime"
$zipPath = Join-Path $artifactsRoot "VIBN_Tools-$Runtime.zip"
$hashPath = "$zipPath.sha256.txt"

New-Item -ItemType Directory -Path $artifactsRoot -Force | Out-Null
foreach ($target in @($publishRoot, $zipPath, $hashPath)) {
    $absoluteTarget = [IO.Path]::GetFullPath($target)
    if (-not $absoluteTarget.StartsWith($artifactsRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Ungültiges Veröffentlichungsziel: $absoluteTarget"
    }
    if (Test-Path -LiteralPath $absoluteTarget) {
        Remove-Item -LiteralPath $absoluteTarget -Recurse -Force
    }
}

Push-Location $repositoryRoot
try {
    if (-not [string]::IsNullOrWhiteSpace($AdditionalPackageSource)) {
        $packageSource = (Resolve-Path -LiteralPath $AdditionalPackageSource).Path
        dotnet restore VIBN_Tools_App.sln "-p:RestoreAdditionalProjectSources=$packageSource"
    }
    else {
        dotnet restore VIBN_Tools_App.sln
    }
    Assert-LastExitCode 'NuGet-Wiederherstellung'

    # A runtime-specific assets target is required for self-contained publish.
    if (-not [string]::IsNullOrWhiteSpace($AdditionalPackageSource)) {
        dotnet restore VIBN_Tools.csproj --runtime $Runtime "-p:RestoreAdditionalProjectSources=$packageSource"
    }
    else {
        dotnet restore VIBN_Tools.csproj --runtime $Runtime
    }
    Assert-LastExitCode 'Runtime-spezifische NuGet-Wiederherstellung'

    $publishArguments = @(
        'publish', 'VIBN_Tools.csproj',
        '--configuration', 'Release',
        '--runtime', $Runtime,
        '--self-contained', 'true',
        '--output', $publishRoot,
        '--no-restore',
        '-p:PublishSingleFile=false',
        '-p:PublishReadyToRun=false',
        "-p:FEE_SCREEN_SIM_ROOT=$feeRoot"
    )
    dotnet @publishArguments
    Assert-LastExitCode 'Portable Veröffentlichung'

    Copy-Item -LiteralPath 'distribution\START_HERE.md' -Destination $publishRoot
    Copy-Item -LiteralPath 'scripts\Configure-VIBN-Tools.ps1' -Destination $publishRoot
    Copy-Item -LiteralPath 'Configure-VIBN-Tools.cmd' -Destination $publishRoot
    Copy-Item -LiteralPath 'docs' -Destination (Join-Path $publishRoot 'docs') -Recurse
    Compress-Archive -Path (Join-Path $publishRoot '*') -DestinationPath $zipPath -CompressionLevel Optimal
    $hash = Get-FileHash -LiteralPath $zipPath -Algorithm SHA256
    Set-Content -LiteralPath $hashPath -Value "$($hash.Hash)  $([IO.Path]::GetFileName($zipPath))" -Encoding ascii
}
finally {
    Pop-Location
}

Write-Host "Portable Paket: $zipPath"
