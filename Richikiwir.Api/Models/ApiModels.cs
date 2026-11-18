using System;
using System.Collections.Generic;

namespace Richikiwir.Api.Models
{
    // ==========================================
    // ENUMS
    // ==========================================
    public enum UserRole
    {
        Siswa = 0,
        PembimbingIndustri = 1,
        PembimbingSekolah = 2,
        Admin = 4
    }

    public enum AttendanceType
    {
        Masuk,
        Pulang,
        Izin,
        Sakit,
        Alpa = 4
    }

    public enum AttendanceApprovalStatus
    {
        Menunggu,
        Disetujui,
        Ditolak
    }

    // ==========================================
    // 3NF RELATIONAL DATABASE ENTITIES (LEVEL 3)
    // ==========================================

    /// <summary>
    /// Master Sekolah (3NF: Entitas mandiri tanpa redundansi data sekolah)
    /// </summary>
    public class Sekolah
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string NamaSekolah { get; set; } = "SMK Negeri 5 Kabupaten Tangerang";
        public string Npsn { get; set; } = "20614748";
        public string Alamat { get; set; } = "Jl. Raya Salembaran No. 01, Kosambi, Kab. Tangerang, Banten";
        public string NoTelepon { get; set; } = "(021) 5593-1234";
        public string Email { get; set; } = "info@smkn5kabtangerang.sch.id";

