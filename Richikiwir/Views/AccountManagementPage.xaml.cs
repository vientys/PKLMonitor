using System.Collections.ObjectModel;
using Richikiwir.Models;
using Richikiwir.Services;

namespace Richikiwir.Views;

public partial class AccountManagementPage : ContentPage
{
    private static readonly (string Code, string Name)[] JurusanOptions =
    {
        ("RPL", "RPL - Rekayasa Perangkat Lunak"),
        ("DKV", "DKV - Desain Komunikasi Visual"),
        ("PH", "PH - Perhotelan"),
        ("TKJ", "TKJ - Teknik Komputer dan Jaringan"),
        ("TSM", "TSM - Teknik Sepeda Motor"),
        ("TKR", "TKR - Teknik Kendaraan Ringan"),
        ("TPM", "TPM - Teknik Pemesinan"),
        ("MP", "MP - Manajemen Perkantoran"),
        ("AK", "AK - Akuntansi"),
        ("APHPI", "APHPI - Agribisnis Pengolahan Hasil Perikanan")
    };

    private readonly UserRole[] _roles = Enum.GetValues<UserRole>();
    private readonly ObservableCollection<UserModel> _visibleUsers = new();
    private List<UserModel> _allUsers = new();
    private UserModel? _editingUser;

    public AccountManagementPage()
    {
        InitializeComponent();
        PickerRole.ItemsSource = _roles.Select(GetRoleLabel).ToList();
        PickerJurusan.ItemsSource = JurusanOptions.Select(option => option.Name).ToList();
        AccountsList.ItemsSource = _visibleUsers;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (AuthService.Current.CurrentUser.Role != UserRole.Admin)
        {
            await DisplayAlertAsync("Akses ditolak", "Halaman ini hanya tersedia untuk admin.", "Tutup");
            await Shell.Current.GoToAsync("..");
            return;
        }

        await LoadAccountsAsync();
    }

    private async Task LoadAccountsAsync()
    {
        var result = await ApiService.Current.GetManagedUsersAsync();
        if (!result.Success)
        {
            await DisplayAlertAsync("Gagal memuat akun", result.Message, "Tutup");
            return;
        }

        _allUsers = result.Users;
        ApplySearch(SearchAccounts.Text);
    }

    private void ApplySearch(string? query)
    {
        _visibleUsers.Clear();
        var filtered = string.IsNullOrWhiteSpace(query)
            ? _allUsers
            : _allUsers.Where(user =>
                user.FullName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                user.Identifier.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var user in filtered)
            _visibleUsers.Add(user);

        LblAccountCount.Text = $"{_visibleUsers.Count} akun";
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e) => ApplySearch(e.NewTextValue);

    private void OnAddClicked(object? sender, EventArgs e)
    {
        _editingUser = null;
        LblEditorTitle.Text = "Tambah akun";
        TxtFullName.Text = string.Empty;
        TxtIdentifier.Text = string.Empty;
        TxtEmail.Text = string.Empty;
        TxtPhone.Text = string.Empty;
        TxtPassword.Text = string.Empty;
        TxtPassword.IsVisible = false;
        LblGeneratedPassword.IsVisible = true;
        TxtNisn.Text = string.Empty;
        PickerRole.IsEnabled = true;
        PickerRole.SelectedIndex = 0;
        PickerJurusan.SelectedIndex = 0;
        EditorCard.IsVisible = true;
        OnRoleChanged(PickerRole, EventArgs.Empty);
    }

