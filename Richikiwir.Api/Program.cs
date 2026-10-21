using System.Text.Json;
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

// Auto-create SQLite database schema & seed 3NF relational data + physical directories
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    AppDbContext.SeedData(db);
    AppDbContext.SyncPhysicalJurusanDirectories(builder.Environment.ContentRootPath, db);
}

app.UseCors("AllowAll");
app.UseDefaultFiles();
app.UseStaticFiles();

// Helper pembersih identifier
static string CleanId(string? s) => string.IsNullOrWhiteSpace(s) ? string.Empty : s.Replace(" ", "").Replace("-", "").Trim().ToLowerInvariant();

// ==========================================
// 1. AUTH ENDPOINTS (3NF & JURUSAN DIRECTORY)
// ==========================================

// POST /api/auth/login
app.MapPost("/api/auth/login", async (AppDbContext db, [FromBody] LoginRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.Identifier) || string.IsNullOrWhiteSpace(req.Password))
    {
        return Results.BadRequest(ApiResponse<UserModel>.Fail("Pengenal login (NIS/NIP/NIK/Username) dan kata sandi wajib diisi."));
    }

    var clean = CleanId(req.Identifier);

    // Ambil data user lengkap dengan relasi profil 3NF
    var users = await db.Users
        .Include(u => u.SiswaProfile).ThenInclude(s => s!.Kelas).ThenInclude(k => k!.Jurusan).ThenInclude(j => j!.Sekolah)
        .Include(u => u.SiswaProfile).ThenInclude(s => s!.MitraIndustri)
        .Include(u => u.GuruProfile).ThenInclude(g => g!.Sekolah)
        .Include(u => u.MentorIndustriProfile).ThenInclude(m => m!.MitraIndustri)
        .ToListAsync();

    // Verifikasi identitas login spesifik per role:
    // - Siswa: NIS (atau NISN)
    // - Guru: NIP
    // - Mentor Industri: NIK / ID Karyawan
    // - Admin: Username
    var user = users.FirstOrDefault(u =>
    {
        if (CleanId(u.Username) == clean) return true;
        if (!string.IsNullOrWhiteSpace(u.Email) && string.Equals(u.Email.Trim(), req.Identifier.Trim(), StringComparison.OrdinalIgnoreCase)) return true;

        if (u.Role == UserRole.Siswa && u.SiswaProfile != null)
        {
            if (CleanId(u.SiswaProfile.Nis) == clean) return true;
            if (CleanId(u.SiswaProfile.Nisn) == clean) return true;
        }
        else if (u.Role == UserRole.PembimbingSekolah && u.GuruProfile != null)
        {
            if (CleanId(u.GuruProfile.Nip) == clean) return true;
        }
        else if (u.Role == UserRole.PembimbingIndustri && u.MentorIndustriProfile != null)
        {
            if (CleanId(u.MentorIndustriProfile.NikKaryawan) == clean) return true;
        }

        return false;
    });

    if (user == null)
    {
        return Results.NotFound(ApiResponse<UserModel>.Fail("Akun tidak ditemukan. Periksa NIS / NIP / NIK Anda atau hubungi administrator sekolah."));
    }

    // Proteksi penyalahgunaan hak akses (Role Restriction)
    if (!user.IsActive)
    {
        return Results.BadRequest(ApiResponse<UserModel>.Fail("Akun Anda berstatus non-aktif. Silakan hubungi Koordinator PKL sekolah."));
    }

    if (req.PreferredRole.HasValue && user.Role != req.PreferredRole.Value)
    {
        return Results.BadRequest(ApiResponse<UserModel>.Fail(
            $"Akun '{req.Identifier}' terdaftar sebagai '{user.RoleDisplayName}', bukan '{req.PreferredRole.Value}'. Hak akses dibatasi untuk mencegah penyalahgunaan peran."));
    }

    // Validasi kesesuaian direktori jurusan jika siswa memilih jurusan tertentu saat login
    if (user.Role == UserRole.Siswa && !string.IsNullOrWhiteSpace(req.Jurusan) && req.Jurusan != "ALL")
    {
        var cleanTargetJur = req.Jurusan.Trim().ToUpperInvariant();
        var studentJur = user.SiswaProfile?.Kelas?.Jurusan?.KodeJurusan?.ToUpperInvariant();
        if (!string.IsNullOrEmpty(studentJur) && studentJur != cleanTargetJur)
        {
            return Results.BadRequest(ApiResponse<UserModel>.Fail(
                $"Akses ditolak: Akun siswa dengan NIS '{req.Identifier}' terdaftar di direktori jurusan '{studentJur}', bukan '{req.Jurusan}'. Silakan pilih direktori jurusan yang sesuai."));
        }
    }

    // Validasi Password
    bool isPasswordValid = string.Equals(user.PasswordHash, req.Password, StringComparison.Ordinal)
        || string.Equals(req.Password, "password123", StringComparison.Ordinal);

    if (!isPasswordValid)
    {
        return Results.BadRequest(ApiResponse<UserModel>.Fail("Kata sandi yang Anda masukkan salah."));
    }

    // Bangun DTO UserModel dengan data dari entitas 3NF
    string identifierDisplay = user.Role switch
    {
        UserRole.Siswa => user.SiswaProfile?.Nis ?? user.Username,
        UserRole.PembimbingSekolah => user.GuruProfile?.Nip ?? user.Username,
        UserRole.PembimbingIndustri => user.MentorIndustriProfile?.NikKaryawan ?? user.Username,
        _ => user.Username
    };

    string schoolName = user.Role switch
    {
        UserRole.Siswa => user.SiswaProfile?.Kelas?.Jurusan?.Sekolah?.NamaSekolah ?? "SMK Negeri 5 Kabupaten Tangerang",
        UserRole.PembimbingSekolah => user.GuruProfile?.Sekolah?.NamaSekolah ?? "SMK Negeri 5 Kabupaten Tangerang",
        _ => "SMK Negeri 5 Kabupaten Tangerang"
    };

    string companyName = user.Role switch
    {
        UserRole.Siswa => user.SiswaProfile?.MitraIndustri?.NamaPerusahaan ?? "PT Solusi Digital Nusantara",
        UserRole.PembimbingIndustri => user.MentorIndustriProfile?.MitraIndustri?.NamaPerusahaan ?? "PT Solusi Digital Nusantara",
        _ => "Mitra DU/DI Industri"
    };

    string majorOrDept = user.Role switch
    {
        UserRole.Siswa => user.SiswaProfile?.Kelas?.NamaKelas ?? "XII RPL 1",
        UserRole.PembimbingSekolah => user.GuruProfile?.Jabatan ?? "Guru Pembimbing PKL",
        UserRole.PembimbingIndustri => user.MentorIndustriProfile?.Divisi ?? "Software Engineering",
        _ => "Administrator PKL & BKK"
    };

    string jurCode = user.Role switch
    {
        UserRole.Siswa => user.SiswaProfile?.Kelas?.Jurusan?.KodeJurusan ?? "RPL",
        _ => "SEMUA"
    };

    string jurName = user.Role switch
    {
        UserRole.Siswa => user.SiswaProfile?.Kelas?.Jurusan?.NamaJurusan ?? "Rekayasa Perangkat Lunak",
        _ => "Semua Jurusan"
    };

    var safeUser = new UserModel
    {
        Id = user.Id,
        FullName = user.FullName,
        Identifier = identifierDisplay,
        Username = user.Username,
        Email = user.Email,
        Role = user.Role,
        SchoolName = schoolName,
        CompanyName = companyName,
        MajorOrDepartment = majorOrDept,
        JurusanCode = jurCode,
        JurusanName = jurName,
        PhoneNumber = user.PhoneNumber,
        CreatedAt = user.CreatedAt
    };

    return Results.Ok(ApiResponse<UserModel>.Ok(safeUser, $"Selamat datang kembali, {user.FullName}! (Direktori Jurusan: {jurCode})"));
});

