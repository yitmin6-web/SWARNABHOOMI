using SWARNABHOOMI.Core.Common;

namespace SWARNABHOOMI.Core.Entities;

public class WeatherObservation : BaseEntity
{
    public string Location { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public DateOnly ObservationDate { get; set; }
    public decimal? RainfallMm { get; set; }
    public decimal? TemperatureC { get; set; }
    public decimal? HumidityPercent { get; set; }
    public string? Provider { get; set; }
    public string? ProviderMetadata { get; set; }
}