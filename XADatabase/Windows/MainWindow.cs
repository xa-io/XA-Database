using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Lumina.Excel.Sheets;
using XADatabase.Collectors;
using XADatabase.Database;
using XADatabase.Models;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using XADatabase.Services;

namespace XADatabase.Windows;

public partial class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private const string PluginVersion = BuildInfo.Version;
    private const int MaxTaskLogEntries = 50;
    private const double HoldToConfirmSeconds = 3.0;

    // Cached collector results — refreshed on demand
    private List<CurrencyEntry> cachedCurrencies = new();
    private List<JobEntry> cachedJobs = new();
    private List<InventorySummary> cachedInventory = new();
    private List<ContainerItemEntry> cachedItems = new();
    private List<RetainerEntry> cachedRetainers = new();
    private List<RetainerListingEntry> cachedListings = new();
    private List<RetainerInventoryItem> cachedRetainerItems = new();
    private FreeCompanyEntry? cachedFc;
    private List<FcMemberEntry> cachedFcMembers = new();
    private SquadronInfo? cachedSquadron;
    private VoyageInfo? cachedVoyages;
    private List<CollectionSummary> cachedCollections = new();
    private List<ActiveQuestEntry> cachedQuests = new();
    private List<MsqMilestoneEntry> cachedMsqMilestones = new();
    private string cachedPersonalEstate = string.Empty;
    private string cachedSharedEstates = string.Empty;
    private string cachedApartment = string.Empty;
    public bool DataCollected { get; private set; }
    private bool charListQueried;

    // Character selector for viewing alt data from DB
    private List<CharacterRow> knownCharacters = new();
    private int selectedCharacterIndex = -1;
    private ulong? viewingContentId;
    private string viewingCharName = string.Empty;
    private string charSelectorSearch = string.Empty;

    // Item search state
    private string itemSearchText = string.Empty;
    private List<ItemLocationResult> itemSearchResults = new();

    // Retainers tab: show all retainers with collapsible sections
    private bool showAllRetainers;

    // Last data refresh timestamp
    private DateTime lastRefreshTime = DateTime.MinValue;

    // Export status
    private string exportStatusMessage = string.Empty;
    private DateTime exportStatusExpiry = DateTime.MinValue;

    // Settings housing maintenance status
    private string settingsHousingStatus = string.Empty;
    private DateTime settingsHousingStatusExpiry = DateTime.MinValue;

    // Snapshot migration + task logging
    private bool taskLogEnabled;
    private readonly List<string> taskLogEntries = new();
    private SaveSnapshotResult? lastSnapshotResult;
    private string migrationStatusMessage = string.Empty;
    private DateTime migrationStatusExpiry = DateTime.MinValue;
    private DateTime? importLegacyHoldStartedAtUtc;
    private DateTime? clearAllHoldStartedAtUtc;
    private bool importLegacyHoldTriggered;
    private bool clearAllHoldTriggered;
    private bool legacyMigrationPending;
    private int legacyCharacterCount;
    private int xaCharacterSnapshotCount;

    public MainWindow(Plugin plugin)
        : base("XA Database##MainWindow", ImGuiWindowFlags.None)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(600, 450),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        this.plugin = plugin;
        RefreshMigrationState();
    }

    public void Dispose() { }

    // ───────────────────────────────────────────────
    //  IPC Data Accessors — called by IpcProvider via Plugin.cs
    // ───────────────────────────────────────────────

    /// <summary>Returns current character name, or empty if not loaded.</summary>
    public string GetCharacterName()
    {
        try { return Plugin.PlayerState.IsLoaded ? Plugin.PlayerState.CharacterName.ToString() : string.Empty; }
        catch { return string.Empty; }
    }

    /// <summary>Returns current character's gil from cached currencies.</summary>
    public int GetGil()
    {
        var gil = cachedCurrencies.Find(c => c.Name == "Gil");
        return gil != null ? (int)gil.Amount : 0;
    }

    /// <summary>Returns total gil across all cached retainers.</summary>
    public int GetRetainerGil()
    {
        return cachedRetainers.Count > 0 ? (int)cachedRetainers.Sum(r => r.Gil) : 0;
    }

    public string GetFcName()
    {
        return cachedFc?.Name ?? string.Empty;
    }

    public string GetFcTag()
    {
        return cachedFc?.Tag ?? string.Empty;
    }

    public int GetFcPoints()
    {
        return cachedFc?.FcPoints ?? 0;
    }

    /// <summary>Returns FC info as pipe-delimited string: Name|Tag|Points|Rank. Empty if no FC.</summary>
    public string GetFcInfo()
    {
        if (cachedFc == null) return string.Empty;
        return $"{cachedFc.Name}|{cachedFc.Tag}|{cachedFc.FcPoints}|{cachedFc.Rank}";
    }

    /// <summary>Returns FC estate info (plot location string). Empty if none.</summary>
    public string GetPlotInfo()
    {
        if (cachedFc == null || string.IsNullOrEmpty(cachedFc.Estate)) return string.Empty;
        return cachedFc.Estate;
    }

    /// <summary>Returns personal estate and apartment as pipe-delimited: Estate|Apartment.</summary>
    public string GetPersonalPlotInfo()
    {
        return $"{cachedPersonalEstate}|{cachedApartment}";
    }

    public string GetApartment()
    {
        return cachedApartment;
    }

    public string GetCharacterSummaryJson()
    {
        try
        {
            var contentId = viewingContentId ?? (Plugin.PlayerState.IsLoaded ? Plugin.PlayerState.ContentId : 0UL);
            var characterName = viewingContentId.HasValue
                ? viewingCharName
                : GetCharacterName();
            var summary = new
            {
                ipcContractVersion = IpcContractInfo.CurrentVersion,
                characterSummaryVersion = IpcContractInfo.CharacterSummaryJsonVersion,
                version = BuildInfo.Version,
                contentId,
                characterName,
                gil = GetGil(),
                retainerGil = GetRetainerGil(),
                retainerCount = cachedRetainers.Count,
                fcName = GetFcName(),
                fcTag = GetFcTag(),
                fcPoints = GetFcPoints(),
                fcEstate = GetPlotInfo(),
                personalEstate = cachedPersonalEstate,
                apartment = cachedApartment,
                trigger = lastSnapshotResult?.Trigger.ToString() ?? string.Empty,
                triggerDetail = lastSnapshotResult?.TriggerDetail ?? string.Empty,
                lastSnapshotAtUtc = lastSnapshotResult?.SavedAtUtc ?? string.Empty,
                snapshotQuality = lastSnapshotResult?.Quality ?? GetSnapshotQualityLabel(),
                warnings = lastSnapshotResult?.Warnings ?? new List<string>()
            };
            return JsonSerializer.Serialize(summary);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[XA] IPC GetCharacterSummaryJson error: {ex}");
            return string.Empty;
        }
    }

    public string GetLastSnapshotResultJson()
    {
        try
        {
            return lastSnapshotResult != null ? JsonSerializer.Serialize(lastSnapshotResult) : string.Empty;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[XA] IPC GetLastSnapshotResultJson error: {ex}");
            return string.Empty;
        }
    }

    /// <summary>Cross-character item search by name. Returns pipe-delimited results (one per line).</summary>
    public string SearchItems(string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2) return string.Empty;
        try
        {
            var results = SearchSnapshotItemsByName(query);
            if (results.Count == 0) return string.Empty;
            return string.Join("\n", results.Select(r =>
                $"{r.CharacterName}|{r.World}|{r.ContainerName}|{r.ItemName}|{r.ItemId}|{r.Quantity}|{r.IsHq}"));
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[XA] IPC SearchItems error: {ex}");
            return string.Empty;
        }
    }

    /// <summary>
    /// Seed all cached data from DB for the current character so the UI shows
    /// persisted data immediately (retainers, FC, inventory, etc.) without
    /// requiring in-game window visits first.
    /// </summary>
    private void SeedFromDatabase()
    {
        var playerState = Plugin.PlayerState;
        if (!playerState.IsLoaded)
            return;

        try
        {
            var contentId = playerState.ContentId;
            var snapshot = plugin.SnapshotRepo.GetSnapshot(contentId);
            if (snapshot != null)
                ApplySnapshotToCache(snapshot);

            Plugin.Log.Information("[XA] Seeded cached data from database for current character.");
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[XA] Error seeding data from DB: {ex}");
        }
    }

    public void RefreshData()
    {
        var playerState = Plugin.PlayerState;
        if (!playerState.IsLoaded)
            return;

        var localPlayer = Plugin.ObjectTable.LocalPlayer;
        var isOnHomeworld = localPlayer == null || localPlayer.CurrentWorld.RowId == localPlayer.HomeWorld.RowId;
        var hasReliableLiveCharacterContext = localPlayer != null
            && !Plugin.Condition[ConditionFlag.BetweenAreas]
            && !Plugin.Condition[ConditionFlag.BetweenAreas51];

        try
        {
            cachedCurrencies = CurrencyCollector.Collect(Plugin.DataManager);
            cachedJobs = JobCollector.Collect(Plugin.PlayerState, Plugin.DataManager);
            cachedInventory = InventoryCollector.Collect();
            var itemCollection = ItemCollector.Collect(Plugin.DataManager);
            lastLiveCollectedItems = itemCollection.Items.Select(CloneContainerItem).ToList();
            lastLoadedItemContainers.Clear();
            foreach (var containerName in itemCollection.LoadedContainers)
                lastLoadedItemContainers.Add(containerName);
            cachedItems = itemCollection.Items;
            // Retainers: only overwrite if live collector returns data (requires summoning bell)
            var freshRetainers = RetainerCollector.CollectRetainerList();
            if (freshRetainers.Count > 0)
                cachedRetainers = freshRetainers;
            MergeActiveRetainerListings();
            MergeActiveRetainerInventory();
            var freshMembers = FcMemberCollector.Collect(Plugin.DataManager);
            // Try reading from currently-open FC/Estate addons before Collect()
            FreeCompanyCollector.TryCollectFromOpenAddons();
            cachedFc = FreeCompanyCollector.Collect();
            var freshSquadron = SquadronCollector.Collect(Plugin.DataManager);
            if (freshSquadron != null)
                cachedSquadron = freshSquadron;

            // Collect voyage data (only available inside FC workshop)
            var freshVoyages = VoyageCollector.Collect();
            if (freshVoyages != null)
                cachedVoyages = freshVoyages;

            // Only replace cached members if the collector returned data
            // (the proxy is empty when FC member list window hasn't been opened)
            if (freshMembers.Count > 0)
                cachedFcMembers = freshMembers;

            // Apply rank labels to FC members
            // Priority: 1) Addon-read name→rank mapping, 2) Sort-based dict, 3) fallback labels
            if (cachedFcMembers.Count > 0)
            {
                var addonRanks = FreeCompanyCollector.LastAddonMemberRanks;
                var sortRanks = FreeCompanyCollector.LastCollectedRankNames;
                foreach (var m in cachedFcMembers)
                {
                    // Try addon name→rank first (most reliable — read directly from member list UI)
                    if (addonRanks.TryGetValue(m.Name, out var addonRank) && addonRank.Length > 0)
                        m.RankName = addonRank;
                    else if (sortRanks.TryGetValue(m.RankSort, out var rn) && rn.Length > 0)
                        m.RankName = rn;
                    else
                        m.RankName = m.RankSort == 0 ? "Master" : $"Rank {m.RankSort + 1}";
                }
            }
            cachedCollections = CollectionCollector.Collect(Plugin.DataManager);
            cachedQuests = QuestCollector.CollectActiveQuests(Plugin.DataManager);
            cachedMsqMilestones = QuestCollector.CollectMsqProgress();
            var housing = HousingCollector.CollectPersonalHousing();
            cachedPersonalEstate = housing.PersonalEstate;
            cachedSharedEstates = housing.SharedEstates;
            cachedApartment = housing.Apartment;

            var persistedSnapshot = plugin.SnapshotRepo.GetSnapshot(playerState.ContentId);

            if (persistedSnapshot == null && freshRetainers.Count == 0)
            {
                cachedRetainers.Clear();
                cachedListings.Clear();
                cachedRetainerItems.Clear();
            }

            ApplyPersistedSaddlebagState(persistedSnapshot, allowObservedSaddlebagClear: false);
            ApplyPersistedSaddlebagInventorySummaries(persistedSnapshot, allowObservedSaddlebagClear: false);

            if (string.IsNullOrEmpty(cachedPersonalEstate) && !hasReliableLiveCharacterContext && persistedSnapshot != null)
                cachedPersonalEstate = persistedSnapshot.Row.PersonalEstate;
            if (string.IsNullOrEmpty(cachedSharedEstates) && persistedSnapshot != null)
                cachedSharedEstates = persistedSnapshot.Row.SharedEstates;
            if (string.IsNullOrEmpty(cachedApartment) && !hasReliableLiveCharacterContext && persistedSnapshot != null)
                cachedApartment = persistedSnapshot.Row.Apartment;
            if (persistedSnapshot != null)
                cachedPersonalEstate = XaCharacterSnapshotRepository.PreferSizedPersonalEstateValue(cachedPersonalEstate, persistedSnapshot.Row.PersonalEstate);

            if (cachedFc == null)
            {
                if (persistedSnapshot != null && (!isOnHomeworld || !hasReliableLiveCharacterContext) && persistedSnapshot.FreeCompany != null)
                {
                    cachedFc = new FreeCompanyEntry
                    {
                        FcId = persistedSnapshot.FreeCompany.FcId,
                        Name = persistedSnapshot.FreeCompany.Name,
                        Tag = persistedSnapshot.FreeCompany.Tag,
                        Master = persistedSnapshot.FreeCompany.Master,
                        Rank = persistedSnapshot.FreeCompany.Rank,
                        GrandCompany = persistedSnapshot.FreeCompany.GrandCompany,
                        GrandCompanyName = persistedSnapshot.FreeCompany.GrandCompanyName,
                        OnlineMembers = persistedSnapshot.FreeCompany.OnlineMembers,
                        TotalMembers = persistedSnapshot.FreeCompany.TotalMembers,
                        HomeWorldId = persistedSnapshot.FreeCompany.HomeWorldId,
                        FcPoints = persistedSnapshot.FreeCompany.FcPoints,
                        Estate = persistedSnapshot.FreeCompany.Estate,
                    };
                }
                else if (isOnHomeworld && hasReliableLiveCharacterContext)
                {
                    ClearPersistedFreeCompanyState();
                }
            }
            else if (persistedSnapshot?.FreeCompany != null)
            {
                var persistedFc = persistedSnapshot.FreeCompany;
                if (cachedFc.FcId == 0 && persistedFc.FcId != 0)
                    cachedFc.FcId = persistedFc.FcId;
                if (string.IsNullOrEmpty(cachedFc.Name) && !string.IsNullOrEmpty(persistedFc.Name))
                    cachedFc.Name = persistedFc.Name;
                if (string.IsNullOrEmpty(cachedFc.Tag) && !string.IsNullOrEmpty(persistedFc.Tag))
                    cachedFc.Tag = persistedFc.Tag;
                if (string.IsNullOrEmpty(cachedFc.Master) && !string.IsNullOrEmpty(persistedFc.Master))
                    cachedFc.Master = persistedFc.Master;
                if (cachedFc.Rank == 0 && persistedFc.Rank > 0)
                    cachedFc.Rank = persistedFc.Rank;
                if (cachedFc.GrandCompany == 0 && persistedFc.GrandCompany > 0)
                    cachedFc.GrandCompany = persistedFc.GrandCompany;
                if (string.IsNullOrEmpty(cachedFc.GrandCompanyName) && !string.IsNullOrEmpty(persistedFc.GrandCompanyName))
                    cachedFc.GrandCompanyName = persistedFc.GrandCompanyName;
                if (cachedFc.TotalMembers == 0 && persistedFc.TotalMembers > 0)
                    cachedFc.TotalMembers = persistedFc.TotalMembers;
                if (cachedFc.HomeWorldId == 0 && persistedFc.HomeWorldId > 0)
                    cachedFc.HomeWorldId = persistedFc.HomeWorldId;
                if (cachedFc.FcPoints == 0 && persistedFc.FcPoints > 0)
                    cachedFc.FcPoints = persistedFc.FcPoints;
                if (string.IsNullOrEmpty(cachedFc.Estate) && !string.IsNullOrEmpty(persistedFc.Estate))
                    cachedFc.Estate = persistedFc.Estate;
            }

            if (cachedFc != null)
            {
                FreeCompanyCollector.SeedPersistedValues(cachedFc.FcPoints, cachedFc.Estate, cachedFc.Name, cachedFc.Tag, cachedFc.Rank);
                if (cachedFc.FcPoints == 0 && FreeCompanyCollector.LastFcPoints > 0)
                    cachedFc.FcPoints = FreeCompanyCollector.LastFcPoints;
                if (cachedFc.Rank == 0 && FreeCompanyCollector.LastFcRank > 0)
                    cachedFc.Rank = FreeCompanyCollector.LastFcRank;
                if (string.IsNullOrEmpty(cachedFc.Estate) && !string.IsNullOrEmpty(FreeCompanyCollector.LastEstate))
                    cachedFc.Estate = FreeCompanyCollector.LastEstate;
                if (string.IsNullOrEmpty(cachedFc.Name) && !string.IsNullOrEmpty(FreeCompanyCollector.LastFcName))
                    cachedFc.Name = FreeCompanyCollector.LastFcName;
                if (string.IsNullOrEmpty(cachedFc.Tag) && !string.IsNullOrEmpty(FreeCompanyCollector.LastFcTag))
                    cachedFc.Tag = FreeCompanyCollector.LastFcTag;
            }

            if (cachedFc != null && cachedFcMembers.Count == 0 && persistedSnapshot != null && persistedSnapshot.FcMembers.Count > 0)
            {
                cachedFcMembers = persistedSnapshot.FcMembers.Select(member => new FcMemberEntry
                {
                    ContentId = member.ContentId,
                    Name = member.Name,
                    Job = member.Job,
                    JobName = member.JobName,
                    OnlineStatus = member.OnlineStatus,
                    CurrentWorld = member.CurrentWorld,
                    CurrentWorldName = member.CurrentWorldName,
                    HomeWorld = member.HomeWorld,
                    HomeWorldName = member.HomeWorldName,
                    GrandCompany = member.GrandCompany,
                    RankSort = member.RankSort,
                    RankName = member.RankName,
                }).ToList();
            }

            // Load persisted squadron data from DB if not in barracks
            if (cachedSquadron == null && persistedSnapshot != null)
                cachedSquadron = persistedSnapshot.Squadron;

            // Load persisted voyage data from DB if not in workshop
            if (cachedVoyages == null && cachedFc != null && persistedSnapshot != null)
                cachedVoyages = persistedSnapshot.Voyages;

            // Personal housing: no DB fallback needed here — GetOwnedHouseId works from anywhere.
            // The collector already validates sentinel data, so empty = character doesn't own that type.
            // Stale DB data is cleared on save.

            NormalizeCachedRetainerState();

            lastRefreshTime = DateTime.UtcNow;
            DataCollected = true;

            // Reset character selector to current character
            viewingContentId = null;
            selectedCharacterIndex = -1;
            viewingCharName = string.Empty;

            Plugin.Log.Information("[XA] Data refreshed successfully.");
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[XA] Error refreshing data: {ex}");
        }
    }

    public SaveSnapshotResult RefreshAndSave(SnapshotTrigger trigger = SnapshotTrigger.Manual, string triggerDetail = "Manual refresh")
    {
        RefreshData();
        return SaveToDatabase(trigger, triggerDetail);
    }

    /// <summary>
    /// Called by AddonWatcher when a tracked game window opens.
    /// For Workshop: collects voyage data immediately (only available while panel is open).
    /// </summary>
    public void OnAddonOpenTrigger(AddonTriggerEvent trigger)
    {
        if (!Plugin.PlayerState.IsLoaded || viewingContentId.HasValue)
            return;

        lastAddonTrigger = trigger;
        if (trigger.Category == "Estate")
            HousingCollector.ObserveEstateAddonDetail(trigger.AddonName, trigger.AddonDetail);
        AddTaskLog($"[XA.DB TASK] Addon opened: {trigger.Category} / {trigger.AddonName}");

        if (trigger.Category != "Workshop")
            return;

        try
        {
            Plugin.Log.Information("[XA] Workshop addon opened — collecting voyage data.");
            var freshVoyages = VoyageCollector.Collect();
            if (freshVoyages != null)
                cachedVoyages = freshVoyages;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[XA] Workshop open collect error: {ex}");
            QueueCollectorWarning("Addon watcher failed to collect workshop voyage data while the workshop window was open.");
        }
    }

    /// <summary>
    /// Called by AddonWatcher when a tracked game window closes (PreFinalize).
    /// Uses the addon pointer directly (GetAddonByName fails at PreFinalize).
    /// Reads addon text nodes at close time when data is fully populated,
    /// then triggers refresh+save.
    /// </summary>
    public void OnAddonSaveTrigger(AddonTriggerEvent trigger)
    {
        if (!Plugin.PlayerState.IsLoaded || viewingContentId.HasValue)
            return;

        lastAddonTrigger = trigger;
        if (trigger.Category == "Estate")
            HousingCollector.ObserveEstateAddonDetail(trigger.AddonName, trigger.AddonDetail);
        string closeCollectorFailure = string.Empty;

        // Read addon text at close time — data is fully populated by now.
        // Must use the event's addon pointer directly; GetAddonByName returns null at PreFinalize.
        try
        {
            switch (trigger.Category)
            {
                case "FC Members" when trigger.AddonName == "FreeCompany":
                    Plugin.Log.Information("[XA] FreeCompany closing — reading FC points via pointer.");
                    FreeCompanyCollector.CollectFromAddon(trigger.AddonPtr);
                    break;

                case "Estate" when trigger.AddonName == "HousingSignBoard":
                    Plugin.Log.Information("[XA] HousingSignBoard closing — reading housing info via pointer.");
                    HousingCollector.CollectFromAddon(trigger.AddonPtr);
                    break;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[XA] Addon close collect error ({trigger.Category}/{trigger.AddonName}): {ex}");
            closeCollectorFailure = $"Addon watcher failed to collect data while closing {trigger.AddonName}.";
        }

        if (!plugin.Configuration.AddonWatcherEnabled)
            return;

        if (!string.IsNullOrWhiteSpace(closeCollectorFailure))
            QueueCollectorWarning(closeCollectorFailure);

        AddTaskLog($"[XA.DB TASK] Addon close trigger: {trigger.Category} / {trigger.AddonName}");
        Plugin.Log.Information($"[XA] Addon trigger ({trigger.Category}) — refreshing and saving.");
        RefreshAndSave(SnapshotTrigger.AddonWatcher, trigger.TriggerDetail);
    }

    public SaveSnapshotResult SaveToDatabase(SnapshotTrigger trigger = SnapshotTrigger.Manual, string triggerDetail = "Manual save")
    {
        var playerState = Plugin.PlayerState;
        var fallbackResult = new SaveSnapshotResult
        {
            Success = false,
            Trigger = trigger,
            TriggerDetail = triggerDetail,
            SavedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            Summary = "Snapshot save skipped."
        };

        if (!playerState.IsLoaded || !DataCollected)
        {
            fallbackResult.Summary = "Snapshot save skipped because live data is not ready.";
            fallbackResult.Quality = GetSnapshotQualityLabel(fallbackResult);
            lastSnapshotResult = fallbackResult;
            AddSaveHistoryEntry(fallbackResult);
            return fallbackResult;
        }

        if (viewingContentId.HasValue)
        {
            fallbackResult.Summary = "Snapshot save skipped because a stored character view is active.";
            fallbackResult.Warnings.Add("Switch back to Current Character (Live) before saving.");
            fallbackResult.Quality = GetSnapshotQualityLabel(fallbackResult);
            lastSnapshotResult = fallbackResult;
            AddSaveHistoryEntry(fallbackResult);
            return fallbackResult;
        }

        try
        {
            var contentId = playerState.ContentId;
            var name = playerState.CharacterName.ToString();
            var savedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            var localPlayer = Plugin.ObjectTable.LocalPlayer;
            var (world, datacenter, region) = ResolveStableWorldAndDatacenter(contentId);
            var isOnHomeworld = localPlayer == null || localPlayer.CurrentWorld.RowId == localPlayer.HomeWorld.RowId;
            var hasReliableLiveCharacterContext = localPlayer != null
                && !Plugin.Condition[ConditionFlag.BetweenAreas]
                && !Plugin.Condition[ConditionFlag.BetweenAreas51];
            var persistedSnapshot = plugin.SnapshotRepo.GetSnapshot(contentId);
            if (string.IsNullOrEmpty(cachedPersonalEstate) && !hasReliableLiveCharacterContext && persistedSnapshot != null)
                cachedPersonalEstate = persistedSnapshot.Row.PersonalEstate;
            if (string.IsNullOrEmpty(cachedSharedEstates) && persistedSnapshot != null)
                cachedSharedEstates = persistedSnapshot.Row.SharedEstates;
            if (string.IsNullOrEmpty(cachedApartment) && !hasReliableLiveCharacterContext && persistedSnapshot != null)
                cachedApartment = persistedSnapshot.Row.Apartment;
            if (persistedSnapshot != null)
                cachedPersonalEstate = XaCharacterSnapshotRepository.PreferSizedPersonalEstateValue(cachedPersonalEstate, persistedSnapshot.Row.PersonalEstate);
            if (cachedFc == null && persistedSnapshot?.FreeCompany != null && (!isOnHomeworld || !hasReliableLiveCharacterContext))
                cachedFc = persistedSnapshot.FreeCompany;
            if (cachedFc != null && cachedFcMembers.Count == 0 && persistedSnapshot != null && persistedSnapshot.FcMembers.Count > 0)
                cachedFcMembers = persistedSnapshot.FcMembers;
            if (isOnHomeworld && cachedFc == null && hasReliableLiveCharacterContext)
                ClearPersistedFreeCompanyState();
            ApplyPersistedSaddlebagState(persistedSnapshot, IsSaddlebagClearConfirmed(trigger));
            ApplyPersistedSaddlebagInventorySummaries(persistedSnapshot, IsSaddlebagClearConfirmed(trigger));
            var validation = BuildValidationSummary(isOnHomeworld);
            AppendWarnings(validation.Warnings, ConsumePendingCollectorWarnings());
            var validationJson = JsonSerializer.Serialize(validation);
            var freshnessJson = JsonSerializer.Serialize(new
            {
                savedAtUtc,
                lastRefreshUtc = lastRefreshTime > DateTime.MinValue ? lastRefreshTime.ToString("yyyy-MM-dd HH:mm:ss") : string.Empty,
                trigger = trigger.ToString(),
                isOnHomeworld,
                dataCollected = DataCollected,
                viewingStoredCharacter = viewingContentId.HasValue
            });
            MergeActiveRetainerListings();
            MergeActiveRetainerInventory();
            NormalizeCachedRetainerState();
            var retainerGil = GetRetainerGil();
            var gil = GetGil();
            var sections = XaCharacterSnapshotRepository.BuildSections(
                contentId,
                name,
                world,
                datacenter,
                region,
                cachedPersonalEstate,
                cachedSharedEstates,
                cachedApartment,
                gil,
                retainerGil,
                cachedCurrencies,
                cachedJobs,
                cachedInventory,
                cachedItems,
                cachedRetainers,
                cachedListings,
                cachedRetainerItems,
                cachedFc,
                cachedFcMembers,
                cachedSquadron,
                cachedVoyages,
                cachedCollections,
                cachedQuests,
                cachedMsqMilestones,
                validationJson);
            var normalizedRetainers = XaCharacterSnapshotRepository.NormalizeRetainerPayload(cachedRetainers, cachedListings, cachedRetainerItems);

            plugin.DatabaseService.BeginTransaction();
            try
            {
                plugin.DatabaseService.UpsertXaCharacterSnapshot(
                    contentId,
                    name,
                    world,
                    datacenter,
                    region,
                    cachedFc?.FcId ?? 0,
                    cachedFc?.Name ?? string.Empty,
                    cachedFc?.Tag ?? string.Empty,
                    cachedFc?.FcPoints ?? 0,
                    cachedFc?.Estate ?? string.Empty,
                    cachedPersonalEstate,
                    cachedSharedEstates,
                    cachedApartment,
                    gil,
                    retainerGil,
                    normalizedRetainers.Retainers.Count,
                    XaCharacterSnapshotRepository.GetHighestJobLevel(cachedJobs),
                    JsonSerializer.Serialize(normalizedRetainers.Retainers.Select(r => r.RetainerId).Distinct()),
                    freshnessJson,
                    sections,
                    1,
                    savedAtUtc,
                    trigger.ToString(),
                    triggerDetail,
                    false,
                    savedAtUtc);
                plugin.DatabaseService.CommitTransaction();
            }
            catch
            {
                plugin.DatabaseService.RollbackTransaction();
                throw;
            }

            // Refresh known characters list (after commit)
            knownCharacters = plugin.CharacterRepo.GetAll();
            InvalidateDashboardSnapshotCache();
            var savedSnapshot = plugin.SnapshotRepo.GetSnapshot(contentId);
            if (savedSnapshot != null)
                ApplySnapshotToCache(savedSnapshot);

            var result = new SaveSnapshotResult
            {
                Success = true,
                ContentId = contentId,
                CharacterName = name,
                HomeWorld = world,
                Trigger = trigger,
                TriggerDetail = triggerDetail,
                SavedAtUtc = savedAtUtc,
                Gil = gil,
                RetainerGil = retainerGil,
                RetainerCount = cachedRetainers.Count,
                SavedFreeCompany = cachedFc != null,
                SavedVoyages = cachedVoyages != null,
                Summary = $"Saved snapshot for {name} @ {world}",
                Warnings = validation.Warnings,
                Quality = string.Empty
            };
            result.Quality = GetSnapshotQualityLabel(result);
            lastSnapshotResult = result;
            RefreshMigrationState();

            Plugin.Log.Information($"[XA] Saved snapshot for {name} @ {world} to database.");
            AddTaskLog($"[XA.DB TASK] Saved snapshot for {name} @ {world} via {trigger} ({result.Quality}).");
            AddSaveHistoryEntry(result);

            // Echo notification in chat if enabled
            if (plugin.Configuration.EchoOnSave)
            {
                try
                {
                    Plugin.ChatGui.Print(new XivChatEntry
                    {
                        Type = XivChatType.Echo,
                        Message = new SeString(new TextPayload("[XA] Database has been saved.")),
                    });
                }
                catch { /* silently ignore if chat fails */ }
            }

            return result;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[XA] Error saving to database: {ex}");
            var result = new SaveSnapshotResult
            {
                Success = false,
                Trigger = trigger,
                TriggerDetail = triggerDetail,
                SavedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                Summary = $"Snapshot save failed: {ex.Message}",
                Warnings = new List<string> { ex.Message },
                Quality = string.Empty
            };
            result.Quality = GetSnapshotQualityLabel(result);
            lastSnapshotResult = result;
            AddTaskLog($"[XA.DB TASK] Save failed: {ex.Message}");
            AddSaveHistoryEntry(result);
            return result;
        }
    }

    private CollectorValidationSummary BuildValidationSummary(bool isOnHomeworld)
    {
        return new CollectorValidationSummary
        {
            InventoryCollected = cachedInventory.Count > 0 || cachedItems.Count > 0,
            RetainersCollected = cachedRetainers.Count > 0,
            FreeCompanyCollected = cachedFc != null && isOnHomeworld,
            VoyagesCollected = cachedVoyages != null,
            CollectionsCollected = cachedCollections.Count > 0,
            QuestsCollected = cachedQuests.Count > 0 || cachedMsqMilestones.Count > 0,
        };
    }

    private string BuildCharacterSnapshotJson(
        string characterName,
        string world,
        string datacenter,
        string savedAtUtc,
        bool importedFromLegacy,
        SnapshotTrigger trigger,
        string triggerDetail,
        CollectorValidationSummary validation)
    {
        var snapshot = new
        {
            snapshotVersion = 1,
            exportedUtc = savedAtUtc,
            importedFromLegacy,
            trigger = trigger.ToString(),
            triggerDetail,
            character = new
            {
                contentId = viewingContentId ?? (Plugin.PlayerState.IsLoaded ? Plugin.PlayerState.ContentId : 0UL),
                name = characterName,
                world,
                datacenter,
                personalEstate = cachedPersonalEstate,
                sharedEstates = cachedSharedEstates,
                apartment = cachedApartment,
                gil = GetGil(),
                retainerGil = GetRetainerGil(),
            },
            freeCompany = cachedFc,
            fcMembers = cachedFcMembers,
            currencies = cachedCurrencies,
            jobs = cachedJobs,
            inventory = cachedInventory,
            items = cachedItems,
            retainers = cachedRetainers,
            listings = cachedListings,
            retainerItems = cachedRetainerItems,
            squadron = cachedSquadron,
            voyages = cachedVoyages,
            collections = cachedCollections,
            activeQuests = cachedQuests,
            msqMilestones = cachedMsqMilestones,
            validation,
        };

        return JsonSerializer.Serialize(snapshot);
    }

    private void AddTaskLog(string message)
    {
        if (!taskLogEnabled)
            return;

        taskLogEntries.Insert(0, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}");
        if (taskLogEntries.Count > MaxTaskLogEntries)
            taskLogEntries.RemoveRange(MaxTaskLogEntries, taskLogEntries.Count - MaxTaskLogEntries);
    }

    private void SetMigrationStatus(string message)
    {
        migrationStatusMessage = message;
        migrationStatusExpiry = DateTime.UtcNow.AddSeconds(10);
    }

    private void RefreshMigrationState()
    {
        legacyCharacterCount = plugin.DatabaseService.GetLegacyCharacterCount();
        xaCharacterSnapshotCount = plugin.DatabaseService.GetXaCharacterCount();
        legacyMigrationPending = plugin.DatabaseService.HasLegacyDataPendingMigration();
    }

    private void ImportLegacyTablesToXaCharacters()
    {
        try
        {
            var characters = plugin.CharacterRepo.GetAllLegacy();
            if (characters.Count == 0)
            {
                SetMigrationStatus("No legacy character rows were found to import.");
                return;
            }

            plugin.DatabaseService.BeginTransaction();
            try
            {
                foreach (var character in characters)
                {
                    var currencies = plugin.CurrencyRepo.GetLatest(character.ContentId);
                    var jobs = plugin.JobRepo.GetLatest(character.ContentId);
                    var inventory = plugin.InventoryRepo.GetLatest(character.ContentId);
                    var items = plugin.ContainerItemRepo.GetAll(character.ContentId);
                    var retainers = plugin.RetainerRepo.GetRetainers(character.ContentId);
                    var listings = plugin.RetainerRepo.GetAllListings(character.ContentId);
                    var retainerItems = plugin.RetainerRepo.GetAllRetainerItems(character.ContentId);
                    var fc = plugin.FcRepo.GetForCharacter(character.ContentId);
                    var fcMembers = fc != null ? plugin.FcMemberRepo.GetMembers(fc.FcId) : new List<FcMemberEntry>();
                    var squadron = plugin.SquadronRepo.GetForCharacter(character.ContentId);
                    var voyages = fc != null ? plugin.VoyageRepo.GetForFc(fc.FcId) : null;
                    var collections = plugin.CollectionRepo.GetLatest(character.ContentId);
                    var quests = plugin.CollectionRepo.GetQuests(character.ContentId);
                    var msq = plugin.CollectionRepo.GetMsqMilestones(character.ContentId);
                    var gil = currencies.Find(c => c.Name == "Gil")?.Amount ?? 0;
                    var retainerGil = retainers.Sum(r => (long)r.Gil);
                    var safeGil = (int)Math.Min(int.MaxValue, gil);
                    var safeRetainerGil = (int)Math.Min(int.MaxValue, retainerGil);
                    var exportedUtc = string.IsNullOrWhiteSpace(character.LastSeenUtc) ? DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") : character.LastSeenUtc;
                    var validation = new CollectorValidationSummary
                    {
                        InventoryCollected = inventory.Count > 0 || items.Count > 0,
                        RetainersCollected = retainers.Count > 0,
                        FreeCompanyCollected = fc != null,
                        VoyagesCollected = voyages != null,
                        CollectionsCollected = collections.Count > 0,
                        QuestsCollected = quests.Count > 0 || msq.Count > 0,
                    };
                    if (fc == null)
                        validation.Warnings.Add("Free Company data unavailable in legacy tables.");

                    var validationJson = JsonSerializer.Serialize(validation);
                    var resolvedDatacenter = XaCharacterSnapshotRepository.ResolveDatacenter(character.World, character.Datacenter);
                    var resolvedRegion = XaCharacterSnapshotRepository.ResolveRegion(character.World, character.Region);
                    var sections = XaCharacterSnapshotRepository.BuildSections(
                        character.ContentId,
                        character.Name,
                        character.World,
                        resolvedDatacenter,
                        resolvedRegion,
                        character.PersonalEstate,
                        character.SharedEstates,
                        character.Apartment,
                        safeGil,
                        safeRetainerGil,
                        currencies,
                        jobs,
                        inventory,
                        items,
                        retainers,
                        listings,
                        retainerItems,
                        fc,
                        fcMembers,
                        squadron,
                        voyages,
                        collections,
                        quests,
                        msq,
                        validationJson);
                    var normalizedRetainers = XaCharacterSnapshotRepository.NormalizeRetainerPayload(retainers, listings, retainerItems);

                    plugin.DatabaseService.UpsertXaCharacterSnapshot(
                        character.ContentId,
                        character.Name,
                        character.World,
                        resolvedDatacenter,
                        resolvedRegion,
                        fc?.FcId ?? 0,
                        fc?.Name ?? string.Empty,
                        fc?.Tag ?? string.Empty,
                        fc?.FcPoints ?? 0,
                        fc?.Estate ?? string.Empty,
                        character.PersonalEstate,
                        character.SharedEstates,
                        character.Apartment,
                        safeGil,
                        safeRetainerGil,
                        normalizedRetainers.Retainers.Count,
                        XaCharacterSnapshotRepository.GetHighestJobLevel(jobs),
                        JsonSerializer.Serialize(normalizedRetainers.Retainers.Select(r => r.RetainerId).Distinct()),
                        JsonSerializer.Serialize(new
                        {
                            savedAtUtc = exportedUtc,
                            trigger = SnapshotTrigger.LegacyImport.ToString(),
                            importedFromLegacy = true
                        }),
                        sections,
                        1,
                        exportedUtc,
                        SnapshotTrigger.LegacyImport.ToString(),
                        "Legacy table import",
                        true,
                        exportedUtc);
                }

                plugin.DatabaseService.DropLegacyTables();
                plugin.DatabaseService.CommitTransaction();
            }
            catch
            {
                plugin.DatabaseService.RollbackTransaction();
                throw;
            }
            knownCharacters = plugin.CharacterRepo.GetAll();
            InvalidateDashboardSnapshotCache();
            charListQueried = true;
            RefreshMigrationState();
            SetMigrationStatus($"Imported {characters.Count} legacy characters into xa_characters and removed legacy tables.");
            AddTaskLog($"[XA.DB TASK] Imported {characters.Count} legacy characters into xa_characters and removed legacy tables.");
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[XA] Legacy import failed: {ex}");
            SetMigrationStatus($"Legacy import failed: {ex.Message}");
            AddTaskLog($"[XA.DB TASK] Legacy import failed: {ex.Message}");
        }
    }

    private void ClearAllTablesAndStartFresh()
    {
        try
        {
            plugin.DatabaseService.ClearAllCharacterData();
            knownCharacters.Clear();
            InvalidateDashboardSnapshotCache();
            charListQueried = false;
            itemSearchResults.Clear();
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
            DataCollected = false;
            viewingContentId = null;
            selectedCharacterIndex = -1;
            viewingCharName = string.Empty;
            lastSnapshotResult = null;
            saveHistoryEntries.Clear();
            pendingCollectorWarnings.Clear();
            lastAddonTrigger = null;
            HousingCollector.ResetPersonalHousingState();
            RefreshMigrationState();
            SetMigrationStatus("All XA Database tables were cleared. Start fresh by using Refresh + Save.");
            AddTaskLog("[XA.DB TASK] Cleared all XA Database tables.");
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[XA] Clear-all failed: {ex}");
            SetMigrationStatus($"Clear-all failed: {ex.Message}");
            AddTaskLog($"[XA.DB TASK] Clear-all failed: {ex.Message}");
        }
    }

    private void DrawLegacyMigrationWarning()
    {
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.3f, 1.0f), "You are using an old database.");
        ImGui.TextWrapped("Click 'import legacy tables' to rebuild xa_characters and remove the old table layout, or click 'clear all tables and start fresh'. Each action requires a 3-second hold.");
        ImGui.TextDisabled($"Legacy characters: {legacyCharacterCount} | xa_characters: {xaCharacterSnapshotCount}");
        DrawHoldToConfirmButton("import legacy tables##XaImport", ref importLegacyHoldStartedAtUtc, ref importLegacyHoldTriggered, ImportLegacyTablesToXaCharacters);
        ImGui.SameLine();
        DrawHoldToConfirmButton("clear all tables and start fresh##XaClear", ref clearAllHoldStartedAtUtc, ref clearAllHoldTriggered, ClearAllTablesAndStartFresh);

        if (!string.IsNullOrEmpty(migrationStatusMessage) && DateTime.UtcNow < migrationStatusExpiry)
        {
            ImGui.TextColored(new Vector4(0.4f, 1.0f, 0.4f, 1.0f), migrationStatusMessage);
        }

        ImGui.Spacing();
    }

    private void DrawHoldToConfirmButton(string label, ref DateTime? holdStartedAtUtc, ref bool holdTriggered, System.Action onConfirmed)
    {
        var now = DateTime.UtcNow;
        ImGui.Button(label);
        var active = ImGui.IsItemActive();
        var progress = 0.0;

        if (active)
        {
            holdStartedAtUtc ??= now;
            progress = Math.Min(1.0, (now - holdStartedAtUtc.Value).TotalSeconds / HoldToConfirmSeconds);
            if (progress >= 1.0 && !holdTriggered)
            {
                holdTriggered = true;
                holdStartedAtUtc = null;
                onConfirmed();
            }
        }
        else
        {
            holdStartedAtUtc = null;
            holdTriggered = false;
        }

        if (progress > 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"{progress * 100:F0}%");
        }
    }

    private void LoadCharacterFromDb(ulong contentId)
    {
        try
        {
            var snapshot = plugin.SnapshotRepo.GetSnapshot(contentId);
            if (snapshot == null)
                return;

            ResetCharacterScopedCache();
            ApplySnapshotToCache(snapshot);
            DataCollected = true;
            viewingContentId = contentId;

            var charRow = knownCharacters.Find(c => c.ContentId == contentId);
            viewingCharName = charRow != null ? $"{charRow.Name} @ {charRow.World}" : contentId.ToString();

            Plugin.Log.Information($"[XA] Loaded DB data for {viewingCharName} (cid={contentId}, retainers={cachedRetainers.Count}, currencies={cachedCurrencies.Count}, jobs={cachedJobs.Count})");
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[XA] Error loading character from DB: {ex}");
        }
    }

    /// <summary>
    /// Called from Plugin.OnFrameworkUpdate when player first loads.
    /// Runs expensive DB seed + data refresh outside of Draw() to avoid HITCH warnings.
    /// </summary>
    public void DoInitialSeed()
    {
        ResetCharacterScopedCache();
        viewingContentId = null;
        selectedCharacterIndex = -1;
        viewingCharName = string.Empty;
        charSelectorSearch = string.Empty;
        SeedFromDatabase();
        knownCharacters = plugin.CharacterRepo.GetAll();
        InvalidateDashboardSnapshotCache();
        RefreshAndSave(SnapshotTrigger.Login, "Initial load");
    }

    public override void Draw()
    {
        var isLoggedIn = Plugin.PlayerState.IsLoaded;
        var showLegacyWarning = legacyMigrationPending;

        // Load character list from DB once (for selector, works logged in or out)
        if (knownCharacters.Count == 0 && !charListQueried)
        {
            charListQueried = true;
            knownCharacters = plugin.CharacterRepo.GetAll();
            InvalidateDashboardSnapshotCache();
        }

        // Refresh button (only when logged in)
        if (isLoggedIn)
        {
            if (ImGui.Button("Refresh + Save"))
                RefreshAndSave();
            if (knownCharacters.Count > 0 && !showLegacyWarning)
                ImGui.SameLine();
        }

        if (showLegacyWarning)
            DrawLegacyMigrationWarning();

        // Character selector combo (always shown if we have characters in DB)
        if (knownCharacters.Count > 0)
        {
            if (!isLoggedIn) ImGui.TextDisabled("View character:");
            if (!isLoggedIn) ImGui.SameLine();
            var charSelectorWidth = MathF.Max(125f, MathF.Min(210f, ImGui.GetContentRegionAvail().X));
            ImGui.SetNextItemWidth(charSelectorWidth);
            ImGui.SetNextWindowSizeConstraints(new Vector2(charSelectorWidth, 0), new Vector2(charSelectorWidth, 420));
            var previewLabel = viewingContentId.HasValue ? viewingCharName
                : isLoggedIn ? "Current Character (Live)" : "Select a character...";
            if (ImGui.BeginCombo("##CharSelector", previewLabel, ImGuiComboFlags.HeightLarge))
            {
                // Search filter
                ImGui.SetNextItemWidth(-1);
                ImGui.InputTextWithHint("##CharSearch", "Search name or world...", ref charSelectorSearch, 128);
                ImGui.Spacing();

                // Live data option (only when logged in)
                if (isLoggedIn)
                {
                    if (ImGui.Selectable("Current Character (Live)", !viewingContentId.HasValue))
                    {
                        viewingContentId = null;
                        selectedCharacterIndex = -1;
                        viewingCharName = string.Empty;
                        charSelectorSearch = string.Empty;
                        RefreshData();
                    }
                }

                // DB characters (filtered by search)
                for (int i = 0; i < knownCharacters.Count; i++)
                {
                    var c = knownCharacters[i];
                    var label = $"{c.Name} @ {c.World}";

                    if (!string.IsNullOrEmpty(charSelectorSearch)
                        && !label.Contains(charSelectorSearch, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var isSelected = viewingContentId.HasValue && viewingContentId.Value == c.ContentId;
                    if (ImGui.Selectable(label, isSelected))
                    {
                        selectedCharacterIndex = i;
                        charSelectorSearch = string.Empty;
                        LoadCharacterFromDb(c.ContentId);
                    }
                }

                ImGui.EndCombo();
            }

            // [X] clear button — reset to live/unselected
            if (viewingContentId.HasValue)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("X##clearChar"))
                {
                    viewingContentId = null;
                    selectedCharacterIndex = -1;
                    viewingCharName = string.Empty;
                    if (isLoggedIn)
                        RefreshData();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Clear selection");
            }
        }

        DrawLatestSnapshotStatusPanel();

        // Reserve space for the sticky footer status bar
        var footerHeight = ImGui.GetFrameHeightWithSpacing() + 4;
        var contentHeight = ImGui.GetContentRegionAvail().Y - footerHeight;

        // Tab bar in scrollable child region
        using (var child = ImRaii.Child("TabContent", new Vector2(0, contentHeight)))
        {
            if (child.Success)
            {
                using (var tabBar = ImRaii.TabBar("XATabBar"))
                {
                    if (tabBar.Success)
                    {
                        DrawOverviewTab();
                        DrawSearchTab();
                        DrawInventoryTab();
                        DrawRetainersTab();
                        DrawCurrenciesTab();
                        DrawJobsTab();
                        DrawFcHousingTab();
                        DrawProgressTab();
                        DrawDashboardTab();
                        DrawSettingsTab();
                        DrawIpcTab();
                    }
                }
            }
        }

        // Sticky status bar — always visible at bottom
        ImGui.Separator();
        DrawStatusBar();
    }


    // ───────────────────────────────────────────────
    //  Status Bar
    // ───────────────────────────────────────────────
    private void DrawStatusBar()
    {
        ImGui.TextDisabled($"XA Database v{PluginVersion}");
        ImGui.SameLine();
        ImGui.TextDisabled("|");
        ImGui.SameLine();
        ImGui.TextDisabled("/xadb to toggle");

        // Show last-updated timestamp
        if (lastRefreshTime > DateTime.MinValue)
        {
            var elapsed = DateTime.UtcNow - lastRefreshTime;
            string agoText;
            if (elapsed.TotalSeconds < 60)
                agoText = "just now";
            else if (elapsed.TotalMinutes < 60)
                agoText = $"{(int)elapsed.TotalMinutes}m ago";
            else
                agoText = $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m ago";

            ImGui.SameLine();
            ImGui.TextDisabled("|");
            ImGui.SameLine();
            ImGui.TextDisabled($"Updated {agoText}");
        }

        if (viewingContentId.HasValue)
        {
            var charRow = knownCharacters.Find(c => c.ContentId == viewingContentId.Value);
            if (charRow != null && !string.IsNullOrEmpty(charRow.LastSeenUtc))
            {
                ImGui.SameLine();
                ImGui.TextDisabled("|");
                ImGui.SameLine();
                ImGui.TextDisabled($"DB snapshot: {charRow.LastSeenUtc}");
            }
        }

        if (lastSnapshotResult != null)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("|");
            ImGui.SameLine();
            ImGui.TextDisabled(lastSnapshotResult.Success ? lastSnapshotResult.Trigger.ToString() : "Save failed");
            ImGui.SameLine();
            ImGui.TextDisabled("|");
            ImGui.SameLine();
            ImGui.TextColored(GetSnapshotQualityColor(lastSnapshotResult), GetSnapshotQualityLabel(lastSnapshotResult));
            if (lastSnapshotResult.Warnings.Count > 0)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("|");
                ImGui.SameLine();
                ImGui.TextDisabled($"Warnings: {lastSnapshotResult.Warnings.Count}");
            }
        }
    }
}

