namespace PriceTracker.Models;

/// <summary>
/// URL bazında küresel olarak benzersiz ürün kaydı.
/// Birden fazla kullanıcı aynı ürünü takip edebilir (UserProduct üzerinden).
/// </summary>
public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;   // UNIQUE
    public string? ImageUrl { get; set; }
    public string? Store { get; set; }
    public decimal? InitialPrice { get; set; }         // İlk scrape fiyatı
    public decimal? CurrentPrice { get; set; }         // Son scrape fiyatı
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastCheckedAt { get; set; }

    public List<PriceHistory> PriceHistories { get; set; } = [];
    public List<UserProduct> UserProducts { get; set; } = [];
}
