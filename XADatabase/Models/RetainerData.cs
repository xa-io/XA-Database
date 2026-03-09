using System.Collections.Generic;

namespace XADatabase.Models;

public class RetainerEntry
{
    public ulong RetainerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public byte ClassJob { get; set; }
    public byte Level { get; set; }
    public uint Gil { get; set; }
    public byte ItemCount { get; set; }
    public byte MarketItemCount { get; set; }
    public string Town { get; set; } = string.Empty;
    public ushort VentureId { get; set; }
    public uint VentureCompleteUnix { get; set; }
    public string VentureStatus { get; set; } = string.Empty;
    public string VentureEta { get; set; } = string.Empty;
}

public class RetainerInventoryItem
{
    public ulong RetainerId { get; set; }
    public string RetainerName { get; set; } = string.Empty;
    public uint ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public bool IsHq { get; set; }
}

public class RetainerListingEntry
{
    public ulong RetainerId { get; set; }
    public string RetainerName { get; set; } = string.Empty;
    public int SlotIndex { get; set; }
    public uint ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public bool IsHq { get; set; }
    public uint UnitPrice { get; set; }
}

public class RetainerSaleEntry
{
    public ulong RetainerId { get; set; }
    public string RetainerName { get; set; } = string.Empty;
    public uint ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public bool IsHq { get; set; }
    public uint UnitPrice { get; set; }
    public long TotalGil { get; set; }
    public string SoldUtc { get; set; } = string.Empty;
}
