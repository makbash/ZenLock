using System.Windows;

namespace ZenLock.Ui;

public partial class SetPasswordDialog : Window
{
    private const int MinLength = 4;

    public string CurrentPassword { get; private set; } = "";
    public string NewPassword { get; private set; } = "";

    public SetPasswordDialog(bool requireCurrent)
    {
        InitializeComponent();
        if (!requireCurrent)
        {
            CurrentLabel.Visibility = Visibility.Collapsed;
            CurrentBox.Visibility = Visibility.Collapsed;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var nw = NewBox.Password;
        var cf = ConfirmBox.Password;

        if (nw.Length < MinLength)
        {
            ShowError($"Şifre en az {MinLength} karakter olmalı.");
            return;
        }
        if (nw != cf)
        {
            ShowError("Yeni şifreler eşleşmiyor.");
            return;
        }

        CurrentPassword = CurrentBox.Password;
        NewPassword = nw;
        DialogResult = true;
        Close();
    }

    private void ShowError(string msg)
    {
        ErrorText.Text = msg;
        ErrorText.Visibility = Visibility.Visible;
    }
}
