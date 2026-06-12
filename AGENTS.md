# ZenLock — Agent Implementation Spec

> Bu dosya, ZenLock üzerinde çalışacak bir kodlama agent'ı içindir. Projeyi devralmadan
> önce bu belgeyi baştan sona oku. Mimari kararlar, **bozulmaması gereken invariant'lar**
> ve Faz 2 yol haritası burada. Kod yazmadan önce "Çalışma konvansiyonları" ve
> "Açık sorular" bölümlerine uy.

## 1. Amaç

Seçilen Windows uygulamalarını (ör. `zen.exe`) yalnızca master şifre ile başlatılabilir
hâle getiren tek-exe'lik bir tray uygulaması. Şifre doğru değilse uygulama **hiç açılmaz**;
3. yanlış denemede uyarı gösterilir, iptal sessizce kapatır.

Tehdit modeli: **aynı bilgisayarı kullanan meraklı kişiler**. Hedeflenmeyen: yönetici
yetkili saldırgan, disk üzerindeki veri gizliliği (o BitLocker'ın işi).

## 2. Plan değerlendirmesi

### Güçlü yönler (koru)
- **IFEO seçimi doğru.** Sürücü yazmadan, kullanıcı modunda *başlamadan önce* engelleyen
  tek yöntem. Pencere flaş'lamaz, polling/CPU yükü yoktur. ETW/WMI alternatifleri reaktiftir
  (process zaten başlamıştır) — bu yüzden tercih edilmedi.
- **Tek resident süreçte birleşik mimari.** Auth, config, tray ve yaşam döngüsü Faz 2
  (panik-gizle) tarafından yeniden kullanılacak şekilde tasarlandı.
- **Loop-safe geçit.** Sayaç tabanlı `OpenGate`/`CloseGate` + 8 sn auto-relock güvenlik ağı.
- **Sağlam kripto.** PBKDF2/SHA-256 600k iterasyon, salt'lı; config DPAPI (CurrentUser) ile şifreli.
- **Fail-safe.** Resident çalışmıyorsa kilitli uygulama açılmaz (bekçi yoksa kapı kapalı).

### Bilinen riskler (bug DEĞİL — bunları "düzeltmeye" çalışma, gerekirse kullanıcıya danış)
1. **AV false-positive.** IFEO `Debugger` yazımı klasik malware persistence tekniğidir.
   Çözüm bug fix değil, **code signing**. Agent bunu kaldırmaya/gizlemeye çalışmamalı.
2. **Mikro yarış penceresi.** `OpenGate` ile `CloseGate` arasında Debugger değeri yokken
   aynı exe'nin başka bir başlatması geçidi atlar. Tehdit modeli için kabul edilebilir.
   Sıfırlamak için karmaşıklık eklemeden önce kullanıcıya sor.
3. **argv ayrıştırması.** IFEO bizi `ZenLock.exe "<hedef tam yol>" <orijinal args>` ile
   çağırır. `Program.Main` `argv[0]`'ı hedef yol, `argv[1..]`'i argümanlar varsayar.
   **Varsayılan tarayıcı senaryosu** (link tıklama → `zen.exe <url>`) ve dosya
   ilişkilendirmeleri TEST EDİLMELİ — arg yönlendirme/tırnak doğru çalışıyor mu?
4. **Pipe ACL → ÇÖZÜLDÜ (2026-06-12).** Elevated resident'ın varsayılan DACL'i, normal
   IL gate'in bağlanmasını engelliyordu ("kilit servisi çalışmıyor" uyarısı şifre girişinden
   sonra). Çözüm: `PipeProtocol.CreateServerStream()` artık açık `PipeSecurity` ile pipe
   açıyor (AuthenticatedUsers → ReadWrite + CreateNewInstance). Resident bu fabrikayı kullanır.
5. **Self-protection yok.** Yönetici görevi durdurup anahtarı silebilir. Tasarım gereği
   kapsam dışı. Gold-plating yapma.

## 3. Mimari

Tek exe, üç mod (`Program.Main` ayrıştırır):