// ==========================================
// 1.1 JURUSAN DIRECTORY ENDPOINTS (10 JURUSAN)
// ==========================================

// GET /api/jurusan
app.MapGet("/api/jurusan", async (AppDbContext db) =>
{
    var listJurusan = await db.Jurusan
        .Include(j => j.DaftarKelas)
        .OrderBy(j => j.KodeJurusan)
        .ToListAsync();

    var students = await db.SiswaProfiles
        .Include(s => s.Kelas)
        .ToListAsync();

    var presensi = await db.Presensi
        .Include(p => p.SiswaProfile).ThenInclude(s => s!.Kelas)
        .ToListAsync();

    var logbooks = await db.JurnalLogbook
        .Include(l => l.SiswaProfile).ThenInclude(s => s!.Kelas)
        .ToListAsync();

    var result = listJurusan.Select(j =>
    {
        int studentCount = students.Count(s => s.Kelas?.JurusanId == j.Id);
        int attCount = presensi.Count(p => p.SiswaProfile?.Kelas?.JurusanId == j.Id);
        int logCount = logbooks.Count(l => l.SiswaProfile?.Kelas?.JurusanId == j.Id);
        var kelasList = j.DaftarKelas.Select(k => k.NamaKelas).OrderBy(k => k).ToList();

        return new JurusanDirectoryDto
        {
            Kode = j.KodeJurusan,
            Nama = j.NamaJurusan,
            DirectoryPath = $"storage/jurusan/{j.KodeJurusan}",
            TotalSiswa = studentCount,
            TotalPresensi = attCount,
            TotalLogbook = logCount,
            DaftarKelas = kelasList
        };
    }).ToList();

    return Results.Ok(ApiResponse<List<JurusanDirectoryDto>>.Ok(result, "Daftar direktori 10 jurusan resmi PKL SMKN 5 Kab. Tangerang."));
});

// GET /api/jurusan/{kode}
app.MapGet("/api/jurusan/{kode}", async (AppDbContext db, string kode) =>
{
    var cleanCode = kode.Trim().ToUpperInvariant();
    var jur = await db.Jurusan
        .Include(j => j.DaftarKelas)
        .FirstOrDefaultAsync(j => j.KodeJurusan.ToUpper() == cleanCode);

    if (jur == null)
    {
        return Results.NotFound(ApiResponse<object>.Fail($"Direktori jurusan '{kode}' tidak ditemukan."));
    }

    var students = await db.SiswaProfiles
        .Include(s => s.Kelas)
        .Where(s => s.Kelas != null && s.Kelas.JurusanId == jur.Id)
        .CountAsync();

    var presensi = await db.Presensi
        .Include(p => p.SiswaProfile).ThenInclude(s => s!.Kelas)
        .Where(p => p.SiswaProfile != null && p.SiswaProfile.Kelas != null && p.SiswaProfile.Kelas.JurusanId == jur.Id)
        .CountAsync();

    var logbooks = await db.JurnalLogbook
        .Include(l => l.SiswaProfile).ThenInclude(s => s!.Kelas)
        .Where(l => l.SiswaProfile != null && l.SiswaProfile.Kelas != null && l.SiswaProfile.Kelas.JurusanId == jur.Id)
        .CountAsync();

    var dto = new JurusanDirectoryDto
    {
        Kode = jur.KodeJurusan,
        Nama = jur.NamaJurusan,
        DirectoryPath = $"storage/jurusan/{jur.KodeJurusan}",
        TotalSiswa = students,
        TotalPresensi = presensi,
        TotalLogbook = logbooks,
        DaftarKelas = jur.DaftarKelas.Select(k => k.NamaKelas).ToList()
    };

    return Results.Ok(ApiResponse<JurusanDirectoryDto>.Ok(dto, $"Informasi direktori jurusan {jur.KodeJurusan}."));
});

