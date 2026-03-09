using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;
using XADatabase.Models;

namespace XADatabase.Collectors;

public static class InventoryCollector
{
    private static readonly (string Name, InventoryType[] Types, int SlotsPerBag)[] BagGroups =
    {
        ("Inventory 1", new[] { InventoryType.Inventory1 }, 35),
        ("Inventory 2", new[] { InventoryType.Inventory2 }, 35),
        ("Inventory 3", new[] { InventoryType.Inventory3 }, 35),
        ("Inventory 4", new[] { InventoryType.Inventory4 }, 35),

        ("Equipped", new[] { InventoryType.EquippedItems }, 13),

        ("Armoury - Main Hand", new[] { InventoryType.ArmoryMainHand }, 50),
        ("Armoury - Off Hand", new[] { InventoryType.ArmoryOffHand }, 35),
        ("Armoury - Head", new[] { InventoryType.ArmoryHead }, 35),
        ("Armoury - Body", new[] { InventoryType.ArmoryBody }, 35),
        ("Armoury - Hands", new[] { InventoryType.ArmoryHands }, 35),
        ("Armoury - Legs", new[] { InventoryType.ArmoryLegs }, 35),
        ("Armoury - Feet", new[] { InventoryType.ArmoryFeets }, 35),
        ("Armoury - Earring", new[] { InventoryType.ArmoryEar }, 35),
        ("Armoury - Necklace", new[] { InventoryType.ArmoryNeck }, 35),
        ("Armoury - Bracelet", new[] { InventoryType.ArmoryWrist }, 35),
        ("Armoury - Ring", new[] { InventoryType.ArmoryRings }, 50),
        ("Armoury - Soul Crystal", new[] { InventoryType.ArmorySoulCrystal }, 25),

        ("Crystals", new[] { InventoryType.Crystals }, 18),

        ("Saddlebag 1", new[] { InventoryType.SaddleBag1 }, 35),
        ("Saddlebag 2", new[] { InventoryType.SaddleBag2 }, 35),

        ("Premium Saddlebag 1", new[] { InventoryType.PremiumSaddleBag1 }, 35),
        ("Premium Saddlebag 2", new[] { InventoryType.PremiumSaddleBag2 }, 35),
    };

    public static unsafe List<InventorySummary> Collect()
    {
        var results = new List<InventorySummary>();

        var inventoryManager = InventoryManager.Instance();
        if (inventoryManager == null)
            return results;

        foreach (var (name, types, slotsPerBag) in BagGroups)
        {
            int used = 0;
            int total = 0;

            foreach (var type in types)
            {
                var container = inventoryManager->GetInventoryContainer(type);
                if (container == null || !container->IsLoaded)
                {
                    total += slotsPerBag;
                    continue;
                }

                // For EquippedItems, exclude deprecated waist slot (index 5) from total
                total += type == InventoryType.EquippedItems
                    ? (int)container->Size - 1
                    : (int)container->Size;

                for (int i = 0; i < container->Size; i++)
                {
                    // Skip deprecated waist slot (index 5) — removed in Stormblood, always empty
                    if (type == InventoryType.EquippedItems && i == 5)
                        continue;

                    var item = container->GetInventorySlot(i);
                    if (item != null && item->ItemId != 0)
                        used++;

                }
            }

            results.Add(new InventorySummary
            {
                Name = name,
                UsedSlots = used,
                TotalSlots = total,
            });
        }

        return results;
    }
}
