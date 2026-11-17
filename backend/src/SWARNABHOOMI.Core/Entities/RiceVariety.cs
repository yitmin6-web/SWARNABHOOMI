using SWARNABHOOMI.Core.Common;

namespace SWARNABHOOMI.Core.Entities;

public class RiceVariety : BaseEntity
{
    public string VarietyCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int MaturityDays { get; set; }
    public decimal? AverageYieldKgPerHectare { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public virtual ICollection<CultivationRecord> CultivationRecords { get; set; } = new List<CultivationRecord>();
    public virtual ICollection<DemandItem> DemandItems { get; set; } = new List<DemandItem>();
    public virtual ICollection<MarketPriceObservation> PriceObservations { get; set; } = new List<MarketPriceObservation>();
}