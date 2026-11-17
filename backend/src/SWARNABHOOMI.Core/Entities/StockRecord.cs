using SWARNABHOOMI.Core.Common;

namespace SWARNABHOOMI.Core.Entities;

public class StockRecord : BaseEntity
{
    public string Location { get; set; } = string.Empty;
    public Guid RiceVarietyId { get; set; }
    public string Grade { get; set; } = string.Empty;
    public string CommodityStage { get; set; } = string.Empty; // Paddy, MilledRice
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = "kg";
    public DateOnly AsOfDate { get; set; }
    public string? VerificationStatus { get; set; }

    // Navigation
    public virtual RiceVariety RiceVariety { get; set; } = null!;
}