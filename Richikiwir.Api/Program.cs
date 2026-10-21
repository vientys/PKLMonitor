using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Richikiwir.Api.Data;
using Richikiwir.Api.Models;

var builder = WebApplication.CreateBuilder(args);

// Configure SQLite Database
var dbPath = Path.Combine(builder.Environment.ContentRootPath, "pkl_database.db");
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlite($"Data Source={dbPath}");
});

// Configure JSON serialization
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.WriteIndented = true;
});

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Auto-create SQLite database schema
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.UseCors("AllowAll");
app.UseDefaultFiles();
app.UseStaticFiles();

// Helper normalization
static string CleanId(string s) => string.IsNullOrWhiteSpace(s) ? string.Empty : s.Replace(" ", "").Replace("-", "").Trim().ToLowerInvariant();

// ==========================================
// 1. AUTH ENDPOINTS (DATABASE PERSISTED)
// ==========================================

// POST /api/auth/login
app.MapPost("/api/auth/login", async (AppDbContext db, [FromBody] LoginRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.Identifier) || string.IsNullOrWhiteSpace(req.Password))
    {
        return Results.BadRequest(ApiResponse<UserModel>.Fail("Identitas (NISN/NIP) dan kata sandi wajib diisi."));
    }

    var clean = CleanId(req.Identifier);
    var users = await db.Users.ToListAsync();
    var user = users.FirstOrDefault(u => 
        CleanId(u.Identifier) == clean || 
        string.Equals(u.Email.Trim(), req.Identifier.Trim(), StringComparison.OrdinalIgnoreCase));

    if (user == null)
    {
        return Results.NotFound(ApiResponse<UserModel>.Fail("Akun tidak ditemukan. Periksa NISN/NIP atau hubungi administrator."));
    }

    if (req.PreferredRole.HasValue && user.Role != req.PreferredRole.Value)
    {
        return Results.BadRequest(ApiResponse<UserModel>.Fail($"Akun terdaftar sebagai '{user.RoleDisplayName}', bukan '{req.PreferredRole.Value}'."));
    }

    bool validPassword = string.Equals(user.Password, req.Password, StringComparison.Ordinal);
    if (!validPassword)
    {
        return Results.BadRequest(ApiResponse<UserModel>.Fail("Kata sandi yang Anda masukkan salah."));
    }

    var safeUser = new UserModel
    {
        Id = user.Id,
        FullName = user.FullName,
        Identifier = user.Identifier,
        Email = user.Email,
        Role = user.Role,
        SchoolName = user.SchoolName,
        CompanyName = user.CompanyName,
        MajorOrDepartment = user.MajorOrDepartment,
        PhoneNumber = user.PhoneNumber,
        CreatedAt = user.CreatedAt
    };

    return Results.Ok(ApiResponse<UserModel>.Ok(safeUser, $"Selamat datang kembali, {user.FullName}!"));
});

// GET /api/users
app.MapGet("/api/users", async (AppDbContext db, string? role) =>
{
    var users = await db.Users.OrderBy(u => u.Role).ThenBy(u => u.FullName).ToListAsync();
    var attendances = await db.Attendances.ToListAsync();
    var logbooks = await db.Logbooks.ToListAsync();

    if (!string.IsNullOrWhiteSpace(role) && Enum.TryParse<UserRole>(role, true, out var r))
    {
        users = users.Where(u => u.Role == r).ToList();
    }

    var list = users.Select(u =>
    {
        var clean = CleanId(u.Identifier);
        int attCount = attendances.Count(a => CleanId(a.StudentNisn) == clean || a.StudentId == u.Id);
        int logCount = logbooks.Count(l => CleanId(l.StudentNisn) == clean);

        return new
        {
            u.Id,
            u.FullName,
            u.Identifier,
            u.Email,
            Role = u.Role.ToString(),
            RoleName = u.RoleDisplayName,
            u.SchoolName,
            u.CompanyName,
            u.MajorOrDepartment,
            u.PhoneNumber,
            u.CreatedAt,
            AttendanceCount = attCount,
            LogbookCount = logCount,
            IsNewAccount = attCount == 0 && logCount == 0
        };
    }).ToList();

    return Results.Ok(ApiResponse<object>.Ok(list, "Daftar seluruh akun tersimpan di database."));
});

