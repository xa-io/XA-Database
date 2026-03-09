namespace XADatabase.Models;

public class InventorySummary
{
    public string Name { get; set; } = string.Empty;
    public int UsedSlots { get; set; }
    public int TotalSlots { get; set; }
}
