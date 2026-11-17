using SWARNABHOOMI.Core.Common;

namespace SWARNABHOOMI.Core.Entities;

public class HarvestRecord : BaseEntity
{
    public Guid CultivationId { get; set; }
    public DateOnly ActualHarvestDate { get; set; }
    public decimal ActualPaddyQuantityKg { get; set; }
    public string? Quality { get; set; }
    public decimal? LossesKg { get; set; }
    public string? EvidenceReference { get; set; }
    public string VerificationStatus { get; set; } = "Pending"; // Pending, Verified, Rejected

    // Navigation
    public virtual CultivationRecord CultivationRecord { get; set; } = null!;
}