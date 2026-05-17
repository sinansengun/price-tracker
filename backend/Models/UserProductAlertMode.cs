namespace PriceTracker.Models;

public static class UserProductAlertMode
{
    public const string Automatic = "automatic";
    public const string Percentage = "percentage";
    public const string TargetPrice = "target_price";

    public static bool IsValid(string? value)
    {
        return value is Automatic or Percentage or TargetPrice;
    }
}