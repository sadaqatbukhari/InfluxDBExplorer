[CmdletBinding()]
param(
    # Raise the patch version (1.0.0 -> 1.0.1) before building. Without it the current Version.props version is built.
    [switch]$Bump
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$versionFile = Join-Path $repositoryRoot 'Version.props'
$bundleProject = Join-Path $PSScriptRoot 'Tickbird.Bundle\Tickbird.Bundle.wixproj'

[xml]$versionDocument = Get-Content -LiteralPath $versionFile
$currentVersion = [Version]$versionDocument.Project.PropertyGroup.Version
$nextVersion = if ($Bump) {
    '{0}.{1}.{2}' -f $currentVersion.Major, $currentVersion.Minor, ($currentVersion.Build + 1)
} else {
    $currentVersion.ToString(3)
}

if ($Bump) {
    $versionDocument.Project.PropertyGroup.Version = $nextVersion
    $versionDocument.Project.PropertyGroup.AssemblyVersion = "$nextVersion.0"
    $versionDocument.Project.PropertyGroup.FileVersion = "$nextVersion.0"
    $versionDocument.Save($versionFile)
}

Write-Host "Building Tickbird $nextVersion..."
dotnet build $bundleProject --configuration Release
if ($LASTEXITCODE -ne 0) {
    throw "Installer build failed with exit code $LASTEXITCODE."
}

$installerDirectory = Join-Path $repositoryRoot 'artifacts\installer'
$setupPath = Join-Path $installerDirectory "Tickbird-$nextVersion-Setup-win-x64.exe"
$msiPath = Join-Path $installerDirectory "Tickbird-$nextVersion-win-x64.msi"

Write-Host "Setup created: $setupPath"
Write-Host "MSI created:   $msiPath"
