using System;
using System.Collections.Generic;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

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
    private Action<AddonTriggerEvent>? onAddonClose;
    private Action<AddonTriggerEvent>? onAddonOpen;

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
        ("Inventory", new[] { "Character", "RecommendEquip", "GearSetList" }),
        ("Retainer", new[] { "RetainerList", "InventoryRetainer", "InventoryRetainerLarge", "RetainerSellList", "RetainerCharacter", "Bank" }),
        ("Saddlebag", new[] { "InventoryBuddy" }),
        ("Journal", new[] { "Journal" }),
        ("Market", new[] { "ItemSearch", "ItemSearchResult", "ItemHistory" }),
        ("NPC", new[] { "Repair", "Shop", "LetterList", "LetterViewer" }),
        ("FC Chest", new[] { "FreeCompanyChest", "FreeCompanyChestLog" }),
        ("Armoire", new[] { "Cabinet" }),
        ("Armoury Chest", new[] { "ArmouryBoard" }),
        ("Glamour Dresser", new[] { "MiragePrismBox", "MiragePrismMiragePlate", "MiragePrismPrismBox" }),
        ("FC Members", new[] { "FreeCompany", "FreeCompanyProfile" }),
        ("Estate", new[] { "HousingSignBoard", "TeleportTown", "HousingSelectBlock", "HousingMenu",
            "HousingGoods", "HousingGoodsStain", "HousingEditExterior", "HousingInteriorPattern",
            "HousingSubmenu", "HousingSelectHouse", "HousingConfig", "HousingGuestBook",
            "OrchestrionPlayList", "OrchestrionPlayListEdit" }),
        ("Workshop", new[] { "SubmarinePartsMenu", "SubmarineExplorationMapSelect",
            "AirShipExploration", "AirShipExplorationDetail", "AirShipExplorationResult",
            "CompanyCraftSupply", "FreeCompanyCreditShop", "CompanyCraftRecipeNoteBook" }),
        ("Menu", new[] { "InputString" }),
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

    public void Enable(Action<AddonTriggerEvent>? onAddonClose = null, Action<AddonTriggerEvent>? onAddonOpen = null)
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
            var addonDetail = ResolveAddonDetail(name, args.Addon);
            try
            {
                onAddonOpen.Invoke(new AddonTriggerEvent
                {
                    Kind = AddonTriggerKind.Open,
                    Category = category,
                    AddonName = name,
                    AddonDetail = addonDetail,
                    AddonPtr = args.Addon,
                    IsPersistent = false,
                    TriggersSave = false,
                });
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
        var addonDetail = ResolveAddonDetail(name, args.Addon);
        log.Information($"[XA] Addon closed: {name} ({category}) — triggering save.");

        try
        {
            onAddonClose?.Invoke(new AddonTriggerEvent
            {
                Kind = AddonTriggerKind.Close,
                Category = category,
                AddonName = name,
                AddonDetail = addonDetail,
                AddonPtr = args.Addon,
                IsPersistent = false,
                TriggersSave = true,
            });
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

    private static unsafe string ResolveAddonDetail(string addonName, nint addonPtr)
    {
        if (addonPtr == nint.Zero)
            return addonName;

        try
        {
            var addon = (AtkUnitBase*)addonPtr;
            var texts = AddonTextReader.ReadAllText(addon);

            return addonName switch
            {
                "HousingMenu" => ResolveHousingMenuDetail(texts),
                "HousingSubmenu" => ResolveHousingSubmenuDetail(texts),
                "HousingSelectHouse" => ResolveHousingSelectHouseDetail(texts),
                _ => addonName,
            };
        }
        catch
        {
            return addonName;
        }
    }

    private static string ResolveHousingMenuDetail(List<(string Path, uint NodeId, string Text)> texts)
    {
        var count = texts.Count;
        return count switch
        {
            2 => "HousingMenuWorkshop",
            3 => "HousingMenuLobby",
            8 => "HousingMenuApartment",
            9 => "HousingMenuHouse",
            10 => "HousingMenuMain",
            _ => count > 0 ? $"HousingMenu({count})" : "HousingMenu",
        };
    }

    private static string ResolveHousingSubmenuDetail(List<(string Path, uint NodeId, string Text)> texts)
    {
        foreach (var (_, _, text) in texts)
        {
            if (text.Equals("Apartment Options", StringComparison.OrdinalIgnoreCase)
                || text.Equals("View Room Details", StringComparison.OrdinalIgnoreCase))
                return "HousingSubmenuApartment";
            if (text.Equals("Free Company Estate", StringComparison.OrdinalIgnoreCase))
                return "HousingSubmenuFreeCompany";
            if (text.Equals("Private Estate", StringComparison.OrdinalIgnoreCase))
                return "HousingSubmenuPersonal";
            if (text.Contains("Shared Estate", StringComparison.OrdinalIgnoreCase))
                return "HousingSubmenuShared";
        }

        return "HousingSubmenu";
    }

    private static string ResolveHousingSelectHouseDetail(List<(string Path, uint NodeId, string Text)> texts)
    {
        var optionCount = 0;
        foreach (var (_, _, text) in texts)
        {
            if (text.Equals("Apartment", StringComparison.OrdinalIgnoreCase)
                || text.Equals("Free Company Estate", StringComparison.OrdinalIgnoreCase)
                || text.Equals("Private Estate", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Shared Estate", StringComparison.OrdinalIgnoreCase))
            {
                optionCount++;
            }
        }

        return optionCount > 0 ? $"HousingSelectHouse({optionCount})" : "HousingSelectHouse";
    }

    public void Dispose() => Disable();
}
