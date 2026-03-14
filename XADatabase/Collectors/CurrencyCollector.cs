using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using XADatabase.Models;

namespace XADatabase.Collectors;

public static class CurrencyCollector
{
    private const int CurrencyContainerType = 2000;

    private static readonly Dictionary<uint, (string Category, string Name, int Cap, bool AlwaysShow)> CurrencyDefs = new()
    {
        { 1, ("Common", "Gil", 999_999_999, true) },
        { 21072, ("Common", "Venture Coins", 65_000, true) },
        { 29, ("Common", "MGP", 9_999_999, true) },
        { 28, ("Tomestones", "Allagan Tomestone of Poetics", 2000, true) },
        { 46, ("Tomestones", "Allagan Tomestone of Aesthetics", 2000, true) },
        { 47, ("Tomestones", "Allagan Tomestone of Heliometry", 2000, true) },
        { 48, ("Tomestones", "Allagan Tomestone of Mathematics", 2000, true) },
        { 49, ("Tomestones", "Allagan Tomestone of Mnemonics", 2000, true) },
        { 20, ("Grand Company", "Storm Seals", 90_000, true) },
        { 21, ("Grand Company", "Serpent Seals", 90_000, true) },
        { 22, ("Grand Company", "Flame Seals", 90_000, true) },
        { 27, ("Grand Company", "Allied Seals", 4_000, true) },
        { 10307, ("Grand Company", "Centurio Seals", 4_000, true) },
        { 26533, ("Grand Company", "Sacks of Nuts", 4_000, true) },
        { 26807, ("Grand Company", "Bicolor Gemstones", 1_500, true) },
        { 25199, ("Scrips", "White Crafters' Scrip", 4_000, true) },
        { 33913, ("Scrips", "Purple Crafters' Scrip", 4_000, true) },
        { 41784, ("Scrips", "Orange Crafters' Scrip", 4_000, true) },
        { 25200, ("Scrips", "White Gatherers' Scrip", 4_000, true) },
        { 33914, ("Scrips", "Purple Gatherers' Scrip", 4_000, true) },
        { 28063, ("Scrips", "Skybuilders' Scrip", 20_000, true) },
        { 25, ("PvP", "Wolf Marks", 20_000, true) },
        { 36656, ("PvP", "Trophy Crystals", 20_000, true) },
        { 21073, ("Tribal", "Steel Amalj'ok", 990, true) },
        { 21074, ("Tribal", "Sylphic Goldleaf", 990, true) },
        { 21075, ("Tribal", "Titan Cobaltpiece", 990, true) },
        { 21076, ("Tribal", "Rainbowtide Psashp", 990, true) },
        { 21077, ("Tribal", "Ixali Oaknot", 990, true) },
    };

    private static readonly Dictionary<string, int> CategoryOrder = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Common"] = 0,
        ["Tomestones"] = 1,
        ["Grand Company"] = 2,
        ["Scrips"] = 3,
        ["PvP"] = 4,
        ["Tribal"] = 5,
        ["Other"] = 6,
    };

    public static unsafe List<CurrencyEntry> Collect(IDataManager dataManager)
    {
        var resultsByItemId = new Dictionary<uint, CurrencyEntry>();
        var inventoryManager = InventoryManager.Instance();
        if (inventoryManager == null)
            return new List<CurrencyEntry>();

        AddCurrency(resultsByItemId, 1, (int)inventoryManager->GetGil(), dataManager);

        var currencyContainer = inventoryManager->GetInventoryContainer((InventoryType)CurrencyContainerType);
        if (currencyContainer != null && currencyContainer->IsLoaded)
        {
            for (int i = 0; i < currencyContainer->Size; i++)
            {
                var slot = currencyContainer->GetInventorySlot(i);
                if (slot == null || slot->ItemId == 0 || slot->IsSymbolic)
                    continue;

                AddCurrency(resultsByItemId, slot->ItemId, slot->Quantity, dataManager);
            }
        }

        foreach (var (itemId, metadata) in CurrencyDefs)
        {
            if (!metadata.AlwaysShow && resultsByItemId.ContainsKey(itemId))
                continue;

            var amount = itemId == 1
                ? (int)inventoryManager->GetGil()
                : inventoryManager->GetInventoryItemCount(itemId);

            if (amount <= 0 && !metadata.AlwaysShow)
                continue;

            AddCurrency(resultsByItemId, itemId, amount, dataManager);
        }

        return resultsByItemId.Values
            .OrderBy(entry => GetCategoryOrder(entry.Category))
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddCurrency(Dictionary<uint, CurrencyEntry> resultsByItemId, uint itemId, int amount, IDataManager dataManager)
    {
        if (!TryResolveCurrencyMetadata(itemId, dataManager, out var category, out var name, out var cap))
            return;

        resultsByItemId[itemId] = new CurrencyEntry
        {
            Category = category,
            Name = name,
            Amount = amount,
            Cap = cap,
        };
    }

    private static bool TryResolveCurrencyMetadata(uint itemId, IDataManager dataManager, out string category, out string name, out int cap)
    {
        if (CurrencyDefs.TryGetValue(itemId, out var metadata))
        {
            category = metadata.Category;
            name = metadata.Name;
            cap = metadata.Cap;
            return true;
        }

        var itemSheet = dataManager.GetExcelSheet<Item>();
        if (itemSheet.TryGetRow(itemId, out var itemRow))
        {
            name = itemRow.Name.ToString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(name))
            {
                category = InferCategory(name);
                cap = 0;
                return true;
            }
        }

        category = string.Empty;
        name = string.Empty;
        cap = 0;
        return false;
    }

    private static string InferCategory(string name)
    {
        if (name.Contains("Tomestone", StringComparison.OrdinalIgnoreCase))
            return "Tomestones";
        if (name.Contains("Scrip", StringComparison.OrdinalIgnoreCase))
            return "Scrips";
        if (name.Contains("Seal", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Nut", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Gemstone", StringComparison.OrdinalIgnoreCase))
            return "Grand Company";
        if (name.Contains("Mark", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Trophy Crystal", StringComparison.OrdinalIgnoreCase))
            return "PvP";
        if (name.Contains("Omnitoken", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Oaknot", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Goldleaf", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Cobaltpiece", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Psashp", StringComparison.OrdinalIgnoreCase))
            return "Tribal";
        if (name.Equals("Gil", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Venture", StringComparison.OrdinalIgnoreCase)
            || name.Equals("MGP", StringComparison.OrdinalIgnoreCase))
            return "Common";

        return "Other";
    }

    private static int GetCategoryOrder(string category)
    {
        return CategoryOrder.TryGetValue(category ?? string.Empty, out var order)
            ? order
            : CategoryOrder["Other"];
    }
}
