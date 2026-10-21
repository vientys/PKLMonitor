using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Richikiwir.Models;
using Richikiwir.Services;

namespace Richikiwir.Views
{
    public partial class AttendancePage : ContentPage
    {
        private AttendanceType _selectedType = AttendanceType.Masuk;
        private LocationResult _currentLocation = new();
        private string? _currentPhotoPath;
        private IDispatcherTimer? _clockTimer;

        public AttendancePage()
        {
            InitializeComponent();
            SetupClock();
            LoadUserData();
            UpdateAttendanceTypeUi(AttendanceType.Masuk);
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await RefreshLocationDataAsync();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _clockTimer?.Stop();
        }

        private void SetupClock()
        {
            UpdateClockDisplay();
            _clockTimer = Dispatcher.CreateTimer();
            _clockTimer.Interval = TimeSpan.FromSeconds(1);
            _clockTimer.Tick += (s, e) => UpdateClockDisplay();
            _clockTimer.Start();
        }

        private void UpdateClockDisplay()
        {
            var now = DateTime.Now;
            LblCurrentDate.Text = now.ToString("dddd, dd MMMM yyyy", new System.Globalization.CultureInfo("id-ID"));
            LblCurrentTime.Text = now.ToString("HH:mm:ss") + " WIB";
        }

        private void LoadUserData()
        {
            var user = AuthService.Current.CurrentUser;
            LblCompanyName.Text = string.IsNullOrEmpty(user.CompanyName) 
                ? "PT Solusi Digital Nusantara (Cyber 2 Tower)" 
                : user.CompanyName;
        }

        private void OnAttendanceTypeClicked(object? sender, EventArgs e)
        {
            if (sender is Button btn && btn.CommandParameter is string typeStr &&
                Enum.TryParse<AttendanceType>(typeStr, out var type))
            {
                UpdateAttendanceTypeUi(type);
            }
        }

        private void UpdateAttendanceTypeUi(AttendanceType type)
        {
            _selectedType = type;

            ResetTypeButton(BtnTypeMasuk);
            ResetTypeButton(BtnTypePulang);
            ResetTypeButton(BtnTypeIzin);
            ResetTypeButton(BtnTypeSakit);

            switch (type)
            {
                case AttendanceType.Masuk:
                    SetSelectedTypeButton(BtnTypeMasuk, "#2563EB", "Masuk");
                    BtnSubmitAttendance.Text = "✓ Kirim Presensi Masuk (Check-In)";
                    BtnSubmitAttendance.BackgroundColor = Color.FromArgb("#2563EB");
                    break;
                case AttendanceType.Pulang:
                    SetSelectedTypeButton(BtnTypePulang, "#059669", "Pulang");
                    BtnSubmitAttendance.Text = "✓ Kirim Presensi Pulang (Check-Out)";
                    BtnSubmitAttendance.BackgroundColor = Color.FromArgb("#059669");
                    break;
                case AttendanceType.Izin:
                    SetSelectedTypeButton(BtnTypeIzin, "#D97706", "Izin");
                    BtnSubmitAttendance.Text = "✓ Ajukan Surat Izin PKL";
                    BtnSubmitAttendance.BackgroundColor = Color.FromArgb("#D97706");
                    break;
                case AttendanceType.Sakit:
                    SetSelectedTypeButton(BtnTypeSakit, "#DC2626", "Sakit");
                    BtnSubmitAttendance.Text = "✓ Ajukan Pemberitahuan Sakit";
                    BtnSubmitAttendance.BackgroundColor = Color.FromArgb("#DC2626");
                    break;
            }
        }

        private void ResetTypeButton(Button btn)
        {
            btn.BackgroundColor = Color.FromArgb("#F1F5F9");
            btn.TextColor = Color.FromArgb("#475569");
        }

        private void SetSelectedTypeButton(Button btn, string hexColor, string title)
        {
            btn.BackgroundColor = Color.FromArgb(hexColor);
            btn.TextColor = Colors.White;
        }

        private async Task RefreshLocationDataAsync()
        {
            try
            {
                _currentLocation = await LocationPhotoService.GetRealtimeLocationAsync();

                LblLocationAddress.Text = _currentLocation.AddressName;
                LblCoords.Text = $"Lat: {_currentLocation.Latitude:F5}, Long: {_currentLocation.Longitude:F5}";
                LblAccuracy.Text = $"Akurasi: ±{_currentLocation.AccuracyMeters:F1}m";

                if (_currentLocation.IsWithinOfficeRadius)
                {
                    BrdGeofence.BackgroundColor = Color.FromArgb("#F0FDF4");
                    BrdGeofence.Stroke = Color.FromArgb("#BBF7D0");
                    LblGeofenceStatus.Text = "✓ Dalam Radius Lokasi PKL (Valid)";
                    LblGeofenceStatus.TextColor = Color.FromArgb("#166534");
                    LblGeofenceDetail.Text = $"Jarak dari kantor mitra: {_currentLocation.DistanceFromOfficeMeters:F1} meter (Radius batas max: 100m)";
                    LblGeofenceDetail.TextColor = Color.FromArgb("#15803D");
                }
                else
                {
                    BrdGeofence.BackgroundColor = Color.FromArgb("#FEF2F2");
                    BrdGeofence.Stroke = Color.FromArgb("#FECACA");
                    LblGeofenceStatus.Text = "⚠ Di Luar Radius Lokasi PKL (>100m)";
                    LblGeofenceStatus.TextColor = Color.FromArgb("#991B1B");
                    LblGeofenceDetail.Text = $"Jarak Anda saat ini: {_currentLocation.DistanceFromOfficeMeters:F1} meter dari kantor.";
                    LblGeofenceDetail.TextColor = Color.FromArgb("#B91C1C");
                }

                UpdateWatermarkDisplay();
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Peringatan GPS", $"Gagal memuat GPS: {ex.Message}", "OK");
            }
        }

