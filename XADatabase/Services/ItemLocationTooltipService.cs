using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Hooking;
using Dalamud.Memory;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using XADatabase.Database;
using XADatabase.Models;

namespace XADatabase.Services;

public sealed unsafe class ItemLocationTooltipService : IDisposable
{
    private const string GenerateItemTooltipSignature = "48 89 5C 24 ?? 55 56 57 41 54 41 55 41 56 41 57 48 83 EC ?? 48 8B 42 ?? 4C 8B EA";
    private const int ItemDescriptionFieldIndex = 13;
    private const int EffectsFieldIndex = 16;

    private readonly Plugin plugin;
    private readonly IGameGui gameGui;
    private readonly IPluginLog log;
    private readonly object syncRoot = new();
    private Hook<GenerateItemTooltipDelegate>? generateItemTooltipHook;

    private readonly Dictionary<ulong, CachedCharacterTooltipSnapshot> cachedSnapshotItems = new();
    private Dictionary<ItemTooltipKey, OwnedItemTooltipSummary> cachedSummaries = new();
    private int cacheGeneration;
    private bool disposed;

    private delegate void* GenerateItemTooltipDelegate(
        AtkUnitBase* addon,
        NumberArrayData* numberArrayData,
        StringArrayData* stringArrayData);

    public ItemLocationTooltipService(
        Plugin plugin,
        IGameInteropProvider gameInterop,
        IGameGui gameGui,
        IPluginLog log)
    {
        this.plugin = plugin;
        this.gameGui = gameGui;
        this.log = log;

        RefreshCache();

        try
        {
            generateItemTooltipHook = gameInterop.HookFromSignature<GenerateItemTooltipDelegate>(
                GenerateItemTooltipSignature,
                GenerateItemTooltipDetour);
            generateItemTooltipHook.Enable();
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[XA] Failed to enable the item tooltip hook.");
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        try
        {
            generateItemTooltipHook?.Dispose();
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[XA] Failed while disposing the item tooltip hook.");
        }
        finally
        {
            generateItemTooltipHook = null;
        }
    }

    public void RefreshCache()
    {
        try
        {
            var snapshots = plugin.SnapshotRepo.GetAllSnapshots();
            var snapshotItems = snapshots.ToDictionary(
                snapshot => snapshot.Row.ContentId,
                CreateCachedSnapshot);
            var rebuilt = BuildSummaries(snapshotItems.Values);

            lock (syncRoot)
            {
                cachedSnapshotItems.Clear();
                foreach (var (contentId, snapshot) in snapshotItems)
                    cachedSnapshotItems[contentId] = snapshot;

                cachedSummaries = rebuilt;
                cacheGeneration++;
            }
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[XA] Failed to rebuild the item tooltip cache.");
        }
    }

    public void UpdateCharacterSnapshot(
        ulong contentId,
        string characterName,
        string world,
        string updatedUtc,
        IEnumerable<ContainerItemEntry> allItems,
        IEnumerable<RetainerInventoryItem> retainerItems)
    {
        if (contentId == 0)
            return;

        var snapshot = new CachedCharacterTooltipSnapshot(
            contentId,
            characterName,
            world,
            updatedUtc,
            allItems.Select(item => new CachedTooltipItem(
                item.ItemId,
                item.IsHq,
                item.ItemName,
                item.Quantity,
                SimplifyTooltipLocation(item.ContainerName))).ToList(),
            retainerItems.Select(item => new CachedTooltipItem(
                item.ItemId,
                item.IsHq,
                item.ItemName,
                item.Quantity,
                "Retainers")).ToList());

        List<CachedCharacterTooltipSnapshot> snapshots;
        int generation;
        lock (syncRoot)
        {
            cachedSnapshotItems[contentId] = snapshot;
            snapshots = cachedSnapshotItems.Values.ToList();
            generation = ++cacheGeneration;
        }

        _ = Task.Run(() => BuildSummaries(snapshots)).ContinueWith(task =>
        {
            if (task.IsFaulted)
            {
                log.Warning(task.Exception, "[XA] Failed to refresh the item tooltip cache from the saved snapshot.");
                return;
            }

            if (task.IsCanceled)
                return;

            lock (syncRoot)
            {
                if (generation == cacheGeneration)
                    cachedSummaries = task.Result;
            }
        }, TaskScheduler.Default);
    }

    public bool TryGetSummary(uint itemId, bool isHq, out OwnedItemTooltipSummary summary)
    {
        lock (syncRoot)
        {
            return cachedSummaries.TryGetValue(new ItemTooltipKey(itemId, isHq), out summary!);
        }
    }

    public static List<string> BuildTooltipLines(OwnedItemTooltipSummary summary, int maxCharacters)
    {
        var lines = new List<string>
        {
            $"Owned: {summary.TotalQuantity}",
            "Locations:",
        };

        var clampedLimit = Math.Clamp(maxCharacters, 1, 25);
        var visibleCharacters = summary.Characters.Take(clampedLimit).ToList();
        foreach (var character in visibleCharacters)
            lines.Add($"      {character.CharacterLabel} - {character.LocationLabel} - {character.TotalQuantity}");

        var hiddenCharacterCount = summary.Characters.Count - visibleCharacters.Count;
        if (hiddenCharacterCount > 0)
            lines.Add($"{hiddenCharacterCount} other characters.");

        return lines;
    }

    private void* GenerateItemTooltipDetour(
        AtkUnitBase* addon,
        NumberArrayData* numberArrayData,
        StringArrayData* stringArrayData)
    {
        try
        {
            if (stringArrayData != null)
                AppendOwnershipSummary(stringArrayData);
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[XA] Failed to populate the item-location tooltip.");
        }

        return generateItemTooltipHook!.Original(addon, numberArrayData, stringArrayData);
    }

    private void AppendOwnershipSummary(StringArrayData* stringArrayData)
    {
        if (!plugin.Configuration.SearchHoverTooltipEnabled)
            return;

        if (!TryResolveHoveredItem(out var itemId, out var isHq))
            return;

        if (!TryGetSummary(itemId, isHq, out var summary) || summary.TotalQuantity <= 0)
            return;

        if (!TrySelectTooltipField(stringArrayData, out var fieldIndex, out var tooltipText))
            return;

        if (tooltipText.TextValue.Contains("\nOwned: ", StringComparison.Ordinal)
            && tooltipText.TextValue.Contains("\nLocations:", StringComparison.Ordinal))
            return;

        var newText = "\n" + string.Join("\n", BuildTooltipLines(summary, plugin.Configuration.SearchHoverTooltipCharacterLimit));
        tooltipText.Payloads.Add(new UIGlowPayload(0));
        tooltipText.Payloads.Add(new UIForegroundPayload(0));
        tooltipText.Payloads.Add(new TextPayload(newText));
        tooltipText.Payloads.Add(new UIGlowPayload(0));
        tooltipText.Payloads.Add(new UIForegroundPayload(0));

        SetTooltipString(stringArrayData, fieldIndex, tooltipText);
    }

    private bool TrySelectTooltipField(StringArrayData* stringArrayData, out int fieldIndex, out SeString tooltipText)
    {
        fieldIndex = -1;
        tooltipText = new SeString();

        var descriptionAvailable = TryReadTooltipField(stringArrayData, ItemDescriptionFieldIndex, out var descriptionText, out var descriptionHasText);
        if (descriptionAvailable && descriptionHasText)
        {
            fieldIndex = ItemDescriptionFieldIndex;
            tooltipText = descriptionText;
            return true;
        }

        var effectsAvailable = TryReadTooltipField(stringArrayData, EffectsFieldIndex, out var effectsText, out var effectsHasText);
        if (effectsAvailable && effectsHasText)
        {
            fieldIndex = EffectsFieldIndex;
            tooltipText = effectsText;
            return true;
        }

        if (descriptionAvailable)
        {
            fieldIndex = ItemDescriptionFieldIndex;
            tooltipText = descriptionText;
            return true;
        }

        if (effectsAvailable)
        {
            fieldIndex = EffectsFieldIndex;
            tooltipText = effectsText;
            return true;
        }

        return false;
    }

    private static bool TryReadTooltipField(StringArrayData* stringArrayData, int fieldIndex, out SeString tooltipText, out bool hasVisibleText)
    {
        tooltipText = new SeString();
        hasVisibleText = false;

        if (stringArrayData == null || fieldIndex < 0 || fieldIndex >= stringArrayData->Size)
            return false;

        var pointer = stringArrayData->StringArray[fieldIndex];
        if (pointer.Value == null)
            return true;

        tooltipText = MemoryHelper.ReadSeStringNullTerminated((nint)pointer.Value);
        hasVisibleText = tooltipText.Payloads.Count > 0 || !string.IsNullOrWhiteSpace(tooltipText.TextValue);
        return true;
    }

    private static void SetTooltipString(StringArrayData* stringArrayData, int fieldIndex, SeString tooltipText)
    {
        var bytes = tooltipText.EncodeWithNullTerminator();
        fixed (byte* textPtr = bytes)
        {
            stringArrayData->SetValue(fieldIndex, textPtr, false, true, false);
        }
    }

    private bool TryResolveHoveredItem(out uint itemId, out bool isHq)
    {
        itemId = 0;
        isHq = false;

        var hoveredItem = (uint)gameGui.HoveredItem;
        if (hoveredItem == 0)
            return false;

        if (hoveredItem >= 2_000_000)
        {
            itemId = hoveredItem;
            return true;
        }

        isHq = hoveredItem >= 1_000_000;
        itemId = hoveredItem % 500_000;
        return itemId != 0;
    }

    private static Dictionary<ItemTooltipKey, OwnedItemTooltipSummary> BuildSummaries(IEnumerable<CachedCharacterTooltipSnapshot> snapshots)
    {
        var items = new Dictionary<ItemTooltipKey, ItemTooltipAccumulator>();

        foreach (var snapshot in snapshots)
        {
            foreach (var item in snapshot.AllItems)
                AccumulateItem(items, snapshot, item);

            foreach (var item in snapshot.RetainerItems)
                AccumulateItem(items, snapshot, item);
        }

        return items.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToSummary());
    }

