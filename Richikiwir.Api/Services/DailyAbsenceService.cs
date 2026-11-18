using Microsoft.EntityFrameworkCore;
using Richikiwir.Api.Data;
using Richikiwir.Api.Models;

namespace Richikiwir.Api.Services;

public sealed class DailyAbsenceService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DailyAbsenceService> _logger;

    public DailyAbsenceService(IServiceScopeFactory scopeFactory, ILogger<DailyAbsenceService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await MarkAbsentStudentsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Gagal menandai siswa yang tidak hadir.");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private async Task MarkAbsentStudentsAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.Now;
        if (now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var students = await db.SiswaProfiles
            .Include(profile => profile.User)
            .Include(profile => profile.MitraIndustri)
            .Where(profile => profile.StatusPkl == "Aktif" && profile.User != null && profile.User.IsActive)
            .ToListAsync(cancellationToken);

        foreach (var student in students)
        {
            if (string.IsNullOrWhiteSpace(student.MitraIndustriId)) continue;

            var endTime = TimeOnly.TryParse(student.MitraIndustri?.JamPulang, out var configuredEndTime)
                ? configuredEndTime
                : new TimeOnly(17, 0);
            if (TimeOnly.FromDateTime(now) < endTime) continue;

            var dayStart = now.Date;
            var nextDay = dayStart.AddDays(1);
            var hasStatus = await db.Presensi.AnyAsync(record =>
                record.SiswaProfileId == student.Id && record.Timestamp >= dayStart && record.Timestamp < nextDay,
                cancellationToken);
            if (hasStatus) continue;

            db.Presensi.Add(new PresensiRecord
            {
                SiswaProfileId = student.Id,
                MitraIndustriId = student.MitraIndustriId,
                Type = AttendanceType.Alpa,
                Timestamp = dayStart.Add(endTime.ToTimeSpan()),
                LocationAddress = "Tidak hadir di lokasi PKL",
                DistanceMeters = 0,
                IsWithinRadius = false,
                Notes = "Tidak ada presensi atau pengajuan izin/sakit hingga akhir jam kerja.",
                ApprovalStatus = AttendanceApprovalStatus.Menunggu
            });
        }

        if (db.ChangeTracker.HasChanges())
            await db.SaveChangesAsync(cancellationToken);
    }
}
