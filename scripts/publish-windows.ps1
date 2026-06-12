# One-shot publish: React web + StoreAPI + StorePOS in a single folder.
# Run StorePOS.exe from the output folder - it starts StoreAPI (and the LAN web app) automatically.
param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",
    [switch] $SkipWebBuild
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$tfm = "net10.0-windows10.0.19041.0"
$r = "win-x64"
$storeWeb = Join-Path $root "StoreWeb"
$publishOut = Join-Path $root "StorePOS\bin\$Configuration\$tfm\$r\publish"
$distOut = Join-Path $root "dist\StorePOS"

function Test-PublishOutput {
    param([string] $Folder)
    $missing = @()
    foreach ($rel in @("StorePOS.exe", "StoreAPI.exe", "browserwww\index.html")) {
        if (-not (Test-Path (Join-Path $Folder $rel))) {
            $missing += $rel
        }
    }
    if ($missing.Count -gt 0) {
        throw "Publish folder is incomplete. Missing: $($missing -join ', ')"
    }
}

Write-Host "=== Kaif Store - full publish ===" -ForegroundColor Cyan
Write-Host ""

$running = Get-Process -Name "StorePOS", "StoreAPI" -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "WARNING: StorePOS or StoreAPI is still running. Close them first or the build may fail." -ForegroundColor Yellow
    Write-Host ""
}

if (-not $SkipWebBuild) {
    Write-Host "[1/3] Building React web app (StoreWeb)..." -ForegroundColor Green
    if (-not (Test-Path (Join-Path $storeWeb "package.json"))) {
        throw "StoreWeb\package.json not found."
    }
    Push-Location $storeWeb
    try {
        if (Test-Path "package-lock.json") {
            npm ci
        }
        else {
            npm install
        }
        npm run build
        if (-not (Test-Path "dist\index.html")) {
            throw "StoreWeb build did not produce dist\index.html"
        }
    }
    finally {
        Pop-Location
    }
    Write-Host "      Web build OK." -ForegroundColor DarkGray
}
else {
    Write-Host "[1/3] Skipping web build (-SkipWebBuild)." -ForegroundColor Yellow
    if (-not (Test-Path (Join-Path $storeWeb "dist\index.html"))) {
        throw 'No StoreWeb\dist\index.html - run without -SkipWebBuild first.'
    }
}

Write-Host "[2/3] Publishing StorePOS + StoreAPI (self-contained, win-x64)..." -ForegroundColor Green
dotnet publish "$root\StorePOS\StorePOS.csproj" `
    -f $tfm `
    -c $Configuration `
    -r $r `
    --self-contained true `
    /p:WindowsPackageType=None `
    /p:BuildReact=false

Test-PublishOutput -Folder $publishOut

Write-Host "[3/3] Copying to dist\StorePOS..." -ForegroundColor Green
if (Test-Path $distOut) {
    Remove-Item -Recurse -Force $distOut
}
New-Item -ItemType Directory -Force -Path $distOut | Out-Null
Copy-Item -Path (Join-Path $publishOut "*") -Destination $distOut -Recurse -Force

Test-PublishOutput -Folder $distOut

Write-Host ""
Write-Host "=== Done ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Portable folder (recommended):"
Write-Host "    $distOut"
Write-Host ""
Write-Host "  Build output (same files):"
Write-Host "    $publishOut"
Write-Host ""
Write-Host "How to run:"
Write-Host "  1. Open the folder above"
Write-Host "  2. Double-click StorePOS.exe"
Write-Host "     -> StoreAPI starts automatically on port 5050"
Write-Host "     -> LAN web app: http://<this-PC-IP>:5050"
Write-Host ""
Write-Host "RunStore.bat is optional (same as StorePOS.exe)."
Write-Host ""
Write-Host "macOS build (run on a Mac):"
Write-Host "  scripts/publish-macos.sh  -> Store POS.app in dist/StorePOS-macOS"
Write-Host ""
Write-Host "Other installers:"
Write-Host "  scripts\publish-windows-user-install.ps1  -> zip + Install-StorePOS.ps1"
Write-Host "  scripts\publish-windows-msix.ps1            -> signed .msix"
