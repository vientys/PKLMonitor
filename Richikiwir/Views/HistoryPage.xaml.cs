using System;
using System.Linq;
using Microsoft.Maui.Controls;
using Richikiwir.Models;
using Richikiwir.Services;

namespace Richikiwir.Views
{
    public partial class HistoryPage : ContentPage
    {
        private string _activeFilter = "All";

        public HistoryPage()
        {
            InitializeComponent();
            DataService.Current.DataChanged += RenderHistoryCards;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            UpdateTabUi("All");
            RenderHistoryCards();

            // Sinkronisasi data terkini dari SQLite database via Web API
            var user = AuthService.Current.CurrentUser;
            if (user != null && !string.IsNullOrWhiteSpace(user.Identifier))
            {
                _ = DataService.Current.SyncWithApiAsync(user.Identifier);
            }
        }

        private void OnFilterTabClicked(object? sender, EventArgs e)
        {
            if (sender is Button btn && btn.CommandParameter is string filter)
            {
                UpdateTabUi(filter);
                RenderHistoryCards();
            }
        }

        private void UpdateTabUi(string activeFilter)
        {
            _activeFilter = activeFilter;

            ResetTabStyle(BtnTabAll);
            ResetTabStyle(BtnTabMasuk);
            ResetTabStyle(BtnTabPulang);
            ResetTabStyle(BtnTabIzin);
            ResetTabStyle(BtnTabJurnal);

            switch (activeFilter)
            {
                case "All":
                    SetSelectedTabStyle(BtnTabAll, "#1E3A8A");
                    break;
                case "Masuk":
                    SetSelectedTabStyle(BtnTabMasuk, "#2563EB");
                    break;
                case "Pulang":
                    SetSelectedTabStyle(BtnTabPulang, "#059669");
                    break;
                case "Izin":
                    SetSelectedTabStyle(BtnTabIzin, "#D97706");
                    break;
                case "Jurnal":
                    SetSelectedTabStyle(BtnTabJurnal, "#7C3AED");
                    break;
            }
        }

        private void ResetTabStyle(Button btn)
        {
            btn.BackgroundColor = Color.FromArgb("#F1F5F9");
            btn.TextColor = Color.FromArgb("#475569");
        }

        private void SetSelectedTabStyle(Button btn, string hex)
        {
            btn.BackgroundColor = Color.FromArgb(hex);
            btn.TextColor = Colors.White;
        }

        private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
        {
            RenderHistoryCards();
        }

