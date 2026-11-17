using SWARNABHOOMI.Core.Common;

namespace SWARNABHOOMI.Core.Entities;

public class AuditLog : BaseEntity
{
    public string? Actor { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? Result { get; set; }
    public string? Reason { get; set; }
    public string? SafeMetadata { get; set; }
}