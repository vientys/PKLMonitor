using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Richikiwir.Api.Data;
using Richikiwir.Api.Models;
using Richikiwir.Api.Services;

var adminSessions = new ConcurrentDictionary<string, (string UserId, DateTimeOffset ExpiresAt)>();
var passwordHasher = new PasswordHasher<UserAccount>();
var builder = WebApplication.CreateBuilder(args);

// Configure SQLite Database
var dbPath = Path.Combine(builder.Environment.ContentRootPath, "pkl_database.db");
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlite($"Data Source={dbPath}");
});
builder.Services.AddHostedService<DailyAbsenceService>();

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

    var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    await db.Database.OpenConnectionAsync();
    try
    {
        await using (var command = db.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = "PRAGMA table_info('users')";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) columns.Add(reader.GetString(1));
        }

        var addedSetupColumns = false;
        if (!columns.Contains("MustCompleteProfile"))
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE users ADD COLUMN MustCompleteProfile INTEGER NOT NULL DEFAULT 0");
            addedSetupColumns = true;
        }
        if (!columns.Contains("MustChangePassword"))
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE users ADD COLUMN MustChangePassword INTEGER NOT NULL DEFAULT 0");
            addedSetupColumns = true;
        }
        if (addedSetupColumns)
            await db.Database.ExecuteSqlRawAsync("UPDATE users SET MustCompleteProfile = 1, MustChangePassword = 1");

        var attendanceColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var command = db.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = "PRAGMA table_info('presensi')";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) attendanceColumns.Add(reader.GetString(1));
        }
        if (!attendanceColumns.Contains("LateMinutes"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE presensi ADD COLUMN LateMinutes INTEGER NOT NULL DEFAULT 0");
        if (!attendanceColumns.Contains("IsLate"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE presensi ADD COLUMN IsLate INTEGER NOT NULL DEFAULT 0");
    }
    finally
    {
        await db.Database.CloseConnectionAsync();
    }

    AppDbContext.SeedData(db);
    AppDbContext.SyncPhysicalJurusanDirectories(builder.Environment.ContentRootPath, db);
}

app.UseCors("AllowAll");
app.UseDefaultFiles();
app.UseStaticFiles();

async Task<UserAccount?> ResolveAdminAsync(HttpContext context, AppDbContext db)
{
    if (!context.Request.IsHttps && !app.Environment.IsDevelopment()) return null;

    var authorization = context.Request.Headers["Authorization"].ToString();
    if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;

    var token = authorization[7..].Trim();
    if (!adminSessions.TryGetValue(token, out var session)) return null;
    if (session.ExpiresAt <= DateTimeOffset.UtcNow)
    {
        adminSessions.TryRemove(token, out _);
        return null;
    }

    return await db.Users.FirstOrDefaultAsync(user =>
        user.Id == session.UserId && user.Role == UserRole.Admin && user.IsActive &&
        !user.MustCompleteProfile && !user.MustChangePassword);
}

// Helper pembersih identifier
static string CleanId(string? s) => string.IsNullOrWhiteSpace(s) ? string.Empty : s.Replace(" ", "").Replace("-", "").Trim().ToLowerInvariant();

static double CalculateDistanceMeters(double latitude1, double longitude1, double latitude2, double longitude2)
{
    const double earthRadiusMeters = 6371000;
    var latitudeDelta = (latitude2 - latitude1) * Math.PI / 180;
    var longitudeDelta = (longitude2 - longitude1) * Math.PI / 180;
    var haversine = Math.Pow(Math.Sin(latitudeDelta / 2), 2) +
        Math.Cos(latitude1 * Math.PI / 180) * Math.Cos(latitude2 * Math.PI / 180) *
        Math.Pow(Math.Sin(longitudeDelta / 2), 2);
    return Math.Round(earthRadiusMeters * 2 * Math.Atan2(Math.Sqrt(haversine), Math.Sqrt(1 - haversine)), 1);
}

static string GenerateTemporaryPassword()
{
    const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$";
    return string.Concat(Enumerable.Range(0, 12)
        .Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]));
}

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
        return Results.NotFound(ApiResponse<UserModel>.Fail("Akun tidak ditemukan. Periksa NISN/NIP atau hubungi administrator."));
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
    bool isHashedPassword = user.PasswordHash.StartsWith("AQAAAA", StringComparison.Ordinal);
    bool isPasswordValid = isHashedPassword
        ? passwordHasher.VerifyHashedPassword(user, user.PasswordHash, req.Password) != PasswordVerificationResult.Failed
        : string.Equals(user.PasswordHash, req.Password, StringComparison.Ordinal);

    if (!isPasswordValid)
    {
        return Results.BadRequest(ApiResponse<UserModel>.Fail("Kata sandi yang Anda masukkan salah."));
    }

    if (!isHashedPassword)
    {
        user.PasswordHash = passwordHasher.HashPassword(user, req.Password);
        await db.SaveChangesAsync();
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
        CreatedAt = user.CreatedAt,
        MustCompleteProfile = user.MustCompleteProfile,
        MustChangePassword = user.MustChangePassword
    };

    var accessToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    adminSessions[accessToken] = (user.Id, DateTimeOffset.UtcNow.AddHours(8));
    safeUser.AccessToken = accessToken;

    return Results.Ok(ApiResponse<UserModel>.Ok(safeUser, $"Selamat datang kembali, {user.FullName}! (Direktori Jurusan: {jurCode})"));
});