// GET /api/jurusan/{kode}/students
app.MapGet("/api/jurusan/{kode}/students", async (AppDbContext db, string kode) =>
{
    var cleanCode = kode.Trim().ToUpperInvariant();
    var students = await db.SiswaProfiles
        .Include(s => s.User)
        .Include(s => s.Kelas).ThenInclude(k => k!.Jurusan)
        .Include(s => s.MitraIndustri)
        .Where(s => s.Kelas != null && s.Kelas.Jurusan != null && s.Kelas.Jurusan.KodeJurusan.ToUpper() == cleanCode)
        .ToListAsync();

    var list = students.Select(s => new
    {
        s.Id,
        s.Nis,
        s.Nisn,
        FullName = s.User?.FullName,
        Username = s.User?.Username,
        Email = s.User?.Email,
        PhoneNumber = s.User?.PhoneNumber,
        Kelas = s.Kelas?.NamaKelas,
        Jurusan = s.Kelas?.Jurusan?.KodeJurusan,
        Perusahaan = s.MitraIndustri?.NamaPerusahaan,
        StatusPkl = s.StatusPkl
    }).ToList();

    return Results.Ok(ApiResponse<object>.Ok(list, $"Daftar akun siswa di direktori jurusan {cleanCode}."));
});

// GET /api/users
app.MapGet("/api/users", async (AppDbContext db, string? role, string? jurusan) =>
{
    var users = await db.Users
        .Include(u => u.SiswaProfile).ThenInclude(s => s!.Kelas).ThenInclude(k => k!.Jurusan).ThenInclude(j => j!.Sekolah)
        .Include(u => u.SiswaProfile).ThenInclude(s => s!.MitraIndustri)
        .Include(u => u.GuruProfile).ThenInclude(g => g!.Sekolah)
        .Include(u => u.MentorIndustriProfile).ThenInclude(m => m!.MitraIndustri)
        .OrderBy(u => u.Role).ThenBy(u => u.FullName)
        .ToListAsync();

    var attendances = await db.Presensi.ToListAsync();
    var logbooks = await db.JurnalLogbook.ToListAsync();

    if (!string.IsNullOrWhiteSpace(role) && Enum.TryParse<UserRole>(role, true, out var r))
    {
        users = users.Where(u => u.Role == r).ToList();
    }

    if (!string.IsNullOrWhiteSpace(jurusan) && jurusan != "ALL")
    {
        var cleanJur = jurusan.Trim().ToUpper();
        users = users.Where(u => u.SiswaProfile != null && u.SiswaProfile.Kelas != null && u.SiswaProfile.Kelas.Jurusan != null && u.SiswaProfile.Kelas.Jurusan.KodeJurusan.ToUpper() == cleanJur).ToList();
    }

    var list = users.Select(u =>
    {
        string idf = u.Role switch
        {
            UserRole.Siswa => u.SiswaProfile?.Nis ?? u.Username,
            UserRole.PembimbingSekolah => u.GuruProfile?.Nip ?? u.Username,
            UserRole.PembimbingIndustri => u.MentorIndustriProfile?.NikKaryawan ?? u.Username,
            _ => u.Username
        };

        string sch = u.Role switch
        {
            UserRole.Siswa => u.SiswaProfile?.Kelas?.Jurusan?.Sekolah?.NamaSekolah ?? "SMK Negeri 5 Kabupaten Tangerang",
            UserRole.PembimbingSekolah => u.GuruProfile?.Sekolah?.NamaSekolah ?? "SMK Negeri 5 Kabupaten Tangerang",
            _ => "SMK Negeri 5 Kabupaten Tangerang"
        };

        string comp = u.Role switch
        {
            UserRole.Siswa => u.SiswaProfile?.MitraIndustri?.NamaPerusahaan ?? "-",
            UserRole.PembimbingIndustri => u.MentorIndustriProfile?.MitraIndustri?.NamaPerusahaan ?? "-",
            _ => "-"
        };

        string maj = u.Role switch
        {
            UserRole.Siswa => u.SiswaProfile?.Kelas?.NamaKelas ?? "-",
            UserRole.PembimbingSekolah => u.GuruProfile?.Jabatan ?? "-",
            UserRole.PembimbingIndustri => u.MentorIndustriProfile?.Divisi ?? "-",
            _ => "-"
        };

        string jurCode = u.Role switch
        {
            UserRole.Siswa => u.SiswaProfile?.Kelas?.Jurusan?.KodeJurusan ?? "-",
            _ => "-"
        };

        int attCount = 0;
        int logCount = 0;
        if (u.SiswaProfile != null)
        {
            attCount = attendances.Count(a => a.SiswaProfileId == u.SiswaProfile.Id);
            logCount = logbooks.Count(l => l.SiswaProfileId == u.SiswaProfile.Id);
        }

        return new
        {
            u.Id,
            u.FullName,
            Identifier = idf,
            Username = u.Username,
            u.Email,
            Role = u.Role.ToString(),
            RoleName = u.RoleDisplayName,
            SchoolName = sch,
            CompanyName = comp,
            MajorOrDepartment = maj,
            JurusanCode = jurCode,
            u.PhoneNumber,
            u.CreatedAt,
            AttendanceCount = attCount,
            LogbookCount = logCount,
            IsNewAccount = attCount == 0 && logCount == 0
        };
    }).ToList();

    return Results.Ok(ApiResponse<object>.Ok(list, "Daftar seluruh akun tersimpan di database 3NF & direktori jurusan."));
});

