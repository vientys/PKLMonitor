using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Maui.Devices;
using Richikiwir.Models;

namespace Richikiwir.Services
{
    public class ApiService
    {
        private static ApiService? _instance;
        public static ApiService Current => _instance ??= new ApiService();

        private readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions _jsonOptions;

        private string? _activeBaseUrl;

        // Daftar kandidat URL server database
        private static readonly string[] CandidateUrls = new[]
        {
            "http://127.0.0.1:5217", // Prioritas 1: USB Cable (via adb reverse)
            "http://localhost:5217", // Prioritas 2: Localhost Windows
            "http://192.168.1.8:5217", // Prioritas 3: Wi-Fi LAN Komputer
            "http://10.0.2.2:5217"   // Prioritas 4: Android Emulator Loopback
        };

        public ApiService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(5)
            };

            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() }
            };
        }

        public async Task<string> GetWorkingBaseUrlAsync()
        {
            if (!string.IsNullOrEmpty(_activeBaseUrl))
            {
                return _activeBaseUrl;
            }

            // Test setiap URL kandidat secara cepat (timeout 1.5 detik)
            using var pingClient = new HttpClient { Timeout = TimeSpan.FromMilliseconds(1500) };

            foreach (var url in CandidateUrls)
            {
                try
                {
                    var res = await pingClient.GetAsync($"{url}/api/health");
                    if (res.IsSuccessStatusCode)
                    {
                        _activeBaseUrl = url;
                        return _activeBaseUrl;
                    }
                }
                catch
                {
                    // Lanjut ke kandidat berikutnya
                }
            }

            // Fallback default sesuai jenis perangkat
            _activeBaseUrl = DeviceInfo.Platform == DevicePlatform.Android 
                ? (DeviceInfo.DeviceType == DeviceType.Physical ? "http://127.0.0.1:5217" : "http://10.0.2.2:5217")
                : "http://localhost:5217";

            return _activeBaseUrl;
        }

        public async Task<bool> IsApiReachableAsync()
        {
            try
            {
                var baseUrl = await GetWorkingBaseUrlAsync();
                var response = await _httpClient.GetAsync($"{baseUrl}/api/health");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<CompanyGeofence?> GetCompanyGeofenceAsync(string? identifier = null)
        {
            try
            {
                var baseUrl = await GetWorkingBaseUrlAsync();
                var query = string.IsNullOrWhiteSpace(identifier)
                    ? string.Empty
                    : $"?identifier={Uri.EscapeDataString(identifier)}";
                var response = await _httpClient.GetAsync($"{baseUrl}/api/company/geofence{query}");
                if (!response.IsSuccessStatusCode) return null;

                var rawJson = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<ApiResponseWrapper<CompanyGeofence>>(rawJson, _jsonOptions);
                return result?.Success == true ? result.Data : null;
            }
            catch
            {
                return null;
            }
        }

        public async Task<(bool Success, string Message, UserModel? User)> LoginAsync(
            string identifier, string password, UserRole? preferredRole = null, string? jurusan = null)
        {
            try
            {
                var baseUrl = await GetWorkingBaseUrlAsync();
                var payload = new
                {
                    identifier,
                    password,
                    preferredRole = preferredRole?.ToString(),
                    jurusan
                };

                var content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptions), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"{baseUrl}/api/auth/login", content);
                var rawJson = await response.Content.ReadAsStringAsync();

                var result = JsonSerializer.Deserialize<ApiResponseWrapper<UserModel>>(rawJson, _jsonOptions);
                if (result != null && result.Success && result.Data != null)
                {
                    return (true, result.Message, result.Data);
                }

                return (false, result?.Message ?? "Identitas atau kata sandi tidak cocok.", null);
            }
            catch (Exception ex)
            {
                return (false, $"Gagal menghubungi database server: {ex.Message}", null);
            }
        }

        public async Task<(bool Success, string Message)> CompleteFirstLoginAsync(
            string fullName, string email, string phoneNumber, string newPassword)
        {
            var result = await SendAdminRequestAsync<object>(HttpMethod.Put, "/api/account/first-login", new
            {
                fullName,
                email,
                phoneNumber,
                newPassword
            });
            return (result.Success, result.Message);
        }

        public async Task<(bool Success, string Message)> SubmitAttendanceAsync(AttendanceRecord record)
        {
            try
            {
                var baseUrl = await GetWorkingBaseUrlAsync();
                var endpoint = record.Type switch
                {
                    AttendanceType.Pulang => $"{baseUrl}/api/attendance/check-out",
                    AttendanceType.Izin or AttendanceType.Sakit or AttendanceType.Alpa => $"{baseUrl}/api/attendance/absence",
                    _ => $"{baseUrl}/api/attendance/check-in"
                };

                string? photoBase64 = null;
                if (!string.IsNullOrEmpty(record.PhotoPath) && File.Exists(record.PhotoPath))
                {
                    try
                    {
                        var bytes = await File.ReadAllBytesAsync(record.PhotoPath);
                        photoBase64 = Convert.ToBase64String(bytes);
                    }
                    catch
                    {
                        // Ignore photo conversion errors
                    }
                }

                var payload = new
                {
                    studentId = record.StudentId,
                    studentName = record.StudentName,
                    studentNisn = record.StudentNisn,
                    companyName = record.CompanyName,
                    type = record.Type.ToString(),
                    latitude = record.Latitude,
                    longitude = record.Longitude,
                    locationAddress = record.LocationAddress,
                    distanceMeters = record.DistanceMeters,
                    notes = record.Notes,
                    photoBase64 = photoBase64
                };

                var content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptions), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(endpoint, content);
                var rawJson = await response.Content.ReadAsStringAsync();

                var result = JsonSerializer.Deserialize<ApiResponseWrapper<object>>(rawJson, _jsonOptions);
                return (result?.Success ?? false, result?.Message ?? "Presensi berhasil disimpan ke database.");
            }
            catch (Exception ex)
            {
                return (false, $"Gagal mengirim presensi ke database: {ex.Message}");
            }
        }

        public async Task<(bool Success, string Message)> SubmitLogbookAsync(
            string studentName, string studentNisn, string companyName, string activityTitle, string description)
        {
            try
            {
                var baseUrl = await GetWorkingBaseUrlAsync();
                var payload = new
                {
                    studentName,
                    studentNisn,
                    companyName,
                    activityTitle,
                    description
                };

                var content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptions), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"{baseUrl}/api/logbooks", content);
                var rawJson = await response.Content.ReadAsStringAsync();

                var result = JsonSerializer.Deserialize<ApiResponseWrapper<object>>(rawJson, _jsonOptions);
                return (result?.Success ?? false, result?.Message ?? "Jurnal berhasil disimpan ke database.");
            }
            catch (Exception ex)
            {
                return (false, $"Gagal menyimpan jurnal ke database: {ex.Message}");
            }
        }

        public async Task<List<AttendanceRecord>?> GetAttendancesAsync(string? identifier = null)
        {
            try
            {
                var baseUrl = await GetWorkingBaseUrlAsync();
                var url = string.IsNullOrWhiteSpace(identifier) 
                    ? $"{baseUrl}/api/attendance" 
                    : $"{baseUrl}/api/attendance?identifier={Uri.EscapeDataString(identifier)}";

                var response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var rawJson = await response.Content.ReadAsStringAsync();
                    var res = JsonSerializer.Deserialize<ApiResponseWrapper<List<AttendanceRecord>>>(rawJson, _jsonOptions);
                    return res?.Data;
                }
            }
            catch
            {
                // Fallback graceful
            }
            return null;
        }

        public async Task<List<LogbookEntry>?> GetLogbooksAsync(string? identifier = null)
        {
            try
            {
                var baseUrl = await GetWorkingBaseUrlAsync();
                var url = string.IsNullOrWhiteSpace(identifier) 
                    ? $"{baseUrl}/api/logbooks" 
                    : $"{baseUrl}/api/logbooks?identifier={Uri.EscapeDataString(identifier)}";

                var response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var rawJson = await response.Content.ReadAsStringAsync();
                    var res = JsonSerializer.Deserialize<ApiResponseWrapper<List<LogbookEntry>>>(rawJson, _jsonOptions);
                    return res?.Data;
                }
            }
            catch
            {
                // Fallback graceful
            }
            return null;
        }

        public async Task<(bool Success, string Message, List<UserModel> Users)> GetManagedUsersAsync()
        {
            var result = await SendAdminRequestAsync<List<UserModel>>(HttpMethod.Get, "/api/admin/users");
            return (result.Success, result.Message, result.Data ?? new List<UserModel>());
        }

        public async Task<(bool Success, string Message, string TemporaryPassword)> CreateManagedUserAsync(
            UserModel user, string? nisn, string? jurusanCode)
        {
            var result = await SendAdminRequestAsync<AdminUserCreationResult>(HttpMethod.Post, "/api/admin/users", new
            {
                fullName = user.FullName,
                identifier = user.Identifier,
                email = user.Email,
                phoneNumber = user.PhoneNumber,
                role = user.Role.ToString(),
                nisn,
                jurusanCode
            });
            return (result.Success, result.Message, result.Data?.TemporaryPassword ?? string.Empty);
        }

        public async Task<(bool Success, string Message)> UpdateManagedUserAsync(
            UserModel user, string password, string? nisn)
        {
            var result = await SendAdminRequestAsync<object>(HttpMethod.Put, $"/api/admin/users/{Uri.EscapeDataString(user.Id)}", new
            {
                fullName = user.FullName,
                identifier = user.Identifier,
                password,
                email = user.Email,
                phoneNumber = user.PhoneNumber,
                role = user.Role.ToString(),
                nisn
            });
            return (result.Success, result.Message);
        }

        public async Task<(bool Success, string Message)> DeleteManagedUserAsync(string userId)
        {
            var result = await SendAdminRequestAsync<object>(HttpMethod.Delete,
                $"/api/admin/users/{Uri.EscapeDataString(userId)}");
            return (result.Success, result.Message);
        }

        private async Task<(bool Success, string Message, T? Data)> SendAdminRequestAsync<T>(
            HttpMethod method, string path, object? payload = null) where T : class
        {
            try
            {
                var token = AuthService.Current.CurrentUser.AccessToken;
                if (string.IsNullOrWhiteSpace(token))
                    return (false, "Sesi admin tidak tersedia. Silakan keluar lalu masuk kembali.", default);

                var baseUrl = await GetWorkingBaseUrlAsync();
                using var request = new HttpRequestMessage(method, $"{baseUrl}{path}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                if (payload != null)
                {
                    request.Content = new StringContent(
                        JsonSerializer.Serialize(payload, _jsonOptions), Encoding.UTF8, "application/json");
                }

                using var response = await _httpClient.SendAsync(request);
                var rawJson = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<ApiResponseWrapper<T>>(rawJson, _jsonOptions);
                return (response.IsSuccessStatusCode && result?.Success == true,
                    result?.Message ?? $"Permintaan gagal ({(int)response.StatusCode}).", result?.Data);
            }
            catch (Exception ex)
            {
                return (false, $"Gagal menghubungi server: {ex.Message}", default);
            }
        }

        private class ApiResponseWrapper<T>
        {
            public bool Success { get; set; }
            public string Message { get; set; } = string.Empty;
            public T? Data { get; set; }
        }

        private sealed class AdminUserCreationResult
        {
            public string Id { get; set; } = string.Empty;
            public string TemporaryPassword { get; set; } = string.Empty;
        }
    }
}
