using SWARNABHOOMI.Core.Common;

namespace SWARNABHOOMI.Core.Entities;

public class CultivationRecord : BaseEntity
{
    public Guid FarmerId { get; set; }
    public Guid FieldId { get; set; }
    public Guid RiceVarietyId { get; set; }
    public string Season { get; set; } = string.Empty; // e.g., "Yala 2027"
    public DateOnly PlantingDate { get; set; }
    public DateOnly? ExpectedHarvestDate { get; set; }
    public decimal AreaHectares { get; set; }
    public string? Notes { get; set; }
    public string Status { get; set; } = "Planned"; // Planned, Active, Harvested, Cancelled

    // Navigation
    public virtual User Farmer { get; set; } = null!;
    public virtual Field Field { get; set; } = null!;
    public virtual RiceVariety RiceVariety { get; set; } = null!;
    public virtual ICollection<HarvestForecast> HarvestForecasts { get; set; } = new List<HarvestForecast>();
    public virtual ICollection<HarvestRecord> HarvestRecords { get; set; } = new List<HarvestRecord>();
}