    private static CachedCharacterTooltipSnapshot CreateCachedSnapshot(XaCharacterSnapshotData snapshot)
    {
        return new CachedCharacterTooltipSnapshot(
            snapshot.Row.ContentId,
            snapshot.Row.CharacterName,
            snapshot.Row.World,
            snapshot.Row.UpdatedUtc,
            snapshot.AllItems.Select(item => new CachedTooltipItem(
                item.ItemId,
                item.IsHq,
                item.ItemName,
                item.Quantity,
                SimplifyTooltipLocation(item.ContainerName))).ToList(),
            snapshot.RetainerItems.Select(item => new CachedTooltipItem(
                item.ItemId,
                item.IsHq,
                item.ItemName,
                item.Quantity,
                "Retainers")).ToList());
    }

    private static void AccumulateItem(
        Dictionary<ItemTooltipKey, ItemTooltipAccumulator> items,
        CachedCharacterTooltipSnapshot snapshot,
        CachedTooltipItem item)
    {
        if (item.ItemId == 0 || item.Quantity <= 0)
            return;

        var key = new ItemTooltipKey(item.ItemId, item.IsHq);
        if (!items.TryGetValue(key, out var itemAccumulator))
        {
            itemAccumulator = new ItemTooltipAccumulator(item.ItemId, item.IsHq, item.ItemName);
            items[key] = itemAccumulator;
        }
        else if (string.IsNullOrWhiteSpace(itemAccumulator.ItemName) && !string.IsNullOrWhiteSpace(item.ItemName))
        {
            itemAccumulator.ItemName = item.ItemName;
        }

        itemAccumulator.Add(snapshot.ContentId, snapshot.CharacterName, snapshot.World, snapshot.UpdatedUtc, item.Quantity, item.LocationName);
    }

