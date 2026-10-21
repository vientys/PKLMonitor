using System;
using System.Collections.Generic;

namespace Richikiwir.Api.Models
{
    public enum UserRole
    {
        Siswa = 0,
        PembimbingIndustri = 1,
        PembimbingSekolah = 2,
        Monitor = 3,
        Admin = 4
    }

    public enum AttendanceType
    {
        Masuk,
        Pulang,
        Izin,
        Sakit
    }

    public enum AttendanceApprovalStatus
    {
        Menunggu,
        Disetujui,
        Ditolak
    }

    public class UserModel
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string FullName { get; set; } = string.Empty;
        public string Identifier { get; set; } = string.Empty; // NISN / NIP / NIK
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public UserRole Role { get; set; } = UserRole.Siswa;
        public string SchoolName { get; set; } = "SMK Negeri 1 Rekayasa Teknologi";
        public string CompanyName { get; set; } = "PT Solusi Digital Nusantara";
        public string MajorOrDepartment { get; set; } = "XII RPL 1";
        public string PhoneNumber { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public string RoleDisplayName => Role switch
        {
            UserRole.Siswa => "Siswa PKL",
            UserRole.PembimbingIndustri => "Pembimbing Industri (DU/DI)",
            UserRole.PembimbingSekolah => "Guru Pembimbing Sekolah",
            UserRole.Monitor => "Koordinator Lapangan & BKK",
            UserRole.Admin => "Administrator PKL",
            _ => "Pengguna"
        };
    }

    public class AttendanceRecord
    {
        public string Id { get; set; } = "att-" + Guid.NewGuid().ToString("N")[..8];
        public string StudentId { get; set; } = string.Empty;
        public string StudentName { get; set; } = string.Empty;
        public string StudentNisn { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public AttendanceType Type { get; set; } = AttendanceType.Masuk;
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string LocationAddress { get; set; } = string.Empty;
        public double DistanceMeters { get; set; }
        public string Notes { get; set; } = string.Empty;
        public string StatusText { get; set; } = "Hadir Tepat Waktu";
        public AttendanceApprovalStatus ApprovalStatus { get; set; } = AttendanceApprovalStatus.Menunggu;
        public string VerifiedBy { get; set; } = "-";
        public string? PhotoBase64 { get; set; }

        public string FormattedTime => Timestamp.ToString("HH:mm");
        public string FormattedDate => Timestamp.ToString("dd MMMM yyyy");
    }

    public class LogbookEntry
    {
        public string Id { get; set; } = "log-" + Guid.NewGuid().ToString("N")[..8];
        public string StudentName { get; set; } = string.Empty;
        public string StudentNisn { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public DateTime Date { get; set; } = DateTime.Today;
        public string ActivityTitle { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsApproved { get; set; }
        public string ApprovedBy { get; set; } = "-";
        public string? Feedback { get; set; }
    }

    public class StudentInternSummary
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string StudentId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Nisn { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public string IndustryMentorName { get; set; } = string.Empty;
        public string SchoolMentorName { get; set; } = string.Empty;
        public string Major { get; set; } = string.Empty;
        public double AttendancePercentage { get; set; }
        public string TodayAttendanceStatus { get; set; } = "Belum Presensi";
        public string TodayCheckInTime { get; set; } = "-";
        public int CompletedLogbooks { get; set; }
        public int TotalLogbooksExpected { get; set; } = 40;
        public string MonitoringStatus { get; set; } = "Aktif";
    }

    // DTO Requests & Responses
    public class LoginRequest
    {
        public string Identifier { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public UserRole? PreferredRole { get; set; }
    }

    public class RegisterRequest
    {
        public string FullName { get; set; } = string.Empty;
        public string Identifier { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public UserRole Role { get; set; } = UserRole.Siswa;
        public string SchoolName { get; set; } = "SMK Negeri 1 Rekayasa Teknologi";
        public string CompanyName { get; set; } = "PT Solusi Digital Nusantara";
        public string MajorOrDepartment { get; set; } = "XII Rekayasa Perangkat Lunak 1";
        public string PhoneNumber { get; set; } = string.Empty;
    }

    public class AttendanceCheckInRequest
    {
        public string StudentId { get; set; } = string.Empty;
        public string StudentName { get; set; } = string.Empty;
        public string StudentNisn { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string? LocationAddress { get; set; }
        public double DistanceMeters { get; set; }
        public string? Notes { get; set; }
        public AttendanceType Type { get; set; } = AttendanceType.Masuk;
        public string? PhotoBase64 { get; set; }
    }

    public class LogbookCreateRequest
    {
        public string StudentName { get; set; } = string.Empty;
        public string StudentNisn { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public string ActivityTitle { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public class ApprovalRequest
    {
        public string ApprovedBy { get; set; } = string.Empty;
        public bool Approve { get; set; } = true;
        public string? Notes { get; set; }
    }

    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public T? Data { get; set; }

        public static ApiResponse<T> Ok(T data, string message = "Berhasil") => new() { Success = true, Message = message, Data = data };
        public static ApiResponse<T> Fail(string message) => new() { Success = false, Message = message, Data = default };
    }

    public class UserStatsResponse
    {
        public int TotalDays { get; set; }
        public int HadirCount { get; set; }
        public int IzinCount { get; set; }
        public double AttendanceRate { get; set; }
        public int TotalLogbooks { get; set; }
    }
}