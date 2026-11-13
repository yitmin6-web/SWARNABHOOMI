using SWARNABHOOMI.Core.Common;
using SWARNABHOOMI.Core.Enums;

namespace SWARNABHOOMI.Core.Entities;

public class User : BaseEntity
{
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Farmer;
    public bool IsEmailConfirmed { get; set; } = false;
    public bool IsPhoneConfirmed { get; set; } = false;
    public bool IsActive { get; set; } = true;
    public bool IsVerified { get; set; } = false;
    public DateTime? LastLoginAt { get; set; }
    public string? District { get; set; }
    public string? Province { get; set; }

    // Navigation properties
    public virtual FarmerProfile? FarmerProfile { get; set; }
    public virtual BuyerProfile? BuyerProfile { get; set; }
    public virtual GovernmentProfile? GovernmentProfile { get; set; }
    public virtual ICollection<OfficerAssignment> OfficerAssignments { get; set; } = new List<OfficerAssignment>();
    public virtual ICollection<Farm> Farms { get; set; } = new List<Farm>();
    public virtual ICollection<CultivationRecord> CultivationRecords { get; set; } = new List<CultivationRecord>();
    public virtual ICollection<Demand> Demands { get; set; } = new List<Demand>();
    public virtual ICollection<PurchaseRequest> PurchaseRequests { get; set; } = new List<PurchaseRequest>();
    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();
    public virtual ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
}