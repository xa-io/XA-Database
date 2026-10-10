using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Hooking;
using Dalamud.Memory;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using XADatabase.Database;
using XADatabase.Models;

namespace XADatabase.Services;

public sealed unsafe class ItemLocationTooltipService : IDisposable
{
    private const string GenerateItemTooltipSignature = "48 89 5C 24 ?? 55 56 57 41 54 41 55 41 56 41 57 48 83 EC ?? 48 8B 42 ?? 4C 8B EA";
    private const int ItemDescriptionFieldIndex = 13;
    private const int EffectsFieldIndex = 16;
    private const int TooltipVisibilityFlagsIndex = 5;
    private const int DescriptionVisibilityFlag = 2;
    private const int EffectsVisibilityFlag = 256;

    private readonly Plugin plugin;
    private readonly IPluginLog log;
    private readonly object syncRoot = new();
    private Hook<GenerateItemTooltipDelegate>? generateItemTooltipHook;

    private readonly Dictionary<ulong, CachedCharacterTooltipSnapshot> cachedSnapshotItems = new();
    private Dictionary<uint, OwnedItemTooltipSummary> cachedSummaries = new();
    private int cacheGeneration;
    private bool disposed;

    public bool IsHookAvailable => generateItemTooltipHook?.IsEnabled == true;
    public string HookStatus { get; private set; } = "Unavailable - item tooltip hook has not been resolved.";

    private delegate void* GenerateItemTooltipDelegate(
        AtkUnitBase* addon,
        NumberArrayData* numberArrayData,
        StringArrayData* stringArrayData);

    public ItemLocationTooltipService(
        Plugin plugin,
        IGameInteropProvider gameInterop,
        IPluginLog log)
    {
        this.plugin = plugin;
        this.log = log;

        RefreshCache();

        try
        {
            generateItemTooltipHook = gameInterop.HookFromSignature<GenerateItemTooltipDelegate>(
                GenerateItemTooltipSignature,
                GenerateItemTooltipDetour);
            generateItemTooltipHook.Enable();
            if (!generateItemTooltipHook.IsEnabled)
                throw new InvalidOperationException("The item tooltip hook did not enter the enabled state.");

            HookStatus = "Available - live game item tooltips can show XA ownership summaries.";
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[XA] Failed to enable the item tooltip hook.");
            HookStatus = "Unavailable on this game/Dalamud build - Search tab hover summaries remain available.";

            try
            {
                generateItemTooltipHook?.Dispose();
            }
            catch (Exception cleanupEx)
            {
                log.Warning(cleanupEx, "[XA] Failed while clearing the unavailable item tooltip hook.");
            }
            finally
            {
                generateItemTooltipHook = null;
            }
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
            HookStatus = "Disposed";
        }
    }

    public void RefreshCache()
    {
        try
        {
            var snapshots = plugin.Snapshots.ItemSections()
                .Where(snapshot => plugin.IsCharacterVisible(snapshot.ContentId));
            var snapshotItems = snapshots.ToDictionary(
                snapshot => snapshot.ContentId,
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

        if (!plugin.IsCharacterVisible(contentId))
        {
            List<CachedCharacterTooltipSnapshot> visibleSnapshots;
            int hiddenGeneration;
            lock (syncRoot)
            {
                cachedSnapshotItems.Remove(contentId);
                visibleSnapshots = cachedSnapshotItems.Values.ToList();
                hiddenGeneration = ++cacheGeneration;
            }

            _ = Task.Run(() => BuildSummaries(visibleSnapshots)).ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    log.Warning(task.Exception, "[XA] Failed to remove an excluded character from the item tooltip cache.");
                    return;
                }

                if (task.IsCanceled)
                    return;

                lock (syncRoot)
                {
                    if (hiddenGeneration == cacheGeneration)
                        cachedSummaries = task.Result;
                }
            }, TaskScheduler.Default);
            return;
        }