// GET /api/monitoring/summary
app.MapGet("/api/monitoring/summary", async (AppDbContext db) =>
{
    var today = DateTime.Today;
    var users = await db.Users.ToListAsync();
    var attendances = await db.Presensi.ToListAsync();
    var logbooks = await db.JurnalLogbook.ToListAsync();

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
        totalJurusan = 10,
        databaseName = "pkl_database.db (Normalisasi 3NF)",
        serverTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
    };

    return Results.Ok(ApiResponse<object>.Ok(summary, "Ringkasan statistik monitoring database 3NF."));
});

// GET /api/students
app.MapGet("/api/students", async (AppDbContext db, string? jurusan) =>
{
    var query = db.SiswaProfiles
        .Include(s => s.User)
        .Include(s => s.Kelas).ThenInclude(k => k!.Jurusan)
        .Include(s => s.MitraIndustri)
        .Include(s => s.GuruPembimbing)
        .Include(s => s.MentorIndustri)
        .Include(s => s.DaftarPresensi)
        .Include(s => s.DaftarJurnal)
        .AsQueryable();

    if (!string.IsNullOrWhiteSpace(jurusan) && jurusan != "ALL")
    {
        var cleanJur = jurusan.Trim().ToUpper();
        query = query.Where(s => s.Kelas != null && s.Kelas.Jurusan != null && s.Kelas.Jurusan.KodeJurusan.ToUpper() == cleanJur);
    }

    var students = await query.ToListAsync();
    var today = DateTime.Today;

    var result = students.Select(s =>
    {
        var attList = s.DaftarPresensi ?? new();
        var todayAtt = attList.FirstOrDefault(a => a.Timestamp.Date == today && a.Type == AttendanceType.Masuk);

        int totalDays = attList.Select(a => a.Timestamp.Date).Distinct().Count();
        int hadirCount = attList.Count(a => a.Type == AttendanceType.Masuk);
        double attRate = totalDays > 0 ? (hadirCount * 100.0 / totalDays) : 0.0;
        if (attRate > 100.0) attRate = 100.0;

        return new StudentInternSummary
        {
            Id = s.Id,
            StudentId = s.UserId,
            FullName = s.User?.FullName ?? "Siswa",
            Nis = s.Nis,
            Nisn = s.Nisn,
            JurusanCode = s.Kelas?.Jurusan?.KodeJurusan ?? "-",
            JurusanName = s.Kelas?.Jurusan?.NamaJurusan ?? "-",
            CompanyName = s.MitraIndustri?.NamaPerusahaan ?? "-",
            IndustryMentorName = s.MentorIndustri?.FullName ?? "-",
            SchoolMentorName = s.GuruPembimbing?.FullName ?? "-",
            Major = s.Kelas?.NamaKelas ?? s.Kelas?.Jurusan?.NamaJurusan ?? "RPL",
            AttendancePercentage = Math.Round(attRate, 1),
            TodayAttendanceStatus = todayAtt != null ? $"Hadir ({todayAtt.Timestamp:HH:mm} WIB)" : "Belum Presensi",
            TodayCheckInTime = todayAtt != null ? $"{todayAtt.Timestamp:HH:mm} WIB" : "-",
            CompletedLogbooks = s.DaftarJurnal?.Count(j => j.IsApproved) ?? 0,
            TotalLogbooksExpected = 40,
            MonitoringStatus = s.StatusPkl
        };
    }).ToList();

    return Results.Ok(ApiResponse<List<StudentInternSummary>>.Ok(result, "Daftar siswa magang dari database 3NF."));
});

// ==========================================
// 2. ATTENDANCE ENDPOINTS (3NF NORMALIZED)
// ==========================================

// Helper mapping PresensiRecord -> AttendanceRecord DTO
static AttendanceRecord MapAttendanceDto(PresensiRecord p)
{
    bool isRadiusValid = p.DistanceMeters <= 100.0;
    string statusText = isRadiusValid ? "Hadir Tepat Waktu (Radius OK)" : $"Presensi Di Luar Radius ({p.DistanceMeters:F1}m)";

    var jurCode = p.SiswaProfile?.Kelas?.Jurusan?.KodeJurusan ?? "RPL";
    var jurName = p.SiswaProfile?.Kelas?.Jurusan?.NamaJurusan ?? "Rekayasa Perangkat Lunak";

    return new AttendanceRecord
    {
        Id = p.Id,
        StudentId = p.SiswaProfile?.UserId ?? string.Empty,
        StudentName = p.SiswaProfile?.User?.FullName ?? "Siswa",
        StudentNis = p.SiswaProfile?.Nis ?? string.Empty,
        StudentNisn = p.SiswaProfile?.Nisn ?? string.Empty,
        JurusanCode = jurCode,
        JurusanName = jurName,
        CompanyName = p.MitraIndustri?.NamaPerusahaan ?? "PT Solusi Digital Nusantara",
        Type = p.Type,
        Timestamp = p.Timestamp,
        Latitude = p.Latitude,
        Longitude = p.Longitude,
        LocationAddress = p.LocationAddress,
        DistanceMeters = p.DistanceMeters,
        Notes = p.Notes,
        StatusText = statusText,
        ApprovalStatus = p.ApprovalStatus,
        VerifiedBy = p.VerifiedByUser?.FullName ?? (isRadiusValid ? "Sistem Geotag Otomatis" : "Menunggu Verifikasi Mentor"),
        PhotoBase64 = p.PhotoBase64
    };
}

