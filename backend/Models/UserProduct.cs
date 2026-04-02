namespace PriceTracker.Models;

public class UserProduct
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public int ProductId { get; set; }
    public decimal? TargetPrice { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    public AppUser User { get; set; } = null!;
    public Product Product { get; set; } = null!;
    public List<Label> Labels { get; set; } = [];
}
