using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using ZenLock.Auth;
using ZenLock.Config;
using ZenLock.Ifeo;

namespace ZenLock.Ui;

public partial class SettingsWindow : Window
{
    // Win32 MOD_* bitleri
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;

    private readonly IfeoManager _ifeo;
    private readonly Action? _onHotkeyChanged;
    private readonly Action? _onTrayChanged;
    private readonly Action? _onExit;
    private AppConfig _cfg;

    public SettingsWindow(IfeoManager ifeo, Action? onHotkeyChanged = null,
        Action? onTrayChanged = null, Action? onExit = null)
    {
        InitializeComponent();
        _ifeo = ifeo;
        _onHotkeyChanged = onHotkeyChanged;
        _onTrayChanged = onTrayChanged;
        _onExit = onExit;
        _cfg = ConfigStore.Load();
        RefreshUi();
    }

    private void RefreshUi()
    {
        PwStatus.Text = _cfg.HasPassword
            ? "Şifre ayarlandı. Kilit etkin."
            : "Şifre ayarlanmadı. Kilit pasif (önce şifre belirleyin).";
        HotkeyBox.Text = FormatHotkey(_cfg.PanicModifiers, _cfg.PanicVk);
        HideTrayCheck.IsChecked = _cfg.HideTrayIcon;
        IdleBox.Text = _cfg.IdleRelockMinutes.ToString();
        AppList.ItemsSource = null;
        AppList.ItemsSource = _cfg.Apps;
    }

    private void HideTray_Click(object sender, RoutedEventArgs e)
    {
        _cfg.HideTrayIcon = HideTrayCheck.IsChecked == true;
        ConfigStore.Save(_cfg);
        _onTrayChanged?.Invoke();
    }

    private void IdleBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(IdleBox.Text, out var minutes) || minutes < 0)
            minutes = 0;
        _cfg.IdleRelockMinutes = minutes;
        ConfigStore.Save(_cfg);
        IdleBox.Text = minutes.ToString(); // normalize edilmiş değeri geri yaz
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Close();
        _onExit?.Invoke();
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true; // Alt'ın menü zilini ve odak gezinmesini engelle

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return; // yalnızca modifier — asıl tuşu bekle

        if (Keyboard.Modifiers == ModifierKeys.None)
        {
            MessageBox.Show("En az bir modifier (Ctrl/Alt/Shift) ile birlikte bir tuşa basın.",
                "ZenLock", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        uint mods = 0;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= MOD_ALT;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods |= MOD_CONTROL;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= MOD_SHIFT;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) mods |= MOD_WIN;

        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);

        _cfg.PanicModifiers = mods;
        _cfg.PanicVk = vk;
        ConfigStore.Save(_cfg);
        HotkeyBox.Text = FormatHotkey(mods, vk);

        _onHotkeyChanged?.Invoke(); // resident hotkey'i yeniden kaydetsin
    }

    private static string FormatHotkey(uint mods, uint vk)
    {
        var parts = new List<string>();
        if ((mods & MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((mods & MOD_ALT) != 0) parts.Add("Alt");
        if ((mods & MOD_SHIFT) != 0) parts.Add("Shift");
        if ((mods & MOD_WIN) != 0) parts.Add("Win");
        var key = KeyInterop.KeyFromVirtualKey((int)vk);
        parts.Add(key.ToString());
        return string.Join(" + ", parts);
    }

    private void SetPassword_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SetPasswordDialog(_cfg.HasPassword) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        // Mevcut şifre varsa doğrula.
        if (_cfg.HasPassword &&
            !PasswordHasher.Verify(dlg.CurrentPassword, _cfg.PasswordHash, _cfg.PasswordSalt))
        {
            MessageBox.Show("Mevcut şifre hatalı.", "ZenLock",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var (hash, salt) = PasswordHasher.Create(dlg.NewPassword);
        _cfg.PasswordHash = hash;
        _cfg.PasswordSalt = salt;
        ConfigStore.Save(_cfg);

        // Şifre yeni ayarlandıysa mevcut uygulamalar için geçitleri kur.
        foreach (var app in _cfg.Apps) TryInstall(app.ExeName);

        RefreshUi();
        MessageBox.Show("Şifre kaydedildi.", "ZenLock",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void AddApp_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog
        {
            Title = "Kilitlenecek uygulamayı seçin",
            Filter = "Uygulamalar (*.exe)|*.exe",
            CheckFileExists = true
        };
        if (ofd.ShowDialog() != true) return;

        var path = ofd.FileName;
        var exe = Path.GetFileName(path);

        if (_cfg.Apps.Any(a => string.Equals(a.ExeName, exe, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("Bu uygulama zaten listede.", "ZenLock",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _cfg.Apps.Add(new GatedApp
        {
            DisplayName = Path.GetFileNameWithoutExtension(path),
            ExeName = exe,
            FullPath = path
        });
        ConfigStore.Save(_cfg);

        if (_cfg.HasPassword) TryInstall(exe);
        else MessageBox.Show("Uygulama eklendi, ancak kilit için önce master şifre belirleyin.",
            "ZenLock", MessageBoxButton.OK, MessageBoxImage.Warning);

        RefreshUi();
    }

    private void RemoveApp_Click(object sender, RoutedEventArgs e)
    {
        if (AppList.SelectedItem is not GatedApp app) return;

        _ifeo.UninstallGate(app.ExeName);
        _cfg.Apps.Remove(app);
        ConfigStore.Save(_cfg);
        RefreshUi();
    }

    private void TryInstall(string exeName)
    {
        try { _ifeo.InstallGate(exeName); }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Geçit kurulamadı (yönetici hakkı gerekebilir):\n" + ex.Message,
                "ZenLock", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
