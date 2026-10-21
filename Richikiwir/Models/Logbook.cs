using System;

namespace Richikiwir.Models
{
    public class LogbookEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string StudentName { get; set; } = string.Empty;
        public string StudentNisn { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public DateTime Date { get; set; } = DateTime.Today;
        public string ActivityTitle { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string CompetencyField { get; set; } = "Pengembangan Aplikasi / Desain UI";
        public bool IsApproved { get; set; }
        public string MentorNotes { get; set; } = string.Empty;
        public int Score { get; set; } = 85;

        public string FormattedDate => Date.ToString("dd MMM yyyy");
    }

    public class StudentInternSummary
    {
        public string StudentId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Nisn { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public string IndustryMentorName { get; set; } = string.Empty;
        public string SchoolMentorName { get; set; } = string.Empty;
        public string Major { get; set; } = "Rekayasa Perangkat Lunak";
        public double AttendancePercentage { get; set; } = 96.5;
        public string TodayAttendanceStatus { get; set; } = "Hadir";
        public string TodayCheckInTime { get; set; } = "07:45 WIB";
        public int CompletedLogbooks { get; set; } = 28;
        public int TotalLogbooksExpected { get; set; } = 30;
        public string MonitoringStatus { get; set; } = "Terpantau Baik";
        public string AvatarInitials => !string.IsNullOrEmpty(FullName) ?
            string.Concat(FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(s => s[0])).ToUpper() : "SW";
    }
}