// GET /api/monitoring/summary
app.MapGet("/api/monitoring/summary", async (AppDbContext db) =>
{
    var today = DateTime.Today;
    var users = await db.Users.ToListAsync();
    var attendances = await db.Attendances.ToListAsync();
    var logbooks = await db.Logbooks.ToListAsync();

    var todayAtt = attendances.Where(a => a.Timestamp.Date == today).ToList();

    var summary = new
    {
        totalUsers = users.Count,
        roleSiswa = users.Count(u => u.Role == UserRole.Siswa),
        roleIndustri = users.Count(u => u.Role == UserRole.PembimbingIndustri),
        roleSekolah = users.Count(u => u.Role == UserRole.PembimbingSekolah),
        roleAdmin = users.Count(u => u.Role == UserRole.Admin),
        totalAttendances = attendances.Count,
        todayTotal = todayAtt.Count,
        todayMasuk = todayAtt.Count(a => a.Type == AttendanceType.Masuk),
        todayPulang = todayAtt.Count(a => a.Type == AttendanceType.Pulang),
        todayIzin = todayAtt.Count(a => a.Type == AttendanceType.Izin || a.Type == AttendanceType.Sakit),
        withinRadiusCount = attendances.Count(a => a.DistanceMeters <= 100.0),
        outsideRadiusCount = attendances.Count(a => a.DistanceMeters > 100.0),
        pendingApprovalCount = attendances.Count(a => a.ApprovalStatus == AttendanceApprovalStatus.Menunggu),
        approvedCount = attendances.Count(a => a.ApprovalStatus == AttendanceApprovalStatus.Disetujui),
        totalLogbooks = logbooks.Count,
        pendingLogbooks = logbooks.Count(l => !l.IsApproved),
        databaseName = "pkl_database.db",
        serverTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
    };

    return Results.Ok(ApiResponse<object>.Ok(summary, "Ringkasan statistik monitoring database."));
});

// GET /api/students
app.MapGet("/api/students", async (AppDbContext db) =>
{
    var students = await db.Students.ToListAsync();
    return Results.Ok(ApiResponse<List<StudentInternSummary>>.Ok(students, "Daftar siswa magang dari database."));
});

// ==========================================
// 2. ATTENDANCE ENDPOINTS (DATABASE PERSISTED)
// ==========================================

// GET /api/attendance
app.MapGet("/api/attendance", async (AppDbContext db, string? identifier, string? studentId, string? type, string? date) =>
{
    var query = db.Attendances.AsQueryable();

    if (!string.IsNullOrWhiteSpace(identifier))
    {
        var clean = CleanId(identifier);
        query = query.Where(a => a.StudentNisn.Contains(clean) || a.StudentId == identifier);
    }

    if (!string.IsNullOrWhiteSpace(studentId))
    {
        query = query.Where(a => a.StudentId == studentId);
    }

    if (!string.IsNullOrWhiteSpace(type) && Enum.TryParse<AttendanceType>(type, true, out var t))
    {
        query = query.Where(a => a.Type == t);
    }

    if (!string.IsNullOrWhiteSpace(date) && DateTime.TryParse(date, out var parsedDate))
    {
        var targetDate = parsedDate.Date;
        query = query.Where(a => a.Timestamp.Date == targetDate);
    }

    var result = await query.OrderByDescending(a => a.Timestamp).ToListAsync();
    return Results.Ok(ApiResponse<List<AttendanceRecord>>.Ok(result, $"Ditemukan {result.Count} riwayat presensi di database."));
});

