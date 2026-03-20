using System;
using System.Collections.Generic;
using System.Linq;
using XADatabase.Collectors;
using XADatabase.Database;
using XADatabase.Models;

namespace XADatabase.Windows;

public partial class MainWindow
{
    private const int SaddlebagContainerSlotCount = 35;
    private static readonly string[] SaddlebagContainerNames =
    {
        "Saddlebag 1",
        "Saddlebag 2",
        "Premium Saddlebag 1",
        "Premium Saddlebag 2",
    };

    private List<ContainerItemEntry> lastLiveCollectedItems = new();
    private readonly HashSet<string> lastLoadedItemContainers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<ulong> liveSessionRetainerListingIds = new();
    private readonly HashSet<ulong> liveSessionRetainerInventoryIds = new();
    private bool hasAuthoritativeLiveRetainerList;
    private readonly Dictionary<ulong, XaCharacterSnapshotData> dashboardSnapshotCache = new();
    private bool dashboardSnapshotCacheDirty = true;

    private void ApplySnapshotToCache(XaCharacterSnapshotData snapshot)
    {
        cachedCurrencies = snapshot.Currencies;
        cachedJobs = snapshot.Jobs;
        cachedInventory = snapshot.InventorySummaries;
        cachedItems = snapshot.AllItems;
        cachedRetainers = snapshot.Retainers;
        cachedListings = snapshot.Listings;
        cachedRetainerItems = snapshot.RetainerItems;
        cachedFc = snapshot.FreeCompany;
        cachedFcMembers = snapshot.FcMembers;
        cachedSquadron = snapshot.Squadron;
        cachedVoyages = snapshot.Voyages;
        cachedCollections = snapshot.Collections;
        cachedQuests = snapshot.ActiveQuests;
        cachedMsqMilestones = snapshot.MsqMilestones;
        cachedPersonalEstate = snapshot.Row.PersonalEstate;
        cachedSharedEstates = snapshot.Row.SharedEstates;
        cachedApartment = snapshot.Row.Apartment;
        lastLiveCollectedItems.Clear();
        lastLoadedItemContainers.Clear();
        liveSessionRetainerListingIds.Clear();
        liveSessionRetainerInventoryIds.Clear();
        hasAuthoritativeLiveRetainerList = false;
        NormalizeSaddlebagInventorySummariesFromItems();
        NormalizeCachedRetainerState(snapshot.Row.ContentId);

        if (cachedFc != null)
            FreeCompanyCollector.SeedPersistedValues(cachedFc.FcPoints, cachedFc.Estate, cachedFc.Name, cachedFc.Tag, cachedFc.Rank);
    }

    private void ResetCharacterScopedCache()
    {
        cachedCurrencies.Clear();
        cachedJobs.Clear();
        cachedInventory.Clear();
        cachedItems.Clear();
        cachedRetainers.Clear();
        cachedListings.Clear();
        cachedRetainerItems.Clear();
        cachedFc = null;
        cachedFcMembers.Clear();
        cachedSquadron = null;
        cachedVoyages = null;
        cachedCollections.Clear();
        cachedQuests.Clear();
        cachedMsqMilestones.Clear();
        cachedPersonalEstate = string.Empty;
        cachedSharedEstates = string.Empty;
        cachedApartment = string.Empty;
        lastLiveCollectedItems.Clear();
        lastLoadedItemContainers.Clear();
        liveSessionRetainerListingIds.Clear();
        liveSessionRetainerInventoryIds.Clear();
        hasAuthoritativeLiveRetainerList = false;
        lastLiveContentId = 0;
        DataCollected = false;
        lastRefreshTime = DateTime.MinValue;
        FreeCompanyCollector.ClearPersistedValues();
        FcMemberCollector.ClearPersistedValues();
        HousingCollector.ResetPersonalHousingState();
    }

    private void InvalidateDashboardSnapshotCache()
    {
        dashboardSnapshotCacheDirty = true;
    }

    private IReadOnlyDictionary<ulong, XaCharacterSnapshotData> GetDashboardSnapshotCache()
    {
        if (!dashboardSnapshotCacheDirty)
            return dashboardSnapshotCache;

        dashboardSnapshotCache.Clear();
        foreach (var snapshot in plugin.SnapshotRepo.GetAllSnapshots())
            dashboardSnapshotCache[snapshot.Row.ContentId] = snapshot;

        dashboardSnapshotCacheDirty = false;
        return dashboardSnapshotCache;
    }

