# Per-user install: copies this folder to %LocalAppData%\Programs\StorePOS and adds shortcuts.
# Usage: extract the zip, then run:
#   powershell -ExecutionPolicy Bypass -File .\Install-StorePOS.ps1
param(
    [string] $InstallDir = (Join-Path $env:LOCALAPPDATA "Programs\StorePOS")
)

$ErrorActionPreference = "Stop"
$source = $PSScriptRoot

if (-not (Test-Path (Join-Path $source "StorePOS.exe"))) {
    Write-Error "StorePOS.exe not found next to this script. Extract the full zip so Install-StorePOS.ps1 sits beside StorePOS.exe."
}

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null

Get-ChildItem -Path $source -Force | ForEach-Object {
    if ($_.Name -ieq "Install-StorePOS.ps1") { return }
    $dest = Join-Path $InstallDir $_.Name
    Copy-Item -LiteralPath $_.FullName -Destination $dest -Recurse -Force
}

$shell = New-Object -ComObject WScript.Shell
$startMenuPrograms = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
$startLnk = Join-Path $startMenuPrograms "Store POS.lnk"
$sc = $shell.CreateShortcut($startLnk)
$sc.TargetPath = Join-Path $InstallDir "StorePOS.exe"
$sc.WorkingDirectory = $InstallDir
$sc.Description = "Store POS"
$sc.Save()

$desktop = [Environment]::GetFolderPath("Desktop")
$deskLnk = Join-Path $desktop "Store POS.lnk"
$sc2 = $shell.CreateShortcut($deskLnk)
$sc2.TargetPath = Join-Path $InstallDir "StorePOS.exe"
$sc2.WorkingDirectory = $InstallDir
$sc2.Description = "Store POS"
$sc2.Save()

Write-Host "Installed to: $InstallDir"
Write-Host "Shortcuts: Start menu and Desktop (Store POS). Run StorePOS.exe or RunStore.bat from that folder."
