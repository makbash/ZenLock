namespace ZenLock.Config;

/// <summary>Diskte (DPAPI ile şifreli) tutulan konfigürasyon.</summary>
public sealed class AppConfig
{
    /// <summary>PBKDF2 türetilmiş hash (base64). null ise şifre henüz kurulmamış.</summary>
    public string? PasswordHash { get; set; }

    /// <summary>PBKDF2 salt (base64).</summary>
    public string? PasswordSalt { get; set; }

    /// <summary>Kilitlenecek uygulamalar.</summary>
    public List<GatedApp> Apps { get; set; } = new();

    /// <summary>Panik kısayolu modifier bitmask'i (Win32 MOD_*: Alt=1, Ctrl=2, Shift=4, Win=8).
    /// Varsayılan Ctrl+Alt = 3.</summary>
    public uint PanicModifiers { get; set; } = 3;

    /// <summary>Panik kısayolu sanal tuş kodu (VK). Varsayılan 'Q' = 0x51.</summary>
    public uint PanicVk { get; set; } = 0x51;

    /// <summary>Tray ikonu gizlensin mi? Gizliyken Ayarlar'a `--settings` veya gizli
    /// kısayol (Ctrl+Alt+Shift+S) ile erişilir.</summary>
    public bool HideTrayIcon { get; set; } = false;

    /// <summary>Bu kadar dakika boşta kalınca "oturumda bir kez" muafiyeti sıfırlanır ve
    /// geçitler geri kurulur. 0 = boşta yeniden kilitleme kapalı. Oturum kilidi (Win+L)
    /// her durumda yeniden kilitler.</summary>
    public int IdleRelockMinutes { get; set; } = 5;

    public bool HasPassword => !string.IsNullOrEmpty(PasswordHash) && !string.IsNullOrEmpty(PasswordSalt);
}

/// <summary>Kilitli tek bir uygulama kaydı.</summary>
public sealed class GatedApp
{
    /// <summary>Görünen ad (UI için).</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>Çalıştırılabilir dosya adı, ör. "zen.exe". IFEO bu ada göre eşler.</summary>
    public string ExeName { get; set; } = "";

    /// <summary>Tam yol (referans/UI için).</summary>
    public string FullPath { get; set; } = "";
}