app.MapPut("/api/account/first-login", async (
    HttpContext context, AppDbContext db, [FromBody] FirstLoginSetupRequest request) =>
{
    if (!context.Request.IsHttps && !app.Environment.IsDevelopment()) return Results.Unauthorized();
    var authorization = context.Request.Headers["Authorization"].ToString();
    if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return Results.Unauthorized();
    var token = authorization[7..].Trim();
    if (!adminSessions.TryGetValue(token, out var session) || session.ExpiresAt <= DateTimeOffset.UtcNow)
        return Results.Unauthorized();

    var user = await db.Users.FirstOrDefaultAsync(account => account.Id == session.UserId && account.IsActive);
    if (user == null) return Results.Unauthorized();
    if (!user.MustCompleteProfile && !user.MustChangePassword)
        return Results.BadRequest(ApiResponse<object>.Fail("Pengaturan awal akun ini sudah selesai."));

    var fullName = request.FullName.Trim();
    var email = request.Email.Trim();
    var phoneNumber = request.PhoneNumber.Trim();
    if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(phoneNumber))
        return Results.BadRequest(ApiResponse<object>.Fail("Nama, email, dan nomor telepon wajib dilengkapi."));
    if (request.NewPassword.Length < 8)
        return Results.BadRequest(ApiResponse<object>.Fail("Kata sandi baru minimal 8 karakter."));
    if (passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.NewPassword) != PasswordVerificationResult.Failed)
        return Results.BadRequest(ApiResponse<object>.Fail("Kata sandi baru harus berbeda dari kata sandi sementara."));
    if (await db.Users.AnyAsync(account => account.Id != user.Id && account.Email.ToLower() == email.ToLower()))
        return Results.Conflict(ApiResponse<object>.Fail("Email sudah digunakan akun lain."));

    user.FullName = fullName;
    user.Email = email;
    user.PhoneNumber = phoneNumber;
    user.PasswordHash = passwordHasher.HashPassword(user, request.NewPassword);
    user.MustCompleteProfile = false;
    user.MustChangePassword = false;
    await db.SaveChangesAsync();

    return Results.Ok(ApiResponse<object>.Ok(new
    {
        user.Id,
        user.FullName,
        user.Email,
        user.PhoneNumber,
        user.MustCompleteProfile,
        user.MustChangePassword
    }, "Profil lengkap dan kata sandi berhasil diperbarui."));
});


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

