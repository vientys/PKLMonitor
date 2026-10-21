using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Richikiwir.Models;

namespace Richikiwir.Services
{
    public class DataService
    {
        private static DataService? _instance;
        public static DataService Current => _instance ??= new DataService();

        public ObservableCollection<AttendanceRecord> Attendances { get; } = new();
        public ObservableCollection<LogbookEntry> Logbooks { get; } = new();
        public ObservableCollection<StudentInternSummary> Students { get; } = new();
        public ObservableCollection<MonitoringVisit> MonitoringVisits { get; } = new();

        public event Action? DataChanged;

        public DataService()
        {
            // Dimulai dari kondisi kosong murni tanpa akun atau data tiruan
        }

        public void AddAttendance(AttendanceRecord record)
        {
            Attendances.Insert(0, record);
            DataChanged?.Invoke();

            // Simpan ke SQLite Database melalui Web API
            _ = Task.Run(async () =>
            {
                await ApiService.Current.SubmitAttendanceAsync(record);
            });
        }

        public async Task SyncWithApiAsync(string? identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier)) return;

            var remoteAttendances = await ApiService.Current.GetAttendancesAsync(identifier);
            if (remoteAttendances != null && remoteAttendances.Count > 0)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    foreach (var remote in remoteAttendances)
                    {
                        if (!Attendances.Any(a => a.Id == remote.Id))
                        {
                            Attendances.Add(remote);
                        }
                    }
                    DataChanged?.Invoke();
                });
            }

            var remoteLogs = await ApiService.Current.GetLogbooksAsync(identifier);
            if (remoteLogs != null && remoteLogs.Count > 0)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    foreach (var log in remoteLogs)
                    {
                        if (!Logbooks.Any(l => l.Id == log.Id))
                        {
                            Logbooks.Add(log);
                        }
                    }
                    DataChanged?.Invoke();
                });
            }
        }

        public void ApproveAttendance(string attendanceId, string mentorName)
        {
            var item = Attendances.FirstOrDefault(x => x.Id == attendanceId);
            if (item != null)
            {
                item.ApprovalStatus = AttendanceApprovalStatus.Disetujui;
                item.VerifiedBy = mentorName;
                DataChanged?.Invoke();
            }
        }
    }
}
