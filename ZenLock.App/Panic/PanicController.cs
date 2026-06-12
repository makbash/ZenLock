using System.Diagnostics;
using System.Text;
using System.Windows;
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
/// Resident (elevated/high IL) çalıştığı için ShowWindow, normal IL'deki hedef
/// pencerelere uygulanabilir (yüksek→düşük IL serbest).
///
/// Şifre kontrolü tuşa basıldığı an yapılır: şifre kurulu değilse hiçbir şey
/// yapmaz (kullanıcı pencerelerini geri getiremez hâle düşmesin).
/// </summary>
internal sealed class PanicController : IDisposable
{
    private const int HotkeyId = 1;
    private const uint VkQ = 0x51; // 'Q'

    private HwndSource? _source;
    private readonly List<IntPtr> _hidden = new();

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

        if (_hidden.Count == 0)
            HideTargets(cfg);
        else
            TryRestore(cfg);
    }

    private void HideTargets(AppConfig cfg)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in cfg.Apps)
        {
            var n = a.ExeName;
            if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n[..^4];
            if (n.Length > 0) names.Add(n);
        }
        if (names.Count == 0) return;

        var pidName = new Dictionary<uint, string?>();

        NativeMethods.EnumWindows((h, _) =>
        {
            if (!NativeMethods.IsWindowVisible(h)) return true;

            var sb = new StringBuilder(256);
            if (NativeMethods.GetWindowText(h, sb, sb.Capacity) == 0) return true; // başlıksız = yardımcı pencere

            NativeMethods.GetWindowThreadProcessId(h, out var pid);
            if (!pidName.TryGetValue(pid, out var pname))
            {
                pname = SafeProcessName(pid);
                pidName[pid] = pname;
            }

            if (pname != null && names.Contains(pname))
            {
                if (NativeMethods.ShowWindow(h, NativeMethods.SW_HIDE))
                    _hidden.Add(h);
            }
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

        foreach (var h in _hidden)
            NativeMethods.ShowWindow(h, NativeMethods.SW_SHOW);
        _hidden.Clear();
    }

    private static string? SafeProcessName(uint pid)
    {
        try { return Process.GetProcessById((int)pid).ProcessName; }
        catch { return null; }
    }

    public void Dispose()
    {
        if (_source != null)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
        }
    }
}