// Admin-only account management endpoints
app.MapGet("/api/admin/users", async (HttpContext context, AppDbContext db) =>
{
    if (await ResolveAdminAsync(context, db) == null) return Results.Unauthorized();

    var accountEntities = await db.Users
        .Include(user => user.SiswaProfile)
            .ThenInclude(profile => profile!.Kelas)
                .ThenInclude(classroom => classroom!.Jurusan)
        .Include(user => user.SiswaProfile)
            .ThenInclude(profile => profile!.MitraIndustri)
        .Include(user => user.GuruProfile)
            .ThenInclude(profile => profile!.Sekolah)
        .Include(user => user.MentorIndustriProfile)
            .ThenInclude(profile => profile!.MitraIndustri)
        .OrderBy(user => user.Role).ThenBy(user => user.FullName)
        .ToListAsync();

    var accounts = accountEntities.Select(user => new UserModel
        {
            Id = user.Id,
            FullName = user.FullName,
            Identifier = user.SiswaProfile != null ? user.SiswaProfile.Nis
                : user.GuruProfile != null ? user.GuruProfile.Nip
                : user.MentorIndustriProfile != null ? user.MentorIndustriProfile.NikKaryawan
                : user.Username,
            Username = user.Username,
            Email = user.Email,
            PhoneNumber = user.PhoneNumber,
            Nisn = user.SiswaProfile == null ? string.Empty : user.SiswaProfile.Nisn,
            SchoolName = user.SiswaProfile?.Kelas?.Jurusan?.Sekolah?.NamaSekolah
                ?? user.GuruProfile?.Sekolah?.NamaSekolah ?? string.Empty,
            CompanyName = user.SiswaProfile?.MitraIndustri?.NamaPerusahaan
                ?? user.MentorIndustriProfile?.MitraIndustri?.NamaPerusahaan ?? string.Empty,
            MajorOrDepartment = user.SiswaProfile?.Kelas?.NamaKelas
                ?? user.GuruProfile?.Jabatan ?? user.MentorIndustriProfile?.Divisi ?? string.Empty,
            JurusanCode = user.SiswaProfile?.Kelas?.Jurusan?.KodeJurusan ?? string.Empty,
            JurusanName = user.SiswaProfile?.Kelas?.Jurusan?.NamaJurusan ?? string.Empty,
            Role = user.Role,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt
        }).ToList();

    return Results.Ok(ApiResponse<List<UserModel>>.Ok(accounts, "Daftar akun berhasil dimuat."));
});

app.MapPost("/api/admin/users", async (HttpContext context, AppDbContext db, [FromBody] AdminUserRequest request) =>
{
    if (await ResolveAdminAsync(context, db) == null) return Results.Unauthorized();

    var fullName = request.FullName.Trim();
    var identifier = request.Identifier.Trim();
    if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(identifier))
        return Results.BadRequest(ApiResponse<object>.Fail("Nama dan identitas login wajib diisi."));
    if (!Enum.IsDefined(request.Role))
        return Results.BadRequest(ApiResponse<object>.Fail("Peran akun tidak valid."));
    if (await db.Users.AnyAsync(user => user.Username.ToLower() == identifier.ToLower()))
        return Results.Conflict(ApiResponse<object>.Fail("Identitas login sudah digunakan."));

    var temporaryPassword = GenerateTemporaryPassword();
    var account = new UserAccount
    {
        Username = identifier,
        FullName = fullName,
        Email = request.Email.Trim(),
        PhoneNumber = request.PhoneNumber.Trim(),
        Role = request.Role,
        IsActive = true,
        MustCompleteProfile = true,
        MustChangePassword = true
    };
    account.PasswordHash = passwordHasher.HashPassword(account, temporaryPassword);

    switch (request.Role)
    {
        case UserRole.Siswa:
            var nisn = request.Nisn.Trim();
            if (string.IsNullOrWhiteSpace(nisn) || string.IsNullOrWhiteSpace(request.JurusanCode))
                return Results.BadRequest(ApiResponse<object>.Fail("NISN dan jurusan wajib diisi untuk akun siswa."));
            if (await db.SiswaProfiles.AnyAsync(profile => profile.Nisn == nisn || profile.Nis == identifier))
                return Results.Conflict(ApiResponse<object>.Fail("NIS atau NISN sudah digunakan."));

            var studentClass = await db.Kelas
                .Include(classroom => classroom.Jurusan)
                .Where(classroom => classroom.Jurusan != null && classroom.Jurusan.KodeJurusan == request.JurusanCode)
                .OrderBy(classroom => classroom.NamaKelas)
                .FirstOrDefaultAsync();
            var studentCompany = await db.MitraIndustri.OrderBy(company => company.NamaPerusahaan).FirstOrDefaultAsync();
            if (studentClass == null || studentCompany == null)
                return Results.BadRequest(ApiResponse<object>.Fail("Data kelas atau mitra industri belum tersedia."));

            account.SiswaProfile = new SiswaProfile
            {
                Nis = identifier,
                Nisn = nisn,
                KelasId = studentClass.Id,
                MitraIndustriId = studentCompany.Id
            };
            break;
        case UserRole.PembimbingSekolah:
            var school = await db.Sekolah.OrderBy(item => item.NamaSekolah).FirstOrDefaultAsync();
            if (school == null) return Results.BadRequest(ApiResponse<object>.Fail("Data sekolah belum tersedia."));
            if (await db.GuruProfiles.AnyAsync(profile => profile.Nip == identifier))
                return Results.Conflict(ApiResponse<object>.Fail("NIP sudah digunakan."));
            account.GuruProfile = new GuruProfile { Nip = identifier, SekolahId = school.Id };
            break;
        case UserRole.PembimbingIndustri:
            var company = await db.MitraIndustri.OrderBy(item => item.NamaPerusahaan).FirstOrDefaultAsync();
            if (company == null) return Results.BadRequest(ApiResponse<object>.Fail("Data mitra industri belum tersedia."));
            if (await db.MentorProfiles.AnyAsync(profile => profile.NikKaryawan == identifier))
                return Results.Conflict(ApiResponse<object>.Fail("NIK atau ID karyawan sudah digunakan."));
            account.MentorIndustriProfile = new MentorIndustriProfile { NikKaryawan = identifier, MitraIndustriId = company.Id };
            break;
    }

    db.Users.Add(account);
    await db.SaveChangesAsync();
    return Results.Created($"/api/admin/users/{account.Id}", ApiResponse<object>.Ok(
        new { account.Id, TemporaryPassword = temporaryPassword }, "Akun berhasil ditambahkan. Bagikan kata sandi sementara ini kepada pengguna."));
});

