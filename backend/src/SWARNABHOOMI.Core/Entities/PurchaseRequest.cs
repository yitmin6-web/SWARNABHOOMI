using SWARNABHOOMI.Core.Common;

namespace SWARNABHOOMI.Core.Entities;

public class PurchaseRequest : BaseEntity
{
    public Guid BuyerId { get; set; }
    public Guid? FarmerId { get; set; }
    public Guid DemandItemId { get; set; }
    public decimal QuantityKg { get; set; }
    public decimal OfferPricePerKg { get; set; }
    public string Status { get; set; } = "Pending"; // Pending, Accepted, Rejected, Fulfilled, Cancelled
    public string? Terms { get; set; }
    public string? DeliveryTerms { get; set; }

    // Navigation
    public virtual User Buyer { get; set; } = null!;
    public virtual User? Farmer { get; set; }
    public virtual DemandItem DemandItem { get; set; } = null!;
}