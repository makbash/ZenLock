# ZenLock

Seçilen Windows uygulamalarını yalnızca **master şifre** ile başlatılabilir hâle getiren küçük bir tray uygulaması. Şifre doğru değilse uygulama hiç açılmaz; yanlış şifrede uyarı gösterilir.

Zen Browser örneği için yazıldı ama herhangi bir `.exe` için çalışır.

## Nasıl çalışır?

Windows'un **Image File Execution Options (IFEO)** mekanizması kullanılır. Kilitli bir exe (ör. `zen.exe`) çağrıldığında Windows onun yerine ZenLock'u çalıştırır. ZenLock şifre sorar; doğruysa geçidi açıp gerçek uygulamayı başlatır. Bu yöntem **başlamadan önce** engeller — pencere bir an bile görünmez, polling/CPU yükü yoktur.

İki süreç modu vardır:
- **Resident** (tray): logon'da *yönetici* olarak başlar, IFEO anahtarlarını yönetir, named pipe sunucusu açar.
- **Gate**: Windows IFEO debugger olarak bizi çağırdığında kullanıcı seviyesinde çalışır, şifreyi sorar, resident'a doğrulatır.

## Derleme

Gereksinim: .NET 8 SDK, Windows 10/11.

```bat
cd ZenLock
dotnet build -c Release
```

Tek dosyalık yayın (önerilir):

```bat
dotnet publish ZenLock.App -c Release -r win-x64 --self-contained false ^
  -p:PublishSingleFile=true -o C:\Tools\ZenLock
```

Çıktı: `C:\Tools\ZenLock\ZenLock.exe`

> **Code signing önerisi:** IFEO Debugger yazımı antivirüslerin izlediği bir tekniktir. İmzasız exe'de false-positive olabilir. Mümkünse exe'yi imzalayın.

## Kurulum

### 1. İlk yapılandırma (bir kez, yönetici olarak)

`ZenLock.exe`'yi **yönetici olarak** çalıştırın → tray simgesine çift tıklayın → Ayarlar:
1. **Şifre Belirle** ile master şifreyi ayarlayın.
2. **Uygulama Ekle...** ile `zen.exe`'yi seçin (genelde `%LOCALAPPDATA%\zen\zen.exe` veya `C:\Program Files\Zen Browser\zen.exe`).

Şifre ayarlandığı an kilit etkinleşir.

### 2. Açılışta otomatik başlatma (yönetici haklarıyla)

Resident sürecin HKLM'e yazabilmesi için yükseltilmiş başlaması gerekir. UAC sormaması için **Task Scheduler** kullanın (yönetici komut istemi):

```bat
schtasks /Create /TN "ZenLock" /SC ONLOGON /RL HIGHEST ^
  /TR "C:\Tools\ZenLock\ZenLock.exe" /F
```

- `/RL HIGHEST` → en yüksek ayrıcalıklarla (UAC istemeden).
- `/SC ONLOGON` → her oturum açılışında.

Test için hemen çalıştırma: `schtasks /Run /TN "ZenLock"`

### 3. Kaldırma

```bat
schtasks /Delete /TN "ZenLock" /F
"C:\Tools\ZenLock\ZenLock.exe" --uninstall   :: tüm IFEO geçitlerini temizler (yönetici olarak)
```

Ardından `C:\Tools\ZenLock` klasörünü ve `%APPDATA%\ZenLock` ayar klasörünü silebilirsiniz.

## Tehdit modeli (dürüst sınırlar)

ZenLock **"meraklı gözlere"** karşıdır — aynı bilgisayarı kullanan başka biri kilitli uygulamayı açamaz. Ancak:

- Disk üzerindeki tarayıcı profili (geçmiş, çerez) **şifrelenmez**. Gerçek veri koruması için **BitLocker** + ayrı kullanıcı hesabı gerekir.
- Yönetici hakkı olan biri Task Scheduler görevini durdurup IFEO anahtarını silerek kilidi aşabilir. Kendini koruma (servis + watchdog) FAZ 2 kapsamı dışıdır.
- Kilit servisi (resident) çalışmıyorsa, kilitli uygulama **açılmaz** — bu bilinçli bir tasarım (bekçi yoksa kapı kapalı kalır). Bu yüzden Task Scheduler ile güvenilir autostart önemlidir.

## Yol haritası

- **FAZ 1 (MVP, bu sürüm):** IFEO tabanlı başlatma kilidi, master şifre (PBKDF2), DPAPI ile şifreli config, tray + ayarlar.
- **FAZ 2:** DontPanic benzeri panik-gizle — global kısayol tuşuyla açık pencereleri anında gizle, şifreyle geri getir. Altyapı (`Interop/NativeMethods.cs`, `Panic/PanicController.cs`) hazır.

## Proje yapısı

```
ZenLock.App/
├── Program.cs            Mod tespiti (gate / resident / uninstall)
├── ResidentHost.cs       Tray, pipe sunucu, IFEO senkron, auto-relock
├── GateClient.cs         Debugger modu: şifre sor + başlat
├── Auth/                 PasswordHasher (PBKDF2), PasswordDialog
├── Config/               AppConfig, ConfigStore (JSON + DPAPI)
├── Ifeo/IfeoManager.cs   Loop-safe geçit aç/kapat
├── Pipe/PipeProtocol.cs  Gate ↔ resident sözleşmesi
├── Ui/                   SettingsWindow, SetPasswordDialog
├── Interop/              FAZ 2 P/Invoke imzaları
└── Panic/                FAZ 2 yer tutucu
```
