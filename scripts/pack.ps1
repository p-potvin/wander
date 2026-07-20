<#
.SYNOPSIS
  Build a Velopack installer + update package for Wander.Dashboard.

.EXAMPLE
  ./scripts/pack.ps1 -Version 0.1.0
  ./scripts/pack.ps1 -Version 0.2.0 -Channel win-x64

  Output lands in ./releases: Setup.exe (installer), a full/delta .nupkg,
  and RELEASES metadata that the app's UpdateUrl feed points at.

.NOTES
  Requires the vpk tool:  dotnet tool install -g vpk
#>
param(
    [Parameter(Mandatory = $true)] [string]$Version,
    [string]$Runtime = "win-x64",
    [string]$Channel = ""
)

$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
$publishDir = Join-Path $repo "artifacts/publish"
$releasesDir = Join-Path $repo "releases"
$icon = Join-Path $repo "Wander.Dashboard/Assets/wander.ico"

Write-Host "==> Publishing Wander.Dashboard ($Runtime, self-contained)…" -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish (Join-Path $repo "Wander.Dashboard/Wander.Dashboard.csproj") `
    -c Release -r $Runtime --self-contained -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

Write-Host "==> Packing with Velopack ($Version)…" -ForegroundColor Cyan
$packArgs = @(
    "pack",
    "--packId", "VaultWaresWander",
    "--packVersion", $Version,
    "--packDir", $publishDir,
    "--mainExe", "Wander.Dashboard.exe",
    "--packTitle", "VaultWares Wander",
    "--packAuthors", "VaultWares",
    "--icon", $icon,
    "--outputDir", $releasesDir
)
if ($Channel) { $packArgs += @("--channel", $Channel) }

vpk @packArgs
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed" }

Write-Host "==> Done. Artifacts in $releasesDir" -ForegroundColor Green
Get-ChildItem $releasesDir | Select-Object Name, Length
