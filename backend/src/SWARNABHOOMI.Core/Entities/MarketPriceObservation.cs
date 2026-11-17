using SWARNABHOOMI.Core.Common;

namespace SWARNABHOOMI.Core.Entities;

public class MarketPriceObservation : BaseEntity
{
    public string Market { get; set; } = string.Empty;
    public Guid RiceVarietyId { get; set; }
    public string Grade { get; set; } = string.Empty;
    public string PriceType { get; set; } = string.Empty; // Wholesale, Retail
    public decimal Value { get; set; }
    public string Currency { get; set; } = "LKR";
    public string Unit { get; set; } = "kg";
    public DateOnly ObservationDate { get; set; }
    public DateOnly? PublicationDate { get; set; }
    public string? Source { get; set; }

    // Navigation
    public virtual RiceVariety RiceVariety { get; set; } = null!;
}