    private void OnEditClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: UserModel user }) return;

        _editingUser = user;
        LblEditorTitle.Text = "Edit akun";
        TxtFullName.Text = user.FullName;
        TxtIdentifier.Text = user.Identifier;
        TxtEmail.Text = user.Email;
        TxtPhone.Text = user.PhoneNumber;
        TxtPassword.Text = string.Empty;
        TxtPassword.Placeholder = "Kosongkan jika kata sandi tidak diubah";
        TxtPassword.IsVisible = true;
        LblGeneratedPassword.IsVisible = false;
        TxtNisn.Text = user.Nisn;
        PickerRole.SelectedIndex = Array.IndexOf(_roles, user.Role);
        PickerRole.IsEnabled = false;
        var jurusanIndex = Array.FindIndex(JurusanOptions, option => option.Code == user.JurusanCode);
        PickerJurusan.SelectedIndex = Math.Max(0, jurusanIndex);
        EditorCard.IsVisible = true;
        OnRoleChanged(PickerRole, EventArgs.Empty);
    }

    private void OnRoleChanged(object? sender, EventArgs e)
    {
        if (PickerRole.SelectedIndex < 0)
        {
            LayoutStudentFields.IsVisible = false;
            LblProfileAssignment.IsVisible = false;
            return;
        }

        var role = _roles[PickerRole.SelectedIndex];
        LayoutStudentFields.IsVisible = role == UserRole.Siswa;
        LblProfileAssignment.Text = role switch
        {
            UserRole.Siswa => "Akun ditautkan ke kelas pertama pada jurusan dan mitra industri utama.",
            UserRole.PembimbingSekolah => "Akun ditautkan ke sekolah utama.",
            UserRole.PembimbingIndustri => "Akun ditautkan ke mitra industri utama.",
            _ => string.Empty
        };
        LblProfileAssignment.IsVisible = role != UserRole.Admin;
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        var fullName = TxtFullName.Text?.Trim() ?? string.Empty;
        var identifier = TxtIdentifier.Text?.Trim() ?? string.Empty;
        var password = TxtPassword.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(identifier))
        {
            await DisplayAlertAsync("Data belum lengkap", "Nama dan identitas login wajib diisi.", "Tutup");
            return;
        }
        if (_editingUser != null && !string.IsNullOrEmpty(password) && password.Length < 8)
        {
            await DisplayAlertAsync("Kata sandi tidak valid", "Kata sandi minimal 8 karakter.", "Tutup");
            return;
        }
        if (PickerRole.SelectedIndex < 0)
        {
            await DisplayAlertAsync("Peran belum dipilih", "Pilih peran untuk akun ini.", "Tutup");
            return;
        }

        var role = _roles[PickerRole.SelectedIndex];
        var nisn = TxtNisn.Text?.Trim() ?? string.Empty;
        var jurusanCode = PickerJurusan.SelectedIndex >= 0
            ? JurusanOptions[PickerJurusan.SelectedIndex].Code
            : string.Empty;
        if (role == UserRole.Siswa && _editingUser == null &&
            (string.IsNullOrWhiteSpace(nisn) || string.IsNullOrWhiteSpace(jurusanCode)))
        {
            await DisplayAlertAsync("Data siswa belum lengkap", "NISN dan jurusan wajib dipilih.", "Tutup");
            return;
        }

        var user = _editingUser ?? new UserModel { Role = role };
        user.FullName = fullName;
        user.Identifier = identifier;
        user.Email = TxtEmail.Text?.Trim() ?? string.Empty;
        user.PhoneNumber = TxtPhone.Text?.Trim() ?? string.Empty;
        string message;
        string temporaryPassword = string.Empty;
        bool success;
        if (_editingUser == null)
        {
            var result = await ApiService.Current.CreateManagedUserAsync(user, nisn, jurusanCode);
            success = result.Success;
            message = result.Message;
            temporaryPassword = result.TemporaryPassword;
        }
        else
        {
            var result = await ApiService.Current.UpdateManagedUserAsync(user, password, nisn);
            success = result.Success;
            message = result.Message;
        }

        if (!success)
        {
            await DisplayAlertAsync("Gagal menyimpan akun", message, "Tutup");
            return;
        }

        EditorCard.IsVisible = false;
        await LoadAccountsAsync();
        var savedMessage = string.IsNullOrWhiteSpace(temporaryPassword)
            ? message
            : $"{message}\n\nKata sandi sementara: {temporaryPassword}\nSampaikan sandi ini kepada pengguna. Pengguna wajib menggantinya saat login pertama.";
        await DisplayAlertAsync("Akun tersimpan", savedMessage, "Tutup");
    }

    private void OnCancelClicked(object? sender, EventArgs e) => EditorCard.IsVisible = false;

    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: UserModel user }) return;
        var confirm = await DisplayAlertAsync("Hapus akun", $"Hapus akun {user.FullName}?", "Hapus", "Batal");
        if (!confirm) return;

        var result = await ApiService.Current.DeleteManagedUserAsync(user.Id);
        if (!result.Success)
        {
            await DisplayAlertAsync("Akun tidak dapat dihapus", result.Message, "Tutup");
            return;
        }

        await LoadAccountsAsync();
    }

    private static string GetRoleLabel(UserRole role) => role switch
    {
        UserRole.Siswa => "Siswa",
        UserRole.PembimbingIndustri => "Pembimbing Industri",
        UserRole.PembimbingSekolah => "Pembimbing Sekolah",
        UserRole.Admin => "Administrator",
        _ => "Pengguna"
    };
}
