using System;
using Microsoft.Maui.Controls;
using Richikiwir.Models;
using Richikiwir.Services;

namespace Richikiwir.Views
{
    public partial class SettingPage : ContentPage
    {
        public SettingPage()
        {
            InitializeComponent();
            AuthService.Current.CurrentUserChanged += OnUserChanged;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            UpdateProfileDisplay(AuthService.Current.CurrentUser);
        }

        private void OnUserChanged(UserModel user)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                UpdateProfileDisplay(user);
            });
        }

        private void UpdateProfileDisplay(UserModel user)
        {
            if (user == null) return;

            LblProfileName.Text = user.FullName;
            LblProfileId.Text = $"{user.IdentifierLabel}: {user.Identifier}";
            LblProfileRole.Text = user.RoleDisplayName;
            LblProfileInitials.Text = user.AvatarInitials;

            BrdProfileAvatar.BackgroundColor = Color.FromArgb(user.RoleBadgeColor);
            BrdProfileRole.BackgroundColor = Color.FromArgb(user.RoleBadgeColor);

            LblProfileSchool.Text = user.SchoolName;
            LblProfileCompany.Text = user.CompanyName;
            LblProfileMajor.Text = user.MajorOrDepartment;
            LblProfileEmail.Text = user.Email;
        }

        private async void OnDownloadSopClicked(object? sender, EventArgs e)
        {
            await DisplayAlertAsync("Buku Panduan PKL", 
                "Buku Panduan & Pedoman Praktik Kerja Lapangan 2026 versi digital telah diunduh ke perangkat Anda.", 
                "Buka Dokumen");
        }

        private async void OnContactCenterClicked(object? sender, EventArgs e)
        {
            await DisplayAlertAsync("BKK Sekolah SMKN 1", 
                "Koordinator Hubungan Industri (BKK):\nTelepon: (021) 7890-1234\nWhatsApp BKK: 0812-3456-7890\nJam Operasional: Senin - Jumat (07:30 - 16:00 WIB)", 
                "Tutup");
        }

        private async void OnLogoutClicked(object? sender, EventArgs e)
        {
            var confirm = await DisplayAlertAsync("Konfirmasi Keluar", 
                "Apakah Anda yakin ingin keluar dari aplikasi SIM-PKL?", 
                "Ya, Keluar", "Batal");

            if (confirm)
            {
                AuthService.Current.Logout();

                if (Application.Current?.Windows.Count > 0)
                {
                    Application.Current.Windows[0].Page = new NavigationPage(new LoginPage());
                }
            }
        }
    }
}