        private void RenderHistoryCards()
        {
            var user = AuthService.Current.CurrentUser;
            var searchText = TxtSearch?.Text?.Trim().ToLowerInvariant() ?? string.Empty;

            // Dapatkan seluruh riwayat pengguna aktif untuk perhitungan ringkasan
            var userAllRecords = DataService.Current.Attendances.AsEnumerable();
            if (user != null && user.Role == UserRole.Siswa)
            {
                userAllRecords = userAllRecords.Where(x =>
                    x.StudentId == user.Id ||
                    string.Equals(x.StudentNisn.Replace(" ", ""), user.Identifier.Replace(" ", ""), StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(x.StudentName, user.FullName, StringComparison.OrdinalIgnoreCase));
            }

            var allList = userAllRecords.ToList();

            // Hitung statistik secara dinamis berdasarkan data aktual akun
            int totalDays = allList.Select(x => x.Timestamp.Date).Distinct().Count();
            int hadirCount = allList.Count(x => x.Type == AttendanceType.Masuk);
            int izinCount = allList.Count(x => x.Type == AttendanceType.Izin || x.Type == AttendanceType.Sakit);
            double rate = totalDays > 0 ? (hadirCount * 100.0 / totalDays) : 0.0;
            if (rate > 100.0) rate = 100.0;

            LblTotalDays.Text = totalDays.ToString();
            LblTotalHadir.Text = hadirCount.ToString();
            LblTotalIzin.Text = izinCount.ToString();
            LblAttendanceRate.Text = totalDays == 0 ? "0%" : $"{rate:F1}%";

            if (_activeFilter == "Jurnal")
            {
                LayoutAttendanceList.IsVisible = false;
                LayoutLogbookList.IsVisible = true;
                RenderLogbooks(searchText);
                return;
            }

            LayoutAttendanceList.IsVisible = true;
            LayoutLogbookList.IsVisible = false;

            var items = allList.AsEnumerable();

            if (_activeFilter == "Masuk")
                items = items.Where(x => x.Type == AttendanceType.Masuk);
            else if (_activeFilter == "Pulang")
                items = items.Where(x => x.Type == AttendanceType.Pulang);
            else if (_activeFilter == "Izin")
                items = items.Where(x => x.Type == AttendanceType.Izin || x.Type == AttendanceType.Sakit);

            if (!string.IsNullOrEmpty(searchText))
            {
                items = items.Where(x => 
                    x.FormattedDate.ToLowerInvariant().Contains(searchText) ||
                    x.LocationAddress.ToLowerInvariant().Contains(searchText) ||
                    x.Notes.ToLowerInvariant().Contains(searchText));
            }

            var list = items.ToList();
            LayoutAttendanceList.Children.Clear();

            if (list.Count == 0)
            {
                var emptyBorder = new Border
                {
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 },
                    BackgroundColor = Colors.White,
                    Padding = 24,
                    Content = new VerticalStackLayout
                    {
                        Spacing = 6,
                        HorizontalOptions = LayoutOptions.Center,
                        Children =
                        {
                            new Label { Text = "📋", FontSize = 36, HorizontalOptions = LayoutOptions.Center },
                            new Label { Text = "Belum Ada Riwayat Presensi", FontSize = 13, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#475569"), HorizontalOptions = LayoutOptions.Center },
                            new Label { Text = "Riwayat presensi realtime dengan GPS dan foto akan muncul di sini.", FontSize = 11, TextColor = Color.FromArgb("#94A3B8"), HorizontalOptions = LayoutOptions.Center }
                        }
                    }
                };
                LayoutAttendanceList.Children.Add(emptyBorder);
                return;
            }

            foreach (var record in list)
            {
                var card = CreateAttendanceCard(record);
                LayoutAttendanceList.Children.Add(card);
            }
        }

        private Border CreateAttendanceCard(AttendanceRecord record)
        {
            var border = new Border
            {
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
                Stroke = Color.FromArgb("#E2E8F0"),
                StrokeThickness = 1,
                BackgroundColor = Colors.White,
                Padding = 14,
                Shadow = new Shadow
                {
                    Brush = Brush.LightGray,
                    Offset = new Point(0, 3),
                    Radius = 6,
                    Opacity = 0.1f
                }
            };

            var stack = new VerticalStackLayout { Spacing = 8 };

            // Row 1: Header (Type Badge + Date & Time)
            var topGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Star }
                }
            };

            var typeBadge = new Border
            {
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
                BackgroundColor = Color.FromArgb(record.TypeBadgeColor),
                Padding = new Thickness(8, 3),
                Content = new Label
                {
                    Text = record.Type.ToString(),
                    FontSize = 10,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.White
                }
            };
            topGrid.Children.Add(typeBadge);

            var timeLabel = new Label
            {
                Text = $"{record.FormattedDate} • {record.FormattedTime} WIB",
                FontSize = 11,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#1E293B"),
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Center
            };
            Grid.SetColumn(timeLabel, 1);
            topGrid.Children.Add(timeLabel);

            stack.Children.Add(topGrid);

