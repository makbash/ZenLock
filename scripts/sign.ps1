<#
.SYNOPSIS
  ZenLock.exe'yi kendinden imzalı (self-signed) bir code-signing sertifikasıyla imzalar.

.DESCRIPTION
  Üretim için gerçek bir code-signing sertifikası gerekir; bu script yalnızca imzalama
  hattını kurmak ve test etmek içindir. Kendinden imzalı sertifika, yalnızca sertifika
  Trusted Root + Trusted Publishers deposuna alınan makinelerde "güvenilir" görünür.
  Diğer makinelerde SmartScreen/AV yine uyarabilir (bkz. docs/SIGNING.md).

  signtool gerektirmez; PowerShell'in Set-AuthenticodeSignature'ını kullanır.

.PARAMETER ExePath
  İmzalanacak exe. Varsayılan: C:\Tools\ZenLock\ZenLock.exe

.PARAMETER Subject
  Sertifika subject'i. Varsayılan: CN=ZenLock Dev
#>
param(
    [string]$ExePath = 'C:\Tools\ZenLock\ZenLock.exe',
    [string]$Subject = 'CN=ZenLock Dev'
)

$ErrorActionPreference = 'Stop'

# 1) Sertifikayı bul ya da oluştur (CurrentUser\My).
$cert = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq $Subject -and $_.EnhancedKeyUsageList.FriendlyName -contains 'Code Signing' } |
    Select-Object -First 1

if (-not $cert) {
    Write-Host "Sertifika bulunamadı, oluşturuluyor: $Subject"
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $Subject `
        -CertStoreLocation Cert:\CurrentUser\My -KeyUsage DigitalSignature `
        -NotAfter (Get-Date).AddYears(5)
}
Write-Host "Sertifika: $($cert.Subject)  Thumbprint: $($cert.Thumbprint)"

# 2) Exe'yi imzala (mümkünse zaman damgası ekle).
if (-not (Test-Path $ExePath)) { throw "Bulunamadı: $ExePath" }

$ts = 'http://timestamp.digicert.com'
try {
    Set-AuthenticodeSignature -FilePath $ExePath -Certificate $cert `
        -HashAlgorithm SHA256 -TimestampServer $ts | Out-Null
} catch {
    Write-Warning "Zaman damgası sunucusuna ulaşılamadı, damgasız imzalanıyor."
    Set-AuthenticodeSignature -FilePath $ExePath -Certificate $cert -HashAlgorithm SHA256 | Out-Null
}

# 3) Sonucu göster.
$sig = Get-AuthenticodeSignature -FilePath $ExePath
Write-Host "İmza durumu: $($sig.Status)"
Write-Host "İmzalayan : $($sig.SignerCertificate.Subject)"
