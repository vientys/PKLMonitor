using System;

namespace Richikiwir.Models
{
    public enum AttendanceType
    {
        Masuk,
        Pulang,
        Izin,
        Sakit
    }

    public enum AttendanceApprovalStatus
    {
        MenungguVerifikasi,
        Disetujui,
        Ditolak
    }

    public class AttendanceRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string StudentId { get; set; } = string.Empty;
        public string StudentName { get; set; } = string.Empty;
        public string StudentNisn { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public AttendanceType Type { get; set; } = AttendanceType.Masuk;
        public DateTime Timestamp { get; set; } = DateTime.Now;

        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string LocationAddress { get; set; } = string.Empty;
        public double DistanceMeters { get; set; } = 35.0;
        public bool IsWithinGeofence => DistanceMeters <= 100.0; // Radius toleransi 100 meter

        public string PhotoPath { get; set; } = string.Empty;
        public bool HasPhoto => !string.IsNullOrEmpty(PhotoPath);
        public string WatermarkText => $"{Timestamp:dd MMM yyyy HH:mm:ss} WIB | Lat: {Latitude:F5}, Long: {Longitude:F5} | {StudentName}";

        public string Notes { get; set; } = string.Empty;
        public string StatusText { get; set; } = "Hadir Tepat Waktu";
        public AttendanceApprovalStatus ApprovalStatus { get; set; } = AttendanceApprovalStatus.MenungguVerifikasi;
        public string VerifiedBy { get; set; } = "-";

        public string TypeBadgeColor => Type switch
        {
            AttendanceType.Masuk => "#2563EB",
            AttendanceType.Pulang => "#059669",
            AttendanceType.Izin => "#D97706",
            AttendanceType.Sakit => "#DC2626",
            _ => "#4B5563"
        };

        public string ApprovalStatusColor => ApprovalStatus switch
        {
            AttendanceApprovalStatus.Disetujui => "#059669",
            AttendanceApprovalStatus.MenungguVerifikasi => "#D97706",
            AttendanceApprovalStatus.Ditolak => "#DC2626",
            _ => "#6B7280"
        };

        public string ApprovalStatusText => ApprovalStatus switch
        {
            AttendanceApprovalStatus.Disetujui => "Terverifikasi Mentor",
            AttendanceApprovalStatus.MenungguVerifikasi => "Menunggu Approval",
            AttendanceApprovalStatus.Ditolak => "Ditolak / Luar Radius",
            _ => "-"
        };

        public string FormattedTime => Timestamp.ToString("HH:mm:ss");
        public string FormattedDate => Timestamp.ToString("dddd, dd MMMM yyyy", new System.Globalization.CultureInfo("id-ID"));
    }
}
