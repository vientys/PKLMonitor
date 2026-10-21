using System;

namespace Richikiwir.Models
{
    public enum UserRole
    {
        Siswa = 0,
        PembimbingIndustri = 1,
        PembimbingSekolah = 2,
        Admin = 4
    }

    public class UserModel
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public UserRole Role { get; set; }
        public string RoleDisplayName => Role switch
        {
            UserRole.Siswa => "Siswa PKL",
            UserRole.PembimbingIndustri => "Pembimbing Industri (DU/DI)",
            UserRole.PembimbingSekolah => "Pembimbing Sekolah (Guru)",
            UserRole.Admin => "Administrator PKL",
            _ => "Pengguna"
        };

        public string Identifier { get; set; } = string.Empty; // NIS / NIP / ID Karyawan / Username
        public string Username { get; set; } = string.Empty;
        public string JurusanCode { get; set; } = "RPL";
        public string JurusanName { get; set; } = "Rekayasa Perangkat Lunak";
        public string IdentifierLabel => Role switch
        {
            UserRole.Siswa => "NIS (Nomor Induk Siswa)",
            UserRole.PembimbingIndustri => "NIK / ID Karyawan",
            UserRole.PembimbingSekolah => "NIP (Nomor Induk Pegawai)",
            UserRole.Admin => "Username Admin",
            _ => "Identitas Pengguna"
        };

        public string SchoolName { get; set; } = "SMK Negeri 5 Kabupaten Tangerang";
        public string CompanyName { get; set; } = "PT Solusi Digital Nusantara";
        public string MajorOrDepartment { get; set; } = "Rekayasa Perangkat Lunak (RPL)";
        public string PhoneNumber { get; set; } = "0812-3456-7890";
        public string AvatarInitials => !string.IsNullOrEmpty(FullName) ? 
            string.Concat(FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(s => s[0])).ToUpper() : "PKL";

        public string RoleBadgeColor => Role switch
        {
            UserRole.Siswa => "#2563EB",            // Blue
            UserRole.PembimbingIndustri => "#059669",// Emerald
            UserRole.PembimbingSekolah => "#7C3AED", // Purple
            UserRole.Admin => "#DC2626",            // Crimson
            _ => "#4B5563"
        };

        public static UserModel Guest => new()
        {
            Id = "guest",
            FullName = "Pengguna",
            Email = "-",
            Role = UserRole.Siswa,
            Identifier = "-",
            SchoolName = "SMK Negeri 5 Kabupaten Tangerang",
            CompanyName = "Mitra Industri",
            MajorOrDepartment = "Kompetensi Keahlian"
        };
    }
}
