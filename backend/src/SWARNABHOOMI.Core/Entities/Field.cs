using SWARNABHOOMI.Core.Common;

namespace SWARNABHOOMI.Core.Entities;

public class Field : BaseEntity
{
    public Guid FarmId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal AreaHectares { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? BoundaryGeoJson { get; set; }

    // Navigation
    public virtual Farm Farm { get; set; } = null!;
    public virtual ICollection<CultivationRecord> CultivationRecords { get; set; } = new List<CultivationRecord>();
    public virtual ICollection<FieldVerification> Verifications { get; set; } = new List<FieldVerification>();
}