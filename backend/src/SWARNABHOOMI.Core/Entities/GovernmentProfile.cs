using SWARNABHOOMI.Core.Common;

namespace SWARNABHOOMI.Core.Entities;

public class GovernmentProfile : BaseEntity
{
    public Guid UserId { get; set; }
    public string Institution { get; set; } = string.Empty;
    public string? Department { get; set; }
    public string? Designation { get; set; }
    public string? District { get; set; }
    public string? Province { get; set; }

    // Navigation
    public virtual User User { get; set; } = null!;
}