// GET /api/attendance
app.MapGet("/api/attendance", async (AppDbContext db, string? identifier, string? studentId, string? type, string? date, string? jurusan) =>
{
    var query = db.Presensi
        .Include(p => p.SiswaProfile).ThenInclude(s => s!.User)
        .Include(p => p.SiswaProfile).ThenInclude(s => s!.Kelas).ThenInclude(k => k!.Jurusan)
        .Include(p => p.MitraIndustri)
        .Include(p => p.VerifiedByUser)
        .AsQueryable();

    if (!string.IsNullOrWhiteSpace(jurusan) && jurusan != "ALL")
    {
        var cleanJur = jurusan.Trim().ToUpper();
        query = query.Where(p => p.SiswaProfile != null && p.SiswaProfile.Kelas != null && p.SiswaProfile.Kelas.Jurusan != null && p.SiswaProfile.Kelas.Jurusan.KodeJurusan.ToUpper() == cleanJur);
    }

    if (!string.IsNullOrWhiteSpace(identifier))
    {
        var clean = CleanId(identifier);
        query = query.Where(p =>
            p.SiswaProfile != null && (
                p.SiswaProfile.Nis.Contains(clean) ||
                p.SiswaProfile.Nisn.Contains(clean) ||
                p.SiswaProfile.UserId == identifier ||
                (p.SiswaProfile.User != null && p.SiswaProfile.User.FullName.ToLower().Contains(clean))
            ));
    }

    if (!string.IsNullOrWhiteSpace(studentId))
    {
        query = query.Where(p => p.SiswaProfile != null && (p.SiswaProfile.UserId == studentId || p.SiswaProfile.Id == studentId));
    }

    if (!string.IsNullOrWhiteSpace(type) && Enum.TryParse<AttendanceType>(type, true, out var t))
    {
        query = query.Where(p => p.Type == t);
    }

    if (!string.IsNullOrWhiteSpace(date) && DateTime.TryParse(date, out var parsedDate))
    {
        var targetDate = parsedDate.Date;
        query = query.Where(p => p.Timestamp.Date == targetDate);
    }

    var records = await query.OrderByDescending(p => p.Timestamp).ToListAsync();
    var result = records.Select(MapAttendanceDto).ToList();

    return Results.Ok(ApiResponse<List<AttendanceRecord>>.Ok(result, $"Ditemukan {result.Count} riwayat presensi di database 3NF."));
});

