[CmdletBinding()]
param(
    [ValidateRange(1, 20)]
    [int]$ValidityYears = 10
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$signingModule = Join-Path $PSScriptRoot 'PinkieCodeSigning.ps1'
. $signingModule

$certificate = Get-PinkieCodeSigningCertificate
if ($null -eq $certificate) {
    Write-Host 'Creating a local PinkieSysMon code-signing certificate...'
    $certificate = New-PinkieLocalCodeSigningCertificate -ValidityYears $ValidityYears
} else {
    Write-Host 'Reusing the existing local PinkieSysMon code-signing certificate.'
}

$tempCertificate = Join-Path ([System.IO.Path]::GetTempPath()) ("PinkieSysMon-{0}.cer" -f $certificate.Thumbprint)
try {
    Export-Certificate -Cert $certificate -FilePath $tempCertificate -Force | Out-Null

    Import-Certificate -FilePath $tempCertificate -CertStoreLocation 'Cert:\CurrentUser\Root' | Out-Null
    Import-Certificate -FilePath $tempCertificate -CertStoreLocation 'Cert:\CurrentUser\TrustedPublisher' | Out-Null
} finally {
    Remove-Item -LiteralPath $tempCertificate -Force -ErrorAction SilentlyContinue
}

$rootCopy = Get-ChildItem -Path 'Cert:\CurrentUser\Root' |
    Where-Object { $_.Thumbprint -eq $certificate.Thumbprint } |
    Select-Object -First 1
$publisherCopy = Get-ChildItem -Path 'Cert:\CurrentUser\TrustedPublisher' |
    Where-Object { $_.Thumbprint -eq $certificate.Thumbprint } |
    Select-Object -First 1

# Verify the exact discovery path used later by build.ps1. This catches
# certificate-provider compatibility problems during one-time setup instead of
# silently leaving subsequent builds unsigned.
$discoveredCertificate = Get-PinkieCodeSigningCertificate -Thumbprint $certificate.Thumbprint
if ($null -eq $discoveredCertificate) {
    throw 'The PinkieSysMon signing certificate was created but cannot be rediscovered as a usable code-signing certificate.'
}

if ($null -eq $rootCopy -or $null -eq $publisherCopy) {
    throw 'The PinkieSysMon signing certificate was not installed into both CurrentUser Root and TrustedPublisher stores.'
}

$chain = [System.Security.Cryptography.X509Certificates.X509Chain]::new()
try {
    $chain.ChainPolicy.RevocationMode = [System.Security.Cryptography.X509Certificates.X509RevocationMode]::NoCheck
    if (-not $chain.Build($certificate)) {
        $errors = @($chain.ChainStatus | ForEach-Object { $_.StatusInformation.Trim() }) -join '; '
        throw "The local signing certificate does not build to a trusted chain: $errors"
    }
} finally {
    $chain.Dispose()
}

Write-Host ''
Write-Host 'PinkieSysMon local code signing is ready.'
Write-Host "Subject:    $($certificate.Subject)"
Write-Host "Thumbprint: $($certificate.Thumbprint)"
Write-Host "Expires:    $($certificate.NotAfter.ToString('u'))"
Write-Host 'Private key: CurrentUser\My, non-exportable'
Write-Host 'Trust:       CurrentUser\Root + CurrentUser\TrustedPublisher'
Write-Host 'Discovery:   OK (CurrentUser\My -CodeSigningCert)'
Write-Host ''
Write-Host "Run '$root\build.ps1' to produce locally trusted signed PinkieSysMon binaries."
Write-Host 'For a CA-issued certificate later, set PINKIESYSMON_SIGNING_THUMBPRINT to its thumbprint.'
