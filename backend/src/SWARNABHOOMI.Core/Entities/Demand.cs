using SWARNABHOOMI.Core.Common;

namespace SWARNABHOOMI.Core.Entities;

public class Demand : BaseEntity
{
    public Guid OwnerId { get; set; } // Buyer or Government user
    public string District { get; set; } = string.Empty;
    public string Season { get; set; } = string.Empty;
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public string Status { get; set; } = "Draft"; // Draft, Submitted, Approved, Fulfilled, Cancelled
    public string? Notes { get; set; }

    // Navigation
    public virtual User Owner { get; set; } = null!;
    public virtual ICollection<DemandItem> DemandItems { get; set; } = new List<DemandItem>();
}