using BioTek.Domain.Common;

namespace BioTek.Domain.Entities;

public class NotificationSubscription : BaseEntity
{
    public string UserId { get; set; }
    public string Token { get; set; }
    public string Device { get; set; }
    public string Platform { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public bool IsActive { get; set; } = true;
}