    private static string SimplifyTooltipLocation(string containerName)
    {
        if (string.IsNullOrWhiteSpace(containerName))
            return "Stored";

        var lower = containerName.ToLowerInvariant();
        if (lower.StartsWith("retainer:", StringComparison.Ordinal))
            return "Retainers";
        if (lower.StartsWith("inventory", StringComparison.Ordinal) && !lower.Contains("retainer", StringComparison.Ordinal) && !lower.Contains("buddy", StringComparison.Ordinal))
            return "Bags";
        if (lower.StartsWith("saddlebag", StringComparison.Ordinal) || lower.Contains("inventorybuddy", StringComparison.Ordinal))
            return "Saddlebag";
        if (lower.StartsWith("premiumsaddlebag", StringComparison.Ordinal))
            return "Premium Saddlebag";
        if (lower.StartsWith("retainerpage", StringComparison.Ordinal) || lower.StartsWith("retainer page", StringComparison.Ordinal))
            return "Retainers";
        if (lower.StartsWith("retainermarket", StringComparison.Ordinal))
            return "Retainer Market";
        if (lower.StartsWith("armoury", StringComparison.Ordinal) || lower.StartsWith("armory", StringComparison.Ordinal))
            return "Armoury";
        if (lower.StartsWith("equipped", StringComparison.Ordinal))
            return "Equipped";
        if (lower == "crystals")
            return "Crystals";
        return containerName;
    }

