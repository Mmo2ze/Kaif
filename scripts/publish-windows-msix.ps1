# Build signed MSIX for Windows (install via double-click after trusting the signing cert).
# Close StorePOS / StoreAPI before running — they lock files under publish\.
#
# First-time: creates a dev cert with Subject CN=Store POS Dev (must match Platforms\Windows\Package.appxmanifest Identity Publisher).
# For production, use a real code-signing cert and pass -CertificateThumbprint, and update the manifest Publisher to match the cert Subject.
#
# See: https://learn.microsoft.com/en-us/dotnet/maui/windows/deployment/publish-cli?view=net-maui-10.0
param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",
    [string] $CertificateThumbprint = "",
    [string] $PublisherCn = "Store POS Dev"
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$tfm = "net10.0-windows10.0.19041.0"
$r = "win-x64"

if ([string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    $existing = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq "CN=$PublisherCn" } | Select-Object -First 1
    if ($existing) {
        $CertificateThumbprint = $existing.Thumbprint
        Write-Host "Using existing certificate $CertificateThumbprint ($($existing.Subject))"
    }
    else {
        Write-Host "Creating self-signed certificate CN=$PublisherCn in CurrentUser\My..."
        $cert = New-SelfSignedCertificate -Type Custom `
            -Subject "CN=$PublisherCn" `
            -KeyUsage DigitalSignature `
            -FriendlyName "Store POS MSIX (dev)" `
            -CertStoreLocation "Cert:\CurrentUser\My" `
            -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
        $CertificateThumbprint = $cert.Thumbprint
        Write-Host "Thumbprint: $CertificateThumbprint"
        Write-Host "Other PCs must trust this cert (Trusted People) before Install is enabled on the .msix — see Microsoft docs link in script header."
    }
}

Write-Host "Publishing StoreAPI ($Configuration, $r, self-contained)..."
dotnet publish "$root\StoreAPI\StoreAPI.csproj" -c $Configuration -r $r --self-contained true

Write-Host "Publishing StorePOS as MSIX (merged API)..."
dotnet publish "$root\StorePOS\StorePOS.csproj" -f $tfm -c $Configuration -r $r --self-contained true `
    -p:RuntimeIdentifierOverride=$r `
    -p:WindowsPackageType=MSIX `
    -p:AppxPackageSigningEnabled=true `
    -p:PackageCertificateThumbprint=$CertificateThumbprint

$appPackages = Join-Path $root "StorePOS\bin\$Configuration\$tfm\$r\AppPackages"
if (-not (Test-Path $appPackages)) {
    Write-Warning "AppPackages folder not found at $appPackages — check build output for errors."
    exit 1
}

$msix = Get-ChildItem -Path $appPackages -Filter "*.msix" -Recurse -File | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $msix) {
    Write-Warning "No .msix file found under $appPackages"
    exit 1
}

Write-Host ""
Write-Host "MSIX package:"
Write-Host "  $($msix.FullName)"
Write-Host ""
Write-Host "Install: trust the signing certificate if needed, then open the .msix."
