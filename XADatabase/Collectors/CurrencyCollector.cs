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
        { 21172, ("Common", "Achievement Certificate", 0, true) },
        { 20, ("Common", "Storm Seal", 90_000, true) },
        { 21, ("Common", "Serpent Seal", 90_000, true) },
        { 22, ("Common", "Flame Seal", 90_000, true) },
        { 21072, ("Common", "Venture", 65_000, true) },
        { 29, ("Common", "MGP", 9_999_999, true) },

        { 23, ("Battle", "Allagan Tomestone of Philosophy", 2000, false) },
        { 24, ("Battle", "Allagan Tomestone of Mythology", 2000, false) },
        { 26, ("Battle", "Allagan Tomestone of Soldiery", 2000, false) },
        { 28, ("Battle", "Allagan Tomestone of Poetics", 2000, true) },
        { 30, ("Battle", "Allagan Tomestone of Law", 2000, false) },
        { 31, ("Battle", "Allagan Tomestone of Esoterics", 2000, false) },
        { 32, ("Battle", "Allagan Tomestone of Lore", 2000, false) },
        { 33, ("Battle", "Allagan Tomestone of Scripture", 2000, false) },
        { 34, ("Battle", "Allagan Tomestone of Verity", 2000, false) },
        { 35, ("Battle", "Allagan Tomestone of Creation", 2000, false) },
        { 36, ("Battle", "Allagan Tomestone of Mendacity", 2000, false) },
        { 37, ("Battle", "Allagan Tomestone of Genesis", 2000, false) },
        { 38, ("Battle", "Allagan Tomestone of Goetia", 2000, false) },
        { 39, ("Battle", "Allagan Tomestone of Phantasmagoria", 2000, false) },
        { 40, ("Battle", "Allagan Tomestone of Allegory", 2000, false) },
        { 41, ("Battle", "Allagan Tomestone of Revelation", 2000, false) },
        { 42, ("Battle", "Allagan Tomestone of Aphorism", 2000, false) },
        { 43, ("Battle", "Allagan Tomestone of Astronomy", 2000, false) },
        { 44, ("Battle", "Allagan Tomestone of Causality", 2000, false) },
        { 45, ("Battle", "Allagan Tomestone of Comedy", 2000, false) },
        { 46, ("Battle", "Allagan Tomestone of Aesthetics", 2000, true) },
        { 47, ("Battle", "Allagan Tomestone of Heliometry", 2000, true) },
        { 48, ("Battle", "Allagan Tomestone of Mathematics", 2000, true) },
        { 49, ("Battle", "Allagan Tomestone of Mnemonics", 2000, true) },
        { 25, ("Battle", "Wolf Mark", 20_000, true) },
        { 36656, ("Battle", "Trophy Crystal", 20_000, true) },
        { 27, ("Battle", "Allied Seal", 4_000, true) },
        { 10307, ("Battle", "Centurio Seal", 4_000, true) },
        { 26533, ("Battle", "Sack of Nuts", 4_000, true) },
        { 26807, ("Battle", "Bicolor Gemstone", 1_500, true) },
        { 31135, ("Battle", "Bozjan Cluster", 0, false) },

        { 25199, ("Other", "White Crafters' Scrip", 4_000, true) },
        { 33913, ("Other", "Purple Crafters' Scrip", 4_000, true) },
        { 41784, ("Other", "Orange Crafters' Scrip", 4_000, true) },
        { 17833, ("Other", "Yellow Crafters' Scrip", 4_000, false) },
        { 25200, ("Other", "White Gatherers' Scrip", 4_000, true) },
        { 33914, ("Other", "Purple Gatherers' Scrip", 4_000, true) },
        { 41785, ("Other", "Orange Gatherers' Scrip", 4_000, true) },
        { 17834, ("Other", "Yellow Gatherers' Scrip", 4_000, false) },
        { 28063, ("Other", "Skybuilders' Scrip", 4_000, true) },
        { 37549, ("Other", "Seafarer's Cowrie", 999_999, true) },
        { 37550, ("Other", "Islander Cowrie", 999_999, true) },
        { 30341, ("Other", "Faux Leaf", 2_000, true) },
        { 41668, ("Other", "Felicitous Token", 0, true) },
        { 41629, ("Other", "MGF", 9_999_999, false) },
        { 45690, ("Other", "Cosmocredit", 999_999, true) },
        { 45691, ("Other", "Lunar Credit", 999_999, true) },
        { 48146, ("Other", "Phaenna Credit", 999_999, true) },
        { 24909, ("Other", "Irregular Tomestone of Philosophy", 0, false) },
        { 26536, ("Other", "Irregular Tomestone of Mythology", 0, false) },
        { 28648, ("Other", "Irregular Tomestone of Soldiery", 0, false) },
        { 30272, ("Other", "Irregular Tomestone of Law", 0, false) },
        { 31339, ("Other", "Irregular Tomestone of Esoterics", 0, false) },
        { 33329, ("Other", "Irregular Tomestone of Pageantry", 0, false) },
        { 33330, ("Other", "Irregular Tomestone of Lore", 0, false) },
        { 35834, ("Other", "Irregular Tomestone of Scripture", 0, false) },
        { 36658, ("Other", "Irregular Tomestone of Verity", 0, false) },
        { 38211, ("Other", "Irregular Tomestone of Creation", 0, false) },
        { 39365, ("Other", "Irregular Tomestone of Mendacity", 0, false) },
        { 39919, ("Other", "Irregular Tomestone of Tenfold Pageantry", 0, false) },
        { 41305, ("Other", "Irregular Tomestone of Genesis I", 0, false) },
        { 41306, ("Other", "Irregular Tomestone of Genesis II", 0, false) },
        { 41786, ("Other", "Irregular Tomestone of Goetia", 0, false) },
        { 44348, ("Other", "Irregular Tomestone of Phantasmagoria", 0, false) },
        { 46179, ("Other", "Irregular Tomestone of Revelation", 0, false) },
        { 46322, ("Other", "Irregular Tomestone of Allegory", 0, false) },
        { 49122, ("Other", "Irregular Tomestone of Aphorism", 0, false) },
        { 15167, ("Other", "Yo-kai Medal", 0, false) },
        { 15168, ("Other", "Yo-kai Legendary Jibanyan Medal", 0, false) },
        { 15169, ("Other", "Yo-kai Legendary Komasan Medal", 0, false) },
        { 15170, ("Other", "Yo-kai Legendary Whisper Medal", 0, false) },
        { 15171, ("Other", "Yo-kai Legendary Blizzaria Medal", 0, false) },
        { 15172, ("Other", "Yo-kai Legendary Kyubi Medal", 0, false) },
        { 15173, ("Other", "Yo-kai Legendary Komajiro Medal", 0, false) },
        { 15174, ("Other", "Yo-kai Legendary Manjimutt Medal", 0, false) },
        { 15175, ("Other", "Yo-kai Legendary Noko Medal", 0, false) },
        { 15176, ("Other", "Yo-kai Legendary Venoct Medal", 0, false) },
        { 15177, ("Other", "Yo-kai Legendary Shogunyan Medal", 0, false) },
        { 15178, ("Other", "Yo-kai Legendary Hovernyan Medal", 0, false) },
        { 15179, ("Other", "Yo-kai Legendary Robonyan F-type Medal", 0, false) },
        { 15180, ("Other", "Yo-kai Legendary Usapyon Medal", 0, false) },
        { 30803, ("Other", "Yo-kai Legendary Zazel Medal", 0, false) },
        { 30804, ("Other", "Yo-kai Legendary Lord Ananta Medal", 0, false) },
        { 30805, ("Other", "Yo-kai Legendary Lord Enma Medal", 0, false) },
        { 30806, ("Other", "Yo-kai Legendary Damona Medal", 0, false) },

        { 21073, ("Societies", "Ixali Oaknot", 999, true) },
        { 21074, ("Societies", "Vanu Whitebone", 999, true) },
        { 21075, ("Societies", "Sylphic Goldleaf", 999, true) },
        { 21076, ("Societies", "Steel Amalj'ok", 999, true) },
        { 21077, ("Societies", "Rainbowtide Psashp", 999, true) },
        { 21078, ("Societies", "Titan Cobaltpiece", 999, true) },
        { 21079, ("Societies", "Black Copper Gil", 999, true) },
        { 21080, ("Societies", "Carved Kupo Nut", 999, true) },
        { 21081, ("Societies", "Kojin Sango", 999, true) },
        { 21935, ("Societies", "Ananta Dreamstaff", 999, true) },
        { 22525, ("Societies", "Namazu Koban", 999, true) },
        { 28186, ("Societies", "Fae Fancy", 999, true) },
        { 28187, ("Societies", "Qitari Compliment", 999, true) },
        { 28188, ("Societies", "Hammered Frogment", 999, true) },
        { 36657, ("Societies", "Arkasodara Pana", 999, true) },
        { 37854, ("Societies", "Omicron Omnitoken", 999, true) },
        { 38952, ("Societies", "Loporrit Carat", 999, true) },
        { 44472, ("Societies", "Pelu Pelplume", 999, true) },
        { 46178, ("Societies", "Yok Huy Ward", 999, true) },
        { 48084, ("Societies", "Mamool Ja Nanook", 999, true) },
    };

    private static readonly Dictionary<string, int> CategoryOrder = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Common"] = 0,
        ["Grand Company"] = 0,
        ["Battle"] = 1,
        ["Tomestones"] = 1,
        ["PvP"] = 1,
        ["Other"] = 2,
        ["Scrips"] = 2,
        ["Societies"] = 3,
        ["Tribal"] = 3,
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
        if (name.Contains("Irregular Tomestone", StringComparison.OrdinalIgnoreCase))
            return "Other";
        if (name.Contains("Tomestone", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Mark", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Trophy Crystal", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Allied Seal", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Centurio Seal", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Nut", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Gemstone", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Bozjan", StringComparison.OrdinalIgnoreCase))
            return "Battle";
        if (name.Contains("Scrip", StringComparison.OrdinalIgnoreCase))
            return "Other";
        if (name.Contains("Omnitoken", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Oaknot", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Goldleaf", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Cobaltpiece", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Psashp", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Whitebone", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Kupo Nut", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Dreamstaff", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Koban", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Fancy", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Compliment", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Frogment", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Pana", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Carat", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Pelplume", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Nanook", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Ward", StringComparison.OrdinalIgnoreCase))
            return "Societies";
        if (name.Equals("Gil", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Company Seal", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Storm Seal", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Serpent Seal", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Flame Seal", StringComparison.OrdinalIgnoreCase)
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
