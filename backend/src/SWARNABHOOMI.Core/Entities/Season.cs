using SWARNABHOOMI.Core.Common;

namespace SWARNABHOOMI.Core.Entities;

public class Season : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public int Year { get; set; }
    public string? District { get; set; }
    public string? AgrarianServiceCentre { get; set; }
    public DateOnly PlantingStart { get; set; }
    public DateOnly PlantingEnd { get; set; }
    public string? WaterScheduleReference { get; set; }
    public string ApprovedVarietyClassesJson { get; set; } = "[]";
    public string Status { get; set; } = "Draft";
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? ClosureReason { get; set; }
}