| Mod | Tetikleyici | IL | Sorumluluk |
|-----|-------------|----|-----|
| **Resident** | argümansız (logon'da Task Scheduler) | Elevated | Tray, IFEO senkron, pipe sunucu, geçit aç/kapat |
| **Gate** | `argv[0]` = kilitli exe (Windows IFEO debugger çağrısı) | Kullanıcı | Şifre sor, resident'a doğrulat, hedefi başlat |
| **Uninstall** | `--uninstall` | Elevated | Tüm IFEO geçitlerini temizle |

### IFEO loop çözümü (kritik invariant)
`HKLM\...\Image File Execution Options\<exe>\Debugger` = `ZenLock.exe` yolu.
Debugger değeri varken hedefi başlatmak **yine bizi tetikler** (sonsuz döngü). Çözüm:
1. Gate şifreyi resident'a yollar (`unlock`).
2. Resident şifreyi doğrular, **`OpenGate`** ile Debugger değerini geçici siler, `ok:true` döner.
3. Gate hedefi başlatır (artık Debugger yok → loop yok), `relock` yollar.
4. Resident **`CloseGate`** ile Debugger değerini geri yazar.
5. Gate çökerse 8 sn sonra `ForceClose` geçidi geri kapatır.

> **Asla:** Gate, Debugger değeri varken hedef exe'yi `Process.Start` etmemeli (loop).
> Başlatma sadece resident geçidi açtığını onayladıktan sonra yapılır.

## 4. Bileşen haritası

```
ZenLock.App/
├── Program.cs            Mod tespiti (gate / resident / uninstall)
├── ResidentHost.cs       Tray, pipe sunucu, IFEO senkron, auto-relock, singleton mutex
├── GateClient.cs         Debugger modu: şifre dialog + başlat + relock
├── Auth/
│   ├── PasswordHasher.cs  PBKDF2 + salt, sabit zamanlı Verify
│   └── PasswordDialog.*   Kilit açma penceresi (3 deneme, iptal sessiz)
├── Config/
│   ├── AppConfig.cs       PasswordHash/Salt + Apps[]
│   └── ConfigStore.cs     %APPDATA%\ZenLock\config.dat — JSON + DPAPI, atomik yazım
├── Ifeo/IfeoManager.cs    Install/Uninstall/Open/Close/ForceClose (sayaç tabanlı)
├── Pipe/PipeProtocol.cs   PipeName (SID'e özel) + UnlockRequest/UnlockResponse
├── Ui/
│   ├── SettingsWindow.*    Uygulama ekle/çıkar, şifre belirle
│   └── SetPasswordDialog.* Şifre belirle/değiştir (min 4, eşleşme kontrolü)
├── Interop/NativeMethods.cs  FAZ 2 P/Invoke (hotkey, ShowWindow, EnumWindows) — hazır
└── Panic/PanicController.cs  FAZ 2 yer tutucu
```

## 5. Pipe protokolü (sözleşme — değiştirirken iki tarafı da güncelle)

Satır bazlı JSON, `PipeProtocol.PipeName` (kullanıcı SID'ine özel).

```
Gate → Resident:  { "Op": "unlock", "Exe": "zen.exe", "Password": "..." }
Resident → Gate:  { "Ok": true,  "Reason": "" }            // şifre doğru, geçit açıldı
                  { "Ok": false, "Reason": "badpass" }      // yanlış şifre
                  { "Ok": false, "Reason": "nopassword" }   // şifre kurulu değil → yine de aç

Gate → Resident:  { "Op": "relock", "Exe": "zen.exe" }
Resident → Gate:  { "Ok": true,  "Reason": "" }
```

## 6. Derleme / kurulum / test

```bat
:: Derleme
dotnet build -c Release

:: Tek dosya yayın
dotnet publish ZenLock.App -c Release -r win-x64 --self-contained false ^
  -p:PublishSingleFile=true -o C:\Tools\ZenLock

:: Elevated autostart (UAC sormadan)
schtasks /Create /TN "ZenLock" /SC ONLOGON /RL HIGHEST /TR "C:\Tools\ZenLock\ZenLock.exe" /F

:: Kaldırma
schtasks /Delete /TN "ZenLock" /F
"C:\Tools\ZenLock\ZenLock.exe" --uninstall
```

> Derleme ortamı **Windows** olmalı (WPF). `net8.0-windows`, .NET 8 SDK.

## 7. Çalışma konvansiyonları (Mustafa'nın çalışma tarzı — uy)

- **Önce plan, sonra kod.** Karar gerektiren noktada kod yazmadan önce planı paylaş ve onay al.
- **Karar vermeden sor.** Belirsizlikte varsayım yapıp ilerleme; "Açık sorular"a bak.
- **Fazlı geliştirme.** İş Faz 1/2/3 olarak ilerler, net TODO listeleriyle.
- **Gereksiz log yok**, özellikle döngülerde; debug logları testten sonra temizle.
- **Aşırı mühendislik yok.** Çalışan çözüm > ideal çözüm; karmaşıklığı (ör. self-protection)
  gerçekten gerekene kadar erteleme.
- Kod tanımlayıcıları İngilizce, yorum/UI metinleri Türkçe (mevcut stil korunsun).

## 8. Regresyona sokma (invariant'lar)

- [ ] Gate, geçit açık olduğu onayı gelmeden hedef exe'yi başlatmaz (loop koruması).
- [ ] Şifre düz metin olarak hiçbir yere yazılmaz; sadece PBKDF2 hash + salt saklanır.
- [ ] Config her zaman DPAPI (CurrentUser) ile şifreli yazılır.
- [ ] Şifre kurulu DEĞİLKEN geçit kurulmaz (kullanıcı kendini kilitlemesin).
- [ ] Resident'a ulaşılamıyorsa gate uygulamayı AÇMAZ (fail-safe) — sadece uyarır.
- [ ] HKLM yazan tüm yollar yalnızca resident (elevated) süreçten çağrılır.
- [ ] WinForms tipleri yalnızca `WinForms.*` alias'ı ile kullanılır (Application/MessageBox belirsizliği).
- [ ] Auto-relock güvenlik ağı (`ForceClose`) `nopassword` yolunda korunur; gate çökmesinde
      geçit açık kalmaz. (İstisna: "oturumda bir kez" muaf exe'lerinde geçit kasıtlı açık
      bırakılır — §9.1. Bu bir bug değildir.)

## 9. Açık sorular — KARARA BAĞLANDI (2026-06-12)

1. **Tarayıcı/tekrar açılış muafiyeti → "oturumda bir kez doğrula" seçildi (UYGULANDI).**
   Şifre bir kez doğru girilince resident o exe'yi `_sessionUnlocked` setine ekler; geçidi
   açık bırakır (relock/auto-relock yapmaz). Sonraki açılışlar geçidi tetiklemez. Resident
   logon'da yeniden başlayınca `SyncGates` geçitleri geri kurar → muafiyet sıfırlanır.
2. **Mikro yarış penceresi → kabul edildi.** Ek karmaşıklık (kuyruk vb.) eklenmeyecek;
   mevcut tehdit modeli için yeterli.
3. **Faz 2 panik tuşu → şimdilik ertelendi.** Önce Faz 1 test/sağlamlaştırma. Karar
   verildiğinde §10 yol haritası uygulanır.

## 10. FAZ 2 — DontPanic benzeri panik-gizle (UYGULANDI 2026-06-12)

`Panic/PanicController.cs` tamamlandı. Kararlar (§9.3): kısayol **Ctrl+Alt+Q sabit**,
hedef = **kilitli uygulamalar** (`cfg.Apps`), geri getirme = **aynı kısayol (toggle)**.

- [x] ResidentHost başlangıcında gizli message-only `HwndSource` + `RegisterHotKey` (Ctrl+Alt+Q).
- [x] WM_HOTKEY → `WndProc` → toggle. Gizleme: `EnumWindows` + `GetWindowThreadProcessId`,
      görünür + başlıklı + kilitli-exe pid'i eşleşen pencereler `ShowWindow(SW_HIDE)`; HWND'ler `_hidden`'da.
- [x] Geri getirme: aynı kısayol → mevcut `PasswordDialog` → doğruysa `SW_SHOW`. Yanlış/iptal → gizli kalır.
- [x] Şifre kontrolü tuşa basıldığı an (lockout önler): şifre yoksa hiç gizlemez.

> Henüz YOK (gerekirse ileride): yapılandırılabilir hotkey, ayrı panik hedef listesi,
> Settings panik sekmesi, sistem sesi mute. `AppConfig.PanicEnabled` şu an kullanılmıyor
> (panik, şifre kuruluyken her zaman aktif).

### Faz 2 invariant'ları
- [ ] Panik geri getirme her zaman `PasswordDialog` + `PasswordHasher.Verify`'dan geçer.
- [ ] Şifre kurulu değilken panik gizleme YAPILMAZ (kullanıcı pencerelerine erişimini kaybetmesin).
- [ ] `ShowWindow` yalnızca `_hidden`'da kayıtlı HWND'lere uygulanır (rastgele pencere gösterilmez).

## 11. Kabul kriterleri / test senaryoları

GUI olduğu için manuel doğrulama (agent otomatik koşamaz, kullanıcıdan teyit iste):

1. **Temel kilit:** Şifre kurulu + `zen.exe` listede → `zen.exe` çalıştır → herhangi bir
   pencere açılmadan şifre diyaloğu gelir.
2. **Doğru şifre:** → Zen normal açılır (elevated DEĞİL, kullanıcı IL'sinde).
3. **Yanlış şifre x3:** → "Şifre hatalı" uyarısı, uygulama açılmaz.
4. **İptal:** → Sessizce kapanır, uygulama açılmaz.
5. **Eşzamanlı 2 başlatma:** → İkisi de ayrı şifre sorar, sayaç doğru çalışır, ikisi de açılır.
6. **Resident kapalı:** → "Kilit servisi çalışmıyor" uyarısı, uygulama açılmaz, loop yok.
7. **Listeden çıkarma:** → `UninstallGate` çağrılır, exe artık serbest açılır.
8. **`--uninstall`:** → Tüm IFEO anahtarları temizlenir.
9. **AV kontrolü:** İmzasız exe'de antivirüs uyarısı çıkıyor mu? (imzalama gereği teyidi)
