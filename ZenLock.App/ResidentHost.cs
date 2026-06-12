using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Windows;
using WinForms = System.Windows.Forms;
using ZenLock.Auth;
using ZenLock.Config;
using ZenLock.Ifeo;
using ZenLock.Pipe;
using ZenLock.Ui;

namespace ZenLock;

/// <summary>
/// Tray'de oturan, logon'da (Task Scheduler ile elevated) başlayan kalıcı süreç.
/// IFEO geçitlerini kurar, gate süreçlerinden gelen unlock/relock isteklerini
/// karşılar ve geçidi loop-safe biçimde aç/kapatır.
/// </summary>
public sealed class ResidentHost
{
    private static readonly TimeSpan AutoRelock = TimeSpan.FromSeconds(8);

    private readonly IfeoManager _ifeo;
    private WinForms.NotifyIcon? _tray;
    private CancellationTokenSource _cts = new();
    private Application? _app;

    // Şifresi bir kez doğrulanan exe'ler bu oturum boyunca tekrar sormaz
    // ("oturumda bir kez" muafiyeti). Resident yeniden başlayınca (logon) sıfırlanır.
    private readonly HashSet<string> _sessionUnlocked = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sessionLock = new();

    private ResidentHost()
    {
        var self = Environment.ProcessPath ?? throw new InvalidOperationException("Çalışma yolu alınamadı.");
        _ifeo = new IfeoManager(self);
    }

    public static int Run()
    {
        // Tek örnek kilidi.
        using var mutex = new Mutex(initiallyOwned: true, "ZenLock_Resident_Singleton", out var isNew);
        if (!isNew) return 0;

        var host = new ResidentHost();
        return host.Start();
    }

    /// <summary>"--uninstall": tüm geçitleri kaldırır. Elevated çalıştırılmalı.</summary>
    public static int Uninstall()
    {
        try
        {
            var self = Environment.ProcessPath!;
            var ifeo = new IfeoManager(self);
            var cfg = ConfigStore.Load();
            foreach (var app in cfg.Apps) ifeo.UninstallGate(app.ExeName);
            return 0;
        }
        catch { return 1; }
    }

    private int Start()
    {
        _app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        _app.Startup += (_, _) =>
        {
            SyncGates();
            SetupTray();
            _ = Task.Run(() => PipeServerLoop(_cts.Token));
        };
        _app.Exit += (_, _) =>
        {
            _cts.Cancel();
            _tray?.Dispose();
        };
        _app.Run();
        return 0;
    }

    /// <summary>Config'teki tüm uygulamalar için IFEO geçidini kur.</summary>
    private void SyncGates()
    {
        var cfg = ConfigStore.Load();
        if (!cfg.HasPassword) return; // şifre yoksa geçit kurma (kilitlenme riski)
        foreach (var app in cfg.Apps)
        {
            try { _ifeo.InstallGate(app.ExeName); } catch { /* yetki yoksa sessiz geç */ }
        }
    }

    private void SetupTray()
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Ayarlar...", null, (_, _) => OpenSettings());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Çıkış", null, (_, _) => _app?.Shutdown());

        _tray = new WinForms.NotifyIcon
        {
            Icon = SystemIcons.Shield,
            Text = "ZenLock",
            Visible = true,
            ContextMenuStrip = menu
        };
        _tray.DoubleClick += (_, _) => OpenSettings();
    }

    private void OpenSettings()
    {
        // WPF UI thread üzerinde aç.
        _app?.Dispatcher.Invoke(() =>
        {
            var win = new SettingsWindow(_ifeo);
            win.ShowDialog();
        });
    }

    // ---- Named pipe sunucusu ----

    private async Task PipeServerLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(
                    PipeProtocol.PipeName, PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(ct);
                var accepted = server;
                server = null; // sahipliği handler'a devret
                _ = Task.Run(() => HandleClient(accepted), ct);
            }
            catch (OperationCanceledException) { break; }
            catch
            {
                await Task.Delay(200, ct).ContinueWith(_ => { });
            }
            finally
            {
                server?.Dispose();
            }
        }
    }

    private void HandleClient(NamedPipeServerStream server)
    {
        try
        {
            using (server)
            using (var reader = new StreamReader(server))
            using (var writer = new StreamWriter(server) { AutoFlush = true })
            {
                var line = reader.ReadLine();
                if (line == null) return;

                var req = JsonSerializer.Deserialize<UnlockRequest>(line);
                if (req == null) { Respond(writer, false, "error"); return; }

                switch (req.Op)
                {
                    case "unlock":
                        HandleUnlock(req, writer);
                        break;
                    case "relock":
                        bool keepOpen;
                        lock (_sessionLock) keepOpen = _sessionUnlocked.Contains(req.Exe);
                        // Oturumda bir kez: muaf exe'de geçidi açık bırak, kapatma.
                        if (!keepOpen) _ifeo.CloseGate(req.Exe);
                        Respond(writer, true, "");
                        break;
                    default:
                        Respond(writer, false, "error");
                        break;
                }
            }
        }
        catch { /* bağlantı koptu */ }
    }

    private void HandleUnlock(UnlockRequest req, StreamWriter writer)
    {
        var cfg = ConfigStore.Load();

        if (!cfg.HasPassword)
        {
            // Şifre kurulu değil -> geçidi aç, gate doğrudan başlatsın.
            _ifeo.OpenGate(req.Exe);
            ScheduleAutoRelock(req.Exe);
            Respond(writer, false, "nopassword");
            return;
        }

        if (!PasswordHasher.Verify(req.Password, cfg.PasswordHash, cfg.PasswordSalt))
        {
            Respond(writer, false, "badpass");
            return;
        }

        // Şifre doğru -> bu oturum boyunca muaf tut, geçidi aç ve AÇIK BIRAK.
        // Relock/auto-relock yapılmaz; sonraki açılışlar geçidi tetiklemez (oturumda bir kez).
        lock (_sessionLock) _sessionUnlocked.Add(req.Exe);
        _ifeo.OpenGate(req.Exe);
        Respond(writer, true, "");
    }

    /// <summary>Gate relock göndermezse (çökme) geçidi belirli süre sonra zorla kapat.</summary>
    private void ScheduleAutoRelock(string exe)
    {
        // System.Threading.Timer herhangi bir thread'de güvenle çalışır (Dispatcher gerekmez).
        Timer? timer = null;
        timer = new Timer(_ =>
        {
            try { _ifeo.ForceClose(exe); }
            finally { timer?.Dispose(); }
        }, null, AutoRelock, Timeout.InfiniteTimeSpan);
    }

    private static void Respond(StreamWriter writer, bool ok, string reason)
        => writer.WriteLine(JsonSerializer.Serialize(new UnlockResponse { Ok = ok, Reason = reason }));
}