// GET /api/attendance/today?identifier=0067821944
app.MapGet("/api/attendance/today", async (AppDbContext db, string identifier) =>
{
    if (string.IsNullOrWhiteSpace(identifier))
    {
        return Results.BadRequest(ApiResponse<object>.Fail("Parameter identifier (NISN/ID) wajib disertakan."));
    }

    var clean = CleanId(identifier);
    var today = DateTime.Today;
    var allRecords = await db.Attendances
        .Where(a => a.Timestamp >= today && a.Timestamp < today.AddDays(1))
        .ToListAsync();

    var todayRecords = allRecords
        .Where(a => CleanId(a.StudentNisn) == clean || CleanId(a.StudentId) == clean)
        .OrderBy(a => a.Timestamp)
        .ToList();

    var checkIn = todayRecords.FirstOrDefault(a => a.Type == AttendanceType.Masuk);
    var checkOut = todayRecords.FirstOrDefault(a => a.Type == AttendanceType.Pulang);

    return Results.Ok(ApiResponse<object>.Ok(new
    {
        HasCheckedIn = checkIn != null,
        CheckIn = checkIn,
        HasCheckedOut = checkOut != null,
        CheckOut = checkOut,
        Date = today.ToString("yyyy-MM-dd")
    }, "Status presensi hari ini."));
});

// POST /api/attendance/check-in
app.MapPost("/api/attendance/check-in", async (AppDbContext db, [FromBody] AttendanceCheckInRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.StudentNisn) && string.IsNullOrWhiteSpace(req.StudentName))
    {
        return Results.BadRequest(ApiResponse<AttendanceRecord>.Fail("Identitas siswa (NISN/Nama) wajib diisi."));
    }

    bool isRadiusValid = req.DistanceMeters <= 100.0;
    string statusText = isRadiusValid ? "Hadir Tepat Waktu (Radius OK)" : $"Presensi Di Luar Radius ({req.DistanceMeters:F1}m)";

    var record = new AttendanceRecord
    {
        StudentId = req.StudentId,
        StudentName = req.StudentName,
        StudentNisn = req.StudentNisn,
        CompanyName = string.IsNullOrWhiteSpace(req.CompanyName) ? "PT Solusi Digital Nusantara" : req.CompanyName,
        Type = AttendanceType.Masuk,
        Timestamp = DateTime.Now,
        Latitude = req.Latitude,
        Longitude = req.Longitude,
        LocationAddress = req.LocationAddress ?? "Gedung Kantor Mitra Industri",
        DistanceMeters = req.DistanceMeters,
        Notes = req.Notes ?? "Presensi masuk via Web/Mobile Portal.",
        StatusText = statusText,
        ApprovalStatus = isRadiusValid ? AttendanceApprovalStatus.Disetujui : AttendanceApprovalStatus.Menunggu,
        VerifiedBy = isRadiusValid ? "Sistem Geotag Otomatis" : "Menunggu Verifikasi Mentor",
        PhotoBase64 = req.PhotoBase64
    };

    await db.Attendances.AddAsync(record);

    // Update student summary in database if exists
    var cleanNisn = CleanId(req.StudentNisn);
    var student = await db.Students.FirstOrDefaultAsync(s => s.Nisn == req.StudentNisn || s.StudentId == req.StudentId);
    if (student != null)
    {
        student.TodayAttendanceStatus = "Hadir (" + record.FormattedTime + " WIB)";
        student.TodayCheckInTime = record.FormattedTime + " WIB";
    }

    await db.SaveChangesAsync();

    return Results.Created($"/api/attendance/{record.Id}", ApiResponse<AttendanceRecord>.Ok(record, "Presensi Check-In berhasil tersimpan di database!"));
});

