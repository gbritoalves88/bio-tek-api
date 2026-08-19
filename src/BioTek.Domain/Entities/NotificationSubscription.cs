using BioTek.Domain.Common;

namespace BioTek.Domain.Entities;

public class NotificationSubscription : BaseEntity
{
    public string UserId { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public string Device { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public DateTime? LastUsedAt { get; set; }
    public bool IsActive { get; set; } = true;
}