app.MapPut("/api/admin/users/{id}", async (HttpContext context, AppDbContext db, string id, [FromBody] AdminUserRequest request) =>
{
    if (await ResolveAdminAsync(context, db) == null) return Results.Unauthorized();

    var account = await db.Users
        .Include(user => user.SiswaProfile)
        .Include(user => user.GuruProfile)
        .Include(user => user.MentorIndustriProfile)
        .FirstOrDefaultAsync(user => user.Id == id);
    if (account == null) return Results.NotFound(ApiResponse<object>.Fail("Akun tidak ditemukan."));
    if (request.Role != account.Role)
        return Results.BadRequest(ApiResponse<object>.Fail("Peran akun tidak dapat diubah setelah akun dibuat."));

    var fullName = request.FullName.Trim();
    var identifier = request.Identifier.Trim();
    if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(identifier))
        return Results.BadRequest(ApiResponse<object>.Fail("Nama dan identitas login wajib diisi."));
    if (await db.Users.AnyAsync(user => user.Id != id && user.Username.ToLower() == identifier.ToLower()))
        return Results.Conflict(ApiResponse<object>.Fail("Identitas login sudah digunakan."));

    account.FullName = fullName;
    account.Username = identifier;
    account.Email = request.Email.Trim();
    account.PhoneNumber = request.PhoneNumber.Trim();
    if (!string.IsNullOrWhiteSpace(request.Password))
    {
        if (request.Password.Length < 8)
            return Results.BadRequest(ApiResponse<object>.Fail("Kata sandi minimal 8 karakter."));
        account.PasswordHash = passwordHasher.HashPassword(account, request.Password);
        account.MustCompleteProfile = true;
        account.MustChangePassword = true;
    }

    if (account.SiswaProfile != null)
    {
        if (await db.SiswaProfiles.AnyAsync(profile => profile.UserId != id && profile.Nis == identifier))
            return Results.Conflict(ApiResponse<object>.Fail("NIS sudah digunakan."));
        account.SiswaProfile.Nis = identifier;
        if (!string.IsNullOrWhiteSpace(request.Nisn))
        {
            if (await db.SiswaProfiles.AnyAsync(profile => profile.UserId != id && profile.Nisn == request.Nisn.Trim()))
                return Results.Conflict(ApiResponse<object>.Fail("NISN sudah digunakan."));
            account.SiswaProfile.Nisn = request.Nisn.Trim();
        }
    }
    else if (account.GuruProfile != null)
    {
        if (await db.GuruProfiles.AnyAsync(profile => profile.UserId != id && profile.Nip == identifier))
            return Results.Conflict(ApiResponse<object>.Fail("NIP sudah digunakan."));
        account.GuruProfile.Nip = identifier;
    }
    else if (account.MentorIndustriProfile != null)
    {
        if (await db.MentorProfiles.AnyAsync(profile => profile.UserId != id && profile.NikKaryawan == identifier))
            return Results.Conflict(ApiResponse<object>.Fail("NIK atau ID karyawan sudah digunakan."));
        account.MentorIndustriProfile.NikKaryawan = identifier;
    }

    await db.SaveChangesAsync();
    return Results.Ok(ApiResponse<object>.Ok(new { account.Id }, "Perubahan akun berhasil disimpan."));
});

