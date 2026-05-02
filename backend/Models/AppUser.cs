using Microsoft.AspNetCore.Identity;

namespace PriceTracker.Models;

public class AppUser : IdentityUser
{
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<UserProduct> UserProducts { get; set; } = [];
    public List<Label> Labels { get; set; } = [];
    public List<NotificationHistory> NotificationHistories { get; set; } = [];
    public string? FcmToken { get; set; }
}
