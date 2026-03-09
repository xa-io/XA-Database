using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using XADatabase.Database;
using XADatabase.Models;

namespace XADatabase.Services;

public static class ExportService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // ── CSV Helpers ──

    private static string CsvEscape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    // ── CSV Export Methods ──

    public static string ExportCurrenciesCsv(List<CurrencyEntry> data)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Category,Name,Amount,Cap");
        foreach (var c in data)
            sb.AppendLine($"{CsvEscape(c.Category)},{CsvEscape(c.Name)},{c.Amount},{c.Cap}");
        return sb.ToString();
    }

    public static string ExportJobsCsv(List<JobEntry> data)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Abbreviation,Name,Category,Level,IsUnlocked");
        foreach (var j in data)
            sb.AppendLine($"{CsvEscape(j.Abbreviation)},{CsvEscape(j.Name)},{CsvEscape(j.Category)},{j.Level},{j.IsUnlocked}");
        return sb.ToString();
    }

    public static string ExportInventoryCsv(List<InventorySummary> data)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Container,UsedSlots,TotalSlots");
        foreach (var i in data)
            sb.AppendLine($"{CsvEscape(i.Name)},{i.UsedSlots},{i.TotalSlots}");
        return sb.ToString();
    }

    public static string ExportItemsCsv(List<ContainerItemEntry> data)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Container,ItemName,ItemId,Quantity,IsHq,Slot");
        foreach (var i in data)
            sb.AppendLine($"{CsvEscape(i.ContainerName)},{CsvEscape(i.ItemName)},{i.ItemId},{i.Quantity},{i.IsHq},{i.SlotIndex}");
        return sb.ToString();
    }

    public static string ExportRetainersCsv(List<RetainerEntry> data)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Name,Level,Gil,Items,MarketItems,Town,VentureStatus,VentureEta");
        foreach (var r in data)
            sb.AppendLine($"{CsvEscape(r.Name)},{r.Level},{r.Gil},{r.ItemCount},{r.MarketItemCount},{CsvEscape(r.Town)},{CsvEscape(r.VentureStatus)},{CsvEscape(r.VentureEta)}");
        return sb.ToString();
    }

    public static string ExportListingsCsv(List<RetainerListingEntry> data)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Retainer,ItemName,ItemId,Quantity,IsHq,UnitPrice,Total");
        foreach (var l in data)
            sb.AppendLine($"{CsvEscape(l.RetainerName)},{CsvEscape(l.ItemName)},{l.ItemId},{l.Quantity},{l.IsHq},{l.UnitPrice},{(long)l.UnitPrice * l.Quantity}");
        return sb.ToString();
    }

    public static string ExportCollectionsCsv(List<CollectionSummary> data)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Category,Unlocked,Total,Percent");
        foreach (var c in data)
        {
            var pct = c.Total > 0 ? ((double)c.Unlocked / c.Total * 100).ToString("F1") : "0.0";
            sb.AppendLine($"{CsvEscape(c.Category)},{c.Unlocked},{c.Total},{pct}");
        }
        return sb.ToString();
    }

    public static string ExportQuestsCsv(List<ActiveQuestEntry> data)
    {
        var sb = new StringBuilder();
        sb.AppendLine("QuestId,Name,Sequence");
        foreach (var q in data)
            sb.AppendLine($"{q.QuestId},{CsvEscape(q.Name)},{q.Sequence}");
        return sb.ToString();
    }

    // ── Master CSV (all characters combined) ──

    public static string BuildMasterCsv<T>(
        string headerSuffix,
        List<(string Name, string World, List<T> Data)> allData,
        Func<T, string> formatRow)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Character,World,{headerSuffix}");
        foreach (var (name, world, data) in allData)
            foreach (var item in data)
                sb.AppendLine($"{CsvEscape(name)},{CsvEscape(world)},{formatRow(item)}");
        return sb.ToString();
    }

    // Row formatters for master CSV
    public static string FmtCurrency(CurrencyEntry c) => $"{CsvEscape(c.Category)},{CsvEscape(c.Name)},{c.Amount},{c.Cap}";
    public static string FmtJob(JobEntry j) => $"{CsvEscape(j.Abbreviation)},{CsvEscape(j.Name)},{CsvEscape(j.Category)},{j.Level},{j.IsUnlocked}";
    public static string FmtInventory(InventorySummary i) => $"{CsvEscape(i.Name)},{i.UsedSlots},{i.TotalSlots}";
    public static string FmtItem(ContainerItemEntry i) => $"{CsvEscape(i.ContainerName)},{CsvEscape(i.ItemName)},{i.ItemId},{i.Quantity},{i.IsHq},{i.SlotIndex}";
    public static string FmtRetainer(RetainerEntry r) => $"{CsvEscape(r.Name)},{r.Level},{r.Gil},{r.ItemCount},{r.MarketItemCount},{CsvEscape(r.Town)},{CsvEscape(r.VentureStatus)},{CsvEscape(r.VentureEta)}";
    public static string FmtListing(RetainerListingEntry l) => $"{CsvEscape(l.RetainerName)},{CsvEscape(l.ItemName)},{l.ItemId},{l.Quantity},{l.IsHq},{l.UnitPrice},{(long)l.UnitPrice * l.Quantity}";
    public static string FmtCollection(CollectionSummary c) => $"{CsvEscape(c.Category)},{c.Unlocked},{c.Total},{(c.Total > 0 ? ((double)c.Unlocked / c.Total * 100).ToString("F1") : "0.0")}";
    public static string FmtQuest(ActiveQuestEntry q) => $"{q.QuestId},{CsvEscape(q.Name)},{q.Sequence}";

    // ── JSON Snapshot ──

    public static string ExportFullJson(
        string characterName,
        string world,
        List<CurrencyEntry> currencies,
        List<JobEntry> jobs,
        List<InventorySummary> inventory,
        List<ContainerItemEntry> items,
        List<RetainerEntry> retainers,
        List<RetainerListingEntry> listings,
        List<RetainerInventoryItem> retainerItems,
        FreeCompanyEntry? fc,
        List<CollectionSummary> collections,
        List<ActiveQuestEntry> quests)
    {
        var snapshot = new Dictionary<string, object?>
        {
            ["export_version"] = "1.0",
            ["exported_utc"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            ["character"] = characterName,
            ["world"] = world,
            ["currencies"] = currencies,
            ["jobs"] = jobs,
            ["inventory"] = inventory,
            ["items"] = items,
            ["retainers"] = retainers,
            ["listings"] = listings,
            ["retainer_items"] = retainerItems,
            ["free_company"] = fc,
            ["collections"] = collections,
            ["active_quests"] = quests,
        };

        return JsonSerializer.Serialize(snapshot, JsonOpts);
    }

    // ── File Write Helper ──

    public static string WriteExport(string basePath, string charName, string suffix, string content)
    {
        var exportDir = Path.Combine(basePath, "exports");
        Directory.CreateDirectory(exportDir);

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var safeName = charName.Replace(" ", "_").Replace("@", "").Trim('_');

        // Split suffix into name + extension so timestamp goes before extension
        // e.g. "currencies.csv" → "currencies" + ".csv" → "CharName_currencies_2026-02-27_15-14-49.csv"
        var ext = Path.GetExtension(suffix);       // ".csv" or ".json"
        var baseSuffix = Path.GetFileNameWithoutExtension(suffix); // "currencies" or "snapshot"
        var filename = $"{safeName}_{baseSuffix}_{timestamp}{ext}";
        var filePath = Path.Combine(exportDir, filename);

        File.WriteAllText(filePath, content, Encoding.UTF8);
        return filePath;
    }

    public static string GetExportDir(string basePath)
    {
        var exportDir = Path.Combine(basePath, "exports");
        Directory.CreateDirectory(exportDir);
        return exportDir;
    }
}
