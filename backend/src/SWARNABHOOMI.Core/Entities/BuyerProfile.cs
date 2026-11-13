using SWARNABHOOMI.Core.Common;

namespace SWARNABHOOMI.Core.Entities;

public class BuyerProfile : BaseEntity
{
    public Guid UserId { get; set; }
    public string BusinessName { get; set; } = string.Empty;
    public string? BusinessRegistrationNumber { get; set; }
    public string? Address { get; set; }
    public string? ContactPerson { get; set; }
    public decimal? MonthlyDemandTonnes { get; set; }
    public string? PreferredVarieties { get; set; }

    // Navigation
    public virtual User User { get; set; } = null!;
    public virtual ICollection<Demand> Demands { get; set; } = new List<Demand>();
    public virtual ICollection<PurchaseRequest> PurchaseRequests { get; set; } = new List<PurchaseRequest>();
}