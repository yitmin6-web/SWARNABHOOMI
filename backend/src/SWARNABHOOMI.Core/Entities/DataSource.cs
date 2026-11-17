using SWARNABHOOMI.Core.Common;

namespace SWARNABHOOMI.Core.Entities;

public class DataSource : BaseEntity
{
    public string Provider { get; set; } = string.Empty;
    public string Dataset { get; set; } = string.Empty;
    public string? LicenseTermsNote { get; set; }
    public DateTime? LastSuccessfulImport { get; set; }
    public string? ImportBatch { get; set; }
    public DateTime? SourceTimestamp { get; set; }
    public string? ValidationResults { get; set; }
}