        // Navigasi Relasi
        public List<Jurusan> DaftarJurusan { get; set; } = new();
        public List<GuruProfile> DaftarGuru { get; set; } = new();
    }

    /// <summary>
    /// Master Jurusan / Kompetensi Keahlian (3NF: Bergantung langsung pada Sekolah)
    /// </summary>
    public class Jurusan
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        public string SekolahId { get; set; } = string.Empty;
        public Sekolah? Sekolah { get; set; }

        public string KodeJurusan { get; set; } = "RPL";
        public string NamaJurusan { get; set; } = "Rekayasa Perangkat Lunak";

        // Navigasi Relasi
        public List<Kelas> DaftarKelas { get; set; } = new();
    }

    /// <summary>
    /// Master Kelas / Rombongan Belajar (3NF: Bergantung langsung pada Jurusan)
    /// </summary>
    public class Kelas
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string JurusanId { get; set; } = string.Empty;
        public Jurusan? Jurusan { get; set; }

        public string NamaKelas { get; set; } = "XII RPL 1";
        public int Tingkat { get; set; } = 12;

        // Navigasi Relasi
        public List<SiswaProfile> DaftarSiswa { get; set; } = new();
    }

    /// <summary>
    /// Master Industri / Mitra DU/DI (3NF: Menyimpan lokasi geofence & profil industri tanpa redundansi)
    /// </summary>
    public class MitraIndustri
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string NamaPerusahaan { get; set; } = "PT Solusi Digital Nusantara";
        public string BidangUsaha { get; set; } = "Teknologi Informasi & Rekayasa Perangkat Lunak";
        public string Alamat { get; set; } = "Gedung Cyber 2 Tower, Jl. H. R. Rasuna Said Blok X-5 No. 13, Jakarta Selatan";
        public double Latitude { get; set; } = -6.22412;
        public double Longitude { get; set; } = 106.83154;
        public double RadiusMeters { get; set; } = 100.0;
        public string JamMasuk { get; set; } = "08:00";
        public string JamPulang { get; set; } = "17:00";
        public string KontakPerson { get; set; } = "Divisi Kerjasama Industri & HRD";
        public string NoTelepon { get; set; } = "021-5299-8800";

        // Navigasi Relasi
        public List<SiswaProfile> DaftarSiswa { get; set; } = new();
        public List<MentorIndustriProfile> DaftarMentor { get; set; } = new();
    }

    /// <summary>
    /// Akun Pengguna & Kredensial Login (3NF: Memisahkan autentikasi dari atribut spesifik peran)
    /// </summary>
    public class UserAccount
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Kredensial Login Unik Sesuai Peran:
        /// - Siswa: NIS (Nomor Induk Siswa, misal: 20241001)
        /// - Pembimbing Sekolah: NIP (Nomor Induk Pegawai, misal: 198503152010011002)
        /// - Pembimbing Industri: NIK / ID Karyawan (misal: SDN-2024-089)
        /// - Admin: Username Administrator (misal: admin_pkl)
        /// </summary>
        public string Username { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public UserRole Role { get; set; } = UserRole.Siswa;
        public bool IsActive { get; set; } = true;
        public bool MustCompleteProfile { get; set; } = true;
        public bool MustChangePassword { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // One-to-One Navigasi Relasi ke Profil Khusus
        public SiswaProfile? SiswaProfile { get; set; }
        public GuruProfile? GuruProfile { get; set; }
        public MentorIndustriProfile? MentorIndustriProfile { get; set; }

        public string RoleDisplayName => Role switch
        {
            UserRole.Siswa => "Siswa PKL",
            UserRole.PembimbingIndustri => "Pembimbing Industri (DU/DI)",
            UserRole.PembimbingSekolah => "Guru Pembimbing Sekolah",
            UserRole.Admin => "Administrator PKL",
            _ => "Pengguna"
        };
    }

    /// <summary>
    /// Profil Khusus Siswa PKL (3NF: Menghilangkan transitive dependency pada UserAccount)
    /// </summary>
    public class SiswaProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string UserId { get; set; } = string.Empty;
        public UserAccount? User { get; set; }

        public string Nis { get; set; } = string.Empty;  // Nomor Induk Siswa (Username login utama)
        public string Nisn { get; set; } = string.Empty; // Nomor Induk Siswa Nasional (10 digit)

        public string KelasId { get; set; } = string.Empty;
        public Kelas? Kelas { get; set; }

        public string MitraIndustriId { get; set; } = string.Empty;
        public MitraIndustri? MitraIndustri { get; set; }

        public string? GuruPembimbingId { get; set; }
        public UserAccount? GuruPembimbing { get; set; }

        public string? MentorIndustriId { get; set; }
        public UserAccount? MentorIndustri { get; set; }

        public string TahunPkl { get; set; } = "2026/2027";
        public string StatusPkl { get; set; } = "Aktif";

        // Navigasi Relasi Presensi & Jurnal
        public List<PresensiRecord> DaftarPresensi { get; set; } = new();
        public List<JurnalKegiatan> DaftarJurnal { get; set; } = new();
    }

    /// <summary>
    /// Profil Khusus Guru Pembimbing Sekolah (3NF)
    /// </summary>
    public class GuruProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string UserId { get; set; } = string.Empty;
        public UserAccount? User { get; set; }

        public string Nip { get; set; } = string.Empty; // Nomor Induk Pegawai 18 digit (Username login guru)

        public string SekolahId { get; set; } = string.Empty;
        public Sekolah? Sekolah { get; set; }

        public string Jabatan { get; set; } = "Guru Pembimbing PKL";
    }

    /// <summary>
    /// Profil Khusus Pembimbing Industri / DU-DI (3NF)
    /// </summary>
    public class MentorIndustriProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string UserId { get; set; } = string.Empty;
        public UserAccount? User { get; set; }

        public string NikKaryawan { get; set; } = string.Empty; // ID Karyawan / NIK (Username login mentor)

        public string MitraIndustriId { get; set; } = string.Empty;
        public MitraIndustri? MitraIndustri { get; set; }

        public string Divisi { get; set; } = "Software Engineering";
    }

    /// <summary>
    /// Transaksi Presensi Harian (3NF: Hanya bergantung pada SiswaProfile & Industri, bebas redundansi nama/sekolah)
    /// </summary>
    public class PresensiRecord
    {
        public string Id { get; set; } = "att-" + Guid.NewGuid().ToString("N")[..8];

        public string SiswaProfileId { get; set; } = string.Empty;
        public SiswaProfile? SiswaProfile { get; set; }

        public string MitraIndustriId { get; set; } = string.Empty;
        public MitraIndustri? MitraIndustri { get; set; }

        public AttendanceType Type { get; set; } = AttendanceType.Masuk;
        public DateTime Timestamp { get; set; } = DateTime.Now;

        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string LocationAddress { get; set; } = string.Empty;
        public double DistanceMeters { get; set; }
        public int LateMinutes { get; set; }
        public bool IsLate { get; set; }
        public bool IsWithinRadius { get; set; } = true;
        public string Notes { get; set; } = string.Empty;
        public AttendanceApprovalStatus ApprovalStatus { get; set; } = AttendanceApprovalStatus.Menunggu;

        public string? VerifiedByUserId { get; set; }
        public UserAccount? VerifiedByUser { get; set; }
        public DateTime? VerifiedAt { get; set; }
        public string? VerificationNotes { get; set; }

        public string? PhotoBase64 { get; set; }
    }

    /// <summary>
    /// Transaksi Jurnal / Logbook Kegiatan (3NF: Hanya bergantung pada SiswaProfile)
    /// </summary>
    public class JurnalKegiatan
    {
        public string Id { get; set; } = "log-" + Guid.NewGuid().ToString("N")[..8];

        public string SiswaProfileId { get; set; } = string.Empty;
        public SiswaProfile? SiswaProfile { get; set; }

        public DateTime Date { get; set; } = DateTime.Today;
        public string ActivityTitle { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string CompetencyField { get; set; } = "Rekayasa Perangkat Lunak";

        public bool IsApproved { get; set; } = false;
        public string? ApprovedByUserId { get; set; }
        public UserAccount? ApprovedByUser { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public string? MentorFeedback { get; set; }
        public int? Score { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    // ==========================================
    // DTO REQUESTS & RESPONSES (API CONTRACT)
    // ==========================================

    /// <summary>
    /// Model Pengguna DTO untuk respon API ke Mobile App & Web Frontend
    /// </summary>
    public class UserModel
    {
        public string Id { get; set; } = string.Empty;
        public string AccessToken { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Identifier { get; set; } = string.Empty; // NIS / NIP / NIK / Username
        public string Nisn { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public UserRole Role { get; set; } = UserRole.Siswa;
        public string SchoolName { get; set; } = "SMK Negeri 5 Kabupaten Tangerang";
        public string CompanyName { get; set; } = "PT Solusi Digital Nusantara";
        public string MajorOrDepartment { get; set; } = "XII RPL 1";
        public string JurusanCode { get; set; } = "RPL";
        public string JurusanName { get; set; } = "Rekayasa Perangkat Lunak";
        public string PhoneNumber { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; set; } = true;
        public bool MustCompleteProfile { get; set; }
        public bool MustChangePassword { get; set; }

        public string RoleDisplayName => Role switch
        {
            UserRole.Siswa => "Siswa PKL",
            UserRole.PembimbingIndustri => "Pembimbing Industri (DU/DI)",
            UserRole.PembimbingSekolah => "Guru Pembimbing Sekolah",
            UserRole.Admin => "Administrator PKL",
            _ => "Pengguna"
        };
    }

    /// <summary>
    /// Respon DTO untuk Presensi (digabungkan secara otomatis dari relasi 3NF)
    /// </summary>
    public class AttendanceRecord
    {
        public string Id { get; set; } = string.Empty;
        public string StudentId { get; set; } = string.Empty;
        public string StudentName { get; set; } = string.Empty;
        public string StudentNisn { get; set; } = string.Empty;
        public string StudentNis { get; set; } = string.Empty;
        public string JurusanCode { get; set; } = string.Empty;
        public string JurusanName { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public AttendanceType Type { get; set; } = AttendanceType.Masuk;
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string LocationAddress { get; set; } = string.Empty;
        public double DistanceMeters { get; set; }
        public int LateMinutes { get; set; }
        public bool IsLate { get; set; }
        public string Notes { get; set; } = string.Empty;
        public string StatusText { get; set; } = "Hadir Tepat Waktu";
        public AttendanceApprovalStatus ApprovalStatus { get; set; } = AttendanceApprovalStatus.Menunggu;
        public string VerifiedBy { get; set; } = "-";
        public string? PhotoBase64 { get; set; }

        public string FormattedTime => Timestamp.ToString("HH:mm");
        public string FormattedDate => Timestamp.ToString("dd MMMM yyyy");
    }

    /// <summary>
    /// Respon DTO untuk Jurnal Kegiatan (digabungkan secara otomatis dari relasi 3NF)
    /// </summary>
    public class LogbookEntry
    {
        public string Id { get; set; } = string.Empty;
        public string StudentName { get; set; } = string.Empty;
        public string StudentNisn { get; set; } = string.Empty;
        public string StudentNis { get; set; } = string.Empty;
        public string JurusanCode { get; set; } = string.Empty;
        public string JurusanName { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public DateTime Date { get; set; } = DateTime.Today;
        public string ActivityTitle { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsApproved { get; set; }
        public string ApprovedBy { get; set; } = "-";
        public string? Feedback { get; set; }
    }

    /// <summary>
    /// Respon DTO untuk Ringkasan Siswa Magang (dihitung dinamis dari database 3NF)
    /// </summary>
    public class StudentInternSummary
    {
        public string Id { get; set; } = string.Empty;
        public string StudentId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Nis { get; set; } = string.Empty;
        public string Nisn { get; set; } = string.Empty;
        public string JurusanCode { get; set; } = string.Empty;
        public string JurusanName { get; set; } = string.Empty;
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

    public class LoginRequest
    {
        /// <summary>
        /// Pengenal login:
        /// - Siswa: NIS (atau NISN)
        /// - Guru: NIP
        /// - Mentor Industri: NIK / ID Karyawan
        /// - Admin: Username Admin
        /// </summary>
        public string Identifier { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public UserRole? PreferredRole { get; set; }
        public string? Jurusan { get; set; } // RPL, DKV, PH, TKJ, TSM, TKR, TPM, MP, AK, APHPI
    }

    public class AdminUserRequest
    {
        public string FullName { get; set; } = string.Empty;
        public string Identifier { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Nisn { get; set; } = string.Empty;
        public string JurusanCode { get; set; } = string.Empty;
        public UserRole Role { get; set; } = UserRole.Siswa;
    }

    public class FirstLoginSetupRequest
    {
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }

    public class JurusanDirectoryDto
    {
        public string Kode { get; set; } = string.Empty; // RPL, DKV, PH, TKJ, TSM, TKR, TPM, MP, AK, APHPI
        public string Nama { get; set; } = string.Empty;
        public string DirectoryPath { get; set; } = string.Empty;
        public int TotalSiswa { get; set; }
        public int TotalPresensi { get; set; }
        public int TotalLogbook { get; set; }
        public List<string> DaftarKelas { get; set; } = new();
    }

    public class AttendanceCheckInRequest
    {
        public string StudentId { get; set; } = string.Empty;
        public string StudentName { get; set; } = string.Empty;
        public string StudentNisn { get; set; } = string.Empty;
        public string? StudentNis { get; set; }
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
        public string? StudentNis { get; set; }
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
        public int SakitCount { get; set; }
        public int AlpaCount { get; set; }
        public double AttendanceRate { get; set; }
        public int TotalLogbooks { get; set; }
    }
}
