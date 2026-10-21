using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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

        // Akun lokal fallback
        private readonly List<UserModel> _localUsers = new();

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

        public async Task<(bool Success, string Message)> LoginAsync(string identifier, string password, UserRole? preferredRole = null, string? jurusan = null)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                return (false, "Nomor identitas login (NIS/NIP/NIK/Username) tidak boleh kosong.");
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                return (false, "Kata sandi tidak boleh kosong.");
            }

            var trimmedId = identifier.Trim();

            // 1. Prioritaskan autentikasi ke server Database API (Normalisasi 3NF & Direktori Jurusan)
            try
            {
                var apiRes = await ApiService.Current.LoginAsync(trimmedId, password, preferredRole, jurusan);
                if (apiRes.Success && apiRes.User != null)
                {
                    _currentUser = apiRes.User;
                    _currentUser.Password = password;
                    CurrentUserChanged?.Invoke(_currentUser);
                    return (true, apiRes.Message);
                }

                // Jika server merespon dengan pesan kesalahan validasi (misal role mismatch atau salah sandi)
                if (!string.IsNullOrEmpty(apiRes.Message) && !apiRes.Message.Contains("Gagal menghubungi database server"))
                {
                    return (false, apiRes.Message);
                }
            }
            catch
            {
                // Fallback ke pengecekan lokal jika offline
            }

            // 2. Fallback offline: Cek pengguna lokal di memori
            var matchedUser = _localUsers.FirstOrDefault(x =>
                NormalizeAndCompare(x.Identifier, trimmedId) ||
                string.Equals(x.Email.Trim(), trimmedId, StringComparison.OrdinalIgnoreCase));

            if (matchedUser == null)
            {
                return (false, "Akun tidak ditemukan. Periksa kembali NIS/NIP Anda atau hubungi Koordinator PKL.");
            }

            if (preferredRole.HasValue && matchedUser.Role != preferredRole.Value)
            {
                return (false, $"Identitas terdaftar sebagai '{matchedUser.RoleDisplayName}', bukan '{preferredRole.Value}'. Akses ditolak.");
            }

            bool isPasswordCorrect = string.Equals(matchedUser.Password, password, StringComparison.Ordinal);
            if (!isPasswordCorrect)
            {
                return (false, "Kata sandi yang Anda masukkan salah.");
            }

            _currentUser = matchedUser;
            CurrentUserChanged?.Invoke(_currentUser);
            return (true, "Berhasil masuk secara offline.");
        }

        public (bool Success, string Message) Login(string identifier, string password, UserRole? preferredRole = null, string? jurusan = null)
        {
            try
            {
                var task = Task.Run(() => LoginAsync(identifier, password, preferredRole, jurusan));
                task.Wait(TimeSpan.FromSeconds(6));
                return task.Result;
            }
            catch (AggregateException ae) when (ae.InnerException != null)
            {
                return (false, ae.InnerException.Message);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public void Logout()
        {
            _currentUser = null;
            CurrentUserChanged?.Invoke(CurrentUser);
        }
    }
}