public enum SnapshotTrigger
{
    Manual,
    Login,
    Logout,
    AddonWatcher,
    AutoSaveTimer,
    XASlave,
    LegacyImport,
}

public sealed class SaveSnapshotResult
{
    public int PayloadVersion { get; init; } = IpcContractInfo.LastSnapshotResultJsonVersion;
    public bool Success { get; init; }
    public ulong ContentId { get; init; }
    public string CharacterName { get; init; } = string.Empty;
    public string HomeWorld { get; init; } = string.Empty;
    public SnapshotTrigger Trigger { get; init; }
    public string TriggerDetail { get; init; } = string.Empty;
    public string SavedAtUtc { get; init; } = string.Empty;
    public int Gil { get; init; }
    public int RetainerGil { get; init; }
    public int RetainerCount { get; init; }
    public bool SavedFreeCompany { get; init; }
    public bool SavedVoyages { get; init; }
    public string Summary { get; set; } = string.Empty;
    public string Quality { get; set; } = string.Empty;
    public List<string> Warnings { get; init; } = new();
}

public sealed class CollectorValidationSummary
{
    public bool InventoryCollected { get; init; }
    public bool RetainersCollected { get; init; }
    public bool FreeCompanyCollected { get; init; }
    public bool VoyagesCollected { get; init; }
    public bool CollectionsCollected { get; init; }
    public bool QuestsCollected { get; init; }
    public List<string> Warnings { get; init; } = new();
}
