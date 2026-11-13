using SWARNABHOOMI.Core.Common;

namespace SWARNABHOOMI.Core.Entities;

public class FarmerProfile : BaseEntity
{
    public Guid UserId { get; set; }
    public string NationalId { get; set; } = string.Empty;
    public string? Address { get; set; }
    public decimal? TotalLandAreaHectares { get; set; }
    public string? PrimaryVariety { get; set; }
    public string? ExperienceYears { get; set; }

    // Navigation
    public virtual User User { get; set; } = null!;
    public virtual ICollection<Farm> Farms { get; set; } = new List<Farm>();
}