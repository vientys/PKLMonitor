namespace Richikiwir.Api.Models;

public sealed class CompanySettingsRequest
{
    public string NamaPerusahaan { get; set; } = string.Empty;
    public string BidangUsaha { get; set; } = string.Empty;
    public string Alamat { get; set; } = string.Empty;
    public string KontakPerson { get; set; } = string.Empty;
    public string NoTelepon { get; set; } = string.Empty;
    public string JamMasuk { get; set; } = "08:00";
    public string JamPulang { get; set; } = "17:00";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double RadiusMeters { get; set; } = 100;
}