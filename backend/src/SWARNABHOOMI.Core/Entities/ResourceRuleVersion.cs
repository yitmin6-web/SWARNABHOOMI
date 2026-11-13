using SWARNABHOOMI.Core.Common;

namespace SWARNABHOOMI.Core.Entities;

public class ResourceRuleVersion : BaseEntity
{
    public string Category { get; set; } = string.Empty;
    public string ApplicabilityJson { get; set; } = "{}";
    public decimal? MinimumValue { get; set; }
    public decimal? MaximumValue { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string SourceReference { get; set; } = string.Empty;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string Status { get; set; } = "Draft";
    public int Version { get; set; } = 1;
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ChangeReason { get; set; }
}
