using System;
using System.Collections.Generic;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;

namespace XADatabase.Services;

/// <summary>
/// Watches game UI windows (addons) and triggers callbacks when they open/close.
/// Persistent addons (inventory) are always loaded — tracked for display only.
/// Transient addons trigger auto-save when they close.
/// </summary>
public sealed class AddonWatcher : IDisposable
{
    private readonly IAddonLifecycle addonLifecycle;
    private readonly IPluginLog log;
    private Action<string, string, nint>? onAddonClose;
    private Action<string, string, nint>? onAddonOpen;

    // Debug: track which addons are currently open
    private readonly HashSet<string> openAddons = new();

    // Persistent addons: loaded once by the game, never finalized.
    // Tracked for debug display only — do NOT trigger auto-save.
    private static readonly (string Category, string[] Addons)[] PersistentGroups =
    {
        ("Inventory (always loaded)", new[] { "Inventory", "InventoryLarge", "InventoryExpansion" }),
    };

    // Transient addons: created on open, destroyed on close.
    // These trigger auto-save when they close.
    private static readonly (string Category, string[] Addons)[] TransientGroups =
    {
        ("Retainer", new[] { "RetainerList", "InventoryRetainer", "InventoryRetainerLarge", "RetainerSellList" }),
        ("Saddlebag", new[] { "InventoryBuddy" }),
        ("Market", new[] { "ItemSearch", "ItemSearchResult" }),
        ("FC Chest", new[] { "FreeCompanyChest" }),
        ("Armoire", new[] { "Cabinet" }),
        ("Armoury Chest", new[] { "ArmouryBoard" }),
        ("Glamour Dresser", new[] { "MiragePrismBox", "MiragePrismMiragePlate", "MiragePrismPrismBox" }),
        ("FC Members", new[] { "FreeCompany", "FreeCompanyProfile" }),
        ("Estate", new[] { "HousingSignBoard" }),
        ("Workshop", new[] { "SubmarinePartsMenu", "SubmarineExplorationMapSelect",
            "AirShipExploration", "AirShipExplorationDetail", "AirShipExplorationResult",
            "CompanyCraftSupply" }),
    };

    // Flat lookup for category by addon name
    private static readonly Dictionary<string, string> AddonToCategory;
    private static readonly HashSet<string> PersistentAddonNames;

    static AddonWatcher()
    {
        PersistentAddonNames = new HashSet<string>();
        AddonToCategory = new Dictionary<string, string>();

        foreach (var (cat, addons) in PersistentGroups)
            foreach (var a in addons)
            {
                PersistentAddonNames.Add(a);
                AddonToCategory[a] = cat;
            }

        foreach (var (cat, addons) in TransientGroups)
            foreach (var a in addons)
                AddonToCategory[a] = cat;
    }

    public AddonWatcher(IAddonLifecycle addonLifecycle, IPluginLog log)
    {
        this.addonLifecycle = addonLifecycle;
        this.log = log;
    }

    public void Enable(Action<string, string, nint>? onAddonClose = null, Action<string, string, nint>? onAddonOpen = null)
    {
        this.onAddonClose = onAddonClose;
        this.onAddonOpen = onAddonOpen;

        // Register persistent addons (debug tracking only)
        foreach (var (_, addons) in PersistentGroups)
            foreach (var addon in addons)
            {
                addonLifecycle.RegisterListener(AddonEvent.PostSetup, addon, OnAddonOpen);
                addonLifecycle.RegisterListener(AddonEvent.PreFinalize, addon, OnPersistentClose);
            }

        // Register transient addons (trigger auto-save)
        foreach (var (_, addons) in TransientGroups)
            foreach (var addon in addons)
            {
                addonLifecycle.RegisterListener(AddonEvent.PostSetup, addon, OnAddonOpen);
                addonLifecycle.RegisterListener(AddonEvent.PreFinalize, addon, OnTransientClose);
            }

        log.Information("[XA] AddonWatcher enabled.");
    }

    public void Disable()
    {
        foreach (var (_, addons) in PersistentGroups)
            foreach (var addon in addons)
            {
                addonLifecycle.UnregisterListener(AddonEvent.PostSetup, addon, OnAddonOpen);
                addonLifecycle.UnregisterListener(AddonEvent.PreFinalize, addon, OnPersistentClose);
            }

        foreach (var (_, addons) in TransientGroups)
            foreach (var addon in addons)
            {
                addonLifecycle.UnregisterListener(AddonEvent.PostSetup, addon, OnAddonOpen);
                addonLifecycle.UnregisterListener(AddonEvent.PreFinalize, addon, OnTransientClose);
            }

        openAddons.Clear();
        log.Information("[XA] AddonWatcher disabled.");
    }

    private void OnAddonOpen(AddonEvent type, AddonArgs args)
    {
        var name = args.AddonName;
        openAddons.Add(name);
        log.Debug($"[XA] Addon opened: {name}");

        // Fire open callback for transient addons (e.g. Workshop → collect voyage data while panel is open)
        if (onAddonOpen != null && !PersistentAddonNames.Contains(name))
        {
            var category = AddonToCategory.GetValueOrDefault(name, "Unknown");
            try
            {
                onAddonOpen.Invoke(category, name, args.Addon);
            }
            catch (Exception ex)
            {
                log.Error($"[XA] AddonWatcher open callback error for {name}: {ex}");
            }
        }
    }

    private void OnPersistentClose(AddonEvent type, AddonArgs args)
    {
        openAddons.Remove(args.AddonName);
        log.Debug($"[XA] Persistent addon closed: {args.AddonName} (no auto-save)");
    }

    private void OnTransientClose(AddonEvent type, AddonArgs args)
    {
        var name = args.AddonName;
        openAddons.Remove(name);

        var category = AddonToCategory.GetValueOrDefault(name, "Unknown");
        log.Information($"[XA] Addon closed: {name} ({category}) — triggering save.");

        try
        {
            onAddonClose?.Invoke(category, name, args.Addon);
        }
        catch (Exception ex)
        {
            log.Error($"[XA] AddonWatcher callback error for {name}: {ex}");
        }
    }

    /// <summary>Returns the set of currently open tracked addon names.</summary>
    public IReadOnlyCollection<string> GetOpenAddons() => openAddons;

    /// <summary>Whether the addon is a persistent (always-loaded) type.</summary>
    public static bool IsPersistent(string addonName) => PersistentAddonNames.Contains(addonName);

    /// <summary>Persistent addon groups (display only, no save trigger).</summary>
    public static (string Category, string[] Addons)[] GetPersistentGroups() => PersistentGroups;

    /// <summary>Transient addon groups (trigger auto-save on close).</summary>
    public static (string Category, string[] Addons)[] GetTransientGroups() => TransientGroups;

    public void Dispose() => Disable();
}