            // Row 2: Location & GPS Geofence details
            var locGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = 24 },
                    new ColumnDefinition { Width = GridLength.Star }
                }
            };

            locGrid.Children.Add(new Label { Text = "📍", FontSize = 14, VerticalOptions = LayoutOptions.Start });

            var locStack = new VerticalStackLayout
            {
                Spacing = 2,
                Children =
                {
                    new Label { Text = record.LocationAddress, FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#334155") },
                    new Label { Text = $"Koordinat: {record.Latitude:F5}, {record.Longitude:F5} • Radius: {record.DistanceMeters:F1}m (Batas: 100m)", FontSize = 10, TextColor = Color.FromArgb("#64748B") }
                }
            };
            Grid.SetColumn(locStack, 1);
            locGrid.Children.Add(locStack);

            stack.Children.Add(locGrid);

            // Row 3: Notes if any
            if (!string.IsNullOrWhiteSpace(record.Notes))
            {
                var noteBox = new Border
                {
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
                    BackgroundColor = Color.FromArgb("#F8FAFC"),
                    Padding = 8,
                    Content = new Label
                    {
                        Text = $"“{record.Notes}”",
                        FontSize = 11,
                        TextColor = Color.FromArgb("#475569")
                    }
                };
                stack.Children.Add(noteBox);
            }

            // Row 4: Footer (Photo Geotag verification + Mentor Approval)
            var bottomGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto }
                }
            };

            var photoVerifyLabel = new HorizontalStackLayout
            {
                Spacing = 4,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label { Text = "📸", FontSize = 11, VerticalOptions = LayoutOptions.Center },
                    new Label { Text = "Geotag Photo Verified", FontSize = 10, TextColor = Color.FromArgb("#059669"), FontAttributes = FontAttributes.Bold, VerticalOptions = LayoutOptions.Center }
                }
            };
            bottomGrid.Children.Add(photoVerifyLabel);

            var approvalBadge = new Border
            {
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
                BackgroundColor = Color.FromArgb(record.ApprovalStatus == AttendanceApprovalStatus.Disetujui ? "#DCFCE7" : "#FEF3C7"),
                Padding = new Thickness(8, 2),
                Content = new Label
                {
                    Text = record.ApprovalStatusText,
                    FontSize = 10,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb(record.ApprovalStatus == AttendanceApprovalStatus.Disetujui ? "#15803D" : "#92400E")
                }
            };
            Grid.SetColumn(approvalBadge, 1);
            bottomGrid.Children.Add(approvalBadge);

            stack.Children.Add(bottomGrid);

            border.Content = stack;

            // Tap gesture to view full geotag details
            var tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += async (s, e) =>
            {
                await DisplayAlertAsync("Detail Presensi PKL", 
                    $"Nama: {record.StudentName}\n" +
                    $"Tipe: {record.Type}\n" +
                    $"Waktu: {record.FormattedDate} • {record.FormattedTime} WIB\n" +
                    $"Lokasi: {record.LocationAddress}\n" +
                    $"Koordinat: {record.Latitude:F5}, {record.Longitude:F5}\n" +
                    $"Radius: {record.DistanceMeters:F1} meter\n" +
                    $"Verifikasi: {record.ApprovalStatusText} ({record.VerifiedBy})\n" +
                    $"Catatan: {record.Notes}", 
                    "Tutup");
            };
            border.GestureRecognizers.Add(tapGesture);

            return border;
        }

        private void RenderLogbooks(string searchText)
        {
            LayoutLogbookList.Children.Clear();
            var user = AuthService.Current.CurrentUser;
            var logs = DataService.Current.Logbooks.AsEnumerable();

            if (user != null && user.Role == UserRole.Siswa)
            {
                logs = logs.Where(x =>
                    string.Equals(x.StudentNisn.Replace(" ", ""), user.Identifier.Replace(" ", ""), StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(x.StudentName, user.FullName, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrEmpty(searchText))
            {
                logs = logs.Where(x => 
                    x.ActivityTitle.ToLowerInvariant().Contains(searchText) ||
                    x.Description.ToLowerInvariant().Contains(searchText));
            }

            var logList = logs.ToList();

            if (logList.Count == 0)
            {
                var emptyBorder = new Border
                {
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 },
                    BackgroundColor = Colors.White,
                    Padding = 24,
                    Content = new VerticalStackLayout
                    {
                        Spacing = 6,
                        HorizontalOptions = LayoutOptions.Center,
                        Children =
                        {
                            new Label { Text = "📖", FontSize = 36, HorizontalOptions = LayoutOptions.Center },
                            new Label { Text = "Belum Ada Jurnal Kegiatan", FontSize = 13, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#475569"), HorizontalOptions = LayoutOptions.Center },
                            new Label { Text = "Jurnal harian PKL yang Anda kirim akan tersimpan dan tampil di sini.", FontSize = 11, TextColor = Color.FromArgb("#94A3B8"), HorizontalOptions = LayoutOptions.Center }
                        }
                    }
                };
                LayoutLogbookList.Children.Add(emptyBorder);
                return;
            }

            foreach (var log in logList)
            {
                var card = new Border
                {
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
                    Stroke = Color.FromArgb("#E2E8F0"),
                    BackgroundColor = Colors.White,
                    Padding = 14,
                    Content = new VerticalStackLayout
                    {
                        Spacing = 6,
                        Children =
                        {
                            new Grid
                            {
                                ColumnDefinitions = new ColumnDefinitionCollection
                                {
                                    new ColumnDefinition { Width = GridLength.Star },
                                    new ColumnDefinition { Width = GridLength.Auto }
                                },
                                Children =
                                {
                                    new Label { Text = log.ActivityTitle, FontSize = 13, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#0F172A") },
                                    new Label { Text = log.FormattedDate, FontSize = 11, TextColor = Color.FromArgb("#64748B"), HorizontalOptions = LayoutOptions.End }
                                }
                            },
                            new Label { Text = log.Description, FontSize = 11, TextColor = Color.FromArgb("#475569") },
                            new Border
                            {
                                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
                                BackgroundColor = Color.FromArgb("#F1F5F9"),
                                Padding = 8,
                                Content = new Label { Text = $"Catatan Mentor: {log.MentorNotes} • Skor: {log.Score}/100", FontSize = 10, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#1E3A8A") }
                            }
                        }
                    }
                };
                LayoutLogbookList.Children.Add(card);
            }
        }
    }
}
