using System;
using Microsoft.Maui.Controls;
using Richikiwir.Models;
using Richikiwir.Services;

namespace Richikiwir.Views
{
    public partial class RegisterPage : ContentPage
    {
        public RegisterPage()
        {
            InitializeComponent();
            PkrRole.SelectedIndex = 0; // Default to Siswa
        }

        private void OnRolePickerChanged(object? sender, EventArgs e)
        {
            switch (PkrRole.SelectedIndex)
            {
                case 0: // Siswa
                    LblRegIdentifier.Text = "NISN (10 Digit Angka):";
                    TxtRegIdentifier.Placeholder = "Contoh: 0068912345";
                    break;
                case 1: // Pembimbing Industri
                    LblRegIdentifier.Text = "ID Karyawan / NIK Industri:";
                    TxtRegIdentifier.Placeholder = "Contoh: IND-TECH-099";
                    break;
                case 2: // Pembimbing Sekolah
                    LblRegIdentifier.Text = "NIP (Nomor Induk Pegawai Guru):";
                    TxtRegIdentifier.Placeholder = "Contoh: 19800101 200501 1 003";
                    break;
                case 3: // Monitor
                    LblRegIdentifier.Text = "NIP / ID Tim Monitoring:";
                    TxtRegIdentifier.Placeholder = "Contoh: 19820304 200902 1 005";
                    break;
                case 4: // Admin
                    LblRegIdentifier.Text = "ID Administrator PKL:";
                    TxtRegIdentifier.Placeholder = "Contoh: ADM-SMKN1-099";
                    break;
            }
        }

        private async void OnBackTapped(object? sender, EventArgs e)
        {
            await Navigation.PopAsync();
        }

        private async void OnSubmitRegisterClicked(object? sender, EventArgs e)
        {
            var fullName = TxtFullName.Text?.Trim() ?? string.Empty;
            var identifier = TxtRegIdentifier.Text?.Trim() ?? string.Empty;
            var email = TxtEmail.Text?.Trim() ?? string.Empty;
            var password = TxtRegPassword.Text?.Trim() ?? string.Empty;
            var school = TxtSchool.Text?.Trim() ?? string.Empty;
            var company = TxtCompany.Text?.Trim() ?? string.Empty;
            var major = TxtMajor.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(identifier))
            {
                await DisplayAlertAsync("Peringatan", "Mohon lengkapi Nama Lengkap dan Nomor Identitas (NISN/NIP).", "OK");
                return;
            }

            if (string.IsNullOrWhiteSpace(password) || password.Length < 4)
            {
                await DisplayAlertAsync("Peringatan", "Kata sandi wajib diisi minimal 4 karakter.", "OK");
                return;
            }

            if (!ChkTerms.IsChecked)
            {
                await DisplayAlertAsync("Peringatan", "Anda wajib menyetujui SOP dan ketentuan pelaksanaan PKL.", "OK");
                return;
            }

            var role = PkrRole.SelectedIndex switch
            {
                0 => UserRole.Siswa,
                1 => UserRole.PembimbingIndustri,
                2 => UserRole.PembimbingSekolah,
                3 => UserRole.Monitor,
                4 => UserRole.Admin,
                _ => UserRole.Siswa
            };

            var result = await AuthService.Current.RegisterAsync(fullName, identifier, email, password, role, school, company, major);
            if (!result.Success)
            {
                await DisplayAlertAsync("Pendaftaran Gagal", result.Message, "OK");
                return;
            }

            await DisplayAlertAsync("Pendaftaran Berhasil", 
                result.Message, 
                "Lanjutkan");

            if (Application.Current?.Windows.Count > 0)
            {
                Application.Current.Windows[0].Page = new AppShell();
            }
        }
    }
}
