using SWARNABHOOMI.Core.Common;

namespace SWARNABHOOMI.Core.Entities;

public class DemandItem : BaseEntity
{
    public Guid DemandId { get; set; }
    public Guid RiceVarietyId { get; set; }
    public string Grade { get; set; } = string.Empty;
    public decimal QuantityTonnes { get; set; }
    public decimal? OfferPricePerKg { get; set; }
    public string? Notes { get; set; }

    // Navigation
    public virtual Demand Demand { get; set; } = null!;
    public virtual RiceVariety RiceVariety { get; set; } = null!;
}