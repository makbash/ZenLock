using System.IO;
using System.Windows;
using Microsoft.Win32;
using ZenLock.Auth;
using ZenLock.Config;
using ZenLock.Ifeo;

namespace ZenLock.Ui;

public partial class SettingsWindow : Window
{
    private readonly IfeoManager _ifeo;
    private AppConfig _cfg;

    public SettingsWindow(IfeoManager ifeo)
    {
        InitializeComponent();
        _ifeo = ifeo;
        _cfg = ConfigStore.Load();
        RefreshUi();
    }

    private void RefreshUi()
    {
        PwStatus.Text = _cfg.HasPassword
            ? "Şifre ayarlandı. Kilit etkin."
            : "Şifre ayarlanmadı. Kilit pasif (önce şifre belirleyin).";
        AppList.ItemsSource = null;
        AppList.ItemsSource = _cfg.Apps;
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
