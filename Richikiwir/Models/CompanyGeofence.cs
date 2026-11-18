namespace Richikiwir.Models;

public sealed class CompanyGeofence
{
    public string CompanyName { get; set; } = string.Empty;
    public double TargetLatitude { get; set; }
    public double TargetLongitude { get; set; }
    public double AllowedRadiusMeters { get; set; } = 100;
    public string Address { get; set; } = string.Empty;
    public string WorkStartTime { get; set; } = string.Empty;
    public string WorkEndTime { get; set; } = string.Empty;
}