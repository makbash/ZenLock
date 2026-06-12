# Kod İmzalama (Code Signing)

ZenLock, IFEO `Debugger` anahtarı yazdığı için bazı antivirüsler imzasız exe'yi
"şüpheli persistence" olarak işaretleyebilir. Bunun **doğru çözümü kod imzalamadır**
(bkz. AGENTS.md §2 #1) — kaldırmak/gizlemek değil.

## Hızlı imzalama (kendinden imzalı / test)

```powershell
# C:\Tools\ZenLock\ZenLock.exe'yi imzalar (çalışmıyorken).
powershell -ExecutionPolicy Bypass -File scripts\sign.ps1
# veya başka bir exe:
powershell -ExecutionPolicy Bypass -File scripts\sign.ps1 -ExePath "yol\ZenLock.exe"
```

`scripts\sign.ps1`:
- `CN=ZenLock Dev` subject'li bir code-signing sertifikası yoksa **oluşturur**
  (CurrentUser\My, 5 yıl), varsa onu kullanır.
- `Set-AuthenticodeSignature` ile imzalar (signtool gerekmez), SHA-256, mümkünse zaman damgası.

> Çalışan resident exe'yi kilitler. İmzalamadan önce ZenLock'u durdurun
> (tray → Çıkış veya `taskkill /IM ZenLock.exe`), imzalayın, sonra yeniden başlatın.
> En temizi: `dotnet publish` sonrası, dağıtmadan önce imzalamak.

## Kendinden imzalı sertifikanın sınırı

`Get-AuthenticodeSignature` durumu **kendinden imzalı** sertifikada `UnknownError`
("güven zinciri kurulu değil") döner — imza **gömülüdür** ama makine sertifikaya güvenmez.

### Yerelde "Valid" yapmak (yalnızca test makinesi)

Sertifikayı Trusted Root + Trusted Publishers depolarına alın (yönetici PowerShell):

```powershell
$c = Get-ChildItem Cert:\CurrentUser\My | Where-Object Subject -eq 'CN=ZenLock Dev' | Select-Object -First 1
$pwd = ConvertTo-SecureString 'temp' -AsPlainText -Force
Export-PfxCertificate -Cert $c -FilePath "$env:TEMP\zenlock.pfx" -Password $pwd | Out-Null
Export-Certificate  -Cert $c -FilePath "$env:TEMP\zenlock.cer" | Out-Null
Import-Certificate -FilePath "$env:TEMP\zenlock.cer" -CertStoreLocation Cert:\LocalMachine\Root
Import-Certificate -FilePath "$env:TEMP\zenlock.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPublisher
```

Bundan sonra imza o makinede `Valid` görünür. **Başka makinelerde** bu güven yoktur;
SmartScreen/AV yine uyarabilir.

## Üretim

Gerçek dağıtım için bir **OV/EV code-signing sertifikası** alın (DigiCert, Sectigo vb.).
Aynı `sign.ps1` mantığı geçerli; `New-SelfSignedCertificate` yerine satın alınan
sertifikayı (PFX'ten import edilmiş) kullanın. EV sertifikalar SmartScreen itibarını
anında sağlar.
