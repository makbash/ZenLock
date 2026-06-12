using System.IO;
using ZenLock.Config;

namespace ZenLock;

internal static class Program
{
    /// <summary>
    /// Giriş noktası. Üç mod var:
    ///  - Gate modu: Windows bizi IFEO debugger olarak çağırdı -> argv[0] kilitli bir exe yolu.
    ///  - Uninstall modu: "--uninstall" -> tüm IFEO geçitlerini temizler (elevated gerekir).
    ///  - Resident modu: argümansız -> tray uygulaması.
    /// </summary>
    [STAThread]
    private static int Main(string[] argv)
    {
        // --uninstall: tüm geçitleri kaldır (yönetici olarak çalıştırılmalı)
        if (argv.Length == 1 && string.Equals(argv[0], "--uninstall", StringComparison.OrdinalIgnoreCase))
            return ResidentHost.Uninstall();

        // Gate modu tespiti: ilk argüman var olan bir dosya ve kilitli listede ise
        if (argv.Length >= 1 && IsGatedInvocation(argv[0]))
            return GateClient.Run(targetPath: argv[0], targetArgs: argv[1..]);

        // Aksi halde resident (tray) modu
        return ResidentHost.Run();
    }

    /// <summary>
    /// argv[0] kilitli bir uygulama çağrısı mı? IFEO image adına (leaf) göre eşler;
    /// bu yüzden biz de leaf isimle karşılaştırıyoruz.
    /// </summary>
    private static bool IsGatedInvocation(string firstArg)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(firstArg)) return false;
            if (!File.Exists(firstArg)) return false;

            var leaf = Path.GetFileName(firstArg);
            var cfg = ConfigStore.Load();
            foreach (var app in cfg.Apps)
            {
                if (string.Equals(app.ExeName, leaf, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
        catch
        {
            // Config okunamazsa gate moduna girmeyelim; resident gibi davran.
            return false;
        }
    }
}
