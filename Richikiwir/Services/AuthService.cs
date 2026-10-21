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
                return (false, "Akun tidak terdaftar. Periksa kembali NISN/NIP atau hubungi administrator PKL.");
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

        public void Logout()
        {
            _currentUser = null;
            CurrentUserChanged?.Invoke(CurrentUser);
        }
    }
}
