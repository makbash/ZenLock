using System.Security.Cryptography;

namespace ZenLock.Auth;

/// <summary>
/// Şifreleri PBKDF2 (SHA-256) ile saltlı olarak türetir ve sabit zamanlı kıyaslar.
/// Düz metin şifre asla diske yazılmaz.
/// </summary>
public static class PasswordHasher
{
    private const int SaltSize = 16;        // bayt
    private const int HashSize = 32;        // bayt
    private const int Iterations = 600_000; // OWASP 2024 önerisine yakın

    /// <summary>Yeni şifre için (hash, salt) üretir — ikisi de base64.</summary>
    public static (string hash, string salt) Create(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Derive(password, salt);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    /// <summary>Girilen şifreyi kayıtlı hash/salt ile sabit zamanlı doğrular.</summary>
    public static bool Verify(string password, string? storedHashB64, string? storedSaltB64)
    {
        if (string.IsNullOrEmpty(storedHashB64) || string.IsNullOrEmpty(storedSaltB64))
            return false;
        try
        {
            var salt = Convert.FromBase64String(storedSaltB64);
            var expected = Convert.FromBase64String(storedHashB64);
            var actual = Derive(password, salt);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] Derive(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
}