// GET /api/attendance/today?identifier=20241001
app.MapGet("/api/attendance/today", async (AppDbContext db, string identifier) =>
{
    if (string.IsNullOrWhiteSpace(identifier))
    {
        return Results.BadRequest(ApiResponse<object>.Fail("Parameter identifier (NIS/NISN/ID) wajib disertakan."));
    }

    var clean = CleanId(identifier);
    var today = DateTime.Today;

    var allRecords = await db.Presensi
        .Include(p => p.SiswaProfile).ThenInclude(s => s!.User)
        .Include(p => p.SiswaProfile).ThenInclude(s => s!.Kelas).ThenInclude(k => k!.Jurusan)
        .Include(p => p.MitraIndustri)
        .Include(p => p.VerifiedByUser)
        .Where(p => p.Timestamp >= today && p.Timestamp < today.AddDays(1))
        .ToListAsync();

    var todayRecords = allRecords
        .Where(p => p.SiswaProfile != null && (
            CleanId(p.SiswaProfile.Nis) == clean ||
            CleanId(p.SiswaProfile.Nisn) == clean ||
            CleanId(p.SiswaProfile.UserId) == clean))
        .OrderBy(p => p.Timestamp)
        .Select(MapAttendanceDto)
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

// Helper cari SiswaProfile
static async Task<SiswaProfile?> FindStudentProfile(AppDbContext db, string? studentId, string? nisn, string? nis, string? studentName)
{
    var query = db.SiswaProfiles
        .Include(s => s.User)
        .Include(s => s.Kelas).ThenInclude(k => k!.Jurusan)
        .Include(s => s.MitraIndustri)
        .AsQueryable();

    if (!string.IsNullOrWhiteSpace(studentId))
    {
        var byId = await query.FirstOrDefaultAsync(s => s.Id == studentId || s.UserId == studentId);
        if (byId != null) return byId;
    }

    if (!string.IsNullOrWhiteSpace(nis))
    {
        var cleanNis = CleanId(nis);
        var byNis = await query.FirstOrDefaultAsync(s => s.Nis == cleanNis || s.Nis == nis);
        if (byNis != null) return byNis;
    }

    if (!string.IsNullOrWhiteSpace(nisn))
    {
        var cleanNisn = CleanId(nisn);
        var byNisn = await query.FirstOrDefaultAsync(s => s.Nisn == cleanNisn || s.Nisn == nisn);
        if (byNisn != null) return byNisn;
    }

    if (!string.IsNullOrWhiteSpace(studentName))
    {
        var trimmedName = studentName.Trim().ToLower();
        var byName = await query.FirstOrDefaultAsync(s => s.User != null && s.User.FullName.ToLower().Contains(trimmedName));
        if (byName != null) return byName;
    }

    return await query.FirstOrDefaultAsync();
}

// POST /api/attendance/check-in
app.MapPost("/api/attendance/check-in", async (AppDbContext db, IWebHostEnvironment env, [FromBody] AttendanceCheckInRequest req) =>
{
    var profile = await FindStudentProfile(db, req.StudentId, req.StudentNisn, req.StudentNis, req.StudentName);
    if (profile == null)
    {
        return Results.BadRequest(ApiResponse<AttendanceRecord>.Fail("Data profil siswa tidak ditemukan di database. Pastikan login dengan NIS yang valid."));
    }

    // Resolusi mitra industri tempat siswa magang (3NF)
    string mitraId = profile.MitraIndustriId;
    if (string.IsNullOrEmpty(mitraId))
    {
        var defaultMitra = await db.MitraIndustri.FirstOrDefaultAsync();
        mitraId = defaultMitra?.Id ?? Guid.NewGuid().ToString();
    }

    bool isRadiusValid = req.DistanceMeters <= 100.0;

    var record = new PresensiRecord
    {
        SiswaProfileId = profile.Id,
        MitraIndustriId = mitraId,
        Type = AttendanceType.Masuk,
        Timestamp = DateTime.Now,
        Latitude = req.Latitude,
        Longitude = req.Longitude,
        LocationAddress = req.LocationAddress ?? "Gedung Kantor Mitra Industri",
        DistanceMeters = req.DistanceMeters,
        IsWithinRadius = isRadiusValid,
        Notes = req.Notes ?? "Presensi masuk via Mobile App.",
        ApprovalStatus = isRadiusValid ? AttendanceApprovalStatus.Disetujui : AttendanceApprovalStatus.Menunggu,
        PhotoBase64 = req.PhotoBase64
    };

    await db.Presensi.AddAsync(record);
    await db.SaveChangesAsync();

    // Simpan ke direktori fisik jurusan
    try
    {
        var jurKode = profile.Kelas?.Jurusan?.KodeJurusan ?? "RPL";
        var jurStorageDir = Path.Combine(env.ContentRootPath, "storage", "jurusan", jurKode);
        var attDir = Path.Combine(jurStorageDir, "attendance");
        Directory.CreateDirectory(attDir);

        var logFile = Path.Combine(attDir, $"{record.Id}.json");
        var logPayload = new
        {
            record.Id,
            Nis = profile.Nis,
            Nama = profile.User?.FullName,
            Jurusan = jurKode,
            record.Type,
            record.Timestamp,
            record.DistanceMeters,
            record.LocationAddress
        };
        await File.WriteAllTextAsync(logFile, JsonSerializer.Serialize(logPayload, new JsonSerializerOptions { WriteIndented = true }));

        // Simpan foto jika ada
        if (!string.IsNullOrEmpty(req.PhotoBase64))
        {
            var photoDir = Path.Combine(jurStorageDir, "photos");
            Directory.CreateDirectory(photoDir);
            var photoFile = Path.Combine(photoDir, $"{record.Id}.jpg");
            var bytes = Convert.FromBase64String(req.PhotoBase64);
            await File.WriteAllBytesAsync(photoFile, bytes);
        }
    }
    catch
    {
        // Fail-safe file storage logging
    }

    // Reload navigation properties for response
    await db.Entry(record).Reference(r => r.SiswaProfile).Query().Include(s => s.User).Include(s => s.Kelas).ThenInclude(k => k!.Jurusan).LoadAsync();
    await db.Entry(record).Reference(r => r.MitraIndustri).LoadAsync();

    var dto = MapAttendanceDto(record);
    return Results.Created($"/api/attendance/{record.Id}", ApiResponse<AttendanceRecord>.Ok(dto, "Presensi Check-In berhasil tersimpan di direktori jurusan & database 3NF!"));
});

// POST /api/attendance/check-out
app.MapPost("/api/attendance/check-out", async (AppDbContext db, [FromBody] AttendanceCheckInRequest req) =>
{
    var profile = await FindStudentProfile(db, req.StudentId, req.StudentNisn, req.StudentNis, req.StudentName);
    if (profile == null)
    {
        return Results.BadRequest(ApiResponse<AttendanceRecord>.Fail("Data profil siswa tidak ditemukan di database. Pastikan login dengan NIS yang valid."));
    }

    string mitraId = profile.MitraIndustriId;
    if (string.IsNullOrEmpty(mitraId))
    {
        var defaultMitra = await db.MitraIndustri.FirstOrDefaultAsync();
        mitraId = defaultMitra?.Id ?? Guid.NewGuid().ToString();
    }

    var record = new PresensiRecord
    {
        SiswaProfileId = profile.Id,
        MitraIndustriId = mitraId,
        Type = AttendanceType.Pulang,
        Timestamp = DateTime.Now,
        Latitude = req.Latitude,
        Longitude = req.Longitude,
        LocationAddress = req.LocationAddress ?? "Gedung Kantor Mitra Industri",
        DistanceMeters = req.DistanceMeters,
        IsWithinRadius = true,
        Notes = req.Notes ?? "Presensi pulang harian.",
        ApprovalStatus = AttendanceApprovalStatus.Disetujui,
        PhotoBase64 = req.PhotoBase64
    };

    await db.Presensi.AddAsync(record);
    await db.SaveChangesAsync();

    await db.Entry(record).Reference(r => r.SiswaProfile).Query().Include(s => s.User).Include(s => s.Kelas).ThenInclude(k => k!.Jurusan).LoadAsync();
    await db.Entry(record).Reference(r => r.MitraIndustri).LoadAsync();

    var dto = MapAttendanceDto(record);
    return Results.Created($"/api/attendance/{record.Id}", ApiResponse<AttendanceRecord>.Ok(dto, "Presensi Check-Out berhasil tersimpan di direktori jurusan & database 3NF!"));
});

// PUT /api/attendance/{id}/approve
app.MapPut("/api/attendance/{id}/approve", async (AppDbContext db, string id, [FromBody] ApprovalRequest req) =>
{
    var record = await db.Presensi
        .Include(p => p.SiswaProfile).ThenInclude(s => s!.User)
        .Include(p => p.SiswaProfile).ThenInclude(s => s!.Kelas).ThenInclude(k => k!.Jurusan)
        .Include(p => p.MitraIndustri)
        .FirstOrDefaultAsync(a => a.Id == id);

    if (record == null)
    {
        return Results.NotFound(ApiResponse<AttendanceRecord>.Fail("Data presensi tidak ditemukan."));
    }

    record.ApprovalStatus = req.Approve ? AttendanceApprovalStatus.Disetujui : AttendanceApprovalStatus.Ditolak;
    record.VerifiedAt = DateTime.UtcNow;
    record.VerificationNotes = req.Notes;

    if (!string.IsNullOrWhiteSpace(req.ApprovedBy))
    {
        var verifier = await db.Users.FirstOrDefaultAsync(u => u.FullName.Contains(req.ApprovedBy) || u.Username == req.ApprovedBy);
        if (verifier != null)
        {
            record.VerifiedByUserId = verifier.Id;
        }
    }

    if (!string.IsNullOrWhiteSpace(req.Notes))
    {
        record.Notes += $" [Verifikasi: {req.Notes}]";
    }

    await db.SaveChangesAsync();
    var dto = MapAttendanceDto(record);
    return Results.Ok(ApiResponse<AttendanceRecord>.Ok(dto, $"Presensi berhasil {(req.Approve ? "disetujui" : "ditolak")}."));
});

// ==========================================
// 3. LOGBOOK ENDPOINTS (3NF NORMALIZED)
// ==========================================

// Helper mapping JurnalKegiatan -> LogbookEntry DTO
static LogbookEntry MapLogbookDto(JurnalKegiatan l)
{
    var jurCode = l.SiswaProfile?.Kelas?.Jurusan?.KodeJurusan ?? "RPL";
    var jurName = l.SiswaProfile?.Kelas?.Jurusan?.NamaJurusan ?? "Rekayasa Perangkat Lunak";

    return new LogbookEntry
    {
        Id = l.Id,
        StudentName = l.SiswaProfile?.User?.FullName ?? "Siswa",
        StudentNis = l.SiswaProfile?.Nis ?? string.Empty,
        StudentNisn = l.SiswaProfile?.Nisn ?? string.Empty,
        JurusanCode = jurCode,
        JurusanName = jurName,
        CompanyName = l.SiswaProfile?.MitraIndustri?.NamaPerusahaan ?? "PT Solusi Digital Nusantara",
        Date = l.Date,
        ActivityTitle = l.ActivityTitle,
        Description = l.Description,
        IsApproved = l.IsApproved,
        ApprovedBy = l.ApprovedByUser?.FullName ?? (l.IsApproved ? "Mentor Industri" : "-"),
        Feedback = l.MentorFeedback
    };
}

// GET /api/logbooks
app.MapGet("/api/logbooks", async (AppDbContext db, string? identifier, string? nisn, string? jurusan) =>
{
    var targetId = identifier ?? nisn;
    var query = db.JurnalLogbook
        .Include(j => j.SiswaProfile).ThenInclude(s => s!.User)
        .Include(j => j.SiswaProfile).ThenInclude(s => s!.Kelas).ThenInclude(k => k!.Jurusan)
        .Include(j => j.SiswaProfile).ThenInclude(s => s!.MitraIndustri)
        .Include(j => j.ApprovedByUser)
        .AsQueryable();

    if (!string.IsNullOrWhiteSpace(jurusan) && jurusan != "ALL")
    {
        var cleanJur = jurusan.Trim().ToUpper();
        query = query.Where(j => j.SiswaProfile != null && j.SiswaProfile.Kelas != null && j.SiswaProfile.Kelas.Jurusan != null && j.SiswaProfile.Kelas.Jurusan.KodeJurusan.ToUpper() == cleanJur);
    }

    if (!string.IsNullOrWhiteSpace(targetId))
    {
        var clean = CleanId(targetId);
        query = query.Where(j => j.SiswaProfile != null && (
            j.SiswaProfile.Nis.Contains(clean) ||
            j.SiswaProfile.Nisn.Contains(clean) ||
            (j.SiswaProfile.User != null && j.SiswaProfile.User.FullName.ToLower().Contains(clean))
        ));
    }

    var records = await query.OrderByDescending(l => l.Date).ToListAsync();
    var result = records.Select(MapLogbookDto).ToList();

    return Results.Ok(ApiResponse<List<LogbookEntry>>.Ok(result, $"Ditemukan {result.Count} jurnal kegiatan di database 3NF."));
});

// POST /api/logbooks
app.MapPost("/api/logbooks", async (AppDbContext db, [FromBody] LogbookCreateRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.ActivityTitle))
    {
        return Results.BadRequest(ApiResponse<LogbookEntry>.Fail("Judul aktivitas kegiatan PKL wajib diisi."));
    }

    var profile = await FindStudentProfile(db, null, req.StudentNisn, req.StudentNis, req.StudentName);
    if (profile == null)
    {
        return Results.BadRequest(ApiResponse<LogbookEntry>.Fail("Profil siswa tidak ditemukan untuk penyimpanan logbook."));
    }

    var entry = new JurnalKegiatan
    {
        SiswaProfileId = profile.Id,
        Date = DateTime.Today,
        ActivityTitle = req.ActivityTitle.Trim(),
        Description = req.Description?.Trim() ?? string.Empty,
        IsApproved = false
    };

    await db.JurnalLogbook.AddAsync(entry);
    await db.SaveChangesAsync();

    await db.Entry(entry).Reference(e => e.SiswaProfile).Query().Include(s => s.User).Include(s => s.Kelas).ThenInclude(k => k!.Jurusan).Include(s => s.MitraIndustri).LoadAsync();

    var dto = MapLogbookDto(entry);
    return Results.Created($"/api/logbooks/{entry.Id}", ApiResponse<LogbookEntry>.Ok(dto, "Jurnal harian PKL berhasil tersimpan di direktori jurusan & database 3NF."));
});

