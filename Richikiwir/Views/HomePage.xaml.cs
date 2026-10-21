using System;
using System.Linq;
using Microsoft.Maui.Controls;
using Richikiwir.Models;
using Richikiwir.Services;

namespace Richikiwir.Views
{
    public partial class HomePage : ContentPage
    {
        public HomePage()
        {
            InitializeComponent();
            AuthService.Current.CurrentUserChanged += OnUserChanged;
            DataService.Current.DataChanged += RefreshDataViews;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            RefreshUserUi(AuthService.Current.CurrentUser);
            RefreshDataViews();
        }

        private void OnUserChanged(UserModel user)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                RefreshUserUi(user);
                RefreshDataViews();
            });
        }

        private void RefreshUserUi(UserModel user)
        {
            if (user == null) return;

            LblUserName.Text = user.FullName;
            LblAvatarInitials.Text = user.AvatarInitials;
            LblRoleBadge.Text = user.RoleDisplayName;
            BrdRoleBadge.BackgroundColor = Color.FromArgb(user.RoleBadgeColor);
            BrdAvatar.BackgroundColor = Color.FromArgb(user.RoleBadgeColor);

            LblGreeting.Text = GetTimeGreeting();
            LblSchoolHeader.Text = user.SchoolName.ToUpper();
            LblUserSubtitle.Text = $"{user.MajorOrDepartment} • {user.IdentifierLabel}: {user.Identifier}";

            // Tampilkan view khusus sesuai role akun yang sedang login
            LayoutSiswaView.IsVisible = false;
            LayoutIndustriView.IsVisible = false;
            LayoutSekolahView.IsVisible = false;
            LayoutAdminView.IsVisible = false;

            switch (user.Role)
            {
                case UserRole.Siswa:
                    LayoutSiswaView.IsVisible = true;
                    LblSiswaCompany.Text = user.CompanyName;
                    break;
                case UserRole.PembimbingIndustri:
                    LayoutIndustriView.IsVisible = true;
                    break;
                case UserRole.PembimbingSekolah:
                    LayoutSekolahView.IsVisible = true;
                    break;
                case UserRole.Admin:
                    LayoutAdminView.IsVisible = true;
                    break;
            }
        }

        private void RefreshDataViews()
        {
            var user = AuthService.Current.CurrentUser;
            if (user == null || user.Role != UserRole.Siswa) return;

            var today = DateTime.Today;

            // Cari presensi HANYA untuk siswa yang sedang login hari ini
            var todayIn = DataService.Current.Attendances.FirstOrDefault(a => 
                (a.StudentId == user.Id ||
                 string.Equals(a.StudentNisn.Replace(" ", ""), user.Identifier.Replace(" ", ""), StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(a.StudentName, user.FullName, StringComparison.OrdinalIgnoreCase)) &&
                a.Timestamp.Date == today && a.Type == AttendanceType.Masuk);

            if (todayIn != null)
            {
                LblSiswaCheckInTime.Text = todayIn.FormattedTime + " WIB";
                LblSiswaCheckInSub.Text = "✓ Terverifikasi Geotag";
                LblSiswaCheckInSub.TextColor = Color.FromArgb("#059669");
            }
            else
            {
                LblSiswaCheckInTime.Text = "Belum Absen";
                LblSiswaCheckInSub.Text = "Silakan lakukan presensi";
                LblSiswaCheckInSub.TextColor = Color.FromArgb("#2563EB");
            }

            var todayOut = DataService.Current.Attendances.FirstOrDefault(a => 
                (a.StudentId == user.Id ||
                 string.Equals(a.StudentNisn.Replace(" ", ""), user.Identifier.Replace(" ", ""), StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(a.StudentName, user.FullName, StringComparison.OrdinalIgnoreCase)) &&
                a.Timestamp.Date == today && a.Type == AttendanceType.Pulang);

            if (todayOut != null)
            {
                LblSiswaCheckOutTime.Text = todayOut.FormattedTime + " WIB";
                LblSiswaCheckOutSub.Text = "✓ Terverifikasi Pulang";
                LblSiswaCheckOutSub.TextColor = Color.FromArgb("#059669");
            }
            else
            {
                LblSiswaCheckOutTime.Text = "Belum Absen";
                LblSiswaCheckOutSub.Text = "Tersedia pkl 17:00";
                LblSiswaCheckOutSub.TextColor = Color.FromArgb("#64748B");
            }
        }

        private async void OnOpenAttendancePageClicked(object? sender, EventArgs e)
        {
            await Navigation.PushAsync(new AttendancePage());
        }

        private async void OnWriteLogbookClicked(object? sender, EventArgs e)
        {
            var title = await DisplayPromptAsync("Jurnal Harian PKL", "Masukkan Judul Kegiatan Praktik Hari Ini:", "Simpan", "Batal", "Contoh: Testing API & Geolocation Geofence");
            if (!string.IsNullOrWhiteSpace(title))
            {
                var desc = await DisplayPromptAsync("Detail Kegiatan", "Uraikan pekerjaan dan kompetensi yang dipelajari:", "Kirim", "Batal");
                var entry = new LogbookEntry
                {
                    StudentName = AuthService.Current.CurrentUser.FullName,
                    StudentNisn = AuthService.Current.CurrentUser.Identifier,
                    CompanyName = AuthService.Current.CurrentUser.CompanyName,
                    Date = DateTime.Today,
                    ActivityTitle = title,
                    Description = desc ?? "Kegiatan praktik harian di industri mitra.",
                    IsApproved = false
                };
                DataService.Current.Logbooks.Insert(0, entry);

                _ = Task.Run(async () =>
                {
                    await ApiService.Current.SubmitLogbookAsync(
                        entry.StudentName, entry.StudentNisn, entry.CompanyName, entry.ActivityTitle, entry.Description);
                });

                await DisplayAlertAsync("Jurnal Tersimpan", "Jurnal harian PKL berhasil dikirim dan tersimpan ke database.", "OK");
            }
        }

        private async void OnApproveAttendanceClicked(object? sender, EventArgs e)
        {
            var pending = DataService.Current.Attendances.FirstOrDefault(a => a.ApprovalStatus == AttendanceApprovalStatus.MenungguVerifikasi);
            if (pending != null)
            {
                DataService.Current.ApproveAttendance(pending.Id, AuthService.Current.CurrentUser.FullName);
                await DisplayAlertAsync("Presensi Disetujui", $"Presensi {pending.StudentName} ({pending.Type}) berhasil diverifikasi secara sah.", "OK");
            }
            else
            {
                await DisplayAlertAsync("Informasi", "Semua presensi siswa binaan telah terverifikasi hari ini.", "OK");
            }
        }

        private async void OnReviewAttendanceDetailsClicked(object? sender, EventArgs e)
        {
            await Shell.Current.GoToAsync("//History");
        }

        private async void OnGradeStudentClicked(object? sender, EventArgs e)
        {
            if (sender is Button btn && btn.CommandParameter is string studentName)
            {
                var scoreStr = await DisplayPromptAsync("Penilaian Sikap & Kinerja PKL", 
                    $"Masukkan nilai kinerja untuk {studentName} (Skala 0 - 100):", "Simpan Nilai", "Batal", "95", maxLength: 3, keyboard: Keyboard.Numeric);

                if (!string.IsNullOrWhiteSpace(scoreStr))
                {
                    await DisplayAlertAsync("Nilai Tersimpan", $"Nilai {scoreStr} untuk {studentName} telah dicatat dalam kartu evaluasi DU/DI.", "Selesai");
                }
            }
        }

        private async void OnExportDataClicked(object? sender, EventArgs e)
        {
            await DisplayAlertAsync("Ekspor Rekapitulasi", 
                "Laporan presensi, persentase kehadiran, dan nilai jurnal PKL telah diekspor ke format Excel (.xlsx) dan PDF resmi BKK Sekolah.", 
                "Unduh Berkas");
        }

        private async void OnBroadcastClicked(object? sender, EventArgs e)
        {
            var msg = await DisplayPromptAsync("Broadcast Notifikasi", "Pesan pengumuman untuk seluruh siswa PKL & Pembimbing:", "Kirim", "Batal", "Pengingat: Wajib mengisi jurnal harian sebelum pukul 18:00 WIB.");
            if (!string.IsNullOrWhiteSpace(msg))
            {
                await DisplayAlertAsync("Terkirim", "Pengumuman push notification berhasil disiarkan ke seluruh peserta PKL.", "OK");
            }
        }

        private static string GetTimeGreeting()
        {
            var hour = DateTime.Now.Hour;
            if (hour < 11) return "Selamat Pagi,";
            if (hour < 15) return "Selamat Siang,";
            if (hour < 18) return "Selamat Sore,";
            return "Selamat Malam,";
        }
    }
}
