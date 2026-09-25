using System;
using System.Collections.Generic;
using System.Linq;
using XADatabase.Collectors;
using XADatabase.Core.Collection;
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

    private readonly HashSet<ulong> liveSessionRetainerListingIds = new();
    private readonly HashSet<ulong> liveSessionRetainerInventoryIds = new();
    private bool hasAuthoritativeLiveRetainerList;
    private CharacterCacheState? retainedLiveCharacterCache;

    private void ApplySnapshotToCache(XaCharacterSnapshotData snapshot)
    {
        cacheOwnerContentId = snapshot.Row.ContentId;
        cacheOwnerCharacterName = snapshot.Row.CharacterName;
        cachedCurrencies = snapshot.Currencies;
        JournalCollector.SeedPersistedValue(snapshot.Currencies);
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
        liveSessionRetainerListingIds.Clear();
        liveSessionRetainerInventoryIds.Clear();
        hasAuthoritativeLiveRetainerList = false;
        NormalizeSaddlebagInventorySummariesFromItems();
        NormalizeCachedRetainerState(snapshot.Row.ContentId);

        if (cachedFc != null)
        {
            ApplyFreeCompanyGilOwnership(snapshot.Row.ContentId, snapshot.Row.CharacterName);
            FreeCompanyCollector.SeedPersistedValues(cachedFc.FcPoints, cachedFc.Estate, cachedFc.Name, cachedFc.Tag, cachedFc.Rank, cachedFc.FcGil, cachedFc.FcGilObserved, cachedFc.FcId);
        }
    }

    private void ResetCharacterScopedCache()
    {
        // Repository snapshots own their parsed list instances. Replacing the UI
        // references keeps a character switch from clearing SnapshotStore data.
        cachedCurrencies = new List<CurrencyEntry>();
        cachedJobs = new List<JobEntry>();
        cachedInventory = new List<InventorySummary>();
        cachedItems = new List<ContainerItemEntry>();
        cachedRetainers = new List<RetainerEntry>();
        cachedListings = new List<RetainerListingEntry>();
        cachedRetainerItems = new List<RetainerInventoryItem>();
        cachedFc = null;
        cachedFcMembers = new List<FcMemberEntry>();
        cachedSquadron = null;
        cachedVoyages = null;
        cachedCollections = new List<CollectionSummary>();
        cachedQuests = new List<ActiveQuestEntry>();
        cachedMsqMilestones = new List<MsqMilestoneEntry>();
        cachedPersonalEstate = string.Empty;
        cachedSharedEstates = string.Empty;
        cachedApartment = string.Empty;
        liveSessionRetainerListingIds.Clear();
        liveSessionRetainerInventoryIds.Clear();
        hasAuthoritativeLiveRetainerList = false;
        cacheOwnerContentId = 0;
        cacheOwnerCharacterName = string.Empty;
        lastPersistedSnapshotContentId = 0;
        lastPersistedSnapshot = null;
        DataCollected = false;
        lastRefreshTime = DateTime.MinValue;
        FreeCompanyCollector.ClearPersistedValues();
        FcMemberCollector.ClearPersistedValues();
        VoyageCollector.ClearPersistedValues();
        JournalCollector.ClearPersistedValues();
        HousingCollector.ResetPersonalHousingState();
    }

    private CharacterCacheState CaptureCharacterCacheState()
    {
        return new CharacterCacheState
        {
            Currencies = cachedCurrencies.ToList(),
            Jobs = cachedJobs.ToList(),
            Inventory = cachedInventory.ToList(),
            Items = cachedItems.ToList(),
            Retainers = cachedRetainers.ToList(),
            Listings = cachedListings.ToList(),
            RetainerItems = cachedRetainerItems.ToList(),
            FreeCompany = cachedFc,
            FcMembers = cachedFcMembers.ToList(),
            Squadron = cachedSquadron,
            Voyages = cachedVoyages,
            Collections = cachedCollections.ToList(),
            Quests = cachedQuests.ToList(),
            MsqMilestones = cachedMsqMilestones.ToList(),
            PersonalEstate = cachedPersonalEstate,
            SharedEstates = cachedSharedEstates,
            Apartment = cachedApartment,
            OwnerContentId = cacheOwnerContentId,
            OwnerCharacterName = cacheOwnerCharacterName,
            DataCollected = DataCollected,
            LastRefreshTime = lastRefreshTime,
            LastPersistedSnapshotContentId = lastPersistedSnapshotContentId,
            LastPersistedSnapshot = lastPersistedSnapshot,
            CollectorSectionStates = new Dictionary<string, SectionState>(lastCollectorSectionStates, StringComparer.Ordinal),
            LiveRetainerListingIds = liveSessionRetainerListingIds.ToHashSet(),
            LiveRetainerInventoryIds = liveSessionRetainerInventoryIds.ToHashSet(),
            HasAuthoritativeLiveRetainerList = hasAuthoritativeLiveRetainerList,
        };
    }

    private void RestoreCharacterCacheState(CharacterCacheState state)
    {
        cachedCurrencies = state.Currencies;
        cachedJobs = state.Jobs;
        cachedInventory = state.Inventory;
        cachedItems = state.Items;
        cachedRetainers = state.Retainers;
        cachedListings = state.Listings;
        cachedRetainerItems = state.RetainerItems;
        cachedFc = state.FreeCompany;
        cachedFcMembers = state.FcMembers;
        cachedSquadron = state.Squadron;
        cachedVoyages = state.Voyages;
        cachedCollections = state.Collections;
        cachedQuests = state.Quests;
        cachedMsqMilestones = state.MsqMilestones;
        cachedPersonalEstate = state.PersonalEstate;
        cachedSharedEstates = state.SharedEstates;
        cachedApartment = state.Apartment;
        cacheOwnerContentId = state.OwnerContentId;
        cacheOwnerCharacterName = state.OwnerCharacterName;
        DataCollected = state.DataCollected;
        lastRefreshTime = state.LastRefreshTime;
        lastPersistedSnapshotContentId = state.LastPersistedSnapshotContentId;
        lastPersistedSnapshot = state.LastPersistedSnapshot;

        lastCollectorSectionStates.Clear();
        foreach (var entry in state.CollectorSectionStates)
            lastCollectorSectionStates[entry.Key] = entry.Value;

        liveSessionRetainerListingIds.Clear();
        liveSessionRetainerListingIds.UnionWith(state.LiveRetainerListingIds);
        liveSessionRetainerInventoryIds.Clear();
        liveSessionRetainerInventoryIds.UnionWith(state.LiveRetainerInventoryIds);
        hasAuthoritativeLiveRetainerList = state.HasAuthoritativeLiveRetainerList;

        FreeCompanyCollector.ClearPersistedValues();
        if (cachedFc != null)
        {
            FreeCompanyCollector.SeedPersistedValues(
                cachedFc.FcPoints,
                cachedFc.Estate,
                cachedFc.Name,
                cachedFc.Tag,
                cachedFc.Rank,
                cachedFc.FcGil,
                cachedFc.FcGilObserved,
                cachedFc.FcId);
        }

        JournalCollector.ClearPersistedValues();
        JournalCollector.SeedPersistedValue(cachedCurrencies);
    }

    private void RememberLiveCharacterCache()
    {
        var playerState = plugin.Services.PlayerState;
        if (!playerState.IsLoaded
            || viewingContentId.HasValue
            || !DataCollected
            || cacheOwnerContentId == 0
            || cacheOwnerContentId != playerState.ContentId)
        {
            return;
        }

        retainedLiveCharacterCache = CaptureCharacterCacheState();
    }

    private void ResetDashboardFreeCompanyGil(ulong contentId, ulong fcId)
    {
        int updated;
        try
        {
            updated = plugin.SnapshotRepo.ResetFreeCompanyGil(contentId, fcId);
        }
        catch (Exception ex)
        {
            SetSettingsStatus("FC chest gil reset failed. No reset was applied; check the log and try again.");
            plugin.Services.Log.Error(ex, $"[XA] FC chest gil reset failed (cid={contentId}, fcId={fcId}).");
            return;
        }

        // The database commit must succeed before any observed/cache state changes.
        FreeCompanyCollector.ResetChestGil(fcId, cachedFc?.FcId ?? 0);
        cachedFc?.ResetChestGil(fcId);
        lastPersistedSnapshot?.FreeCompany?.ResetChestGil(fcId);
        retainedLiveCharacterCache?.FreeCompany?.ResetChestGil(fcId);
        retainedLiveCharacterCache?.LastPersistedSnapshot?.FreeCompany?.ResetChestGil(fcId);
        InvalidateDashboardSnapshotCache();
        SetSettingsStatus($"FC chest gil reset to 0 in {updated} saved character(s). Visit the FC chest to capture it again.");
        plugin.Services.Log.Information($"[XA] Reset saved FC chest gil (fcId={fcId}, snapshots={updated}).");
    }

    private void InvalidateDashboardSnapshotCache()
    {
        plugin.Snapshots.Invalidate();
    }

    private IReadOnlyDictionary<ulong, XaCharacterSnapshotData> GetDashboardSnapshotCache()
    {
        return plugin.Snapshots.All();
    }

    private List<CharacterRow> GetVisibleCharacters()
        => plugin.CharacterRepo.GetAll()
            .Where(character => plugin.IsCharacterVisible(character.ContentId))
            .ToList();

    internal void OnCharacterVisibilityChanged()
    {
        var hiddenViewedCharacter = viewingContentId.HasValue
            && !plugin.IsCharacterVisible(viewingContentId.Value);

        knownCharacters = GetVisibleCharacters();
        charListQueried = true;
        selectedCharacterIndex = viewingContentId.HasValue
            ? knownCharacters.FindIndex(character => character.ContentId == viewingContentId.Value)
            : -1;

        if (hiddenViewedCharacter)
        {
            viewingContentId = null;
            viewingCharName = string.Empty;
            charSelectorSearch = string.Empty;
            selectedCharacterIndex = -1;
            if (plugin.Services.PlayerState.IsLoaded)
                RefreshData();
            else
                ResetCharacterScopedCache();
        }

        InvalidateDashboardSnapshotCache();
        RefreshItemSearchResults();
        RefreshItemTooltipCache();
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

    private static bool IsSaddlebagContainerName(string containerName)
    {
        return !string.IsNullOrWhiteSpace(containerName)
            && (containerName.StartsWith("Saddlebag ", StringComparison.OrdinalIgnoreCase)
                || containerName.StartsWith("Premium Saddlebag ", StringComparison.OrdinalIgnoreCase));
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
        try { world = plugin.Services.PlayerState.HomeWorld.Value.Name.ToString(); }
        catch { world = string.Empty; }

        if (string.IsNullOrWhiteSpace(world))
        {
            try { world = plugin.Services.ObjectTable.LocalPlayer?.HomeWorld.Value.Name.ToString() ?? string.Empty; }
            catch { world = string.Empty; }
        }

        if (string.IsNullOrWhiteSpace(world))
        {
            try { world = plugin.Services.ObjectTable.LocalPlayer?.CurrentWorld.Value.Name.ToString() ?? string.Empty; }
            catch { world = string.Empty; }
        }

        if (string.IsNullOrWhiteSpace(world))
            world = persistedCharacter?.World ?? string.Empty;

        var datacenter = XaCharacterSnapshotRepository.ResolveDatacenter(world);
        if (string.IsNullOrWhiteSpace(datacenter))
        {
            try { datacenter = plugin.Services.ObjectTable.LocalPlayer?.HomeWorld.Value.DataCenter.Value.Name.ToString() ?? string.Empty; }
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

    private void ApplyFcMemberRankNames(XaCharacterSnapshotData? persistedSnapshot)
    {
        if (cachedFcMembers.Count == 0)
            return;

        var addonRanksByName = BuildRankNameByNameLookup(FreeCompanyCollector.LastAddonMemberRanks);
        var persistedRanks = BuildPersistedFcMemberRankLookup(persistedSnapshot?.FcMembers);
        var sortRanks = FreeCompanyCollector.LastCollectedRankNames;

        foreach (var member in cachedFcMembers)
        {
            if (TryGetRankNameByName(member.Name, addonRanksByName, out var rankName)
                || TryGetPersistedFcMemberRankName(member, persistedRanks.ByContentId, persistedRanks.ByNameWorld, persistedRanks.ByName, out rankName)
                || TryGetRankNameBySort(member.RankSort, sortRanks, out rankName)
                || TryKeepExistingSpecificFcRankName(member, out rankName))
            {
                member.RankName = rankName;
                continue;
            }

            member.RankName = string.Empty;
        }
    }

    private static string GetFcMemberRankDisplayName(FcMemberEntry member)
    {
        return IsUsableFcRankName(member.RankName)
            ? member.RankName.Trim()
            : GetFallbackFcMemberRankName(member.RankSort);
    }

    private static string GetFallbackFcMemberRankName(byte rankSort)
    {
        return rankSort == 0 ? "Master" : $"Rank {rankSort + 1}";
    }

    private static bool TryGetRankNameByName(string memberName, IReadOnlyDictionary<string, string> rankNamesByName, out string rankName)
    {
        rankName = string.Empty;
        if (string.IsNullOrWhiteSpace(memberName))
            return false;

        if (!rankNamesByName.TryGetValue(memberName.Trim(), out var resolvedRankName) || !IsUsableFcRankName(resolvedRankName))
            return false;

        rankName = resolvedRankName.Trim();
        return true;
    }

    private static bool TryGetRankNameBySort(byte rankSort, IReadOnlyDictionary<int, string> rankNamesBySort, out string rankName)
    {
        rankName = string.Empty;
        if (!rankNamesBySort.TryGetValue(rankSort, out var resolvedRankName) || !IsUsableFcRankName(resolvedRankName))
            return false;

        rankName = resolvedRankName.Trim();
        return true;
    }

    private static bool TryGetPersistedFcMemberRankName(
        FcMemberEntry member,
        IReadOnlyDictionary<ulong, string> rankNamesByContentId,
        IReadOnlyDictionary<string, string> rankNamesByNameWorld,
        IReadOnlyDictionary<string, string> rankNamesByName,
        out string rankName)
    {
        rankName = string.Empty;

        if (member.ContentId != 0
            && rankNamesByContentId.TryGetValue(member.ContentId, out var contentIdRankName)
            && IsUsableFcRankName(contentIdRankName))
        {
            rankName = contentIdRankName.Trim();
            return true;
        }

        var nameWorldKey = BuildFcMemberNameWorldKey(member);
        if (nameWorldKey.Length > 0
            && rankNamesByNameWorld.TryGetValue(nameWorldKey, out var nameWorldRankName)
            && IsUsableFcRankName(nameWorldRankName))
        {
            rankName = nameWorldRankName.Trim();
            return true;
        }

        var nameKey = NormalizeFcMemberName(member.Name);
        if (nameKey.Length > 0
            && rankNamesByName.TryGetValue(nameKey, out var nameRankName)
            && IsUsableFcRankName(nameRankName))
        {
            rankName = nameRankName.Trim();
            return true;
        }

        return false;
    }

    private static bool TryKeepExistingSpecificFcRankName(FcMemberEntry member, out string rankName)
    {
        rankName = string.Empty;
        if (!IsUsableFcRankName(member.RankName) || IsFallbackFcMemberRankName(member.RankName, member.RankSort))
            return false;

        rankName = member.RankName.Trim();
        return true;
    }

    private static Dictionary<string, string> BuildRankNameByNameLookup(IReadOnlyDictionary<string, string> rankNamesByName)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, rankName) in rankNamesByName)
        {
            var nameKey = NormalizeFcMemberName(name);
            if (nameKey.Length == 0 || !IsUsableFcRankName(rankName) || lookup.ContainsKey(nameKey))
                continue;

            lookup[nameKey] = rankName.Trim();
        }

        return lookup;
    }

    private static (Dictionary<ulong, string> ByContentId, Dictionary<string, string> ByNameWorld, Dictionary<string, string> ByName)
        BuildPersistedFcMemberRankLookup(IEnumerable<FcMemberEntry>? persistedMembers)
    {
        var byContentId = new Dictionary<ulong, string>();
        var byNameWorld = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var member in persistedMembers ?? Enumerable.Empty<FcMemberEntry>())
        {
            if (!IsUsableFcRankName(member.RankName))
                continue;

            var rankName = member.RankName.Trim();
            if (member.ContentId != 0 && !byContentId.ContainsKey(member.ContentId))
                byContentId[member.ContentId] = rankName;

            var nameWorldKey = BuildFcMemberNameWorldKey(member);
            if (nameWorldKey.Length > 0 && !byNameWorld.ContainsKey(nameWorldKey))
                byNameWorld[nameWorldKey] = rankName;

            var nameKey = NormalizeFcMemberName(member.Name);
            if (nameKey.Length > 0 && !byName.ContainsKey(nameKey))
                byName[nameKey] = rankName;
        }

        return (byContentId, byNameWorld, byName);
    }

    private static string BuildFcMemberNameWorldKey(FcMemberEntry member)
    {
        var name = NormalizeFcMemberName(member.Name);
        if (name.Length == 0)
            return string.Empty;

        var world = member.HomeWorld != 0
            ? member.HomeWorld.ToString()
            : NormalizeFcMemberName(member.HomeWorldName);
        return world.Length == 0 ? string.Empty : $"{name}@{world}";
    }

    private static string NormalizeFcMemberName(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static bool IsUsableFcRankName(string rankName)
    {
        return !string.IsNullOrWhiteSpace(rankName);
    }

    private static bool IsFallbackFcMemberRankName(string rankName, byte rankSort)
    {
        return IsUsableFcRankName(rankName)
            && rankName.Trim().Equals(GetFallbackFcMemberRankName(rankSort), StringComparison.OrdinalIgnoreCase);
    }

    private List<ItemLocationResult> SearchSnapshotItemsByName(string searchText)
    {
        return SearchSnapshotItemsByName(searchText, 200);
    }
    private List<ItemLocationResult> SearchSnapshotItemsByName(string searchText, int? maxResults)
    {
        if (string.IsNullOrWhiteSpace(searchText) || searchText.Length < 2)
            return new List<ItemLocationResult>();

        var results = new List<ItemLocationResult>();
        foreach (var snapshot in plugin.Snapshots.ItemSections())
        {
            if (!plugin.IsCharacterVisible(snapshot.ContentId))
                continue;

            foreach (var item in snapshot.AllItems)
            {
                if (string.IsNullOrWhiteSpace(item.ItemName) || item.ItemName.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                results.Add(new ItemLocationResult
                {
                    ContentId = snapshot.ContentId,
                    CharacterName = snapshot.CharacterName,
                    World = snapshot.World,
                    UpdatedUtc = snapshot.UpdatedUtc,
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
                    ContentId = snapshot.ContentId,
                    CharacterName = snapshot.CharacterName,
                    World = snapshot.World,
                    UpdatedUtc = snapshot.UpdatedUtc,
                    ContainerName = $"Retainer: {item.RetainerName}",
                    ItemId = item.ItemId,
                    ItemName = item.ItemName,
                    Quantity = item.Quantity,
                    IsHq = item.IsHq,
                });
            }
        }

        var orderedResults = results
            .OrderBy(r => r.ItemName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.CharacterName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.ContainerName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (maxResults.HasValue)
            return orderedResults.Take(Math.Max(0, maxResults.Value)).ToList();

        return orderedResults;
    }

    private void MergeActiveRetainerListings()
    {
        var activeRetainerId = RetainerCollector.GetActiveRetainerId();
        if (activeRetainerId == 0 || !RetainerCollector.IsActiveRetainerMarketLoaded())
            return;

        var retainerListings = RetainerCollector.CollectActiveRetainerListings(plugin.Services.DataManager);
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

        var retainerInventory = RetainerCollector.CollectActiveRetainerInventory(plugin.Services.DataManager);
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

    private sealed class CharacterCacheState
    {
        public List<CurrencyEntry> Currencies { get; init; } = new();
        public List<JobEntry> Jobs { get; init; } = new();
        public List<InventorySummary> Inventory { get; init; } = new();
        public List<ContainerItemEntry> Items { get; init; } = new();
        public List<RetainerEntry> Retainers { get; init; } = new();
        public List<RetainerListingEntry> Listings { get; init; } = new();
        public List<RetainerInventoryItem> RetainerItems { get; init; } = new();
        public FreeCompanyEntry? FreeCompany { get; init; }
        public List<FcMemberEntry> FcMembers { get; init; } = new();
        public SquadronInfo? Squadron { get; init; }
        public VoyageInfo? Voyages { get; init; }
        public List<CollectionSummary> Collections { get; init; } = new();
        public List<ActiveQuestEntry> Quests { get; init; } = new();
        public List<MsqMilestoneEntry> MsqMilestones { get; init; } = new();
        public string PersonalEstate { get; init; } = string.Empty;
        public string SharedEstates { get; init; } = string.Empty;
        public string Apartment { get; init; } = string.Empty;
        public ulong OwnerContentId { get; init; }
        public string OwnerCharacterName { get; init; } = string.Empty;
        public bool DataCollected { get; init; }
        public DateTime LastRefreshTime { get; init; }
        public ulong LastPersistedSnapshotContentId { get; init; }
        public XaCharacterSnapshotData? LastPersistedSnapshot { get; init; }
        public Dictionary<string, SectionState> CollectorSectionStates { get; init; } = new(StringComparer.Ordinal);
        public HashSet<ulong> LiveRetainerListingIds { get; init; } = new();
        public HashSet<ulong> LiveRetainerInventoryIds { get; init; } = new();
        public bool HasAuthoritativeLiveRetainerList { get; init; }
    }
}
