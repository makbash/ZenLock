using System.Windows;
using System.Windows.Input;

namespace ZenLock.Auth;

public partial class PasswordDialog : Window
{
    public string EnteredPassword { get; private set; } = "";

    /// <param name="exeName">Kilitli uygulamanın adı (başlıkta gösterilir).</param>
    /// <param name="retry">Önceki deneme hatalıysa hata satırını göster.</param>
    public PasswordDialog(string exeName, bool retry)
    {
        InitializeComponent();
        SubText.Text = $"\"{exeName}\" açmak için şifre girin.";
        ErrorText.Visibility = retry ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) => PwBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Accept();

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void PwBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Accept();
    }

    private void Accept()
    {
        EnteredPassword = PwBox.Password;
        DialogResult = true;
        Close();
    }
}