    private void ApplyPersistedSaddlebagState(XaCharacterSnapshotData? persistedSnapshot, bool allowObservedSaddlebagClear)
    {
        var liveItems = (lastLiveCollectedItems.Count > 0 || lastLoadedItemContainers.Count > 0)
            ? lastLiveCollectedItems
            : cachedItems;

        var mergedItems = liveItems
            .Where(item => !IsSaddlebagContainerName(item.ContainerName))
            .Select(CloneContainerItem)
            .ToList();

        var liveSaddlebagByContainer = liveItems
            .Where(item => IsSaddlebagContainerName(item.ContainerName))
            .GroupBy(item => item.ContainerName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(CloneContainerItem).ToList(), StringComparer.OrdinalIgnoreCase);

        var persistedSaddlebagByContainer = (persistedSnapshot?.SaddlebagItems ?? new List<ContainerItemEntry>())
            .Where(item => IsSaddlebagContainerName(item.ContainerName))
            .GroupBy(item => item.ContainerName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(CloneContainerItem).ToList(), StringComparer.OrdinalIgnoreCase);

        var saddlebagContainers = new HashSet<string>(SaddlebagContainerNames, StringComparer.OrdinalIgnoreCase);
        saddlebagContainers.UnionWith(liveSaddlebagByContainer.Keys);
        saddlebagContainers.UnionWith(persistedSaddlebagByContainer.Keys);

        foreach (var containerName in saddlebagContainers)
        {
            if (liveSaddlebagByContainer.TryGetValue(containerName, out var liveContainerItems) && liveContainerItems.Count > 0)
            {
                mergedItems.AddRange(liveContainerItems);
                continue;
            }

            if (lastLoadedItemContainers.Contains(containerName))
            {
                if (!allowObservedSaddlebagClear && persistedSaddlebagByContainer.TryGetValue(containerName, out var persistedLoadedContainerItems))
                    mergedItems.AddRange(persistedLoadedContainerItems);

                continue;
            }

            if (persistedSaddlebagByContainer.TryGetValue(containerName, out var persistedContainerItems))
                mergedItems.AddRange(persistedContainerItems);
        }

        cachedItems = mergedItems;
    }

    private void ApplyPersistedSaddlebagInventorySummaries(XaCharacterSnapshotData? persistedSnapshot, bool allowObservedSaddlebagClear)
    {
        var mergedInventory = cachedInventory
            .Where(summary => !IsSaddlebagContainerName(summary.Name))
            .Select(CloneInventorySummary)
            .ToList();

        var liveSaddlebagSummaries = cachedInventory
            .Where(summary => IsSaddlebagContainerName(summary.Name))
            .ToDictionary(summary => summary.Name, CloneInventorySummary, StringComparer.OrdinalIgnoreCase);

        var persistedSaddlebagSummaries = (persistedSnapshot?.InventorySummaries ?? new List<InventorySummary>())
            .Where(summary => IsSaddlebagContainerName(summary.Name))
            .ToDictionary(summary => summary.Name, CloneInventorySummary, StringComparer.OrdinalIgnoreCase);

        var saddlebagContainers = new HashSet<string>(SaddlebagContainerNames, StringComparer.OrdinalIgnoreCase);
        saddlebagContainers.UnionWith(liveSaddlebagSummaries.Keys);
        saddlebagContainers.UnionWith(persistedSaddlebagSummaries.Keys);

        foreach (var containerName in saddlebagContainers)
        {
            if (lastLoadedItemContainers.Contains(containerName))
            {
                if (liveSaddlebagSummaries.TryGetValue(containerName, out var liveSummary))
                    mergedInventory.Add(CloneInventorySummary(liveSummary));
                else if (!allowObservedSaddlebagClear && persistedSaddlebagSummaries.TryGetValue(containerName, out var persistedLoadedSummary))
                    mergedInventory.Add(CloneInventorySummary(persistedLoadedSummary));

                continue;
            }

            if (persistedSaddlebagSummaries.TryGetValue(containerName, out var persistedSummary))
            {
                mergedInventory.Add(CloneInventorySummary(persistedSummary));
                continue;
            }

            if (liveSaddlebagSummaries.TryGetValue(containerName, out var fallbackLiveSummary))
                mergedInventory.Add(CloneInventorySummary(fallbackLiveSummary));
        }

        cachedInventory = mergedInventory;
    }

