namespace PriceTracker.Models;

public class Label
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#6366f1";

    public List<Product> Products { get; set; } = [];
}
