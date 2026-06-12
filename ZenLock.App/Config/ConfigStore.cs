using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ZenLock.Config;

/// <summary>
/// Konfigürasyonu %APPDATA%\ZenLock\config.dat içinde DPAPI (CurrentUser) ile
/// şifreleyerek saklar. Başka kullanıcı dosyayı kopyalasa bile çözemez.
/// </summary>
public static class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    public static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZenLock");

    public static string FilePath => Path.Combine(Dir, "config.dat");

    public static AppConfig Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppConfig();
            var protectedBytes = File.ReadAllBytes(FilePath);
            var plain = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(plain);
            return JsonSerializer.Deserialize<AppConfig>(json, JsonOpts) ?? new AppConfig();
        }
        catch
        {
            // Bozuk/okunamayan config -> boş config (kullanıcıyı kilitlemeyelim).
            return new AppConfig();
        }
    }

    public static void Save(AppConfig cfg)
    {
        Directory.CreateDirectory(Dir);
        var json = JsonSerializer.Serialize(cfg, JsonOpts);
        var plain = Encoding.UTF8.GetBytes(json);
        var protectedBytes = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
        // Atomik yazım: önce geçici dosya, sonra taşı.
        var tmp = FilePath + ".tmp";
        File.WriteAllBytes(tmp, protectedBytes);
        if (File.Exists(FilePath)) File.Delete(FilePath);
        File.Move(tmp, FilePath);
    }
}
