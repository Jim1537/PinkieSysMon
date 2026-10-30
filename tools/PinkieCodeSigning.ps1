$script:PinkieSigningSubject = 'CN=PinkieSysMon Project'
$script:PinkieSigningFriendlyName = 'PinkieSysMon Local Code Signing'

function Test-PinkieCodeSigningCertificate {
    param(
        [Parameter(Mandatory = $true)]
        $Certificate
    )

    if (-not $Certificate.HasPrivateKey) {
        return $false
    }

    $now = Get-Date
    if ($Certificate.NotBefore -gt $now -or $Certificate.NotAfter -le $now) {
        return $false
    }

    # Code-signing authority is established by the Certificate provider when the
    # certificate is selected with -CodeSigningCert. Keep this helper focused on
    # the remaining properties that still need explicit validation.
    return $true
}

function Get-PinkieCodeSigningCertificate {
    param(
        [string]$Thumbprint
    )

    # Let the Windows Certificate provider perform the EKU/private-key filter.
    # Microsoft documents -CodeSigningCert specifically for retrieving certificates
    # suitable for Set-AuthenticodeSignature. Avoid re-parsing EnhancedKeyUsageList
    # because its projected object shape differs across PowerShell/provider versions.
    $certificates = @(Get-ChildItem -Path 'Cert:\CurrentUser\My' -CodeSigningCert -ErrorAction Stop)

    if (-not [string]::IsNullOrWhiteSpace($Thumbprint)) {
        $normalized = ($Thumbprint -replace '\s', '').ToUpperInvariant()
        $certificate = $certificates |
            Where-Object { $_.Thumbprint.ToUpperInvariant() -eq $normalized } |
            Select-Object -First 1

        if ($null -eq $certificate) {
            throw "Configured code-signing certificate was not found in Cert:\CurrentUser\My: $normalized"
        }

        if (-not (Test-PinkieCodeSigningCertificate -Certificate $certificate)) {
            throw "Configured certificate is not a currently valid code-signing certificate with a private key: $normalized"
        }

        return $certificate
    }

    return $certificates |
        Where-Object {
            $_.Subject -eq $script:PinkieSigningSubject -and
            (Test-PinkieCodeSigningCertificate -Certificate $_)
        } |
        Sort-Object -Property NotAfter -Descending |
        Select-Object -First 1
}

function New-PinkieLocalCodeSigningCertificate {
    param(
        [ValidateRange(1, 20)]
        [int]$ValidityYears = 10
    )

    if (-not (Get-Command New-SelfSignedCertificate -ErrorAction SilentlyContinue)) {
        throw 'New-SelfSignedCertificate is unavailable. Run this setup on Windows with the PKI module installed.'
    }

    $params = @{
        Type = 'CodeSigningCert'
        Subject = $script:PinkieSigningSubject
        FriendlyName = $script:PinkieSigningFriendlyName
        CertStoreLocation = 'Cert:\CurrentUser\My'
        KeyAlgorithm = 'RSA'
        KeyLength = 3072
        HashAlgorithm = 'SHA256'
        KeyExportPolicy = 'NonExportable'
        KeyUsage = 'DigitalSignature'
        NotAfter = (Get-Date).AddYears($ValidityYears)
    }

    return New-SelfSignedCertificate @params
}

function Invoke-PinkieArtifactSigning {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Path,

        [Parameter(Mandatory = $true)]
        $Certificate,

        [string]$TimestampServer
    )

    if (-not (Get-Command Set-AuthenticodeSignature -ErrorAction SilentlyContinue)) {
        throw 'Set-AuthenticodeSignature is unavailable in this PowerShell environment.'
    }

    foreach ($file in $Path) {
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
            throw "Code-signing input does not exist: $file"
        }

        $signParams = @{
            FilePath = $file
            Certificate = $Certificate
            HashAlgorithm = 'SHA256'
        }

        if (-not [string]::IsNullOrWhiteSpace($TimestampServer)) {
            $signParams.TimestampServer = $TimestampServer
        }

        $result = Set-AuthenticodeSignature @signParams
        if ($result.Status -ne 'Valid') {
            throw "Authenticode signing failed for '$file': $($result.Status) - $($result.StatusMessage)"
        }

        $verification = Get-AuthenticodeSignature -LiteralPath $file
        if ($verification.Status -ne 'Valid') {
            throw "Authenticode verification failed for '$file': $($verification.Status) - $($verification.StatusMessage)"
        }

        if ($null -eq $verification.SignerCertificate -or
            $verification.SignerCertificate.Thumbprint -ne $Certificate.Thumbprint) {
            throw "Authenticode verification used an unexpected signer for '$file'."
        }
    }
}
