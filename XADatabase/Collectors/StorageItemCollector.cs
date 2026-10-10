using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using XADatabase.Models;
using XADatabase.Services;
using CabinetRow = Lumina.Excel.Sheets.Cabinet;

namespace XADatabase.Collectors;

internal static class StorageItemCollector
{
    internal const string ArmoireName = StorageItemAccess.ArmoireName;
    internal const string DresserName = StorageItemAccess.DresserName;
    internal const int ArmoireType = 2500;
    internal const int DresserType = 2501;
    // The item array also contains outfit pieces; its length is not slot capacity.
    internal const int DresserCapacity = 800;

    internal static void Collect(XaServices services, ItemCollectionResult result, bool dresserClosing)
    {
        // Commit each storage only after its entire observation succeeds.
        // Failure in one storage must not discard readable bags or the other storage.
        try { CollectArmoire(services, result); }
        catch (Exception ex) { services.Log.Warning(ex, "[XA] Armoire unavailable; retaining its last observation."); }
        try { CollectDresser(services, result, dresserClosing); }
        catch (Exception ex) { services.Log.Warning(ex, "[XA] Glamour dresser unavailable; retaining its last observation."); }
    }

    private static unsafe void CollectArmoire(XaServices services, ItemCollectionResult result)
    {
        var ui = UIState.Instance();
        if (ui == null || !ui->Cabinet.IsCabinetLoaded())
            return;

        var cabinets = services.DataManager.GetExcelSheet<CabinetRow>();
        var items = services.DataManager.GetExcelSheet<Item>();
        if (cabinets == null || cabinets.Count == 0 || items == null)
            return;

        var observed = new List<ContainerItemEntry>();
        var capacity = 0;
        foreach (var row in cabinets)
        {
            var itemId = row.Item.RowId;
            if (itemId == 0 || !items.TryGetRow(itemId, out var item))
                continue;

            capacity++;
            // Cabinet takes its sheet row ID, not the corresponding Item row ID.
            if (!ui->Cabinet.IsItemInCabinet(row.RowId))
                continue;

            observed.Add(new ContainerItemEntry
            {
                ContainerName = ArmoireName,
                ContainerType = ArmoireType,
                SlotIndex = checked((int)row.RowId),
                ItemId = itemId,
                ItemName = item.Name.ToString(),
                Quantity = 1,
            });
        }

        if (capacity == 0)
            return;
        result.Items.AddRange(observed);
        result.Summaries.Add(new InventorySummary { Name = ArmoireName, UsedSlots = observed.Count, TotalSlots = capacity });
        result.LoadedContainers.Add(ArmoireName);
    }

    private static unsafe void CollectDresser(XaServices services, ItemCollectionResult result, bool closing)
    {
        var agent = AgentMiragePrismPrismBox.Instance();
        if (agent == null || (!closing && !agent->IsAgentActive()) || !agent->IsDataLoaded || agent->Data == null)
            return;

        var data = agent->Data;
        if (data->IsPopulatingList || !data->IsPopulatingComplete
            || data->UsedSlots < 0 || data->UsedSlots > DresserCapacity)
            return;

        var items = services.DataManager.GetExcelSheet<Item>();
        if (items == null)
            return;

        var observed = new List<ContainerItemEntry>();
        var entries = data->PrismBoxItems;
        for (var index = 0; index < entries.Length; index++)
        {
            var rawId = entries[index].ItemId;
            if (rawId == 0)
                continue;

            var (itemId, isHq) = NormalizeDresserItemId(rawId);
            // A partially populated/invalid array is unavailable, never recorded empty.
            if (!items.TryGetRow(itemId, out var item))
                return;

            observed.Add(new ContainerItemEntry
            {
                ContainerName = DresserName,
                ContainerType = DresserType,
                SlotIndex = index,
                ItemId = itemId,
                ItemName = item.Name.ToString(),
                Quantity = 1,
                IsHq = isHq,
            });
        }

        if (observed.Count < data->UsedSlots || (observed.Count > 0 && data->UsedSlots == 0))
            return;
        result.Items.AddRange(observed);
        result.Summaries.Add(new InventorySummary { Name = DresserName, UsedSlots = data->UsedSlots, TotalSlots = DresserCapacity });
        result.LoadedContainers.Add(DresserName);
    }

    internal static (uint ItemId, bool IsHq) NormalizeDresserItemId(uint rawId)
        => rawId >= 1_000_000 && rawId < 2_000_000 ? (rawId - 1_000_000, true) : (rawId, false);

}
