namespace XADatabase.Models;

public class CurrencyEntry
{
    public string Category { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Amount { get; set; }
    public int Cap { get; set; }
}