// POST /api/attendance/check-out
app.MapPost("/api/attendance/check-out", async (AppDbContext db, [FromBody] AttendanceCheckInRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.StudentNisn) && string.IsNullOrWhiteSpace(req.StudentName))
    {
        return Results.BadRequest(ApiResponse<AttendanceRecord>.Fail("Identitas siswa (NISN/Nama) wajib diisi."));
    }

    var record = new AttendanceRecord
    {
        StudentId = req.StudentId,
        StudentName = req.StudentName,
        StudentNisn = req.StudentNisn,
        CompanyName = string.IsNullOrWhiteSpace(req.CompanyName) ? "PT Solusi Digital Nusantara" : req.CompanyName,
        Type = AttendanceType.Pulang,
        Timestamp = DateTime.Now,
        Latitude = req.Latitude,
        Longitude = req.Longitude,
        LocationAddress = req.LocationAddress ?? "Gedung Kantor Mitra Industri",
        DistanceMeters = req.DistanceMeters,
        Notes = req.Notes ?? "Presensi pulang kerja harian.",
        StatusText = "Pulang Kerja",
        ApprovalStatus = AttendanceApprovalStatus.Disetujui,
        VerifiedBy = "Sistem Geotag Otomatis"
    };

    await db.Attendances.AddAsync(record);
    await db.SaveChangesAsync();

    return Results.Created($"/api/attendance/{record.Id}", ApiResponse<AttendanceRecord>.Ok(record, "Presensi Check-Out berhasil tersimpan di database!"));
});

// PUT /api/attendance/{id}/approve
app.MapPut("/api/attendance/{id}/approve", async (AppDbContext db, string id, [FromBody] ApprovalRequest req) =>
{
    var record = await db.Attendances.FirstOrDefaultAsync(a => a.Id == id);
    if (record == null)
    {
        return Results.NotFound(ApiResponse<AttendanceRecord>.Fail("Data presensi tidak ditemukan."));
    }

    record.ApprovalStatus = req.Approve ? AttendanceApprovalStatus.Disetujui : AttendanceApprovalStatus.Ditolak;
    record.VerifiedBy = string.IsNullOrWhiteSpace(req.ApprovedBy) ? "Pembimbing PKL" : req.ApprovedBy;
    if (!string.IsNullOrWhiteSpace(req.Notes))
    {
        record.Notes += $" [Catatan Verifikasi: {req.Notes}]";
    }

    await db.SaveChangesAsync();
    return Results.Ok(ApiResponse<AttendanceRecord>.Ok(record, $"Presensi berhasil {(req.Approve ? "disetujui" : "ditolak")}."));
});

// ==========================================
// 3. LOGBOOK ENDPOINTS (DATABASE PERSISTED)
// ==========================================

// GET /api/logbooks
app.MapGet("/api/logbooks", async (AppDbContext db, string? identifier, string? nisn) =>
{
    var targetId = identifier ?? nisn;
    var query = db.Logbooks.AsQueryable();

    if (!string.IsNullOrWhiteSpace(targetId))
    {
        var clean = CleanId(targetId);
        query = query.Where(l => l.StudentNisn.Contains(clean) || l.StudentName.Contains(targetId));
    }

    var result = await query.OrderByDescending(l => l.Date).ToListAsync();
    return Results.Ok(ApiResponse<List<LogbookEntry>>.Ok(result, $"Ditemukan {result.Count} jurnal kegiatan di database."));
});

// POST /api/logbooks
app.MapPost("/api/logbooks", async (AppDbContext db, [FromBody] LogbookCreateRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.ActivityTitle) || string.IsNullOrWhiteSpace(req.StudentName))
    {
        return Results.BadRequest(ApiResponse<LogbookEntry>.Fail("Judul kegiatan dan Nama Siswa wajib diisi."));
    }

    var entry = new LogbookEntry
    {
        StudentName = req.StudentName.Trim(),
        StudentNisn = req.StudentNisn.Trim(),
        CompanyName = string.IsNullOrWhiteSpace(req.CompanyName) ? "PT Solusi Digital Nusantara" : req.CompanyName.Trim(),
        Date = DateTime.Today,
        ActivityTitle = req.ActivityTitle.Trim(),
        Description = req.Description?.Trim() ?? string.Empty,
        IsApproved = false,
        ApprovedBy = "-",
        Feedback = null
    };

    await db.Logbooks.AddAsync(entry);

    // Increment completed logbooks count for student
    var student = await db.Students.FirstOrDefaultAsync(s => s.Nisn == req.StudentNisn);
    if (student != null)
    {
        student.CompletedLogbooks++;
    }

    await db.SaveChangesAsync();
    return Results.Created($"/api/logbooks/{entry.Id}", ApiResponse<LogbookEntry>.Ok(entry, "Jurnal harian berhasil tersimpan di database."));
});

