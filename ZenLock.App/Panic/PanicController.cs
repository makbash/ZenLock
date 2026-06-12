using System.Diagnostics;
using System.Text;
using System.Windows.Interop;
using ZenLock.Auth;
using ZenLock.Config;
using ZenLock.Interop;

namespace ZenLock.Panic;

/// <summary>
/// FAZ 2 — DontPanic benzeri panik-gizle.
///
/// Global kısayol (Ctrl+Alt+Q) ile kilitli uygulamaların açık pencerelerini gizler;
/// aynı kısayola tekrar basınca şifre sorar ve doğruysa geri gösterir (toggle).
///
/// Panik aktifken bir WinEvent kancası (EVENT_OBJECT_SHOW) kurulur: hedef uygulamanın
/// herhangi bir penceresi yeniden görünür olursa (ör. kısayola basılıp mevcut instance
/// öne getirilince) anında tekrar gizlenir. Böylece pencere şifre girilene dek gizli kalır.
///
/// Resident (elevated/high IL) çalıştığı için ShowWindow, normal IL'deki hedef
/// pencerelere uygulanabilir (yüksek→düşük IL serbest).
///
/// Şifre kontrolü tuşa basıldığı an yapılır: şifre kurulu değilse hiçbir şey yapmaz
/// (kullanıcı pencerelerini geri getiremez hâle düşmesin).
/// </summary>
internal sealed class PanicController : IDisposable
{
    private const int HotkeyId = 1;
    private const uint VkQ = 0x51; // 'Q'

    private HwndSource? _source;
    private readonly List<IntPtr> _hidden = new();
    private readonly HashSet<string> _targets = new(StringComparer.OrdinalIgnoreCase);

    private volatile bool _panicActive;
    private IntPtr _winEventHook;
    private NativeMethods.WinEventDelegate? _winEventProc; // GC tutması için alanda saklanır

    /// <summary>Panik aktifken (pencereler gizliyken) true. Pipe thread'inden okunur.</summary>
    public bool IsPanicActive => _panicActive;

    /// <summary>Resident'ın WPF UI thread'inde çağrılmalı (HwndSource + dialog için).</summary>
    public void Start()
    {
        var parameters = new HwndSourceParameters("ZenLockPanicWindow")
        {
            Width = 0,
            Height = 0,
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE: yalnızca mesaj alan pencere
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);

        NativeMethods.RegisterHotKey(
            _source.Handle, HotkeyId,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, VkQ);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            Toggle();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void Toggle()
    {
        var cfg = ConfigStore.Load();
        if (!cfg.HasPassword) return; // şifre yoksa geri getirilemez -> hiç gizleme

        if (!_panicActive)
            EnterPanic(cfg);
        else
            TryRestore(cfg);
    }

    private void EnterPanic(AppConfig cfg)
    {
        _targets.Clear();
        foreach (var a in cfg.Apps)
        {
            var n = a.ExeName;
            if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n[..^4];
            if (n.Length > 0) _targets.Add(n);
        }
        if (_targets.Count == 0) return; // gizlenecek uygulama tanımlı değil

        _panicActive = true;
        InstallWinEventHook();

        // Şu an açık olan hedef pencereleri gizle.
        var pidCache = new Dictionary<uint, string?>();
        NativeMethods.EnumWindows((h, _) =>
        {
            if (IsTargetWindow(h, pidCache)) HideWindow(h);
            return true;
        }, IntPtr.Zero);
    }

    private void TryRestore(AppConfig cfg)
    {
        var dlg = new PasswordDialog("Gizlenen uygulamalar", retry: false) { Topmost = true };
        var ok = dlg.ShowDialog();
        if (ok != true) return; // iptal -> gizli kalsın

        if (!PasswordHasher.Verify(dlg.EnteredPassword, cfg.PasswordHash, cfg.PasswordSalt))
            return; // yanlış şifre -> gizli kalsın

        // Önce kancayı kaldır; aksi halde SW_SHOW olayları pencereleri hemen geri gizler.
        RemoveWinEventHook();
        foreach (var h in _hidden)
            NativeMethods.ShowWindow(h, NativeMethods.SW_SHOW);
        _hidden.Clear();
        _targets.Clear();
        _panicActive = false;
    }

    // Panik sırasında yeniden görünen hedef pencereleri tekrar gizle.
    private void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd,
        int idObject, int idChild, uint thread, uint time)
    {
        if (!_panicActive || hwnd == IntPtr.Zero) return;
        if (idObject != NativeMethods.OBJID_WINDOW || idChild != 0) return;

        var pidCache = new Dictionary<uint, string?>();
        if (IsTargetWindow(hwnd, pidCache)) HideWindow(hwnd);
    }

    private bool IsTargetWindow(IntPtr hwnd, Dictionary<uint, string?> pidCache)
    {
        if (!NativeMethods.IsWindowVisible(hwnd)) return false;

        var sb = new StringBuilder(256);
        if (NativeMethods.GetWindowText(hwnd, sb, sb.Capacity) == 0) return false; // başlıksız = yardımcı pencere

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (!pidCache.TryGetValue(pid, out var pname))
        {
            pname = SafeProcessName(pid);
            pidCache[pid] = pname;
        }
        return pname != null && _targets.Contains(pname);
    }

    private void HideWindow(IntPtr hwnd)
    {
        if (NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE) && !_hidden.Contains(hwnd))
            _hidden.Add(hwnd);
    }

    private void InstallWinEventHook()
    {
        if (_winEventHook != IntPtr.Zero) return;
        _winEventProc = OnWinEvent;
        _winEventHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_OBJECT_SHOW, NativeMethods.EVENT_OBJECT_SHOW,
            IntPtr.Zero, _winEventProc, 0, 0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);
    }

    private void RemoveWinEventHook()
    {
        if (_winEventHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_winEventHook);
            _winEventHook = IntPtr.Zero;
        }
        _winEventProc = null;
    }

    private static string? SafeProcessName(uint pid)
    {
        try { return Process.GetProcessById((int)pid).ProcessName; }
        catch { return null; }
    }

    public void Dispose()
    {
        RemoveWinEventHook();
        if (_source != null)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
        }
    }
}
