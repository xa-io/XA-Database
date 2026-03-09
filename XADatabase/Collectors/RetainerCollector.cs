using System;
using System.Collections.Generic;
using System.Text;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using XADatabase.Models;

namespace XADatabase.Collectors;

public static class RetainerCollector
{
    private static readonly string[] TownNames =
    {
        "", "Limsa Lominsa", "Gridania", "Ul'dah", "Ishgard",
        "", "", "Kugane", "", "",
        "Crystarium", "", "Old Sharlayan",
    };

    public static unsafe List<RetainerEntry> CollectRetainerList()
    {
        var results = new List<RetainerEntry>();

        var retainerManager = RetainerManager.Instance();
        if (retainerManager == null || !retainerManager->IsReady)
            return results;

        var count = retainerManager->GetRetainerCount();
        for (uint i = 0; i < count; i++)
        {
            var retainer = retainerManager->GetRetainerBySortedIndex(i);
            if (retainer == null || retainer->RetainerId == 0)
                continue;

            var townByte = (int)retainer->Town;
            var townName = townByte >= 0 && townByte < TownNames.Length ? TownNames[townByte] : "Unknown";

            // Venture status
            var ventureId = retainer->VentureId;
            var ventureComplete = retainer->VentureComplete;
            string ventureStatus;
            string ventureEta = string.Empty;

            if (ventureId == 0)
            {
                ventureStatus = "Idle";
            }
            else
            {
                var now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (ventureComplete <= now)
                {
                    ventureStatus = "Complete";
                }
                else
                {
                    var remaining = TimeSpan.FromSeconds(ventureComplete - now);
                    ventureStatus = "Active";
                    ventureEta = remaining.TotalHours >= 1
                        ? $"{(int)remaining.TotalHours}h {remaining.Minutes}m"
                        : $"{remaining.Minutes}m {remaining.Seconds}s";
                }
            }

            results.Add(new RetainerEntry
            {
                RetainerId = retainer->RetainerId,
                Name = retainer->NameString,
                ClassJob = retainer->ClassJob,
                Level = retainer->Level,
                Gil = retainer->Gil,
                ItemCount = retainer->ItemCount,
                MarketItemCount = retainer->MarketItemCount,
                Town = townName,
                VentureId = ventureId,
                VentureCompleteUnix = ventureComplete,
                VentureStatus = ventureStatus,
                VentureEta = ventureEta,
            });
        }

        return results;
    }

    public static unsafe ulong GetActiveRetainerId()
    {
        var retainerManager = RetainerManager.Instance();
        if (retainerManager == null || !retainerManager->IsReady)
            return 0;

        var activeRetainer = retainerManager->GetActiveRetainer();
        if (activeRetainer == null)
            return 0;

        return activeRetainer->RetainerId;
    }

    public static unsafe List<RetainerListingEntry> CollectActiveRetainerListings(IDataManager dataManager)
    {
        var results = new List<RetainerListingEntry>();

        var retainerManager = RetainerManager.Instance();
        if (retainerManager == null || !retainerManager->IsReady)
            return results;

        var activeRetainer = retainerManager->GetActiveRetainer();
        if (activeRetainer == null || activeRetainer->RetainerId == 0)
            return results;

        var retainerId = activeRetainer->RetainerId;
        var retainerName = activeRetainer->NameString;

        var inventoryManager = InventoryManager.Instance();
        if (inventoryManager == null)
            return results;

        var itemSheet = dataManager.GetExcelSheet<Item>();

        // Retainer market listings
        var marketContainer = inventoryManager->GetInventoryContainer(InventoryType.RetainerMarket);
        if (marketContainer != null && marketContainer->IsLoaded)
        {
            for (int i = 0; i < marketContainer->Size; i++)
            {
                var slot = marketContainer->GetInventorySlot(i);
                if (slot == null || slot->ItemId == 0 || slot->IsSymbolic)
                    continue;

                var itemName = string.Empty;
                if (itemSheet.TryGetRow(slot->ItemId, out var itemRow))
                    itemName = itemRow.Name.ToString();

                // Unit price is stored via InventoryManager
                var unitPrice = (uint)inventoryManager->GetRetainerMarketPrice((short)i);

                results.Add(new RetainerListingEntry
                {
                    RetainerId = retainerId,
                    RetainerName = retainerName,
                    SlotIndex = i,
                    ItemId = slot->ItemId,
                    ItemName = itemName,
                    Quantity = slot->Quantity,
                    IsHq = (slot->Flags & InventoryItem.ItemFlags.HighQuality) != 0,
                    UnitPrice = unitPrice,
                });
            }
        }

        return results;
    }

    public static unsafe List<ContainerItemEntry> CollectActiveRetainerInventory(IDataManager dataManager)
    {
        var results = new List<ContainerItemEntry>();

        var retainerManager = RetainerManager.Instance();
        if (retainerManager == null || !retainerManager->IsReady)
            return results;

        var activeRetainer = retainerManager->GetActiveRetainer();
        if (activeRetainer == null || activeRetainer->RetainerId == 0)
            return results;

        var inventoryManager = InventoryManager.Instance();
        if (inventoryManager == null)
            return results;

        var itemSheet = dataManager.GetExcelSheet<Item>();

        // Retainer inventory pages
        var retainerPages = new[]
        {
            InventoryType.RetainerPage1,
            InventoryType.RetainerPage2,
            InventoryType.RetainerPage3,
            InventoryType.RetainerPage4,
            InventoryType.RetainerPage5,
            InventoryType.RetainerPage6,
            InventoryType.RetainerPage7,
        };

        for (int p = 0; p < retainerPages.Length; p++)
        {
            var container = inventoryManager->GetInventoryContainer(retainerPages[p]);
            if (container == null || !container->IsLoaded)
                continue;

            for (int i = 0; i < container->Size; i++)
            {
                var slot = container->GetInventorySlot(i);
                if (slot == null || slot->ItemId == 0 || slot->IsSymbolic)
                    continue;

                var itemName = string.Empty;
                if (itemSheet.TryGetRow(slot->ItemId, out var itemRow))
                    itemName = itemRow.Name.ToString();

                results.Add(new ContainerItemEntry
                {
                    ContainerName = $"Retainer Page {p + 1}",
                    ContainerType = (int)retainerPages[p],
                    SlotIndex = i,
                    ItemId = slot->ItemId,
                    ItemName = itemName,
                    Quantity = slot->Quantity,
                    IsHq = (slot->Flags & InventoryItem.ItemFlags.HighQuality) != 0,
                });
            }
        }

        return results;
    }
}
