namespace ZenLock.Panic;

/// <summary>
/// FAZ 2 — DontPanic benzeri özellik (henüz uygulanmadı).
///
/// Planlanan akış:
///  1. ResidentHost başlarken NativeMethods.RegisterHotKey ile global panik tuşu (ör. Ctrl+Alt+Q) kaydedilir.
///  2. WM_HOTKEY geldiğinde hedef uygulamaların pencereleri EnumWindows + GetWindowThreadProcessId
///     ile bulunur ve ShowWindow(SW_HIDE) ile gizlenir; handle'lar listede tutulur.
///  3. Geri getirme: ikinci kısayol veya tray -> aynı PasswordDialog -> doğruysa ShowWindow(SW_SHOW).
///
/// Şifre/Config/Tray altyapısı MVP'den hazır geldiği için bu sınıf yalnızca
/// hotkey + pencere handle yönetimi ekleyecek.
/// </summary>
internal sealed class PanicController
{
    // FAZ 2'de doldurulacak.
}
