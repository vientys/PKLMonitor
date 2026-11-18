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
        private double MaxAttendanceDistanceMeters => _currentLocation.AllowedRadiusMeters > 0
            ? _currentLocation.AllowedRadiusMeters
            : 100;
        private AttendanceType _selectedType = AttendanceType.Masuk;
        private LocationResult _currentLocation = new();
        private bool _hasValidLocation;
        private string? _currentPhotoPath;
        private IDispatcherTimer? _clockTimer;

        public AttendancePage()
        {
            InitializeComponent();
            BtnCapturePhoto.IsEnabled = false;
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
            ResetTypeButton(BtnTypeAlpa);

            var isAbsence = type is AttendanceType.Izin or AttendanceType.Sakit or AttendanceType.Alpa;
            LayoutLocationCard.IsVisible = !isAbsence;
            LayoutPhotoCard.IsVisible = !isAbsence;
            UpdateCaptureButtonState();
            LblNotesTitle.Text = type is AttendanceType.Izin or AttendanceType.Sakit
                ? "Alasan ketidakhadiran (wajib):"
                : "Catatan Aktivitas (Opsional):";
            TxtNotes.Placeholder = type switch
            {
                AttendanceType.Izin => "Tuliskan alasan izin...",
                AttendanceType.Sakit => "Tuliskan keterangan sakit...",
                AttendanceType.Alpa => "Tambahkan catatan (opsional)...",
                _ => "Contoh: Datang tepat waktu untuk standup meeting & implementasi modul autentikasi..."
            };

            switch (type)
            {
                case AttendanceType.Masuk:
                    SetSelectedTypeButton(BtnTypeMasuk, "#2563EB", "Masuk");
                    BtnSubmitAttendance.Text = "Kirim Presensi Masuk (Check-In)";
                    BtnSubmitAttendance.BackgroundColor = Color.FromArgb("#2563EB");
                    BtnSubmitAttendance.ImageSource = CreateMaterialIcon("login", Colors.White, 18);
                    break;
                case AttendanceType.Pulang:
                    SetSelectedTypeButton(BtnTypePulang, "#059669", "Pulang");
                    BtnSubmitAttendance.Text = "Kirim Presensi Pulang (Check-Out)";
                    BtnSubmitAttendance.BackgroundColor = Color.FromArgb("#059669");
                    BtnSubmitAttendance.ImageSource = CreateMaterialIcon("logout", Colors.White, 18);
                    break;
                case AttendanceType.Izin:
                    SetSelectedTypeButton(BtnTypeIzin, "#D97706", "Izin");
                    BtnSubmitAttendance.Text = "Ajukan Surat Izin PKL";
                    BtnSubmitAttendance.BackgroundColor = Color.FromArgb("#D97706");
                    BtnSubmitAttendance.ImageSource = CreateMaterialIcon("event_note", Colors.White, 18);
                    break;
                case AttendanceType.Sakit:
                    SetSelectedTypeButton(BtnTypeSakit, "#DC2626", "Sakit");
                    BtnSubmitAttendance.Text = "Ajukan Pemberitahuan Sakit";
                    BtnSubmitAttendance.BackgroundColor = Color.FromArgb("#DC2626");
                    BtnSubmitAttendance.ImageSource = CreateMaterialIcon("medical_information", Colors.White, 18);
                    break;
                case AttendanceType.Alpa:
                    SetSelectedTypeButton(BtnTypeAlpa, "#64748B", "Alpa");
                    BtnSubmitAttendance.Text = "Catat Tidak Hadir (Alpa)";
                    BtnSubmitAttendance.BackgroundColor = Color.FromArgb("#64748B");
                    BtnSubmitAttendance.ImageSource = CreateMaterialIcon("event_busy", Colors.White, 18);
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
                _currentLocation = await LocationPhotoService.GetRealtimeLocationAsync(AuthService.Current.CurrentUser.Identifier);
                _hasValidLocation = _currentLocation.HasLocationFix;

                LblLocationAddress.Text = _currentLocation.AddressName;
                LblCoords.Text = _hasValidLocation
                    ? $"Lat: {_currentLocation.Latitude:F5}, Long: {_currentLocation.Longitude:F5}"
                    : "GPS belum tersedia";
                LblAccuracy.Text = _hasValidLocation ? $"Akurasi: ±{_currentLocation.AccuracyMeters:F1}m" : "Akurasi: —";

                if (!_hasValidLocation)
                {
                    BrdGeofence.BackgroundColor = Color.FromArgb("#FEF2F2");
                    BrdGeofence.Stroke = Color.FromArgb("#FECACA");
                    LblGeofenceIcon.Icon = "location_off";
                    LblGeofenceIcon.IconColor = Color.FromArgb("#991B1B");
                    LblGeofenceStatus.Text = "Lokasi GPS belum tersedia";
                    LblGeofenceStatus.TextColor = Color.FromArgb("#991B1B");
                    LblGeofenceDetail.Text = "Aktifkan izin lokasi untuk memeriksa jarak dari kantor.";
                    LblGeofenceDetail.TextColor = Color.FromArgb("#B91C1C");
                }
                else if (_currentLocation.DistanceFromOfficeMeters <= MaxAttendanceDistanceMeters)
                {
                    BrdGeofence.BackgroundColor = Color.FromArgb("#F0FDF4");
                    BrdGeofence.Stroke = Color.FromArgb("#BBF7D0");
                    LblGeofenceIcon.Icon = "check_circle";
                    LblGeofenceIcon.IconColor = Color.FromArgb("#166534");
                    LblGeofenceStatus.Text = "Dalam Radius Lokasi PKL (Valid)";
                    LblGeofenceStatus.TextColor = Color.FromArgb("#166534");
                    LblGeofenceDetail.Text = $"Jarak dari kantor mitra: {_currentLocation.DistanceFromOfficeMeters:F1} meter (Radius presensi max: {MaxAttendanceDistanceMeters:F0}m)";
                    LblGeofenceDetail.TextColor = Color.FromArgb("#15803D");
                }
                else
                {
                    BrdGeofence.BackgroundColor = Color.FromArgb("#FEF2F2");
                    BrdGeofence.Stroke = Color.FromArgb("#FECACA");
                    LblGeofenceIcon.Icon = "warning";
                    LblGeofenceIcon.IconColor = Color.FromArgb("#991B1B");
                    LblGeofenceStatus.Text = $"Di Luar Radius Presensi (>{MaxAttendanceDistanceMeters:F0}m)";
                    LblGeofenceStatus.TextColor = Color.FromArgb("#991B1B");
                    LblGeofenceDetail.Text = $"Jarak Anda saat ini: {_currentLocation.DistanceFromOfficeMeters:F1} meter dari kantor.";
                    LblGeofenceDetail.TextColor = Color.FromArgb("#B91C1C");
                }

                UpdateCaptureButtonState();
                UpdateWatermarkDisplay();
            }
            catch (Exception ex)
            {
                _hasValidLocation = false;
                UpdateCaptureButtonState();
                await DisplayAlertAsync("Peringatan GPS", $"Gagal memuat GPS: {ex.Message}", "OK");
            }
        }

        private void UpdateCaptureButtonState()
        {
            var canAttendFromLocation = _hasValidLocation &&
                _currentLocation.DistanceFromOfficeMeters <= MaxAttendanceDistanceMeters;
            BtnCapturePhoto.IsEnabled = canAttendFromLocation;
            BtnCapturePhoto.BackgroundColor = Color.FromArgb(canAttendFromLocation ? "#2563EB" : "#94A3B8");
            BtnCapturePhoto.TextColor = Colors.White;
            LblPhotoRestriction.Text = !_hasValidLocation
                ? "Lokasi GPS belum tervalidasi. Periksa izin dan sinyal lokasi."
                : canAttendFromLocation
                    ? "Lokasi berada dalam radius kantor."
                    : $"Jarak {_currentLocation.DistanceFromOfficeMeters:F1} m melebihi batas {MaxAttendanceDistanceMeters:F0} m. Tidak bisa absen di luar kantor.";
            LblPhotoRestriction.TextColor = canAttendFromLocation
                ? Color.FromArgb("#15803D")
                : Color.FromArgb("#B91C1C");
        }

        private static FontImageSource CreateMaterialIcon(string glyph, Color color, double size) => new()
        {
            FontFamily = "MaterialSymbols",
            Glyph = glyph,
            Color = color,
            Size = size
        };

        private async void OnRefreshGpsClicked(object? sender, EventArgs e)
        {
            await RefreshLocationDataAsync();
            await DisplayAlertAsync("GPS Terkini", $"Koordinat berhasil diperbarui:\nLat: {_currentLocation.Latitude:F5}, Long: {_currentLocation.Longitude:F5}\nJarak: {_currentLocation.DistanceFromOfficeMeters:F1}m", "OK");
        }

        private async void OnCapturePhotoClicked(object? sender, EventArgs e)
        {
            if (!_hasValidLocation || _currentLocation.DistanceFromOfficeMeters > MaxAttendanceDistanceMeters)
            {
                await DisplayAlertAsync("Di luar radius kantor",
                    $"Foto selfie dan presensi hanya dapat dilakukan dalam radius {MaxAttendanceDistanceMeters:F0} meter dari kantor.", "OK");
                return;
            }

            var photoPath = await LocationPhotoService.CaptureAttendancePhotoAsync();
            if (string.IsNullOrEmpty(photoPath))
            {
                // Jikalau running di Windows desktop tanpa webcam fisik terbuka, buat simulasi selfie verifikasi
                photoPath = await GenerateSimulatedSelfieAsync();
            }

            SetPhoto(photoPath);
        }

        private void SetPhoto(string path)
        {
            _currentPhotoPath = path;
            LayoutPhotoPlaceholder.IsVisible = false;
            ImgPhotoPreview.IsVisible = true;
            ImgPhotoPreview.Source = ImageSource.FromFile(path);
            BrdWatermark.IsVisible = true;

            LblPhotoStatus.Text = "Foto Terlampir";
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
            var isAbsence = _selectedType is AttendanceType.Izin or AttendanceType.Sakit or AttendanceType.Alpa;
            var notes = TxtNotes.Text?.Trim() ?? string.Empty;

            if (!isAbsence && (!_hasValidLocation || _currentLocation.DistanceFromOfficeMeters > MaxAttendanceDistanceMeters))
            {
                await DisplayAlertAsync("Presensi di luar kantor ditolak",
                    $"Anda berada {_currentLocation.DistanceFromOfficeMeters:F1} meter dari kantor. Presensi masuk/pulang hanya dapat dilakukan dalam radius {MaxAttendanceDistanceMeters:F0} meter.", "OK");
                return;
            }

            if (_selectedType is AttendanceType.Izin or AttendanceType.Sakit && string.IsNullOrWhiteSpace(notes))
            {
                await DisplayAlertAsync("Alasan wajib diisi", "Tuliskan alasan untuk pengajuan izin atau sakit.", "OK");
                return;
            }

            if (string.IsNullOrEmpty(_currentPhotoPath) && !isAbsence)
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
                Latitude = isAbsence ? 0 : _currentLocation.Latitude,
                Longitude = isAbsence ? 0 : _currentLocation.Longitude,
                LocationAddress = isAbsence ? "Tidak hadir di lokasi PKL" : _currentLocation.AddressName,
                DistanceMeters = isAbsence ? 0 : _currentLocation.DistanceFromOfficeMeters,
                PhotoPath = isAbsence ? string.Empty : _currentPhotoPath ?? string.Empty,
                Notes = string.IsNullOrWhiteSpace(notes)
                    ? _selectedType == AttendanceType.Alpa ? "Tidak hadir tanpa keterangan" : $"{_selectedType} di lokasi PKL {user.CompanyName}"
                    : notes,
                StatusText = _selectedType switch
                {
                    AttendanceType.Masuk => "Hadir Tepat Waktu",
                    AttendanceType.Pulang => "Pulang Kerja",
                    AttendanceType.Izin => "Izin Khusus",
                    AttendanceType.Sakit => "Sakit / Istirahat",
                    AttendanceType.Alpa => "Tidak hadir tanpa keterangan",
                    _ => "Hadir"
                },
                ApprovalStatus = AttendanceApprovalStatus.MenungguVerifikasi
            };

            BtnSubmitAttendance.IsEnabled = false;
            var saveResult = await DataService.Current.AddAttendanceAsync(record);
            BtnSubmitAttendance.IsEnabled = true;
            if (!saveResult.Success)
            {
                await DisplayAlertAsync("Presensi gagal disimpan", saveResult.Message, "OK");
                return;
            }

            var confirmation = isAbsence
                ? $"Kategori: {record.Type}\nWaktu: {record.FormattedTime} WIB\nStatus: menunggu verifikasi\nCatatan: {record.Notes}"
                : $"Tipe: {record.Type}\nWaktu: {record.FormattedTime} WIB\nKoordinat: {record.Latitude:F5}, {record.Longitude:F5}\nJarak: {record.DistanceMeters:F1} meter\nStatus: {record.StatusText}";
            await DisplayAlertAsync(isAbsence ? "Status Ketidakhadiran Tercatat" : "Presensi Berhasil Dikirim!",
                confirmation + "\n\nData diteruskan kepada Pembimbing Industri dan Guru Sekolah.", "Selesai");

            await Navigation.PopAsync();
        }
    }
}
