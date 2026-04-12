using Microsoft.AspNetCore.Identity;

namespace PriceTracker.Models;

public class AppUser : IdentityUser
{
    public List<UserProduct> UserProducts { get; set; } = [];
    public List<Label> Labels { get; set; } = [];
    public string? FcmToken { get; set; }
}
