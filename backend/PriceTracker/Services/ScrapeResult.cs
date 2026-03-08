namespace PriceTracker.Services;

public class ScrapeResult
{
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string? ImageUrl { get; set; }
    public string? Store { get; set; }
}