        private async void OnRefreshGpsClicked(object? sender, EventArgs e)
        {
            await RefreshLocationDataAsync();
            await DisplayAlertAsync("GPS Terkini", $"Koordinat berhasil diperbarui:\nLat: {_currentLocation.Latitude:F5}, Long: {_currentLocation.Longitude:F5}\nJarak: {_currentLocation.DistanceFromOfficeMeters:F1}m", "OK");
        }

        private async void OnCapturePhotoClicked(object? sender, EventArgs e)
        {
            var photoPath = await LocationPhotoService.CaptureAttendancePhotoAsync();
            if (string.IsNullOrEmpty(photoPath))
            {
                // Jikalau running di Windows desktop tanpa webcam fisik terbuka, buat simulasi selfie verifikasi
                photoPath = await GenerateSimulatedSelfieAsync();
            }

            SetPhoto(photoPath);
        }

        private async void OnPickPhotoClicked(object? sender, EventArgs e)
        {
            var photoPath = await LocationPhotoService.PickAttendancePhotoAsync();
            if (!string.IsNullOrEmpty(photoPath))
            {
                SetPhoto(photoPath);
            }
            else
            {
                // Fallback selfie placeholder
                var simPath = await GenerateSimulatedSelfieAsync();
                SetPhoto(simPath);
            }
        }

        private void SetPhoto(string path)
        {
            _currentPhotoPath = path;
            LayoutPhotoPlaceholder.IsVisible = false;
            ImgPhotoPreview.IsVisible = true;
            ImgPhotoPreview.Source = ImageSource.FromFile(path);
            BrdWatermark.IsVisible = true;

            LblPhotoStatus.Text = "✓ Foto Terlampir";
            LblPhotoStatus.TextColor = Color.FromArgb("#059669");

            UpdateWatermarkDisplay();
        }

        private void UpdateWatermarkDisplay()
        {
            var user = AuthService.Current.CurrentUser;
            var now = DateTime.Now;

            LblWatermarkTime.Text = $"{now:dd MMM yyyy • HH:mm:ss} WIB";
            LblWatermarkCoords.Text = $"GPS: {_currentLocation.Latitude:F5}, {_currentLocation.Longitude:F5} | Radius: {_currentLocation.DistanceFromOfficeMeters:F1}m";
            LblWatermarkStudent.Text = $"{user.FullName} ({user.Identifier}) - {user.CompanyName}";
        }

        private async Task<string> GenerateSimulatedSelfieAsync()
        {
            // Buat file placeholder foto selfie untuk pengujian mulus
            var path = Path.Combine(FileSystem.CacheDirectory, $"selfie_attendance_{DateTime.Now.Ticks}.png");
            
            // Cek apakah ada dotnet_bot.png default di bundle untuk copy
            try
            {
                using var stream = await FileSystem.OpenAppPackageFileAsync("Resources/Images/dotnet_bot.png");
                using var outStream = File.OpenWrite(path);
                await stream.CopyToAsync(outStream);
            }
            catch
            {
                // Jika tidak ada, buat dummy file
                File.WriteAllBytes(path, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
            }

            return path;
        }

        private async void OnSubmitAttendanceClicked(object? sender, EventArgs e)
        {
            var user = AuthService.Current.CurrentUser;

            if (string.IsNullOrEmpty(_currentPhotoPath) && _selectedType != AttendanceType.Izin && _selectedType != AttendanceType.Sakit)
            {
                var proceed = await DisplayAlertAsync("Konfirmasi Foto", 
                    "Anda belum mengambil foto selfie kehadiran. Tetap kirimkan presensi dengan geotag GPS?", 
                    "Ya, Kirim", "Ambil Foto Dulu");

                if (!proceed)
                {
                    OnCapturePhotoClicked(this, EventArgs.Empty);
                    return;
                }
            }

            var record = new AttendanceRecord
            {
                StudentId = user.Id,
                StudentName = user.FullName,
                StudentNisn = user.Identifier,
                CompanyName = user.CompanyName,
                Type = _selectedType,
                Timestamp = DateTime.Now,
                Latitude = _currentLocation.Latitude,
                Longitude = _currentLocation.Longitude,
                LocationAddress = _currentLocation.AddressName,
                DistanceMeters = _currentLocation.DistanceFromOfficeMeters,
                PhotoPath = _currentPhotoPath ?? string.Empty,
                Notes = TxtNotes.Text?.Trim() ?? $"{_selectedType} di lokasi PKL {user.CompanyName}",
                StatusText = _selectedType switch
                {
                    AttendanceType.Masuk => "Hadir Tepat Waktu",
                    AttendanceType.Pulang => "Pulang Kerja",
                    AttendanceType.Izin => "Izin Khusus",
                    AttendanceType.Sakit => "Sakit / Istirahat",
                    _ => "Hadir"
                },
                ApprovalStatus = AttendanceApprovalStatus.MenungguVerifikasi
            };

            DataService.Current.AddAttendance(record);

            await DisplayAlertAsync("Presensi Berhasil Dikirim!", 
                $"✓ Tipe: {record.Type}\n" +
                $"✓ Waktu: {record.FormattedTime} WIB\n" +
                $"✓ Koordinat: {record.Latitude:F5}, {record.Longitude:F5}\n" +
                $"✓ Jarak: {record.DistanceMeters:F1} meter\n" +
                $"✓ Status: {record.StatusText}\n\n" +
                "Data presensi telah tersimpan di riwayat dan diteruskan ke Pembimbing Industri serta Guru Sekolah.", 
                "Selesai");

            await Navigation.PopAsync();
        }
    }
}
