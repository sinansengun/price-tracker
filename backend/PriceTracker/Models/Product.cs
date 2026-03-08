namespace PriceTracker.Models;

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string? Store { get; set; }
    public decimal? InitialPrice { get; set; }
    public decimal? CurrentPrice { get; set; }
    public decimal? TargetPrice { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastCheckedAt { get; set; }

    public List<PriceHistory> PriceHistories { get; set; } = [];
}
