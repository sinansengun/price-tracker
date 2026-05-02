namespace PriceTracker.Models;

public class NotificationHistory
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public int? ProductId { get; set; }
    public int? UserProductId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public decimal? OldPrice { get; set; }
    public decimal? NewPrice { get; set; }
    public bool IsSuccess { get; set; }
    public string? Error { get; set; }
    public DateTime SentAt { get; set; } = DateTime.UtcNow;

    public AppUser User { get; set; } = null!;
    public Product? Product { get; set; }
    public UserProduct? UserProduct { get; set; }
}
