namespace XADatabase.Models;

public class CurrencyEntry
{
    /// <summary>Game Item row ID for item-backed currencies; zero for synthetic or legacy values.</summary>
    public uint ItemId { get; set; }

    /// <summary>Stable identity for synthetic values whose display name may be localized.</summary>
    public string Key { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Amount { get; set; }
    public int Cap { get; set; }
}
