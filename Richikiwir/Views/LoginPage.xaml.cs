using System;
using Microsoft.Maui.Controls;
using Richikiwir.Models;
using Richikiwir.Services;

namespace Richikiwir.Views
{
    public partial class LoginPage : ContentPage
    {
        private UserRole _selectedRole = UserRole.Siswa;

        public LoginPage()
        {
            InitializeComponent();
            UpdateRoleSelectionUi(UserRole.Siswa);
        }

        private void OnRoleSelectedClicked(object? sender, EventArgs e)
        {
            if (sender is Button btn && btn.CommandParameter is string roleStr &&
                Enum.TryParse<UserRole>(roleStr, out var role))
            {
                UpdateRoleSelectionUi(role);
            }
        }

        private void UpdateRoleSelectionUi(UserRole role)
        {
            _selectedRole = role;

            // Reset all role buttons
            ResetButtonStyle(BtnRoleSiswa);
            ResetButtonStyle(BtnRoleIndustri);
            ResetButtonStyle(BtnRoleSekolah);
            ResetButtonStyle(BtnRoleAdmin);

            // Highlight selected button & update info
            switch (role)
            {
                case UserRole.Siswa:
                    SetSelectedButtonStyle(BtnRoleSiswa, "#2563EB");
                    LblRoleInfo.Text = "Masuk sebagai Siswa PKL: Mengakses absensi live GPS, selfie kehadiran, & jurnal harian.";
                    LblIdentifier.Text = "NIS (Nomor Induk Siswa):";
                    TxtIdentifier.Placeholder = "Masukkan NIS (misal: 20241001) atau NISN";
                    LayoutJurusanPicker.IsVisible = true;
                    if (PickerJurusan.SelectedIndex < 0) PickerJurusan.SelectedIndex = 0;
                    break;
                case UserRole.PembimbingIndustri:
                    SetSelectedButtonStyle(BtnRoleIndustri, "#059669");
                    LblRoleInfo.Text = "Masuk sebagai Mentor DU/DI: Verifikasi kehadiran real-time dan nilai jurnal logbook siswa.";
                    LblIdentifier.Text = "NIK / ID Karyawan Industri:";
                    TxtIdentifier.Placeholder = "Masukkan NIK / ID Karyawan (misal: SDN-2024-089)";
                    LayoutJurusanPicker.IsVisible = false;
                    break;
                case UserRole.PembimbingSekolah:
                    SetSelectedButtonStyle(BtnRoleSekolah, "#7C3AED");
                    LblRoleInfo.Text = "Masuk sebagai Guru Pembimbing: Pantau kehadiran siswa, verifikasi izin, & koordinasi mitra.";
                    LblIdentifier.Text = "NIP (Nomor Induk Pegawai Guru):";
                    TxtIdentifier.Placeholder = "Masukkan 18 digit NIP Guru";
                    LayoutJurusanPicker.IsVisible = false;
                    TxtIdentifier.Placeholder = "Masukkan NIP Guru atau Email";
                    break;
                case UserRole.Admin:
                    SetSelectedButtonStyle(BtnRoleAdmin, "#DC2626");
                    LblRoleInfo.Text = "Masuk sebagai Administrator: Kelola data master siswa, pemetaan industri, & export rekap.";
                    LblIdentifier.Text = "Username Administrator:";
                    TxtIdentifier.Placeholder = "Masukkan Username Admin (misal: admin_pkl)";
                    LayoutJurusanPicker.IsVisible = false;
                    break;
            }
        }

        private void ResetButtonStyle(Button btn)
        {
            btn.BackgroundColor = Color.FromArgb("#F1F5F9");
            btn.TextColor = Color.FromArgb("#475569");
            SetRoleButtonIcon(btn, Color.FromArgb("#475569"));
        }

        private void SetSelectedButtonStyle(Button btn, string hexColor)
        {
            btn.BackgroundColor = Color.FromArgb(hexColor);
            btn.TextColor = Colors.White;
            SetRoleButtonIcon(btn, Colors.White);
        }

        private static void SetRoleButtonIcon(Button btn, Color color)
        {
            var glyph = btn.CommandParameter?.ToString() switch
            {
                "Siswa" => "school",
                "PembimbingIndustri" => "business",
                "PembimbingSekolah" => "co_present",
                "Admin" => "settings",
                _ => "person"
            };

            btn.ImageSource = new FontImageSource
            {
                FontFamily = "MaterialSymbols",
                Glyph = glyph,
                Size = 16,
                Color = color
            };
        }

        private void OnTogglePasswordClicked(object? sender, EventArgs e)
        {
            TxtPassword.IsPassword = !TxtPassword.IsPassword;
            BtnTogglePassword.ImageSource = new FontImageSource
            {
                FontFamily = "MaterialSymbols",
                Glyph = TxtPassword.IsPassword ? "visibility" : "visibility_off",
                Size = 18,
                Color = Color.FromArgb("#64748B")
            };
        }

        private async void OnLoginClicked(object? sender, EventArgs e)
        {
            var id = TxtIdentifier.Text?.Trim() ?? string.Empty;
            var pass = TxtPassword.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrEmpty(id))
            {
                await DisplayAlertAsync("Peringatan", "Silakan masukkan nomor identitas (NIS/NIP/NIK) atau username Anda.", "OK");
                return;
            }

            if (string.IsNullOrEmpty(pass))
            {
                await DisplayAlertAsync("Peringatan", "Silakan masukkan kata sandi akun Anda.", "OK");
                return;
            }

            BtnLogin.IsEnabled = false;
            BtnLogin.Text = "Memverifikasi...";

            string? selectedJurusan = null;
            if (_selectedRole == UserRole.Siswa && PickerJurusan.SelectedItem is string jurStr)
            {
                selectedJurusan = jurStr.Split(' ')[0]; // Ambil kode jurusan "RPL", "DKV", dll.
            }

            try
            {
                var result = await AuthService.Current.LoginAsync(id, pass, _selectedRole, selectedJurusan);
                if (!result.Success)
                {
                    await DisplayAlertAsync("Gagal Masuk", result.Message, "OK");
                    return;
                }

                if (Application.Current?.Windows.Count > 0)
                {
                    var user = AuthService.Current.CurrentUser;
                    Application.Current.Windows[0].Page = user.MustCompleteProfile || user.MustChangePassword
                        ? new NavigationPage(new FirstLoginSetupPage())
                        : new AppShell();
                }
            }
            finally
            {
                BtnLogin.IsEnabled = true;
                BtnLogin.Text = "Masuk ke Sistem PKL";
            }
        }

        private async void OnForgotPasswordTapped(object? sender, EventArgs e)
        {
            await DisplayAlertAsync("Bantuan Akun PKL", 
                "Untuk reset kata sandi atau kendala NISN/NIP, silakan hubungi Koordinator BKK PKL di Ruang Tata Usaha atau WhatsApp: 0812-3456-7890.", 
                "Tutup");
        }
    }
}
