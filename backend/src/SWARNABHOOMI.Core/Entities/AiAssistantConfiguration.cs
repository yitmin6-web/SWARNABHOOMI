using SWARNABHOOMI.Core.Common;

namespace SWARNABHOOMI.Core.Entities;

public class AiAssistantConfiguration : BaseEntity
{
    public string BaseUrl { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string EncryptedApiKey { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
}
