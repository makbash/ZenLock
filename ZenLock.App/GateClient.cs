using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Windows;
using ZenLock.Auth;
using ZenLock.Pipe;

namespace ZenLock;

/// <summary>
/// Windows bizi IFEO debugger olarak çağırdığında çalışır (kullanıcı seviyesinde).
/// Şifreyi sorar, resident sürece doğrulatır; doğruysa geçit açılır ve hedef
/// uygulamayı BİZ başlatırız (normal IL'de, loop'a girmeden).
/// </summary>
public static class GateClient
{
    private const int MaxAttempts = 3;
    private const int ConnectTimeoutMs = 4000;

    public static int Run(string targetPath, string[] targetArgs)
    {
        // WPF dialog'ları için bir Application örneği gerekiyor (App.xaml yok).
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        int exitCode = 0;

        app.Startup += (_, _) =>
        {
            try { exitCode = Gate(targetPath, targetArgs); }
            finally { app.Shutdown(); }
        };
        app.Run();
        return exitCode;
    }

    private static int Gate(string targetPath, string[] targetArgs)
    {
        var exeName = Path.GetFileName(targetPath);

        // Panik aktifse: kilitli uygulamayı başlatma, şifre de sorma -> sessizce çık.
        // (Resident'a ulaşılamazsa pre == null; o durumda normal akışa düşülür ve
        //  şifre denemesinde "servis çalışmıyor" uyarısı verilir.)
        var pre = Send(new UnlockRequest { Op = "check", Exe = exeName });
        if (pre != null && pre.Reason == "panic")
            return 0;

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var dlg = new Auth.PasswordDialog(exeName, attempt > 1);
            var result = dlg.ShowDialog();

            if (result != true)
                return 0; // İptal -> uygulama açılmaz (sessiz)

            var password = dlg.EnteredPassword;
            var resp = SendUnlock(exeName, password);

            if (resp == null)
            {
                // Resident'a ulaşılamadı -> kilit servisi çalışmıyor.
                // Güvenlik gereği uygulamayı AÇMIYORUZ (geçidi kaldıramayız, loop riski).
                MessageBox.Show(
                    "ZenLock kilit servisi çalışmıyor gibi görünüyor.\n\n" +
                    "Lütfen ZenLock'u (yönetici olarak) başlatın ve tekrar deneyin.",
                    "ZenLock", MessageBoxButton.OK, MessageBoxImage.Warning);
                return 1;
            }

            if (resp.Ok)
            {
                // Geçit açıldı (Debugger değeri kaldırıldı). Hedefi şimdi başlat.
                LaunchTarget(targetPath, targetArgs);
                // Geçidi geri kapat.
                SendRelock(exeName);
                return 0;
            }

            if (resp.Reason == "nopassword")
            {
                // Şifre kurulu değil -> kullanıcıyı kilitlemeyelim, doğrudan başlat.
                SendUnlockBypassRelock(exeName, targetPath, targetArgs);
                return 0;
            }

            // Yanlış şifre: son denemeyse uyarı göster, değilse dialog tekrar açılır.
            if (attempt == MaxAttempts)
            {
                MessageBox.Show(
                    "Şifre hatalı. Uygulama açılmadı.",
                    "ZenLock", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
        }
        return 1;
    }

    private static void LaunchTarget(string targetPath, string[] targetArgs)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = targetPath,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(targetPath) ?? Environment.CurrentDirectory
            };
            foreach (var a in targetArgs) psi.ArgumentList.Add(a);
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Uygulama başlatılamadı:\n{ex.Message}",
                "ZenLock", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>`--settings`: resident'a Ayarlar'ı açma sinyali yollar (tray gizliyken erişim).</summary>
    public static int SignalOpenSettings()
    {
        var resp = Send(new UnlockRequest { Op = "settings" });
        if (resp == null)
        {
            // Bu mod kendi başına WPF Application başlatmaz; MessageBox tek başına çalışır.
            MessageBox.Show(
                "ZenLock kilit servisi çalışmıyor gibi görünüyor.\n\n" +
                "Lütfen ZenLock'u (yönetici olarak) başlatın ve tekrar deneyin.",
                "ZenLock", MessageBoxButton.OK, MessageBoxImage.Warning);
            return 1;
        }
        return 0;
    }

    private static UnlockResponse? SendUnlock(string exe, string password)
        => Send(new UnlockRequest { Op = "unlock", Exe = exe, Password = password });

    private static void SendRelock(string exe)
        => Send(new UnlockRequest { Op = "relock", Exe = exe });

    /// <summary>Şifre kurulu değilken: geçidi aç, başlat, geri kapat.</summary>
    private static void SendUnlockBypassRelock(string exe, string targetPath, string[] args)
    {
        // "nopassword" yanıtında resident geçidi zaten açtı; sadece başlat + relock.
        LaunchTarget(targetPath, args);
        SendRelock(exe);
    }

    private static UnlockResponse? Send(UnlockRequest req)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeProtocol.PipeName,
                PipeDirection.InOut, PipeOptions.None);
            client.Connect(ConnectTimeoutMs);

            using var reader = new StreamReader(client);
            using var writer = new StreamWriter(client) { AutoFlush = true };

            writer.WriteLine(JsonSerializer.Serialize(req));
            var line = reader.ReadLine();
            if (line == null) return null;
            return JsonSerializer.Deserialize<UnlockResponse>(line);
        }
        catch
        {
            return null;
        }
    }
}
