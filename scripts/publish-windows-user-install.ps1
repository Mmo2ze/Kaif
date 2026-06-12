# Build portable output via publish-windows.ps1, then zip with Install-StorePOS.ps1.
param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",
    [switch] $SkipWebBuild
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

& (Join-Path $PSScriptRoot "publish-windows.ps1") -Configuration $Configuration -SkipWebBuild:$SkipWebBuild

$publishOut = Join-Path $root "dist\StorePOS"
$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force -Path $dist | Out-Null

$ver = "1.0"
$csprojText = Get-Content -Raw "$root\StorePOS\StorePOS.csproj"
if ($csprojText -match '<ApplicationDisplayVersion>\s*([^<]+?)\s*</ApplicationDisplayVersion>') {
    $ver = $Matches[1].Trim()
}

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$zipName = "StorePOS-$ver-userinstall-$stamp.zip"
$zipPath = Join-Path $dist $zipName

$staging = Join-Path $env:TEMP "storepos-userinstall-$stamp"
if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }
New-Item -ItemType Directory -Path $staging | Out-Null

Copy-Item -Path (Join-Path $publishOut "*") -Destination $staging -Recurse -Force
Copy-Item -Path (Join-Path $root "scripts\install\Install-StorePOS.ps1") -Destination $staging -Force

if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
Compress-Archive -Path (Join-Path $staging "*") -DestinationPath $zipPath
Remove-Item -Recurse -Force $staging

Write-Host ""
Write-Host "Installer zip:"
Write-Host "  $zipPath"
