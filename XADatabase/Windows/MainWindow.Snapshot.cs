using System;
using System.Collections.Generic;
using System.Linq;
using XADatabase.Collectors;
using XADatabase.Database;
using XADatabase.Models;

namespace XADatabase.Windows;

public partial class MainWindow
{
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
        cachedApartment = snapshot.Row.Apartment;
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
        cachedApartment = string.Empty;
        DataCollected = false;
        lastRefreshTime = DateTime.MinValue;
        FreeCompanyCollector.ClearPersistedValues();
        FcMemberCollector.ClearPersistedValues();
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

    private void MergeActiveRetainerInventory()
    {
        var retainerInventory = RetainerCollector.CollectActiveRetainerInventory(Plugin.DataManager);
        if (retainerInventory.Count == 0)
            return;

        var activeRetainerId = RetainerCollector.GetActiveRetainerId();
        if (activeRetainerId == 0)
            return;

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
