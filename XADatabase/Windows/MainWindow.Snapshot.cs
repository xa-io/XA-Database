using System;
using System.Collections.Generic;
using System.Linq;
using XADatabase.Collectors;
using XADatabase.Database;
using XADatabase.Models;

namespace XADatabase.Windows;

public partial class MainWindow
{
    private static readonly string[] SaddlebagContainerNames =
    {
        "Saddlebag 1",
        "Saddlebag 2",
        "Premium Saddlebag 1",
        "Premium Saddlebag 2",
    };

    private List<ContainerItemEntry> lastLiveCollectedItems = new();
    private readonly HashSet<string> lastLoadedItemContainers = new(StringComparer.OrdinalIgnoreCase);
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
        NormalizeCachedRetainerState();

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

    private void NormalizeCachedRetainerState()
    {
        var normalized = XaCharacterSnapshotRepository.NormalizeRetainerPayload(cachedRetainers, cachedListings, cachedRetainerItems);
        cachedRetainers = normalized.Retainers;
        cachedListings = normalized.Listings;
        cachedRetainerItems = normalized.RetainerItems;
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
        cachedListings.RemoveAll(item => item.RetainerId == activeRetainerId);
        cachedListings.AddRange(retainerListings);
    }

    private void MergeActiveRetainerInventory()
    {
        var activeRetainerId = RetainerCollector.GetActiveRetainerId();
        if (activeRetainerId == 0 || !RetainerCollector.IsActiveRetainerInventoryLoaded())
            return;

        var retainerInventory = RetainerCollector.CollectActiveRetainerInventory(Plugin.DataManager);

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
