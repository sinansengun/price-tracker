namespace PriceTracker.Models;

public class PriceHistory
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public decimal Price { get; set; }
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;

    public Product Product { get; set; } = null!;
}