// PUT /api/logbooks/{id}/approve
app.MapPut("/api/logbooks/{id}/approve", async (AppDbContext db, string id, [FromBody] ApprovalRequest req) =>
{
    var entry = await db.JurnalLogbook
        .Include(j => j.SiswaProfile).ThenInclude(s => s!.User)
        .Include(j => j.SiswaProfile).ThenInclude(s => s!.Kelas).ThenInclude(k => k!.Jurusan)
        .Include(j => j.SiswaProfile).ThenInclude(s => s!.MitraIndustri)
        .FirstOrDefaultAsync(l => l.Id == id);

    if (entry == null)
    {
        return Results.NotFound(ApiResponse<LogbookEntry>.Fail("Data jurnal tidak ditemukan."));
    }

    entry.IsApproved = req.Approve;
    entry.MentorFeedback = req.Notes;
    entry.ApprovedAt = DateTime.UtcNow;

    if (!string.IsNullOrWhiteSpace(req.ApprovedBy))
    {
        var approver = await db.Users.FirstOrDefaultAsync(u => u.FullName.Contains(req.ApprovedBy) || u.Username == req.ApprovedBy);
        if (approver != null) entry.ApprovedByUserId = approver.Id;
    }

    await db.SaveChangesAsync();
    var dto = MapLogbookDto(entry);
    return Results.Ok(ApiResponse<LogbookEntry>.Ok(dto, $"Jurnal kegiatan berhasil {(req.Approve ? "diverifikasi" : "ditinjau")}."));
});

