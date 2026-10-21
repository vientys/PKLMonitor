using System;
using System.Collections.Generic;
using System.Linq;
using Richikiwir.Models;

namespace Richikiwir.Services
{
    public class AuthService
    {
        private static AuthService? _instance;
        public static AuthService Current => _instance ??= new AuthService();

        public event Action<UserModel>? CurrentUserChanged;

        private UserModel? _currentUser;
        public UserModel CurrentUser => _currentUser ?? UserModel.Guest;
        public bool IsAuthenticated => _currentUser != null;

        // Daftar akun lokal aplikasi (dimulai dari kosong, terisi saat pengguna mendaftar)
        private readonly List<UserModel> _users = new();

        public AuthService()
        {
            _currentUser = null;
        }

        private static bool NormalizeAndCompare(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            var cleanA = a.Replace(" ", "").Replace("-", "").Trim();
            var cleanB = b.Replace(" ", "").Replace("-", "").Trim();
            return string.Equals(cleanA, cleanB, StringComparison.OrdinalIgnoreCase);
        }

        public (bool Success, string Message) Login(string identifier, string password, UserRole? preferredRole = null)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                return (false, "Nomor identitas (NISN/NIP/ID) atau email tidak boleh kosong.");
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                return (false, "Kata sandi tidak boleh kosong.");
            }

            var trimmedId = identifier.Trim();

            // Cari pengguna berdasarkan identitas atau email di memori lokal
            var matchedUser = _users.FirstOrDefault(x =>
                NormalizeAndCompare(x.Identifier, trimmedId) ||
                string.Equals(x.Email.Trim(), trimmedId, StringComparison.OrdinalIgnoreCase));

            // Jika belum ada di memori lokal, coba verifikasi ke server Database API
            if (matchedUser == null)
            {
                try
                {
                    var task = Task.Run(() => ApiService.Current.LoginAsync(trimmedId, password, preferredRole));
                    task.Wait(TimeSpan.FromSeconds(3));
                    if (task.IsCompletedSuccessfully && task.Result.Success && task.Result.User != null)
                    {
                        matchedUser = task.Result.User;
                        _users.Add(matchedUser);
                    }
                }
                catch { }
            }

            if (matchedUser == null)
            {
                return (false, "Akun tidak terdaftar. Periksa kembali NISN/NIP atau silakan daftar akun baru terlebih dahulu.");
            }

            // Validasi peran jika dipilih
            if (preferredRole.HasValue && matchedUser.Role != preferredRole.Value)
            {
                return (false, $"Identitas terdaftar sebagai '{matchedUser.RoleDisplayName}', bukan '{preferredRole.Value}'. Silakan pilih peran yang tepat.");
            }

            // Validasi kata sandi sesuai akun yang didaftarkan
            bool isPasswordCorrect = string.Equals(matchedUser.Password, password, StringComparison.Ordinal);

            if (!isPasswordCorrect)
            {
                return (false, "Kata sandi yang Anda masukkan salah.");
            }

            // Berhasil autentikasi
            _currentUser = matchedUser;
            CurrentUserChanged?.Invoke(_currentUser);
            return (true, "Berhasil masuk.");
        }

        public async Task<(bool Success, string Message)> RegisterAsync(string fullName, string identifier, string email, string password, UserRole role, string schoolName, string companyName, string major)
        {
            if (string.IsNullOrWhiteSpace(fullName))
            {
                return (false, "Nama lengkap wajib diisi.");
            }

            if (string.IsNullOrWhiteSpace(identifier))
            {
                return (false, "Nomor identitas (NISN/NIP) wajib diisi.");
            }

            if (string.IsNullOrWhiteSpace(password) || password.Length < 4)
            {
                return (false, "Kata sandi minimal 4 karakter.");
            }

            var trimmedId = identifier.Trim();

            // 1. Simpan ke database pusat melalui Web API
            var apiResult = await ApiService.Current.RegisterAsync(fullName, trimmedId, email, password, role, schoolName, companyName, major);

            // 2. Simpan ke cache lokal aplikasi
            var newUser = apiResult.User ?? new UserModel
            {
                FullName = fullName,
                Identifier = trimmedId,
                Email = string.IsNullOrWhiteSpace(email) ? $"{trimmedId}@smkn1pkl.sch.id" : email.Trim(),
                Password = password,
                Role = role,
                SchoolName = string.IsNullOrWhiteSpace(schoolName) ? "SMK Negeri 1 Rekayasa Teknologi" : schoolName.Trim(),
                CompanyName = string.IsNullOrWhiteSpace(companyName) ? "PT Solusi Digital Nusantara" : companyName.Trim(),
                MajorOrDepartment = string.IsNullOrWhiteSpace(major) ? "Kompetensi Keahlian" : major.Trim()
            };

            if (!_users.Any(u => NormalizeAndCompare(u.Identifier, trimmedId)))
            {
                _users.Add(newUser);
            }

            if (role == UserRole.Siswa)
            {
                if (!DataService.Current.Students.Any(s => NormalizeAndCompare(s.Nisn, trimmedId)))
                {
                    DataService.Current.Students.Add(new StudentInternSummary
                    {
                        StudentId = newUser.Id,
                        FullName = newUser.FullName,
                        Nisn = newUser.Identifier,
                        CompanyName = newUser.CompanyName,
                        IndustryMentorName = "-",
                        SchoolMentorName = "-",
                        Major = newUser.MajorOrDepartment,
                        AttendancePercentage = 0.0,
                        TodayAttendanceStatus = "Belum Presensi",
                        TodayCheckInTime = "-",
                        CompletedLogbooks = 0,
                        TotalLogbooksExpected = 40,
                        MonitoringStatus = "Siswa Baru"
                    });
                }
            }

            _currentUser = newUser;
            CurrentUserChanged?.Invoke(_currentUser);

            if (apiResult.Success)
            {
                return (true, "Registrasi akun berhasil dan tersimpan permanen di Database!");
            }
            else
            {
                return (true, $"Akun tersimpan di HP. Catatan sinkronisasi database: {apiResult.Message}");
            }
        }

        public (bool Success, string Message) Register(string fullName, string identifier, string email, string password, UserRole role, string schoolName, string companyName, string major)
        {
            return Task.Run(() => RegisterAsync(fullName, identifier, email, password, role, schoolName, companyName, major)).GetAwaiter().GetResult();
        }

        public void Logout()
        {
            _currentUser = null;
            CurrentUserChanged?.Invoke(CurrentUser);
        }
    }
}
