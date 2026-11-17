using SWARNABHOOMI.Core.Common;

namespace SWARNABHOOMI.Core.Entities;

public class Notification : BaseEntity
{
    public Guid UserId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string MessageKey { get; set; } = string.Empty;
    public string? Payload { get; set; }
    public bool IsRead { get; set; } = false;
    public DateTime? ReadAt { get; set; }
    public string? DeliveryStatus { get; set; }

    // Navigation
    public virtual User User { get; set; } = null!;
}