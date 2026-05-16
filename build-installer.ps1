#Requires -Version 5.1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = Split-Path -Parent $PSCommandPath
$Project = Join-Path $Root 'src\LengLeng.KeyboardLayoutIndicator\LengLeng.KeyboardLayoutIndicator.csproj'
$Artifacts = Join-Path $Root 'artifacts'
$Package = Join-Path $Artifacts 'installer\LengLeng.KeyboardLayoutIndicator'
$App = Join-Path $Package 'app'
$Config = Join-Path $Package 'config'
$Zip = Join-Path $Artifacts 'LengLeng.KeyboardLayoutIndicator-installer.zip'

function Remove-DirectorySafely {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PathToRemove
    )

    if (-not (Test-Path -LiteralPath $PathToRemove)) {
        return
    }

    $resolvedRoot = [System.IO.Path]::GetFullPath($Artifacts)
    $resolvedTarget = [System.IO.Path]::GetFullPath($PathToRemove)
    if (-not $resolvedTarget.StartsWith($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to delete outside artifacts: $resolvedTarget"
    }

    Remove-Item -LiteralPath $resolvedTarget -Recurse -Force
}

Remove-DirectorySafely -PathToRemove $Package
if (Test-Path -LiteralPath $Zip) {
    Remove-Item -LiteralPath $Zip -Force
}

New-Item -ItemType Directory -Force -Path $App, $Config | Out-Null

$nugetSources = & dotnet nuget list source
if ($LASTEXITCODE -eq 0 -and (($nugetSources -join "`n") -notmatch 'api\.nuget\.org')) {
    dotnet nuget add source https://api.nuget.org/v3/index.json -n nuget.org
    if ($LASTEXITCODE -ne 0) {
        throw "Cannot add nuget.org package source. Exit code: $LASTEXITCODE"
    }
}

dotnet publish $Project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $App
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Copy-Item -LiteralPath (Join-Path $Root 'installer\install.ps1') -Destination $Package -Force
Copy-Item -LiteralPath (Join-Path $Root 'installer\uninstall.ps1') -Destination $Package -Force
Copy-Item -LiteralPath (Join-Path $Root 'installer\Install.cmd') -Destination $Package -Force
Copy-Item -LiteralPath (Join-Path $Root 'installer\Uninstall.cmd') -Destination $Package -Force
Copy-Item -LiteralPath (Join-Path $Root 'src\LengLeng.KeyboardLayoutIndicator\appsettings.json') -Destination (Join-Path $Config 'appsettings.json') -Force
Copy-Item -LiteralPath (Join-Path $Root 'README.md') -Destination $Package -Force

Compress-Archive -Path (Join-Path $Package '*') -DestinationPath $Zip -Force

Write-Host "Installer package: $Package"
Write-Host "Installer zip: $Zip"
