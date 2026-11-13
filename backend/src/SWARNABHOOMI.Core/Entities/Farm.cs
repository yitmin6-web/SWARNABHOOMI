using SWARNABHOOMI.Core.Common;

namespace SWARNABHOOMI.Core.Entities;

public class Farm : BaseEntity
{
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal AreaHectares { get; set; }
    public string? District { get; set; }
    public string? Province { get; set; }
    public string? Address { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    // Navigation
    public virtual User User { get; set; } = null!;
    public virtual ICollection<Field> Fields { get; set; } = new List<Field>();
}