app.MapDelete("/api/admin/users/{id}", async (HttpContext context, AppDbContext db, string id) =>
{
    var admin = await ResolveAdminAsync(context, db);
    if (admin == null) return Results.Unauthorized();
    if (admin.Id == id) return Results.BadRequest(ApiResponse<object>.Fail("Akun admin yang sedang digunakan tidak dapat dihapus."));

    var account = await db.Users.FirstOrDefaultAsync(user => user.Id == id);
    if (account == null) return Results.NotFound(ApiResponse<object>.Fail("Akun tidak ditemukan."));
    if (account.Role == UserRole.Admin && account.IsActive &&
        await db.Users.CountAsync(user => user.Role == UserRole.Admin && user.IsActive) <= 1)
        return Results.BadRequest(ApiResponse<object>.Fail("Admin aktif terakhir tidak dapat dihapus."));

    var hasHistory = await db.Presensi.AnyAsync(record =>
            record.VerifiedByUserId == id || (record.SiswaProfile != null && record.SiswaProfile.UserId == id))
        || await db.JurnalLogbook.AnyAsync(entry =>
            entry.ApprovedByUserId == id || (entry.SiswaProfile != null && entry.SiswaProfile.UserId == id))
        || await db.SiswaProfiles.AnyAsync(profile => profile.GuruPembimbingId == id || profile.MentorIndustriId == id);
    if (hasHistory)
        return Results.Conflict(ApiResponse<object>.Fail("Akun masih terhubung ke presensi, jurnal, atau penugasan dan tidak dapat dihapus."));

    db.Users.Remove(account);
    await db.SaveChangesAsync();
    foreach (var session in adminSessions.Where(pair => pair.Value.UserId == id).ToArray())
        adminSessions.TryRemove(session.Key, out _);

    return Results.Ok(ApiResponse<object>.Ok(new { account.Id }, "Akun berhasil dihapus."));
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
        todayIzin = todayAtt.Count(a => a.Type == AttendanceType.Izin),
        todaySakit = todayAtt.Count(a => a.Type == AttendanceType.Sakit),
        todayAlpa = todayAtt.Count(a => a.Type == AttendanceType.Alpa),
        todayLate = todayAtt.Count(a => a.Type == AttendanceType.Masuk && a.IsLate),
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
    bool isRadiusValid = p.DistanceMeters <= (p.MitraIndustri?.RadiusMeters ?? 100.0);
    string statusText = !isRadiusValid
        ? $"Presensi Di Luar Radius ({p.DistanceMeters:F1}m)"
        : p.Type == AttendanceType.Masuk && p.IsLate
            ? $"Terlambat {p.LateMinutes} menit"
            : p.Type == AttendanceType.Masuk ? "Hadir Tepat Waktu" : p.Type.ToString();

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
        LateMinutes = p.LateMinutes,
        IsLate = p.IsLate,
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
    var absence = todayRecords.FirstOrDefault(a =>
        a.Type == AttendanceType.Izin || a.Type == AttendanceType.Sakit || a.Type == AttendanceType.Alpa);

    return Results.Ok(ApiResponse<object>.Ok(new
    {
        HasCheckedIn = checkIn != null,
        CheckIn = checkIn,
        HasCheckedOut = checkOut != null,
        CheckOut = checkOut,
        HasAbsence = absence != null,
        Absence = absence,
        Status = checkIn != null ? "Hadir" : absence?.Type.ToString() ?? "Belum Absen",
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

app.MapPost("/api/attendance/absence", async (AppDbContext db, [FromBody] AttendanceCheckInRequest req) =>
{
    if (req.Type is not (AttendanceType.Izin or AttendanceType.Sakit or AttendanceType.Alpa))
        return Results.BadRequest(ApiResponse<AttendanceRecord>.Fail("Kategori ketidakhadiran harus Izin, Sakit, atau Alpa."));

    var profile = await FindStudentProfile(db, req.StudentId, req.StudentNisn, req.StudentNis, req.StudentName);
    if (profile == null)
        return Results.BadRequest(ApiResponse<AttendanceRecord>.Fail("Data profil siswa tidak ditemukan."));

    var today = DateTime.Today;
    var hasTodayRecord = await db.Presensi.AnyAsync(record =>
        record.SiswaProfileId == profile.Id && record.Timestamp >= today && record.Timestamp < today.AddDays(1));
    if (hasTodayRecord)
        return Results.Conflict(ApiResponse<AttendanceRecord>.Fail("Presensi hari ini sudah tercatat; status ketidakhadiran tidak dapat ditambahkan."));

    var companyId = profile.MitraIndustriId;
    if (string.IsNullOrWhiteSpace(companyId))
    {
        companyId = await db.MitraIndustri.Select(company => company.Id).FirstOrDefaultAsync();
        if (companyId == null)
            return Results.BadRequest(ApiResponse<AttendanceRecord>.Fail("Mitra industri siswa belum tersedia."));
    }

    var record = new PresensiRecord
    {
        SiswaProfileId = profile.Id,
        MitraIndustriId = companyId,
        Type = req.Type,
        Timestamp = DateTime.Now,
        LocationAddress = "Tidak hadir di lokasi PKL",
        DistanceMeters = 0,
        IsWithinRadius = false,
        Notes = string.IsNullOrWhiteSpace(req.Notes) ? req.Type.ToString() : req.Notes.Trim(),
        ApprovalStatus = AttendanceApprovalStatus.Menunggu
    };

    db.Presensi.Add(record);
    await db.SaveChangesAsync();
    await db.Entry(record).Reference(item => item.SiswaProfile).Query()
        .Include(student => student!.User)
        .Include(student => student!.Kelas).ThenInclude(classroom => classroom!.Jurusan)
        .LoadAsync();
    await db.Entry(record).Reference(item => item.MitraIndustri).LoadAsync();

    return Results.Created($"/api/attendance/{record.Id}",
        ApiResponse<AttendanceRecord>.Ok(MapAttendanceDto(record), $"Status {req.Type} berhasil dicatat."));
});

// POST /api/attendance/check-in
app.MapPost("/api/attendance/check-in", async (AppDbContext db, IWebHostEnvironment env, [FromBody] AttendanceCheckInRequest req) =>
{
    var profile = await FindStudentProfile(db, req.StudentId, req.StudentNisn, req.StudentNis, req.StudentName);
    if (profile == null)
    {
        return Results.BadRequest(ApiResponse<AttendanceRecord>.Fail("Data profil siswa tidak ditemukan di database. Pastikan login dengan NIS yang valid."));
    }

    if (req.Type != AttendanceType.Masuk)
        return Results.BadRequest(ApiResponse<AttendanceRecord>.Fail("Gunakan kategori Masuk untuk check-in."));
    var today = DateTime.Today;
    var hasExistingCheckInOrAbsence = await db.Presensi.AnyAsync(record =>
        record.SiswaProfileId == profile.Id && record.Timestamp >= today && record.Timestamp < today.AddDays(1) &&
        (record.Type == AttendanceType.Masuk || record.Type == AttendanceType.Izin ||
         record.Type == AttendanceType.Sakit || record.Type == AttendanceType.Alpa));
    if (hasExistingCheckInOrAbsence)
        return Results.Conflict(ApiResponse<AttendanceRecord>.Fail("Status kehadiran hari ini sudah tercatat."));

    var company = profile.MitraIndustri ?? await db.MitraIndustri
        .FirstOrDefaultAsync(item => item.Id == profile.MitraIndustriId);
    if (company == null)
        return Results.BadRequest(ApiResponse<AttendanceRecord>.Fail("Mitra industri siswa belum memiliki lokasi presensi."));
    if (req.Latitude is < -90 or > 90 || req.Longitude is < -180 or > 180)
        return Results.BadRequest(ApiResponse<AttendanceRecord>.Fail("Koordinat GPS tidak valid."));

    var distanceMeters = CalculateDistanceMeters(req.Latitude, req.Longitude, company.Latitude, company.Longitude);
    if (distanceMeters > company.RadiusMeters)
        return Results.BadRequest(ApiResponse<AttendanceRecord>.Fail(
            $"Presensi ditolak. Jarak Anda {distanceMeters:F1} m, melebihi radius {company.RadiusMeters:F0} m dari lokasi kantor."));

    var checkInTimestamp = DateTime.Now;
    var lateMinutes = 0;
    if (TimeOnly.TryParse(company.JamMasuk, out var scheduledStart))
    {
        var minutesAfterStart = (TimeOnly.FromDateTime(checkInTimestamp) - scheduledStart).TotalMinutes;
        if (minutesAfterStart > 0) lateMinutes = (int)Math.Ceiling(minutesAfterStart);
    }

    var record = new PresensiRecord
    {
        SiswaProfileId = profile.Id,
        MitraIndustriId = company.Id,
        Type = AttendanceType.Masuk,
        Timestamp = checkInTimestamp,
        Latitude = req.Latitude,
        Longitude = req.Longitude,
        LocationAddress = req.LocationAddress ?? "Gedung Kantor Mitra Industri",
        DistanceMeters = distanceMeters,
        LateMinutes = lateMinutes,
        IsLate = lateMinutes > 0,
        IsWithinRadius = true,
        Notes = req.Notes ?? "Presensi masuk via Mobile App.",
        ApprovalStatus = AttendanceApprovalStatus.Disetujui,
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

    if (req.Type != AttendanceType.Pulang)
        return Results.BadRequest(ApiResponse<AttendanceRecord>.Fail("Gunakan kategori Pulang untuk check-out."));
    var today = DateTime.Today;
    var hasAbsenceToday = await db.Presensi.AnyAsync(record =>
        record.SiswaProfileId == profile.Id && record.Timestamp >= today && record.Timestamp < today.AddDays(1) &&
        (record.Type == AttendanceType.Izin || record.Type == AttendanceType.Sakit || record.Type == AttendanceType.Alpa));
    if (hasAbsenceToday)
        return Results.BadRequest(ApiResponse<AttendanceRecord>.Fail("Check-out tidak tersedia karena status hari ini adalah ketidakhadiran."));
    var hasCheckedInToday = await db.Presensi.AnyAsync(record =>
        record.SiswaProfileId == profile.Id && record.Timestamp >= today && record.Timestamp < today.AddDays(1) && record.Type == AttendanceType.Masuk);
    if (!hasCheckedInToday)
        return Results.BadRequest(ApiResponse<AttendanceRecord>.Fail("Presensi masuk hari ini belum tercatat."));
    var hasCheckedOutToday = await db.Presensi.AnyAsync(record =>
        record.SiswaProfileId == profile.Id && record.Timestamp >= today && record.Timestamp < today.AddDays(1) && record.Type == AttendanceType.Pulang);
    if (hasCheckedOutToday)
        return Results.Conflict(ApiResponse<AttendanceRecord>.Fail("Presensi pulang hari ini sudah tercatat."));

    var company = profile.MitraIndustri ?? await db.MitraIndustri
        .FirstOrDefaultAsync(item => item.Id == profile.MitraIndustriId);
    if (company == null)
        return Results.BadRequest(ApiResponse<AttendanceRecord>.Fail("Mitra industri siswa belum memiliki lokasi presensi."));
    if (req.Latitude is < -90 or > 90 || req.Longitude is < -180 or > 180)
        return Results.BadRequest(ApiResponse<AttendanceRecord>.Fail("Koordinat GPS tidak valid."));
    var distanceMeters = CalculateDistanceMeters(req.Latitude, req.Longitude, company.Latitude, company.Longitude);
    if (distanceMeters > company.RadiusMeters)
        return Results.BadRequest(ApiResponse<AttendanceRecord>.Fail(
            $"Check-out ditolak. Jarak Anda {distanceMeters:F1} m, melebihi radius {company.RadiusMeters:F0} m dari lokasi kantor."));

    var record = new PresensiRecord
    {
        SiswaProfileId = profile.Id,
        MitraIndustriId = company.Id,
        Type = AttendanceType.Pulang,
        Timestamp = DateTime.Now,
        Latitude = req.Latitude,
        Longitude = req.Longitude,
        LocationAddress = req.LocationAddress ?? "Gedung Kantor Mitra Industri",
        DistanceMeters = distanceMeters,
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
    int izinCount = records.Count(r => r.Type == AttendanceType.Izin);
    int sakitCount = records.Count(r => r.Type == AttendanceType.Sakit);
    int alpaCount = records.Count(r => r.Type == AttendanceType.Alpa);
    double attendanceRate = totalDays > 0 ? (hadirCount * 100.0 / totalDays) : 0.0;
    if (attendanceRate > 100.0) attendanceRate = 100.0;

    var stats = new UserStatsResponse
    {
        TotalDays = totalDays,
        HadirCount = hadirCount,
        IzinCount = izinCount,
        SakitCount = sakitCount,
        AlpaCount = alpaCount,
        AttendanceRate = Math.Round(attendanceRate, 1),
        TotalLogbooks = logs.Count
    };

    return Results.Ok(ApiResponse<UserStatsResponse>.Ok(stats, "Statistik kehadiran siswa dihitung dari database 3NF."));
});

app.MapGet("/api/admin/company-settings", async (HttpContext context, AppDbContext db) =>
{
    if (await ResolveAdminAsync(context, db) == null) return Results.Unauthorized();

    var companies = await db.MitraIndustri
        .OrderBy(company => company.NamaPerusahaan)
        .Select(company => new
        {
            company.Id,
            company.NamaPerusahaan,
            company.BidangUsaha,
            company.Alamat,
            company.KontakPerson,
            company.NoTelepon,
            company.JamMasuk,
            company.JamPulang,
            company.Latitude,
            company.Longitude,
            company.RadiusMeters
        })
        .ToListAsync();

    return Results.Ok(ApiResponse<object>.Ok(companies, "Pengaturan mitra berhasil dimuat."));
});

app.MapPut("/api/admin/company-settings/{id}", async (
    HttpContext context, AppDbContext db, string id, [FromBody] CompanySettingsRequest request) =>
{
    if (await ResolveAdminAsync(context, db) == null) return Results.Unauthorized();

    if (string.IsNullOrWhiteSpace(request.NamaPerusahaan) || string.IsNullOrWhiteSpace(request.Alamat))
        return Results.BadRequest(ApiResponse<object>.Fail("Nama perusahaan dan alamat wajib diisi."));
    if (request.Latitude is < -90 or > 90 || request.Longitude is < -180 or > 180)
        return Results.BadRequest(ApiResponse<object>.Fail("Koordinat latitude atau longitude tidak valid."));
    if (request.RadiusMeters is < 1 or > 5000)
        return Results.BadRequest(ApiResponse<object>.Fail("Radius geofence harus antara 1 dan 5000 meter."));
    if (!TimeOnly.TryParse(request.JamMasuk, out _) || !TimeOnly.TryParse(request.JamPulang, out _))
        return Results.BadRequest(ApiResponse<object>.Fail("Format jam kerja harus HH:mm."));

    var company = await db.MitraIndustri.FirstOrDefaultAsync(item => item.Id == id);
    if (company == null) return Results.NotFound(ApiResponse<object>.Fail("Data perusahaan tidak ditemukan."));

    company.NamaPerusahaan = request.NamaPerusahaan.Trim();
    company.BidangUsaha = request.BidangUsaha.Trim();
    company.Alamat = request.Alamat.Trim();
    company.KontakPerson = request.KontakPerson.Trim();
    company.NoTelepon = request.NoTelepon.Trim();
    company.JamMasuk = request.JamMasuk.Trim();
    company.JamPulang = request.JamPulang.Trim();
    company.Latitude = request.Latitude;
    company.Longitude = request.Longitude;
    company.RadiusMeters = request.RadiusMeters;

    await db.SaveChangesAsync();
    return Results.Ok(ApiResponse<object>.Ok(new
    {
        company.Id,
        company.NamaPerusahaan,
        company.BidangUsaha,
        company.Alamat,
        company.KontakPerson,
        company.NoTelepon,
        company.JamMasuk,
        company.JamPulang,
        company.Latitude,
        company.Longitude,
        company.RadiusMeters
    }, "Data perusahaan dan lokasi presensi berhasil disimpan."));
});

// GET /api/company/geofence?identifier=20241001
app.MapGet("/api/company/geofence", async (AppDbContext db, string? identifier) =>
{
    MitraIndustri? company;
    if (!string.IsNullOrWhiteSpace(identifier))
    {
        var studentProfile = await db.SiswaProfiles.FirstOrDefaultAsync(profile =>
            profile.UserId == identifier || profile.Nis == identifier || profile.Nisn == identifier);
        if (studentProfile == null)
            return Results.NotFound(ApiResponse<object>.Fail("Profil siswa tidak ditemukan untuk menentukan lokasi perusahaan."));
        company = await db.MitraIndustri.FirstOrDefaultAsync(item => item.Id == studentProfile.MitraIndustriId);
    }
    else
    {
        company = await db.MitraIndustri.FirstOrDefaultAsync();
    }

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
