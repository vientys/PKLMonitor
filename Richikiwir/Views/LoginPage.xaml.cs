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
            ResetButtonStyle(BtnRoleSiswa, "🎓 Siswa");
            ResetButtonStyle(BtnRoleIndustri, "🏢 Pembimbing Industri");
            ResetButtonStyle(BtnRoleSekolah, "🏫 Guru Sekolah");
            ResetButtonStyle(BtnRoleMonitor, "📋 Tim Monitor");
            ResetButtonStyle(BtnRoleAdmin, "⚙️ Admin PKL");

            // Highlight selected button & update info
            switch (role)
            {
                case UserRole.Siswa:
                    SetSelectedButtonStyle(BtnRoleSiswa, "#2563EB");
                    LblRoleInfo.Text = "Masuk sebagai Siswa PKL: Mengakses absensi live GPS, selfie kehadiran, & jurnal harian.";
                    LblIdentifier.Text = "NISN (Nomor Induk Siswa Nasional):";
                    TxtIdentifier.Placeholder = "Masukkan 10 digit NISN atau Email";
                    break;
                case UserRole.PembimbingIndustri:
                    SetSelectedButtonStyle(BtnRoleIndustri, "#059669");
                    LblRoleInfo.Text = "Masuk sebagai Mentor DU/DI: Verifikasi kehadiran real-time dan nilai jurnal logbook siswa.";
                    LblIdentifier.Text = "ID Karyawan / NIK Industri:";
                    TxtIdentifier.Placeholder = "Masukkan ID Karyawan atau Email";
                    break;
                case UserRole.PembimbingSekolah:
                    SetSelectedButtonStyle(BtnRoleSekolah, "#7C3AED");
                    LblRoleInfo.Text = "Masuk sebagai Guru Pembimbing: Pantau kehadiran siswa, verifikasi izin, & koordinasi mitra.";
                    LblIdentifier.Text = "NIP (Nomor Induk Pegawai Guru):";
                    TxtIdentifier.Placeholder = "Masukkan NIP Guru atau Email";
                    break;
                case UserRole.Monitor:
                    SetSelectedButtonStyle(BtnRoleMonitor, "#D97706");
                    LblRoleInfo.Text = "Masuk sebagai Tim Monitoring: Inspeksi lapangan ke industri, evaluasi fasilitas & kelayakan PKL.";
                    LblIdentifier.Text = "NIP / ID Tim Monitoring:";
                    TxtIdentifier.Placeholder = "Masukkan NIP Tim Monitoring atau Email";
                    break;
                case UserRole.Admin:
                    SetSelectedButtonStyle(BtnRoleAdmin, "#DC2626");
                    LblRoleInfo.Text = "Masuk sebagai Administrator: Kelola data master siswa, pemetaan industri, & export rekap.";
                    LblIdentifier.Text = "ID Administrator PKL:";
                    TxtIdentifier.Placeholder = "Masukkan ID Administrator atau Email";
                    break;
            }
        }

        private void ResetButtonStyle(Button btn, string text)
        {
            btn.BackgroundColor = Color.FromArgb("#F1F5F9");
            btn.TextColor = Color.FromArgb("#475569");
        }

        private void SetSelectedButtonStyle(Button btn, string hexColor)
        {
            btn.BackgroundColor = Color.FromArgb(hexColor);
            btn.TextColor = Colors.White;
        }

        private void OnTogglePasswordClicked(object? sender, EventArgs e)
        {
            TxtPassword.IsPassword = !TxtPassword.IsPassword;
        }

        private async void OnLoginClicked(object? sender, EventArgs e)
        {
            var id = TxtIdentifier.Text?.Trim() ?? string.Empty;
            var pass = TxtPassword.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrEmpty(id))
            {
                await DisplayAlertAsync("Peringatan", "Silakan masukkan nomor identitas (NISN/NIP) atau email Anda.", "OK");
                return;
            }

            if (string.IsNullOrEmpty(pass))
            {
                await DisplayAlertAsync("Peringatan", "Silakan masukkan kata sandi akun Anda.", "OK");
                return;
            }

            var result = AuthService.Current.Login(id, pass, _selectedRole);
            if (!result.Success)
            {
                await DisplayAlertAsync("Gagal Masuk", result.Message, "OK");
                return;
            }

            // Arahkan ke dashboard utama setelah autentikasi berhasil
            if (Application.Current?.Windows.Count > 0)
            {
                Application.Current.Windows[0].Page = new AppShell();
            }
        }

        private async void OnRegisterTapped(object? sender, EventArgs e)
        {
            await Navigation.PushAsync(new RegisterPage());
        }

        private async void OnForgotPasswordTapped(object? sender, EventArgs e)
        {
            await DisplayAlertAsync("Bantuan Akun PKL", 
                "Untuk reset kata sandi atau kendala NISN/NIP, silakan hubungi Koordinator BKK PKL di Ruang Tata Usaha atau WhatsApp: 0812-3456-7890.", 
                "Tutup");
        }
    }
}
