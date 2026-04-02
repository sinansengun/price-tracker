namespace PriceTracker.Models;

/// <summary>
/// Kullanıcıya ait etiket. UserProduct'lara uygulanabilir.
/// </summary>
public class Label
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#6366f1";
    public string UserId { get; set; } = string.Empty;

    public AppUser User { get; set; } = null!;
    public List<UserProduct> UserProducts { get; set; } = [];
}
