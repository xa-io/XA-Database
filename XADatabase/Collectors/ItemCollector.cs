using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using XADatabase.Core.Collection;
using XADatabase.Models;
using XADatabase.Services;

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

    public static unsafe ItemCollectionResult Collect(IDataManager dataManager, ulong contentId = 0, bool saddlebagVisible = false)
    {
        var results = new ItemCollectionResult();
        var itemSheet = dataManager.GetExcelSheet<Item>();

        var inventoryManager = InventoryManager.Instance();
        if (inventoryManager == null)
            return results;

        results.ManagerReady = true;

        foreach (var (containerName, invType) in TrackedContainers)
        {
            var container = inventoryManager->GetInventoryContainer(invType);
            if (!InventoryReadiness.IsReadable(inventoryManager, invType, contentId, saddlebagVisible))
                continue;

            results.LoadedContainers.Add(containerName);
            var usedSlots = 0;

            for (int i = 0; i < container->Size; i++)
            {
                var slot = container->GetInventorySlot(i);
                if (slot == null || slot->ItemId == 0 || slot->IsSymbolic)
                    continue;

                if (invType != InventoryType.EquippedItems || i != 5)
                    usedSlots++;

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

            results.Summaries.Add(new InventorySummary
            {
                Name = containerName,
                UsedSlots = usedSlots,
                TotalSlots = (int)container->Size - (invType == InventoryType.EquippedItems ? 1 : 0),
            });
        }

        var readable = InventoryContainerMerge.SelectReadableContainers(results.LoadedContainers);
        results.LoadedContainers.IntersectWith(readable);
        results.Items.RemoveAll(item => !readable.Contains(item.ContainerName));
        results.Summaries.RemoveAll(summary => !readable.Contains(summary.Name));
        return results;
    }

    public static SectionResult<ItemCollectionResult> CollectSection(XaServices services, bool saddlebagClosing = false)
    {
        try
        {
            if (!InventoryReadiness.CanCapture(services))
                return SectionResult<ItemCollectionResult>.Unavailable(new(), "live character is not ready or is zoning");

            var value = Collect(services.DataManager, services.PlayerState.ContentId,
                saddlebagClosing || InventoryReadiness.IsSaddlebagVisible(services));
            if (!value.ManagerReady)
                return SectionResult<ItemCollectionResult>.Unavailable(value, "inventory manager is not ready");

            if (value.LoadedContainers.Count == 0)
                return SectionResult<ItemCollectionResult>.Unavailable(value, "no coherent inventory containers are readable");

            var missing = TrackedContainers.Length - value.LoadedContainers.Count;
            var detail = missing > 0
                ? $"{missing} containers were unavailable; only those containers retained their previous data"
                : string.Empty;

            return value.Items.Count == 0
                ? SectionResult<ItemCollectionResult>.AuthoritativeEmpty(value, detail)
                : SectionResult<ItemCollectionResult>.Available(value, detail);
        }
        catch (Exception ex)
        {
            services.Log.Error(ex, "[XA] Item collection failed.");
            return SectionResult<ItemCollectionResult>.Failed(new ItemCollectionResult(), ex.Message);
        }
    }
}

public sealed class ItemCollectionResult
{
    public bool ManagerReady { get; set; }
    public List<ContainerItemEntry> Items { get; } = new();
    public List<InventorySummary> Summaries { get; } = new();
    public HashSet<string> LoadedContainers { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool WasContainerLoaded(string containerName)
    {
        return LoadedContainers.Contains(containerName);
    }
}