// PUT /api/logbooks/{id}/approve
app.MapPut("/api/logbooks/{id}/approve", async (AppDbContext db, string id, [FromBody] ApprovalRequest req) =>
{
    var entry = await db.Logbooks.FirstOrDefaultAsync(l => l.Id == id);
    if (entry == null)
    {
        return Results.NotFound(ApiResponse<LogbookEntry>.Fail("Data jurnal tidak ditemukan."));
    }

    entry.IsApproved = req.Approve;
    entry.ApprovedBy = string.IsNullOrWhiteSpace(req.ApprovedBy) ? "Pembimbing Industri" : req.ApprovedBy;
    entry.Feedback = req.Notes;

    await db.SaveChangesAsync();
    return Results.Ok(ApiResponse<LogbookEntry>.Ok(entry, $"Jurnal kegiatan berhasil {(req.Approve ? "diverifikasi" : "ditinjau")}."));
});

// ==========================================
// 4. STATS & GEOFENCE ENDPOINTS
// ==========================================

// GET /api/stats/{identifier} (Calculated from Database; 0 for new account!)
app.MapGet("/api/stats/{identifier}", async (AppDbContext db, string identifier) =>
{
    var clean = CleanId(identifier);

    var records = await db.Attendances
        .Where(a => a.StudentNisn.Contains(clean) || a.StudentId == identifier || a.StudentName.Contains(identifier))
        .ToListAsync();

    var logs = await db.Logbooks
        .Where(l => l.StudentNisn.Contains(clean) || l.StudentName.Contains(identifier))
        .ToListAsync();

    int totalDays = records.Select(r => r.Timestamp.Date).Distinct().Count();
    int hadirCount = records.Count(r => r.Type == AttendanceType.Masuk);
    int izinCount = records.Count(r => r.Type == AttendanceType.Izin || r.Type == AttendanceType.Sakit);
    double attendanceRate = totalDays > 0 ? (hadirCount * 100.0 / totalDays) : 0.0;
    if (attendanceRate > 100.0) attendanceRate = 100.0;

    var stats = new UserStatsResponse
    {
        TotalDays = totalDays,
        HadirCount = hadirCount,
        IzinCount = izinCount,
        AttendanceRate = Math.Round(attendanceRate, 1),
        TotalLogbooks = logs.Count
    };

    return Results.Ok(ApiResponse<UserStatsResponse>.Ok(stats, "Statistik kehadiran siswa dari database."));
});

// GET /api/company/geofence
app.MapGet("/api/company/geofence", () =>
{
    return Results.Ok(ApiResponse<object>.Ok(new
    {
        CompanyName = "PT Solusi Digital Nusantara",
        TargetLatitude = -6.22412,
        TargetLongitude = 106.83154,
        AllowedRadiusMeters = 100.0,
        Address = "Gedung Cyber 2 Tower, Jl. H. R. Rasuna Said Blok X-5 No. 13, RT.7/RW.2, Kuningan Timur, Setiabudi, Jakarta Selatan",
        WorkStartTime = "08:00 WIB",
        WorkEndTime = "17:00 WIB"
    }, "Data koordinat geofence kantor mitra industri."));
});

// GET /api/health
app.MapGet("/api/health", (AppDbContext db) => Results.Ok(new
{
    status = "Healthy",
    service = "Richikiwir PKL Web API (SQLite Database Persisted)",
    database = "pkl_database.db",
    totalUsers = db.Users.Count(),
    totalAttendances = db.Attendances.Count(),
    serverTime = DateTime.UtcNow
}));

app.Run();
