#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Allows inbound TCP 5050 for StoreAPI (LAN access to API + Blazor web dashboard).

.NOTES
  Run once on the POS PC as Administrator.
  Service install (example):
    sc.exe create StoreAPI binPath= "C:\Path\To\StoreAPI.exe" start= auto
    sc.exe description StoreAPI "Clothing Store API and web dashboard"
    sc.exe start StoreAPI
#>

$ruleName = "StoreAPI TCP 5050"
$existing = Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Firewall rule '$ruleName' already exists."
    exit 0
}

New-NetFirewallRule -DisplayName $ruleName -Direction Inbound `
    -Protocol TCP -LocalPort 5050 -Action Allow

Write-Host "Created firewall rule: $ruleName"
