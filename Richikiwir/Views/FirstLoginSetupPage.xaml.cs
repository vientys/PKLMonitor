using Richikiwir.Services;

namespace Richikiwir.Views;

public partial class FirstLoginSetupPage : ContentPage
{
    public FirstLoginSetupPage()
    {
        InitializeComponent();
        var user = AuthService.Current.CurrentUser;
        TxtFullName.Text = user.FullName;
        TxtEmail.Text = user.Email == "-" ? string.Empty : user.Email;
        TxtPhone.Text = user.PhoneNumber;
    }

    protected override bool OnBackButtonPressed() => true;

    private async void OnCompleteSetupClicked(object? sender, EventArgs e)
    {
        LblSetupError.IsVisible = false;
        var fullName = TxtFullName.Text?.Trim() ?? string.Empty;
        var email = TxtEmail.Text?.Trim() ?? string.Empty;
        var phone = TxtPhone.Text?.Trim() ?? string.Empty;
        var newPassword = TxtNewPassword.Text ?? string.Empty;
        var confirmation = TxtConfirmPassword.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(phone))
        {
            ShowError("Nama, email, dan nomor telepon wajib diisi.");
            return;
        }
        if (newPassword.Length < 8)
        {
            ShowError("Kata sandi baru minimal 8 karakter.");
            return;
        }
        if (!string.Equals(newPassword, confirmation, StringComparison.Ordinal))
        {
            ShowError("Konfirmasi kata sandi tidak sama.");
            return;
        }

        BtnCompleteSetup.IsEnabled = false;
        BtnCompleteSetup.Text = "Menyimpan...";
        try
        {
            var result = await AuthService.Current.CompleteFirstLoginAsync(fullName, email, phone, newPassword);
            if (!result.Success)
            {
                ShowError(result.Message);
                return;
            }

            if (Application.Current?.Windows.Count > 0)
                Application.Current.Windows[0].Page = new AppShell();
        }
        finally
        {
            BtnCompleteSetup.IsEnabled = true;
            BtnCompleteSetup.Text = "Simpan dan Lanjutkan";
        }
    }

    private void ShowError(string message)
    {
        LblSetupError.Text = message;
        LblSetupError.IsVisible = true;
    }
}