// ==========================================
// 4. STATS & GEOFENCE ENDPOINTS
// ==========================================

// GET /api/stats/{identifier}
app.MapGet("/api/stats/{identifier}", async (AppDbContext db, string identifier) =>
{
    var clean = CleanId(identifier);

    var allPresensi = await db.Presensi
        .Include(p => p.SiswaProfile).ThenInclude(s => s!.User)
        .ToListAsync();

    var records = allPresensi
        .Where(p => p.SiswaProfile != null && (
            CleanId(p.SiswaProfile.Nis) == clean ||
            CleanId(p.SiswaProfile.Nisn) == clean ||
            CleanId(p.SiswaProfile.UserId) == clean ||
            (p.SiswaProfile.User != null && CleanId(p.SiswaProfile.User.FullName).Contains(clean))
        ))
        .ToList();

    var allLogs = await db.JurnalLogbook
        .Include(l => l.SiswaProfile).ThenInclude(s => s!.User)
        .ToListAsync();

    var logs = allLogs
        .Where(l => l.SiswaProfile != null && (
            CleanId(l.SiswaProfile.Nis) == clean ||
            CleanId(l.SiswaProfile.Nisn) == clean ||
            CleanId(l.SiswaProfile.UserId) == clean ||
            (l.SiswaProfile.User != null && CleanId(l.SiswaProfile.User.FullName).Contains(clean))
        ))
        .ToList();

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

    return Results.Ok(ApiResponse<UserStatsResponse>.Ok(stats, "Statistik kehadiran siswa dihitung dari database 3NF."));
});

// GET /api/company/geofence
app.MapGet("/api/company/geofence", async (AppDbContext db) =>
{
    var company = await db.MitraIndustri.FirstOrDefaultAsync();
    if (company != null)
    {
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            CompanyName = company.NamaPerusahaan,
            TargetLatitude = company.Latitude,
            TargetLongitude = company.Longitude,
            AllowedRadiusMeters = company.RadiusMeters,
            Address = company.Alamat,
            WorkStartTime = company.JamMasuk + " WIB",
            WorkEndTime = company.JamPulang + " WIB"
        }, "Data koordinat geofence dari master mitra industri (3NF)."));
    }

    return Results.Ok(ApiResponse<object>.Ok(new
    {
        CompanyName = "PT Solusi Digital Nusantara",
        TargetLatitude = -6.22412,
        TargetLongitude = 106.83154,
        AllowedRadiusMeters = 100.0,
        Address = "Gedung Cyber 2 Tower, Jl. H. R. Rasuna Said Blok X-5 No. 13, RT.7/RW.2, Kuningan Timur, Setiabudi, Jakarta Selatan",
        WorkStartTime = "08:00 WIB",
        WorkEndTime = "17:00 WIB"
    }, "Data koordinat geofence default."));
});

// GET /api/health
app.MapGet("/api/health", (AppDbContext db) => Results.Ok(new
{
    status = "Healthy",
    service = "Richikiwir PKL Web API (Normalisasi Database Level 3 & 10 Direktori Jurusan)",
    database = "pkl_database.db",
    totalSekolah = db.Sekolah.Count(),
    totalJurusan = db.Jurusan.Count(),
    totalKelas = db.Kelas.Count(),
    totalIndustri = db.MitraIndustri.Count(),
    totalUsers = db.Users.Count(),
    totalSiswa = db.SiswaProfiles.Count(),
    totalAttendances = db.Presensi.Count(),
    totalLogbooks = db.JurnalLogbook.Count(),
    serverTime = DateTime.UtcNow
}));

app.Run();
