using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;
using XADatabase.Models;

namespace XADatabase.Collectors;

public static class CurrencyCollector
{
    // Special currency item IDs from the game's Item sheet
    private static readonly (string Category, string Name, int ItemId, int Cap)[] CurrencyDefs =
    {
        // Common
        ("Common", "Gil", 1, 999_999_999),
        ("Common", "Venture Coins", 21072, 65_000),
        ("Common", "MGP", 29, 9_999_999),

        // Tomestones
        ("Tomestones", "Allagan Tomestone of Poetics", 28, 2000),
        ("Tomestones", "Allagan Tomestone of Aesthetics", 46, 2000),
        ("Tomestones", "Allagan Tomestone of Heliometry", 47, 2000),
        ("Tomestones", "Allagan Tomestone of Mathematics", 48, 2000),
        ("Tomestones", "Allagan Tomestone of Mnemonics", 49, 2000),

        // Grand Company Seals
        ("Grand Company", "Storm Seals", 20, 90000),
        ("Grand Company", "Serpent Seals", 21, 90000),
        ("Grand Company", "Flame Seals", 22, 90000),
        ("Grand Company", "Allied Seals", 27, 4000),
        ("Grand Company", "Centurio Seals", 10307, 4000),
        ("Grand Company", "Sacks of Nuts", 26533, 4000),
        ("Grand Company", "Bicolor Gemstones", 26807, 1500),

        // Scrips
        ("Scrips", "White Crafters' Scrip", 25199, 4000),
        ("Scrips", "Purple Crafters' Scrip", 33913, 4000),
        ("Scrips", "Orange Crafters' Scrip", 41784, 4000),
        ("Scrips", "White Gatherers' Scrip", 25200, 4000),
        ("Scrips", "Purple Gatherers' Scrip", 33914, 4000),
        ("Scrips", "Skybuilders' Scrip", 28063, 20000),

        // PvP
        ("PvP", "Wolf Marks", 25, 20000),
        ("PvP", "Trophy Crystals", 36656, 20000),

        // Tribal / Beast Tribe
        ("Tribal", "Steel Amalj'ok", 21073, 990),
        ("Tribal", "Sylphic Goldleaf", 21074, 990),
        ("Tribal", "Titan Cobaltpiece", 21075, 990),
        ("Tribal", "Rainbowtide Psashp", 21076, 990),
        ("Tribal", "Ixali Oaknot", 21077, 990),
    };

    public static unsafe List<CurrencyEntry> Collect()
    {
        var results = new List<CurrencyEntry>();

        var inventoryManager = InventoryManager.Instance();
        if (inventoryManager == null)
            return results;

        foreach (var (category, name, itemId, cap) in CurrencyDefs)
        {
            int amount;

            if (itemId == 1)
            {
                // Gil is stored separately
                amount = (int)inventoryManager->GetGil();
            }
            else
            {
                amount = inventoryManager->GetInventoryItemCount((uint)itemId);
            }

            results.Add(new CurrencyEntry
            {
                Category = category,
                Name = name,
                Amount = amount,
                Cap = cap,
            });
        }

        return results;
    }
}
