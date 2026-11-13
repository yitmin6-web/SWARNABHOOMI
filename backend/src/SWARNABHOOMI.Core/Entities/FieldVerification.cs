using SWARNABHOOMI.Core.Common;

namespace SWARNABHOOMI.Core.Entities;

public class FieldVerification : BaseEntity
{
    public Guid FieldId { get; set; }
    public string Status { get; set; } = "Pending";
    public string? ReviewerId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewNotes { get; set; }

    public virtual Field Field { get; set; } = null!;
}