        var snapshot = new CachedCharacterTooltipSnapshot(
            contentId,
            characterName,
            world,
            updatedUtc,
            allItems.Select(item => new CachedTooltipItem(
                item.ItemId,
                item.ItemName,
                item.Quantity,
                SimplifyTooltipLocation(item.ContainerName))).ToList(),
            retainerItems.Select(item => new CachedTooltipItem(
                item.ItemId,
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

    public bool TryGetSummary(uint itemId, out OwnedItemTooltipSummary summary)
    {
        lock (syncRoot)
        {
            return cachedSummaries.TryGetValue(NormalizeItemId(itemId), out summary!);
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
            if (numberArrayData != null && stringArrayData != null)
                AppendOwnershipSummary(numberArrayData, stringArrayData);
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[XA] Failed to populate the item-location tooltip.");
        }

        return generateItemTooltipHook!.Original(addon, numberArrayData, stringArrayData);
    }

    private void AppendOwnershipSummary(NumberArrayData* numberArrayData, StringArrayData* stringArrayData)
    {
        if (numberArrayData == null || numberArrayData->Size <= TooltipVisibilityFlagsIndex || numberArrayData->IntArray == null)
            return;

        RemoveOwnershipSummary(numberArrayData, stringArrayData, ItemDescriptionFieldIndex, DescriptionVisibilityFlag);
        RemoveOwnershipSummary(numberArrayData, stringArrayData, EffectsFieldIndex, EffectsVisibilityFlag);

        if (!plugin.Configuration.SearchHoverTooltipEnabled)
            return;

        if (!TryResolveTooltipItem(out var itemId))
            return;

        if (!TryGetSummary(itemId, out var summary) || summary.TotalQuantity <= 0)
            return;

        if (!TrySelectTooltipField(stringArrayData, out var fieldIndex, out var tooltipText))
            return;

        var newText = "\n" + string.Join("\n", BuildTooltipLines(summary, plugin.Configuration.SearchHoverTooltipCharacterLimit));
        tooltipText.Payloads.Add(new UIGlowPayload(0));
        tooltipText.Payloads.Add(new UIForegroundPayload(0));
        tooltipText.Payloads.Add(new TextPayload(newText));
        tooltipText.Payloads.Add(new UIGlowPayload(0));
        tooltipText.Payloads.Add(new UIForegroundPayload(0));

        SetTooltipString(stringArrayData, fieldIndex, tooltipText);
        var visibilityFlag = fieldIndex == ItemDescriptionFieldIndex ? DescriptionVisibilityFlag : EffectsVisibilityFlag;
        numberArrayData->SetValue(TooltipVisibilityFlagsIndex, numberArrayData->IntArray[TooltipVisibilityFlagsIndex] | visibilityFlag);
    }

    private static void RemoveOwnershipSummary(NumberArrayData* numberArrayData, StringArrayData* stringArrayData, int fieldIndex, int visibilityFlag)
    {
        if (!TryReadTooltipField(stringArrayData, fieldIndex, out var tooltipText, out _))
            return;

        if (!RemoveOwnershipPayloads(tooltipText))
            return;

        SetTooltipString(stringArrayData, fieldIndex, tooltipText);
        if (tooltipText.Payloads.Count == 0)
            numberArrayData->SetValue(TooltipVisibilityFlagsIndex, numberArrayData->IntArray[TooltipVisibilityFlagsIndex] & ~visibilityFlag);
    }

    private static bool RemoveOwnershipPayloads(SeString tooltipText)
    {
        var removed = false;
        for (var index = 0; index <= tooltipText.Payloads.Count - 5;)
        {
            if (tooltipText.Payloads[index] is UIGlowPayload { ColorKey: 0 }
                && tooltipText.Payloads[index + 1] is UIForegroundPayload { ColorKey: 0 }
                && tooltipText.Payloads[index + 2] is TextPayload { Text: string text }
                && text.StartsWith("\nOwned: ", StringComparison.Ordinal)
                && text.Contains("\nLocations:", StringComparison.Ordinal)
                && tooltipText.Payloads[index + 3] is UIGlowPayload { ColorKey: 0 }
                && tooltipText.Payloads[index + 4] is UIForegroundPayload { ColorKey: 0 })
            {
                tooltipText.Payloads.RemoveRange(index, 5);
                removed = true;
            }
            else
            {
                index++;
            }
        }

        return removed;
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

        if (stringArrayData == null || stringArrayData->StringArray == null || fieldIndex < 0 || fieldIndex >= stringArrayData->Size)
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

    private static bool TryResolveTooltipItem(out uint itemId)
    {
        itemId = 0;
        var agent = AgentItemDetail.Instance();
        if (agent == null)
            return false;

        itemId = NormalizeItemId(agent->ItemId);
        return itemId != 0;
    }

    private static uint NormalizeItemId(uint rawItemId)
        => rawItemId >= 2_000_000 ? rawItemId : rawItemId % 500_000;

    private static Dictionary<uint, OwnedItemTooltipSummary> BuildSummaries(IEnumerable<CachedCharacterTooltipSnapshot> snapshots)
    {
        var items = new Dictionary<uint, ItemTooltipAccumulator>();

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

    private static CachedCharacterTooltipSnapshot CreateCachedSnapshot(XaCharacterItemsData snapshot)
    {
        return new CachedCharacterTooltipSnapshot(
            snapshot.ContentId,
            snapshot.CharacterName,
            snapshot.World,
            snapshot.UpdatedUtc,
            snapshot.AllItems.Select(item => new CachedTooltipItem(
                item.ItemId,
                item.ItemName,
                item.Quantity,
                SimplifyTooltipLocation(item.ContainerName))).ToList(),
            snapshot.RetainerItems.Select(item => new CachedTooltipItem(
                item.ItemId,
                item.ItemName,
                item.Quantity,
                "Retainers")).ToList());
    }

    private static void AccumulateItem(
        Dictionary<uint, ItemTooltipAccumulator> items,
        CachedCharacterTooltipSnapshot snapshot,
        CachedTooltipItem item)
    {
        var itemId = NormalizeItemId(item.ItemId);
        if (itemId == 0 || item.Quantity <= 0)
            return;

        if (!items.TryGetValue(itemId, out var itemAccumulator))
        {
            itemAccumulator = new ItemTooltipAccumulator(itemId, item.ItemName);
            items[itemId] = itemAccumulator;
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
        return string.Join(", ", ordered);
    }

    private sealed record CachedCharacterTooltipSnapshot(
        ulong ContentId,
        string CharacterName,
        string World,
        string UpdatedUtc,
        List<CachedTooltipItem> AllItems,
        List<CachedTooltipItem> RetainerItems);

    private readonly record struct CachedTooltipItem(
        uint ItemId,
        string ItemName,
        int Quantity,
        string LocationName);

    private sealed class ItemTooltipAccumulator
    {
        private readonly Dictionary<ulong, CharacterTooltipAccumulator> characters = new();

        public ItemTooltipAccumulator(uint itemId, string itemName)
        {
            ItemId = itemId;
            ItemName = itemName;
        }

        public uint ItemId { get; }
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

            if (SnapshotTime.TryParseUtc(updatedUtc, out var parsedUpdated))
            {
                if (!SnapshotTime.TryParseUtc(character.UpdatedUtc, out var existingUpdated) || parsedUpdated > existingUpdated)
                    character.UpdatedUtc = SnapshotTime.Format(parsedUpdated);
            }
        }

        public OwnedItemTooltipSummary ToSummary()
        {
            var orderedCharacters = characters.Values
                .OrderByDescending(character => SnapshotTime.TryParseUtc(character.UpdatedUtc, out var updatedUtc) ? updatedUtc : DateTime.MinValue)
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
    string ItemName,
    int TotalQuantity,
    IReadOnlyList<OwnedItemTooltipCharacterSummary> Characters);

public sealed record OwnedItemTooltipCharacterSummary(
    string CharacterLabel,
    string LocationLabel,
    int TotalQuantity,
    string UpdatedUtc);
