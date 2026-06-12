using Microsoft.Win32;

namespace ZenLock.Ifeo;

/// <summary>
/// Image File Execution Options (IFEO) "Debugger" değerini yöneterek
/// kilitli exe'leri başlatma öncesinde bize yönlendirir.
///
/// HKLM yazımı yapar -> sadece YÜKSELTİLMİŞ (elevated) süreçte çalışır.
/// Yalnızca resident süreç bu sınıfı çağırmalı.
///
/// Loop tuzağı: Debugger değeri varken zen.exe'yi başlatmak yine bizi tetikler.
/// Bu yüzden gerçek başlatma anında değeri geçici kaldırırız (OpenGate),
/// uygulama başladıktan sonra geri ekleriz (CloseGate).
/// </summary>
public sealed class IfeoManager
{
    private const string IfeoRoot =
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";

    private readonly string _debuggerPath; // bizim exe'mizin tam yolu (tırnaklı)
    private readonly object _sync = new();

    // exe adı -> açık geçit sayacı (eşzamanlı başlatmalar için)
    private readonly Dictionary<string, int> _openCount = new(StringComparer.OrdinalIgnoreCase);

    public IfeoManager(string selfExePath)
    {
        // Boşluklu yolları tırnakla — Windows debugger değerini olduğu gibi prepend eder.
        _debuggerPath = selfExePath.Contains(' ') ? $"\"{selfExePath}\"" : selfExePath;
    }

    /// <summary>Kalıcı geçit kur: exe her açıldığında bize yönlensin.</summary>
    public void InstallGate(string exeName)
    {
        lock (_sync)
        {
            using var key = Registry.LocalMachine.CreateSubKey($@"{IfeoRoot}\{exeName}", writable: true);
            key!.SetValue("Debugger", _debuggerPath, RegistryValueKind.String);
        }
    }

    /// <summary>Kalıcı geçidi tamamen kaldır (listeden çıkarma/uninstall).</summary>
    public void UninstallGate(string exeName)
    {
        lock (_sync)
        {
            try
            {
                using var root = Registry.LocalMachine.OpenSubKey(IfeoRoot, writable: true);
                root?.DeleteSubKeyTree(exeName, throwOnMissingSubKey: false);
            }
            catch { /* zaten yok */ }
            _openCount.Remove(exeName);
        }
    }

    /// <summary>
    /// Geçidi geçici olarak aç (Debugger değerini sil) ki gate süreci uygulamayı
    /// loop'a girmeden başlatabilsin. Sayaç tabanlı — eşzamanlı açılışlara dayanıklı.
    /// </summary>
    public void OpenGate(string exeName)
    {
        lock (_sync)
        {
            _openCount.TryGetValue(exeName, out var c);
            _openCount[exeName] = c + 1;
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey($@"{IfeoRoot}\{exeName}", writable: true);
                key?.DeleteValue("Debugger", throwOnMissingValue: false);
            }
            catch { /* yoksa sorun değil */ }
        }
    }

    /// <summary>Geçidi geri kapat (Debugger değerini yeniden yaz). Sayaç sıfırlanınca uygulanır.</summary>
    public void CloseGate(string exeName)
    {
        lock (_sync)
        {
            if (_openCount.TryGetValue(exeName, out var c))
            {
                c--;
                if (c > 0) { _openCount[exeName] = c; return; }
                _openCount.Remove(exeName);
            }
            // Hâlâ kilitli listede ise Debugger değerini geri koy.
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey($@"{IfeoRoot}\{exeName}", writable: true);
                if (key != null) key.SetValue("Debugger", _debuggerPath, RegistryValueKind.String);
            }
            catch { /* geçit kaldırılmış olabilir */ }
        }
    }

    /// <summary>Çökme güvenliği: bir exe için tüm açık sayacı sıfırla ve geçidi geri kapat.</summary>
    public void ForceClose(string exeName)
    {
        lock (_sync)
        {
            _openCount.Remove(exeName);
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey($@"{IfeoRoot}\{exeName}", writable: true);
                if (key != null) key.SetValue("Debugger", _debuggerPath, RegistryValueKind.String);
            }
            catch { }
        }
    }
}