    private void ApplyPersistedRetainerState(XaCharacterSnapshotData? persistedSnapshot)
    {
        MergePersistedRetainers(persistedSnapshot);
        ApplyPersistedRetainerListings(persistedSnapshot);
        ApplyPersistedRetainerInventory(persistedSnapshot);
    }

    private void MergePersistedRetainers(XaCharacterSnapshotData? persistedSnapshot)
    {
        if (persistedSnapshot == null)
            return;

        var mergedRetainers = persistedSnapshot.Retainers
            .Where(retainer => retainer.RetainerId != 0)
            .Select(CloneRetainerEntry)
            .ToDictionary(retainer => retainer.RetainerId);

        foreach (var retainer in cachedRetainers.Where(retainer => retainer.RetainerId != 0))
            mergedRetainers[retainer.RetainerId] = CloneRetainerEntry(retainer);

        cachedRetainers = mergedRetainers.Values
            .OrderBy(retainer => retainer.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(retainer => retainer.RetainerId)
            .ToList();
    }

    private void ApplyPersistedRetainerListings(XaCharacterSnapshotData? persistedSnapshot)
    {
        var mergedListings = (persistedSnapshot?.Listings ?? new List<RetainerListingEntry>())
            .Where(listing => listing.RetainerId != 0)
            .Select(CloneRetainerListing)
            .ToList();

        foreach (var retainerId in liveSessionRetainerListingIds)
        {
            mergedListings.RemoveAll(entry => entry.RetainerId == retainerId);
            mergedListings.AddRange(cachedListings
                .Where(entry => entry.RetainerId == retainerId)
                .Select(CloneRetainerListing));
        }

        if (hasAuthoritativeLiveRetainerList)
        {
            var zeroMarketRetainerIds = cachedRetainers
                .Where(retainer => retainer.RetainerId != 0 && retainer.MarketItemCount == 0)
                .Select(retainer => retainer.RetainerId)
                .ToHashSet();

            if (zeroMarketRetainerIds.Count > 0)
                mergedListings.RemoveAll(entry => zeroMarketRetainerIds.Contains(entry.RetainerId));
        }

        cachedListings = mergedListings;
    }

    private void ApplyPersistedRetainerInventory(XaCharacterSnapshotData? persistedSnapshot)
    {
        var mergedRetainerItems = (persistedSnapshot?.RetainerItems ?? new List<RetainerInventoryItem>())
            .Where(item => item.RetainerId != 0)
            .Select(CloneRetainerInventoryItem)
            .ToList();

        foreach (var retainerId in liveSessionRetainerInventoryIds)
        {
            mergedRetainerItems.RemoveAll(entry => entry.RetainerId == retainerId);
            mergedRetainerItems.AddRange(cachedRetainerItems
                .Where(entry => entry.RetainerId == retainerId)
                .Select(CloneRetainerInventoryItem));
        }

        cachedRetainerItems = mergedRetainerItems;
    }

    private bool IsSaddlebagClearConfirmed(SnapshotTrigger trigger)
    {
        return trigger == SnapshotTrigger.AddonWatcher
            && lastAddonTrigger != null
            && lastAddonTrigger.Category.Equals("Saddlebag", StringComparison.OrdinalIgnoreCase)
            && lastAddonTrigger.AddonName.Equals("InventoryBuddy", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSaddlebagContainerName(string containerName)
    {
        return !string.IsNullOrWhiteSpace(containerName)
            && (containerName.StartsWith("Saddlebag ", StringComparison.OrdinalIgnoreCase)
                || containerName.StartsWith("Premium Saddlebag ", StringComparison.OrdinalIgnoreCase));
    }

    private static ContainerItemEntry CloneContainerItem(ContainerItemEntry item)
    {
        return new ContainerItemEntry
        {
            ContainerName = item.ContainerName,
            ContainerType = item.ContainerType,
            SlotIndex = item.SlotIndex,
            ItemId = item.ItemId,
            ItemName = item.ItemName,
            Quantity = item.Quantity,
            IsHq = item.IsHq,
        };
    }

    private static InventorySummary CloneInventorySummary(InventorySummary summary)
    {
        return new InventorySummary
        {
            Name = summary.Name,
            UsedSlots = summary.UsedSlots,
            TotalSlots = summary.TotalSlots,
        };
    }

    private static RetainerEntry CloneRetainerEntry(RetainerEntry retainer)
    {
        return new RetainerEntry
        {
            OwnerContentId = retainer.OwnerContentId,
            RetainerId = retainer.RetainerId,
            Name = retainer.Name,
            ClassJob = retainer.ClassJob,
            Level = retainer.Level,
            Gil = retainer.Gil,
            ItemCount = retainer.ItemCount,
            MarketItemCount = retainer.MarketItemCount,
            Town = retainer.Town,
            VentureId = retainer.VentureId,
            VentureCompleteUnix = retainer.VentureCompleteUnix,
            VentureStatus = retainer.VentureStatus,
            VentureEta = retainer.VentureEta,
        };
    }

    private static RetainerListingEntry CloneRetainerListing(RetainerListingEntry listing)
    {
        return new RetainerListingEntry
        {
            RetainerId = listing.RetainerId,
            RetainerName = listing.RetainerName,
            SlotIndex = listing.SlotIndex,
            ItemId = listing.ItemId,
            ItemName = listing.ItemName,
            Quantity = listing.Quantity,
            IsHq = listing.IsHq,
            UnitPrice = listing.UnitPrice,
        };
    }

    private static RetainerInventoryItem CloneRetainerInventoryItem(RetainerInventoryItem item)
    {
        return new RetainerInventoryItem
        {
            RetainerId = item.RetainerId,
            RetainerName = item.RetainerName,
            ItemId = item.ItemId,
            ItemName = item.ItemName,
            Quantity = item.Quantity,
            IsHq = item.IsHq,
        };
    }

    private void NormalizeCachedRetainerState(ulong expectedOwnerContentId = 0)
    {
        var normalized = XaCharacterSnapshotRepository.NormalizeRetainerPayload(cachedRetainers, cachedListings, cachedRetainerItems, expectedOwnerContentId);
        cachedRetainers = normalized.Retainers;
        cachedListings = normalized.Listings;
        cachedRetainerItems = normalized.RetainerItems;
    }

    private void NormalizeSaddlebagInventorySummariesFromItems()
    {
        var usedSlotsByContainer = cachedItems
            .Where(item => IsSaddlebagContainerName(item.ContainerName))
            .GroupBy(item => item.ContainerName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.SlotIndex).Distinct().Count(),
                StringComparer.OrdinalIgnoreCase);

        var existingSaddlebagSummaries = cachedInventory
            .Where(summary => IsSaddlebagContainerName(summary.Name))
            .ToDictionary(summary => summary.Name, CloneInventorySummary, StringComparer.OrdinalIgnoreCase);

        if (usedSlotsByContainer.Count == 0 && existingSaddlebagSummaries.Count == 0)
            return;

        var mergedInventory = cachedInventory
            .Where(summary => !IsSaddlebagContainerName(summary.Name))
            .Select(CloneInventorySummary)
            .ToList();

        var saddlebagContainers = new HashSet<string>(SaddlebagContainerNames, StringComparer.OrdinalIgnoreCase);
        saddlebagContainers.UnionWith(existingSaddlebagSummaries.Keys);
        saddlebagContainers.UnionWith(usedSlotsByContainer.Keys);

        foreach (var containerName in saddlebagContainers)
        {
            var hasExistingSummary = existingSaddlebagSummaries.TryGetValue(containerName, out var existingSummary);
            var hasItemData = usedSlotsByContainer.TryGetValue(containerName, out var usedSlots);
            if (!hasExistingSummary && !hasItemData)
                continue;

            mergedInventory.Add(new InventorySummary
            {
                Name = containerName,
                UsedSlots = hasItemData ? usedSlots : existingSummary?.UsedSlots ?? 0,
                TotalSlots = SaddlebagContainerSlotCount,
            });
        }

        cachedInventory = mergedInventory;
    }

    private (string World, string Datacenter, string Region) ResolveStableWorldAndDatacenter(ulong contentId)
    {
        var persistedCharacter = plugin.CharacterRepo.Get(contentId);

        string world;
        try { world = Plugin.PlayerState.HomeWorld.Value.Name.ToString(); }
        catch { world = string.Empty; }

        if (string.IsNullOrWhiteSpace(world))
        {
            try { world = Plugin.ObjectTable.LocalPlayer?.HomeWorld.Value.Name.ToString() ?? string.Empty; }
            catch { world = string.Empty; }
        }

        if (string.IsNullOrWhiteSpace(world))
        {
            try { world = Plugin.ObjectTable.LocalPlayer?.CurrentWorld.Value.Name.ToString() ?? string.Empty; }
            catch { world = string.Empty; }
        }

        if (string.IsNullOrWhiteSpace(world))
            world = persistedCharacter?.World ?? string.Empty;

        var datacenter = XaCharacterSnapshotRepository.ResolveDatacenter(world);
        if (string.IsNullOrWhiteSpace(datacenter))
        {
            try { datacenter = Plugin.ObjectTable.LocalPlayer?.HomeWorld.Value.DataCenter.Value.Name.ToString() ?? string.Empty; }
            catch { datacenter = string.Empty; }
        }

        if (string.IsNullOrWhiteSpace(datacenter))
            datacenter = persistedCharacter?.Datacenter ?? string.Empty;

        var region = XaCharacterSnapshotRepository.ResolveRegion(world);
        if (string.IsNullOrWhiteSpace(region))
            region = persistedCharacter?.Region ?? string.Empty;

        return (world, datacenter, region);
    }

    private void ClearPersistedFreeCompanyState()
    {
        cachedFc = null;
        cachedFcMembers.Clear();
        cachedVoyages = null;
        FreeCompanyCollector.ClearPersistedValues();
        FcMemberCollector.ClearPersistedValues();
        HousingCollector.ResetPersonalHousingState();
    }

    private List<ItemLocationResult> SearchSnapshotItemsByName(string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText) || searchText.Length < 2)
            return new List<ItemLocationResult>();

        var results = new List<ItemLocationResult>();
        foreach (var snapshot in plugin.SnapshotRepo.GetAllSnapshots())
        {
            foreach (var item in snapshot.AllItems)
            {
                if (string.IsNullOrWhiteSpace(item.ItemName) || item.ItemName.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                results.Add(new ItemLocationResult
                {
                    CharacterName = snapshot.Row.CharacterName,
                    World = snapshot.Row.World,
                    ContainerName = item.ContainerName,
                    ItemId = item.ItemId,
                    ItemName = item.ItemName,
                    Quantity = item.Quantity,
                    IsHq = item.IsHq,
                });
            }

            foreach (var item in snapshot.RetainerItems)
            {
                if (string.IsNullOrWhiteSpace(item.ItemName) || item.ItemName.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                results.Add(new ItemLocationResult
                {
                    CharacterName = snapshot.Row.CharacterName,
                    World = snapshot.Row.World,
                    ContainerName = $"Retainer: {item.RetainerName}",
                    ItemId = item.ItemId,
                    ItemName = item.ItemName,
                    Quantity = item.Quantity,
                    IsHq = item.IsHq,
                });
            }
        }

        return results
            .OrderBy(r => r.ItemName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.CharacterName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.ContainerName, StringComparer.OrdinalIgnoreCase)
            .Take(200)
            .ToList();
    }

    private void MergeActiveRetainerListings()
    {
        var activeRetainerId = RetainerCollector.GetActiveRetainerId();
        if (activeRetainerId == 0 || !RetainerCollector.IsActiveRetainerMarketLoaded())
            return;

        var retainerListings = RetainerCollector.CollectActiveRetainerListings(Plugin.DataManager);
        liveSessionRetainerListingIds.Add(activeRetainerId);
        cachedListings.RemoveAll(item => item.RetainerId == activeRetainerId);
        cachedListings.AddRange(retainerListings);

        var activeRetainer = cachedRetainers.FirstOrDefault(item => item.RetainerId == activeRetainerId);
        if (activeRetainer != null)
            activeRetainer.MarketItemCount = (byte)Math.Clamp(retainerListings.Count, 0, byte.MaxValue);
    }

    private void MergeActiveRetainerInventory()
    {
        var activeRetainerId = RetainerCollector.GetActiveRetainerId();
        if (activeRetainerId == 0 || !RetainerCollector.IsActiveRetainerInventoryLoaded())
            return;

        var retainerInventory = RetainerCollector.CollectActiveRetainerInventory(Plugin.DataManager);
        liveSessionRetainerInventoryIds.Add(activeRetainerId);

        var retainerName = cachedRetainers.FirstOrDefault(item => item.RetainerId == activeRetainerId)?.Name ?? string.Empty;
        var retainerInvItems = retainerInventory.Select(item => new RetainerInventoryItem
        {
            RetainerId = activeRetainerId,
            RetainerName = retainerName,
            ItemId = item.ItemId,
            ItemName = item.ItemName,
            Quantity = item.Quantity,
            IsHq = item.IsHq,
        }).ToList();

        cachedRetainerItems.RemoveAll(item => item.RetainerId == activeRetainerId);
        cachedRetainerItems.AddRange(retainerInvItems);
    }
}
