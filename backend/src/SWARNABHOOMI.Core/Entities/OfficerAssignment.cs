using SWARNABHOOMI.Core.Common;

namespace SWARNABHOOMI.Core.Entities;

public class OfficerAssignment : BaseEntity
{
    public Guid UserId { get; set; }
    public string District { get; set; } = string.Empty;
    public string? Province { get; set; }
    public string? AssignedBy { get; set; }
    public DateTime? AssignedDate { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public virtual User User { get; set; } = null!;
}