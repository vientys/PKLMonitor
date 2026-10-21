using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Richikiwir.Api.Models;

namespace Richikiwir.Api.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        // ==========================================
        // 3NF DATABASE TABLES
        // ==========================================
        public DbSet<Sekolah> Sekolah => Set<Sekolah>();
        public DbSet<Jurusan> Jurusan => Set<Jurusan>();
        public DbSet<Kelas> Kelas => Set<Kelas>();
        public DbSet<MitraIndustri> MitraIndustri => Set<MitraIndustri>();
        public DbSet<UserAccount> Users => Set<UserAccount>();
        public DbSet<SiswaProfile> SiswaProfiles => Set<SiswaProfile>();
        public DbSet<GuruProfile> GuruProfiles => Set<GuruProfile>();
        public DbSet<MentorIndustriProfile> MentorProfiles => Set<MentorIndustriProfile>();
        public DbSet<PresensiRecord> Presensi => Set<PresensiRecord>();
        public DbSet<JurnalKegiatan> JurnalLogbook => Set<JurnalKegiatan>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // 1. Sekolah
            modelBuilder.Entity<Sekolah>(entity =>
            {
                entity.ToTable("sekolah");
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Npsn).IsUnique();
                entity.Property(e => e.NamaSekolah).IsRequired().HasMaxLength(150);
            });

            // 2. Jurusan
            modelBuilder.Entity<Jurusan>(entity =>
            {
                entity.ToTable("jurusan");
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.KodeJurusan).IsUnique();
                entity.HasOne(e => e.Sekolah)
                      .WithMany(s => s.DaftarJurusan)
                      .HasForeignKey(e => e.SekolahId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // 3. Kelas
            modelBuilder.Entity<Kelas>(entity =>
            {
                entity.ToTable("kelas");
                entity.HasKey(e => e.Id);
                entity.HasOne(e => e.Jurusan)
                      .WithMany(j => j.DaftarKelas)
                      .HasForeignKey(e => e.JurusanId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // 4. Mitra Industri
            modelBuilder.Entity<MitraIndustri>(entity =>
            {
                entity.ToTable("mitra_industri");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.NamaPerusahaan).IsRequired().HasMaxLength(150);
            });

            // 5. User Account (Core Auth)
            modelBuilder.Entity<UserAccount>(entity =>
            {
                entity.ToTable("users");
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Username).IsUnique();
                entity.Property(e => e.Username).IsRequired().HasMaxLength(50);
                entity.Property(e => e.FullName).IsRequired().HasMaxLength(150);
            });

            // 6. Siswa Profile (Relasi 1:1 dengan UserAccount)
            modelBuilder.Entity<SiswaProfile>(entity =>
            {
                entity.ToTable("siswa_profiles");
                entity.HasKey(e => e.Id);
                
                entity.HasOne(e => e.User)
                      .WithOne(u => u.SiswaProfile)
                      .HasForeignKey<SiswaProfile>(e => e.UserId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(e => e.Nis).IsUnique();
                entity.HasIndex(e => e.Nisn).IsUnique();

                entity.HasOne(e => e.Kelas)
                      .WithMany(k => k.DaftarSiswa)
                      .HasForeignKey(e => e.KelasId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.MitraIndustri)
                      .WithMany(m => m.DaftarSiswa)
                      .HasForeignKey(e => e.MitraIndustriId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.GuruPembimbing)
                      .WithMany()
                      .HasForeignKey(e => e.GuruPembimbingId)
                      .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(e => e.MentorIndustri)
                      .WithMany()
                      .HasForeignKey(e => e.MentorIndustriId)
                      .OnDelete(DeleteBehavior.SetNull);
            });

            // 7. Guru Profile (Relasi 1:1 dengan UserAccount)
            modelBuilder.Entity<GuruProfile>(entity =>
            {
                entity.ToTable("guru_profiles");
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.User)
                      .WithOne(u => u.GuruProfile)
                      .HasForeignKey<GuruProfile>(e => e.UserId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(e => e.Nip).IsUnique();

                entity.HasOne(e => e.Sekolah)
                      .WithMany(s => s.DaftarGuru)
                      .HasForeignKey(e => e.SekolahId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // 8. Mentor Industri Profile (Relasi 1:1 dengan UserAccount)
            modelBuilder.Entity<MentorIndustriProfile>(entity =>
            {
                entity.ToTable("mentor_profiles");
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.User)
                      .WithOne(u => u.MentorIndustriProfile)
                      .HasForeignKey<MentorIndustriProfile>(e => e.UserId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(e => e.NikKaryawan).IsUnique();

                entity.HasOne(e => e.MitraIndustri)
                      .WithMany(m => m.DaftarMentor)
                      .HasForeignKey(e => e.MitraIndustriId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // 9. Presensi (Transaksi 3NF)
            modelBuilder.Entity<PresensiRecord>(entity =>
            {
                entity.ToTable("presensi");
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.SiswaProfile)
                      .WithMany(s => s.DaftarPresensi)
                      .HasForeignKey(e => e.SiswaProfileId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.MitraIndustri)
                      .WithMany()
                      .HasForeignKey(e => e.MitraIndustriId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.VerifiedByUser)
                      .WithMany()
                      .HasForeignKey(e => e.VerifiedByUserId)
                      .OnDelete(DeleteBehavior.SetNull);

                entity.HasIndex(e => e.Timestamp);
            });

            // 10. Jurnal Logbook (Transaksi 3NF)
            modelBuilder.Entity<JurnalKegiatan>(entity =>
            {
                entity.ToTable("jurnal_logbook");
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.SiswaProfile)
                      .WithMany(s => s.DaftarJurnal)
                      .HasForeignKey(e => e.SiswaProfileId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.ApprovedByUser)
                      .WithMany()
                      .HasForeignKey(e => e.ApprovedByUserId)
                      .OnDelete(DeleteBehavior.SetNull);

                entity.HasIndex(e => e.Date);
            });
        }

        /// <summary>
        /// Seeding Master Data 10 Jurusan Lengkap, Kelas, dan Akun Siswa per Jurusan
        /// Serta memastikan tabel presensi dan logbook KOSONG saat awal
        /// </summary>
        public static void SeedData(AppDbContext db)
        {
            // Pastikan absensi dan logbook selalu dikosongkan jika ada data uji coba lama
            if (db.Presensi.Any())
            {
                db.Presensi.RemoveRange(db.Presensi);
                db.SaveChanges();
            }

            if (db.JurnalLogbook.Any())
            {
                db.JurnalLogbook.RemoveRange(db.JurnalLogbook);
                db.SaveChanges();
            }

            // Jika sekolah & jurusan sudah pernah di-seed, keluar
            if (db.Sekolah.Any() && db.Jurusan.Count() >= 10) return;

            // Jika belum ada sekolah
            var sekolah = db.Sekolah.FirstOrDefault();
            if (sekolah == null)
            {
                sekolah = new Sekolah
                {
                    Id = "sch-smkn5-tgr",
                    NamaSekolah = "SMK Negeri 5 Kabupaten Tangerang",
                    Npsn = "20614748",
                    Alamat = "Jl. Raya Salembaran No. 01, Kosambi, Kab. Tangerang, Banten",
                    NoTelepon = "(021) 5593-1234",
                    Email = "info@smkn5kabtangerang.sch.id"
                };
                db.Sekolah.Add(sekolah);
                db.SaveChanges();
            }

            // 10 JURUSAN RESMI SMKN 5 KAB. TANGERANG:
            // RPL, DKV, PH, TKJ, TSM, TKR, TPM, MP, AK, APHPI
            var listJurusan = new (string Kode, string Nama)[]
            {
                ("RPL", "Rekayasa Perangkat Lunak"),
                ("DKV", "Desain Komunikasi Visual"),
                ("PH", "Perhotelan"),
                ("TKJ", "Teknik Komputer dan Jaringan"),
                ("TSM", "Teknik Sepeda Motor"),
                ("TKR", "Teknik Kendaraan Ringan"),
                ("TPM", "Teknik Pemesinan"),
                ("MP", "Manajemen Perkantoran"),
                ("AK", "Akuntansi"),
                ("APHPI", "Agribisnis Pengolahan Hasil Perikanan")
            };

            var jurusanEntities = new Dictionary<string, Jurusan>();

            foreach (var (kode, nama) in listJurusan)
            {
                var jur = db.Jurusan.FirstOrDefault(j => j.KodeJurusan == kode);
                if (jur == null)
                {
                    jur = new Jurusan
                    {
                        Id = $"jur-{kode.ToLowerInvariant()}",
                        SekolahId = sekolah.Id,
                        KodeJurusan = kode,
                        NamaJurusan = nama
                    };
                    db.Jurusan.Add(jur);
                    db.SaveChanges();
                }
                jurusanEntities[kode] = jur;

                // Tambahkan Rombel / Kelas (misal Kelas 1 dan Kelas 2)
                var kls1Name = $"XII {kode} 1";
                if (!db.Kelas.Any(k => k.NamaKelas == kls1Name))
                {
                    db.Kelas.Add(new Kelas
                    {
                        Id = $"kls-{kode.ToLowerInvariant()}-1",
                        JurusanId = jur.Id,
                        NamaKelas = kls1Name,
                        Tingkat = 12
                    });
                }

                var kls2Name = $"XII {kode} 2";
                if (!db.Kelas.Any(k => k.NamaKelas == kls2Name))
                {
                    db.Kelas.Add(new Kelas
                    {
                        Id = $"kls-{kode.ToLowerInvariant()}-2",
                        JurusanId = jur.Id,
                        NamaKelas = kls2Name,
                        Tingkat = 12
                    });
                }
            }
            db.SaveChanges();

            // Mitra Industri Master
            var dudiSolusi = db.MitraIndustri.FirstOrDefault(m => m.Id == "dudi-sdn");
            if (dudiSolusi == null)
            {
                dudiSolusi = new MitraIndustri
                {
                    Id = "dudi-sdn",
                    NamaPerusahaan = "PT Solusi Digital Nusantara",
                    BidangUsaha = "Teknologi Informasi & Rekayasa Perangkat Lunak",
                    Alamat = "Gedung Cyber 2 Tower, Jl. H. R. Rasuna Said Blok X-5 No. 13, Kuningan Timur, Setiabudi, Jakarta Selatan",
                    Latitude = -6.22412,
                    Longitude = 106.83154,
                    RadiusMeters = 100.0,
                    JamMasuk = "08:00",
                    JamPulang = "17:00",
                    KontakPerson = "Divisi Kerjasama Industri & HRD",
                    NoTelepon = "021-5299-8800"
                };
                db.MitraIndustri.Add(dudiSolusi);
            }

            var dudiTelkom = db.MitraIndustri.FirstOrDefault(m => m.Id == "dudi-telkom");
            if (dudiTelkom == null)
            {
                dudiTelkom = new MitraIndustri
                {
                    Id = "dudi-telkom",
                    NamaPerusahaan = "PT Telekomunikasi Indonesia (Telkom) Tangerang",
                    BidangUsaha = "Jaringan & Infrastruktur Telekomunikasi",
                    Alamat = "BSD Green Office Park, Kec. Cisauk, Kabupaten Tangerang",
                    Latitude = -6.30210,
                    Longitude = 106.65210,
                    RadiusMeters = 100.0,
                    JamMasuk = "08:00",
                    JamPulang = "17:00",
                    KontakPerson = "Area Network Operations",
                    NoTelepon = "021-537-1234"
                };
                db.MitraIndustri.Add(dudiTelkom);
            }
            db.SaveChanges();

            // Akun Guru Pembimbing Sekolah
            var guruUser = db.Users.FirstOrDefault(u => u.Id == "usr-guru-01");
            if (guruUser == null)
            {
                guruUser = new UserAccount
                {
                    Id = "usr-guru-01",
                    Username = "198503152010011002", // NIP guru
                    FullName = "Drs. Budi Santoso, M.Kom.",
                    Email = "budi.santoso@smkn5kabtangerang.sch.id",
                    PhoneNumber = "0812-3456-7890",
                    PasswordHash = "password123",
                    Role = UserRole.PembimbingSekolah,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                db.Users.Add(guruUser);

                var guruProfile = new GuruProfile
                {
                    Id = "prof-guru-01",
                    UserId = guruUser.Id,
                    Nip = "198503152010011002",
                    SekolahId = sekolah.Id,
                    Jabatan = "Koordinator Hubin & PKL Seluruh Jurusan"
                };
                db.GuruProfiles.Add(guruProfile);
            }

            // Akun Mentor Industri (DU/DI)
            var mentorUser = db.Users.FirstOrDefault(u => u.Id == "usr-mentor-01");
            if (mentorUser == null)
            {
                mentorUser = new UserAccount
                {
                    Id = "usr-mentor-01",
                    Username = "SDN-2024-089", // NIK mentor
                    FullName = "Hendrawan Wicaksono, S.T.",
                    Email = "hendrawan@solusidigital.id",
                    PhoneNumber = "0821-8899-0011",
                    PasswordHash = "password123",
                    Role = UserRole.PembimbingIndustri,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                db.Users.Add(mentorUser);

                var mentorProfile = new MentorIndustriProfile
                {
                    Id = "prof-mentor-01",
                    UserId = mentorUser.Id,
                    NikKaryawan = "SDN-2024-089",
                    MitraIndustriId = dudiSolusi.Id,
                    Divisi = "Software Engineering & Mobile Development"
                };
                db.MentorProfiles.Add(mentorProfile);
            }

            // Akun Administrator PKL
            var adminUser = db.Users.FirstOrDefault(u => u.Id == "usr-admin-01");
            if (adminUser == null)
            {
                adminUser = new UserAccount
                {
                    Id = "usr-admin-01",
                    Username = "admin_pkl",
                    FullName = "Administrator BKK PKL SMKN 5",
                    Email = "bkk.smkn5@kabtangerang.sch.id",
                    PhoneNumber = "0812-5555-6666",
                    PasswordHash = "password123",
                    Role = UserRole.Admin,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                db.Users.Add(adminUser);
            }
            db.SaveChanges();

            // DAFTAR AKUN SISWA RESMI TIAP DIREKTORI JURUSAN
            var sampleStudents = new (string Nis, string Nisn, string Nama, string KodeJurusan, string Email, string NoTelp)[]
            {
                ("20241001", "0067821944", "Ahmad Rizki Pratama", "RPL", "ahmad.rizki@smkn5kabtangerang.sch.id", "0812-9876-5432"),
                ("20241002", "0067821945", "Siti Nurhaliza", "RPL", "siti.nurhaliza@smkn5kabtangerang.sch.id", "0813-1122-3344"),
                ("20242001", "0067821946", "Dimas Aditya Pratama", "DKV", "dimas.aditya@smkn5kabtangerang.sch.id", "0814-2233-4455"),
                ("20243001", "0067821947", "Putri Ayu Lestari", "PH", "putri.ayu@smkn5kabtangerang.sch.id", "0815-3344-5566"),
                ("20244001", "0067821948", "Fajar Maulana Syahputra", "TKJ", "fajar.maulana@smkn5kabtangerang.sch.id", "0816-4455-6677"),
                ("20245001", "0067821949", "Bagus Kurniawan", "TSM", "bagus.kurniawan@smkn5kabtangerang.sch.id", "0817-5566-7788"),
                ("20246001", "0067821950", "Bayu Prasetyo", "TKR", "bayu.prasetyo@smkn5kabtangerang.sch.id", "0818-6677-8899"),
                ("20247001", "0067821951", "Eko Wahyu Saputra", "TPM", "eko.saputra@smkn5kabtangerang.sch.id", "0819-7788-9900"),
                ("20248001", "0067821952", "Dewi Sartika", "MP", "dewi.sartika@smkn5kabtangerang.sch.id", "0821-8899-0011"),
                ("20249001", "0067821953", "Anisa Rahmawati", "AK", "anisa.rahma@smkn5kabtangerang.sch.id", "0822-9900-1122"),
                ("20240001", "0067821954", "Fikri Firmansyah", "APHPI", "fikri.firmansyah@smkn5kabtangerang.sch.id", "0823-0011-2233")
            };

            foreach (var s in sampleStudents)
            {
                var existingUser = db.Users.FirstOrDefault(u => u.Username == s.Nis);
                if (existingUser == null)
                {
                    var user = new UserAccount
                    {
                        Id = $"usr-{s.KodeJurusan.ToLowerInvariant()}-{s.Nis}",
                        Username = s.Nis, // NIS sebagai username login resmi
                        FullName = s.Nama,
                        Email = s.Email,
                        PhoneNumber = s.NoTelp,
                        PasswordHash = "password123",
                        Role = UserRole.Siswa,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    };
                    db.Users.Add(user);

                    var kls = db.Kelas.FirstOrDefault(k => k.NamaKelas == $"XII {s.KodeJurusan} 1");
                    var profile = new SiswaProfile
                    {
                        Id = $"prof-{s.KodeJurusan.ToLowerInvariant()}-{s.Nis}",
                        UserId = user.Id,
                        Nis = s.Nis,
                        Nisn = s.Nisn,
                        KelasId = kls?.Id ?? $"kls-{s.KodeJurusan.ToLowerInvariant()}-1",
                        MitraIndustriId = dudiSolusi.Id,
                        GuruPembimbingId = guruUser.Id,
                        MentorIndustriId = mentorUser.Id,
                        TahunPkl = "2026/2027",
                        StatusPkl = "Aktif"
                    };
                    db.SiswaProfiles.Add(profile);
                }
            }

            db.SaveChanges();
        }

        /// <summary>
        /// Sinkronisasi Fisik Direktori Penyimpanan File dan Akun per Jurusan
        /// </summary>
        public static void SyncPhysicalJurusanDirectories(string contentRootPath, AppDbContext db)
        {
            try
            {
                var baseStorageDir = Path.Combine(contentRootPath, "storage", "jurusan");
                Directory.CreateDirectory(baseStorageDir);

                var listJurusan = new[] { "RPL", "DKV", "PH", "TKJ", "TSM", "TKR", "TPM", "MP", "AK", "APHPI" };

                var students = db.SiswaProfiles
                    .Include(s => s.User)
                    .Include(s => s.Kelas).ThenInclude(k => k!.Jurusan)
                    .Include(s => s.MitraIndustri)
                    .ToList();

                foreach (var kode in listJurusan)
                {
                    var jurDir = Path.Combine(baseStorageDir, kode);
                    var accDir = Path.Combine(jurDir, "accounts");
                    var attDir = Path.Combine(jurDir, "attendance");
                    var photoDir = Path.Combine(jurDir, "photos");
                    var logDir = Path.Combine(jurDir, "logbooks");

                    Directory.CreateDirectory(accDir);
                    Directory.CreateDirectory(attDir);
                    Directory.CreateDirectory(photoDir);
                    Directory.CreateDirectory(logDir);

                    // Simpan file akun JSON per jurusan
                    var jurStudents = students
                        .Where(s => s.Kelas?.Jurusan?.KodeJurusan == kode)
                        .Select(s => new
                        {
                            s.Nis,
                            s.Nisn,
                            NamaLengkap = s.User?.FullName,
                            Email = s.User?.Email,
                            Kelas = s.Kelas?.NamaKelas,
                            Perusahaan = s.MitraIndustri?.NamaPerusahaan,
                            TahunPkl = s.TahunPkl,
                            Status = s.StatusPkl
                        })
                        .ToList();

                    var catalogPath = Path.Combine(accDir, "daftar_akun.json");
                    File.WriteAllText(catalogPath, JsonSerializer.Serialize(jurStudents, new JsonSerializerOptions { WriteIndented = true }));

                    // Simpan file profil individual tiap NIS
                    foreach (var s in jurStudents)
                    {
                        var singlePath = Path.Combine(accDir, $"{s.Nis}.json");
                        File.WriteAllText(singlePath, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Storage Warning] Error syncing physical jurusan directories: {ex.Message}");
            }
        }
    }
}
