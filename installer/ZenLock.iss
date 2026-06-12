; ZenLock — Inno Setup kurulum scripti
; Derleme: önce yayın al, sonra bu scripti Inno Setup ile derle (ISCC.exe).
;
;   dotnet publish ZenLock.App -c Release -r win-x64 --self-contained false ^
;     -p:PublishSingleFile=true -o publish
;   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\ZenLock.iss
;
; Çıktı: installer\Output\ZenLockSetup.exe

#define AppName "ZenLock"
; Sürüm dışarıdan verilebilir: ISCC /DAppVersion=1.2.3 (CI tag'inden). Yoksa varsayılan.
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppExe "ZenLock.exe"

[Setup]
AppId={{8E7B2C14-3A6D-4F2B-9C1E-1A2B3C4D5E6F}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=ZenLock
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=ZenLockSetup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; IFEO (HKLM) ve zamanlanmış görev için yönetici şart.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "tr"; MessagesFile: "compiler:Languages\Turkish.isl"

[Files]
; Yayın klasöründeki tek-dosya exe (önce dotnet publish ile üretin).
Source: "..\publish\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; Tray gizliyken Ayarlar'a erişim için masaüstü kısayolu.
Name: "{autodesktop}\ZenLock Ayarlar"; Filename: "{app}\{#AppExe}"; \
    Parameters: "--settings"; IconFilename: "{app}\{#AppExe}"; \
    Comment: "ZenLock Ayarlar (tray gizliyken erişim)"

[Run]
; Logon'da elevated otomatik başlatma görevi.
Filename: "{sys}\schtasks.exe"; \
    Parameters: "/Create /TN ""ZenLock"" /SC ONLOGON /RL HIGHEST /TR ""\""{app}\{#AppExe}\"""" /F"; \
    Flags: runhidden; StatusMsg: "Otomatik başlatma görevi kuruluyor..."
; Resident'ı hemen başlat.
Filename: "{app}\{#AppExe}"; Description: "ZenLock'u şimdi başlat"; \
    Flags: nowait postinstall skipifsilent

[UninstallRun]
; Önce IFEO geçitlerini temizle (kilitli uygulamalar serbest kalsın), sonra görevi sil.
Filename: "{app}\{#AppExe}"; Parameters: "--uninstall"; Flags: runhidden; RunOnceId: "ZenLockUninstallGates"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""ZenLock"" /F"; \
    Flags: runhidden; RunOnceId: "ZenLockDeleteTask"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
