namespace XADatabase.Models;

public class ContainerItemEntry
{
    public string ContainerName { get; set; } = string.Empty;
    public int ContainerType { get; set; }
    public int SlotIndex { get; set; }
    public uint ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public bool IsHq { get; set; }
}
