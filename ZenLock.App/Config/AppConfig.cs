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

    /// <summary>FAZ 2 — panik tuşu ile gizlenecek uygulamalar (şimdilik kullanılmıyor).</summary>
    public bool PanicEnabled { get; set; } = false;

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
