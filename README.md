# ZenLock

Seçilen Windows uygulamalarını yalnızca **master şifre** ile başlatılabilir hâle getiren
küçük bir tray uygulaması. Şifre doğru değilse uygulama hiç açılmaz. Ayrıca bir **panik
kısayolu** ile kilitli uygulamaların açık pencerelerini anında gizleyip şifreyle geri getirir.

Zen Browser örneği için yazıldı ama herhangi bir `.exe` için çalışır.

## Özellikler

- **Başlatma kilidi** — kilitli bir exe çağrıldığında, pencere açılmadan önce şifre sorulur.
- **Panik-gizle** — global kısayol (varsayılan Ctrl+Alt+Q): kilitli uygulamaların pencerelerini
  gizler, sesi susturur; tekrar basınca şifreyle geri getirir. Kısayol yapılandırılabilir.
- **Oturumda bir kez** — doğru şifre girilince o uygulama oturum boyunca tekrar sormaz.
- **Yeniden kilitle** — ekran kilidinde (Win+L) ve boşta kalınca muafiyet sıfırlanır.
- **Tray gizleme** — ikon gizlenebilir; Ayarlar'a `Ctrl+Alt+Shift+S` ya da `ZenLock.exe --settings`
  ile erişilir. Ayarlar/Çıkış şifre korumalıdır.
- **Şifre kurtarma** — unutulursa `ZenLock.exe --reset` (yönetici).

## Nasıl çalışır?

Windows'un **Image File Execution Options (IFEO)** mekanizması kullanılır. Kilitli bir exe
(ör. `zen.exe`) çağrıldığında Windows onun yerine ZenLock'u çalıştırır. ZenLock şifre sorar;
doğruysa geçidi açıp gerçek uygulamayı başlatır. Bu yöntem **başlamadan önce** engeller —
pencere bir an bile görünmez, polling/CPU yükü yoktur.

Kripto: master şifre **PBKDF2/SHA-256 (600k iterasyon, salt'lı)** ile saklanır; config
`%APPDATA%\ZenLock\config.dat` içinde **DPAPI (CurrentUser)** ile şifrelenir. Düz metin şifre
hiçbir yere yazılmaz.

Süreç modları: **Resident** (tray, logon'da yönetici olarak başlar, IFEO + pipe + panik),
**Gate** (IFEO debugger çağrısı; şifre sorar), ayrıca `--settings`, `--uninstall`, `--reset`.

## Derleme

Gereksinim: .NET 8 SDK, Windows 10/11.

```bat
dotnet publish ZenLock.App -c Release -r win-x64 --self-contained false ^
  -p:PublishSingleFile=true -o publish
```

Çıktı: `publish\ZenLock.exe`

## Kurulum

### Seçenek A — Installer (önerilir)

[Inno Setup](https://jrsoftware.org/isinfo.php) ile `installer\ZenLock.iss` derlenir; çıkan
`ZenLockSetup.exe` dosyayı kurar, logon görevini ve `--settings` masaüstü kısayolunu oluşturur.

```bat
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\ZenLock.iss
```

### Seçenek B — Manuel

1. `publish\ZenLock.exe`'yi bir klasöre kopyalayın (ör. `C:\Tools\ZenLock`).
2. Logon'da yönetici haklarıyla otomatik başlatma (UAC sormadan):
   ```bat
   schtasks /Create /TN "ZenLock" /SC ONLOGON /RL HIGHEST /TR "C:\Tools\ZenLock\ZenLock.exe" /F
   ```
3. `ZenLock.exe`'yi yönetici olarak çalıştırın → tray → Ayarlar → **Şifre Belirle** + **Uygulama Ekle**.

### İlk yapılandırma

Tray simgesine çift tıklayın → Ayarlar:
1. **Şifre Belirle** ile master şifreyi ayarlayın (kilit ancak şifre kurulunca etkinleşir).
2. **Uygulama Ekle...** ile kilitlenecek exe'yi seçin (ör. `C:\Program Files\Zen Browser\zen.exe`).
3. (İsteğe bağlı) Panik tuşunu değiştirin, tray'i gizleyin, boşta yeniden kilit süresini ayarlayın.

## Kaldırma

```bat
schtasks /Delete /TN "ZenLock" /F
"C:\Tools\ZenLock\ZenLock.exe" --uninstall   :: tüm IFEO geçitlerini temizler (yönetici)
```

Ardından kurulum klasörünü ve `%APPDATA%\ZenLock` ayar klasörünü silebilirsiniz.

## Kod imzalama

IFEO `Debugger` yazımı, antivirüslerin izlediği bir tekniktir; **imzasız** exe'de false-positive
olabilir. Çözüm imzalamadır — bkz. [docs/SIGNING.md](docs/SIGNING.md) ve `scripts/sign.ps1`.

## Tehdit modeli (dürüst sınırlar)

ZenLock **"meraklı gözlere"** karşıdır — aynı bilgisayarı kullanan **yönetici olmayan** biri
kilitli uygulamayı açamaz. Ancak:

- Disk üzerindeki veri (tarayıcı profili, çerez) **şifrelenmez**. Gerçek veri koruması için
  **BitLocker** + ayrı kullanıcı hesabı gerekir.
- **Yönetici** hakkı olan biri görevi durdurup IFEO anahtarını silerek kilidi aşabilir.
  Kendini koruma kapsam dışıdır.
- Kilit servisi (resident) çalışmıyorsa kilitli uygulama **açılmaz** — bilinçli tasarım
  (bekçi yoksa kapı kapalı). Bu yüzden güvenilir autostart önemlidir.

## Proje yapısı

```
ZenLock.App/
├── Program.cs            Mod tespiti (gate / resident / uninstall / settings / reset)
├── ResidentHost.cs       Tray, pipe sunucu, IFEO senkron, panik, yeniden kilit
├── GateClient.cs         Debugger modu: şifre sor + başlat; --settings sinyali
├── Auth/                 PasswordHasher (PBKDF2), PasswordDialog
├── Config/               AppConfig, ConfigStore (JSON + DPAPI)
├── Ifeo/IfeoManager.cs   Loop-safe geçit aç/kapat
├── Pipe/PipeProtocol.cs  Gate ↔ resident sözleşmesi (+ güvenli pipe ACL)
├── Ui/                   SettingsWindow, SetPasswordDialog
├── Interop/              P/Invoke (hotkey, ShowWindow, WinEvent, idle, mute)
└── Panic/                PanicController (panik-gizle)
installer/ZenLock.iss     Inno Setup kurulum scripti
scripts/sign.ps1          Kod imzalama (self-signed test)
docs/SIGNING.md           İmzalama belgesi
```

Mimari kararlar, invariant'lar ve geliştirme notları için [AGENTS.md](AGENTS.md).

## Lisans

[MIT](LICENSE)
