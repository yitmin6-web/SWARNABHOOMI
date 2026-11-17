using SWARNABHOOMI.Core.Common;

namespace SWARNABHOOMI.Core.Entities;

public class HarvestForecast : BaseEntity
{
    public Guid CultivationId { get; set; }
    public string RunVersion { get; set; } = string.Empty;
    public DateOnly TargetWindowStart { get; set; }
    public DateOnly TargetWindowEnd { get; set; }
    public decimal PredictedPaddyQuantityKg { get; set; }
    public string? Assumptions { get; set; }
    public string MethodVersion { get; set; } = string.Empty;
    public string ConfidenceCategory { get; set; } = string.Empty; // High, Medium, Low

    // Navigation
    public virtual CultivationRecord CultivationRecord { get; set; } = null!;
}