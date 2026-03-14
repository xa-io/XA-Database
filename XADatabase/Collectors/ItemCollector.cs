using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using XADatabase.Models;

namespace XADatabase.Collectors;

public static class ItemCollector
{
    private static readonly (string Name, InventoryType Type)[] TrackedContainers =
    {
        // Main inventory
        ("Inventory 1", InventoryType.Inventory1),
        ("Inventory 2", InventoryType.Inventory2),
        ("Inventory 3", InventoryType.Inventory3),
        ("Inventory 4", InventoryType.Inventory4),

        // Equipped
        ("Equipped", InventoryType.EquippedItems),

        // Armoury
        ("Armoury - Main Hand", InventoryType.ArmoryMainHand),
        ("Armoury - Off Hand", InventoryType.ArmoryOffHand),
        ("Armoury - Head", InventoryType.ArmoryHead),
        ("Armoury - Body", InventoryType.ArmoryBody),
        ("Armoury - Hands", InventoryType.ArmoryHands),
        ("Armoury - Legs", InventoryType.ArmoryLegs),
        ("Armoury - Feet", InventoryType.ArmoryFeets),
        ("Armoury - Earring", InventoryType.ArmoryEar),
        ("Armoury - Necklace", InventoryType.ArmoryNeck),
        ("Armoury - Bracelet", InventoryType.ArmoryWrist),
        ("Armoury - Ring", InventoryType.ArmoryRings),
        ("Armoury - Soul Crystal", InventoryType.ArmorySoulCrystal),

        // Crystals
        ("Crystals", InventoryType.Crystals),

        // Saddlebag
        ("Saddlebag 1", InventoryType.SaddleBag1),
        ("Saddlebag 2", InventoryType.SaddleBag2),
        ("Premium Saddlebag 1", InventoryType.PremiumSaddleBag1),
        ("Premium Saddlebag 2", InventoryType.PremiumSaddleBag2),
    };

    public static unsafe ItemCollectionResult Collect(IDataManager dataManager)
    {
        var results = new ItemCollectionResult();
        var itemSheet = dataManager.GetExcelSheet<Item>();

        var inventoryManager = InventoryManager.Instance();
        if (inventoryManager == null)
            return results;

        foreach (var (containerName, invType) in TrackedContainers)
        {
            var container = inventoryManager->GetInventoryContainer(invType);
            if (container == null || !container->IsLoaded)
                continue;

            results.LoadedContainers.Add(containerName);

            for (int i = 0; i < container->Size; i++)
            {
                var slot = container->GetInventorySlot(i);
                if (slot == null || slot->ItemId == 0 || slot->IsSymbolic)
                    continue;

                var itemName = string.Empty;
                var baseItemId = slot->ItemId;
                if (itemSheet.TryGetRow(baseItemId, out var itemRow))
                    itemName = itemRow.Name.ToString();

                results.Items.Add(new ContainerItemEntry
                {
                    ContainerName = containerName,
                    ContainerType = (int)invType,
                    SlotIndex = i,
                    ItemId = baseItemId,
                    ItemName = itemName,
                    Quantity = slot->Quantity,
                    IsHq = (slot->Flags & InventoryItem.ItemFlags.HighQuality) != 0,
                });
            }
        }

        return results;
    }
}

public sealed class ItemCollectionResult
{
    public List<ContainerItemEntry> Items { get; } = new();
    public HashSet<string> LoadedContainers { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool WasContainerLoaded(string containerName)
    {
        return LoadedContainers.Contains(containerName);
    }
}