    private static string BuildLocationLabel(HashSet<string> locations)
    {
        if (locations.Count == 0)
            return "Stored";

        var ordered = locations
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ordered.Count <= 2)
            return string.Join(", ", ordered);

        return $"{ordered[0]}, {ordered[1]}, +{ordered.Count - 2} more";
    }

    private readonly record struct ItemTooltipKey(uint ItemId, bool IsHq);

    private sealed record CachedCharacterTooltipSnapshot(
        ulong ContentId,
        string CharacterName,
        string World,
        string UpdatedUtc,
        List<CachedTooltipItem> AllItems,
        List<CachedTooltipItem> RetainerItems);

    private readonly record struct CachedTooltipItem(
        uint ItemId,
        bool IsHq,
        string ItemName,
        int Quantity,
        string LocationName);

    private sealed class ItemTooltipAccumulator
    {
        private readonly Dictionary<ulong, CharacterTooltipAccumulator> characters = new();

        public ItemTooltipAccumulator(uint itemId, bool isHq, string itemName)
        {
            ItemId = itemId;
            IsHq = isHq;
            ItemName = itemName;
        }

        public uint ItemId { get; }
        public bool IsHq { get; }
        public string ItemName { get; set; }

        public void Add(ulong contentId, string characterName, string world, string updatedUtc, int quantity, string locationName)
        {
            if (!characters.TryGetValue(contentId, out var character))
            {
                character = new CharacterTooltipAccumulator(characterName, world, updatedUtc);
                characters[contentId] = character;
            }

            character.TotalQuantity += quantity;
            character.Locations.Add(locationName);

            if (DateTime.TryParse(updatedUtc, out var parsedUpdated))
            {
                if (!DateTime.TryParse(character.UpdatedUtc, out var existingUpdated) || parsedUpdated > existingUpdated)
                    character.UpdatedUtc = parsedUpdated.ToString("O");
            }
        }

        public OwnedItemTooltipSummary ToSummary()
        {
            var orderedCharacters = characters.Values
                .OrderByDescending(character => DateTime.TryParse(character.UpdatedUtc, out var updatedUtc) ? updatedUtc : DateTime.MinValue)
                .ThenByDescending(character => character.TotalQuantity)
                .ThenBy(character => character.CharacterName, StringComparer.OrdinalIgnoreCase)
                .Select(character => new OwnedItemTooltipCharacterSummary(
                    character.CharacterLabel,
                    BuildLocationLabel(character.Locations),
                    character.TotalQuantity,
                    character.UpdatedUtc))
                .ToList();

            return new OwnedItemTooltipSummary(
                ItemId,
                IsHq,
                ItemName,
                orderedCharacters.Sum(character => character.TotalQuantity),
                orderedCharacters);
        }
    }

    private sealed class CharacterTooltipAccumulator
    {
        public CharacterTooltipAccumulator(string characterName, string world, string updatedUtc)
        {
            CharacterName = characterName;
            World = world;
            UpdatedUtc = updatedUtc;
        }

        public string CharacterName { get; }
        public string World { get; }
        public string UpdatedUtc { get; set; }
        public int TotalQuantity { get; set; }
        public HashSet<string> Locations { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string CharacterLabel => string.IsNullOrWhiteSpace(World)
            ? CharacterName
            : $"{CharacterName} ({World})";
    }
}

public sealed record OwnedItemTooltipSummary(
    uint ItemId,
    bool IsHq,
    string ItemName,
    int TotalQuantity,
    IReadOnlyList<OwnedItemTooltipCharacterSummary> Characters);

public sealed record OwnedItemTooltipCharacterSummary(
    string CharacterLabel,
    string LocationLabel,
    int TotalQuantity,
    string UpdatedUtc);
