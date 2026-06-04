using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
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
    private static readonly JsonSerializerOptions CurrentCharacterItemsJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

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
    private ulong lastLiveContentId;
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
    private SearchItemRequest? activeExactItemSearch;
    private bool selectSearchTabOnNextDraw;

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
    private bool refreshAndSaveQueued;
    private SnapshotTrigger queuedRefreshAndSaveTrigger = SnapshotTrigger.Manual;
    private string queuedRefreshAndSaveDetail = string.Empty;
    private ulong lastPersistedSnapshotContentId;
    private XaCharacterSnapshotData? lastPersistedSnapshot;

    public MainWindow(Plugin plugin)
        : base("XA Database##MainWindow", ImGuiWindowFlags.None)
    {
        this.plugin = plugin;
        UpdateSizeConstraints(UiScaleSafe);
        RefreshMigrationState();
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        UpdateSizeConstraints(UiScale);
        WindowName = plugin.Configuration.ShowVersionInWindowTitle
            ? $"XA Database v{PluginVersion}###MainWindow"
            : "XA Database###MainWindow";
    }

    private static float UiScale => ImGuiHelpers.GlobalScale;

    private static float UiScaleSafe => ImGuiHelpers.GlobalScale;

    private static float Scale(float value)
        => value * UiScale;

    private void RefreshItemTooltipCache()
    {
        plugin.ItemLocationTooltip.RefreshCache();
    }

    private void RefreshItemTooltipCacheForCurrentSnapshot(ulong contentId, string characterName, string world, string updatedUtc)
    {
        plugin.ItemLocationTooltip.UpdateCharacterSnapshot(
            contentId,
            characterName,
            world,
            updatedUtc,
            cachedItems,
            cachedRetainerItems);
    }

    private static Vector2 ScaledVector(float x, float y)
        => ImGuiHelpers.ScaledVector2(x, y);

    private void UpdateSizeConstraints(float scale)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(300f * scale, 240f * scale),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

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
    public long GetRetainerGil()
    {
        return GetRetainerGilValue();
    }

    private long GetRetainerGilValue()
    {
        return cachedRetainers.Count > 0 ? cachedRetainers.Sum(r => (long)r.Gil) : 0L;
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

    private int GetCurrentFcChestGil()
    {
        return cachedFc?.FcGil ?? 0;
    }

    /// <summary>Returns FC info as pipe-delimited string: Name|Tag|Points|Rank. Empty if no FC.</summary>
    public string GetFcInfo()
    {
        if (cachedFc == null) return string.Empty;
        return $"{cachedFc.Name}|{cachedFc.Tag}|{cachedFc.FcPoints}|{cachedFc.Rank}";
    }

    private sealed class CurrencyDisplayEntry
    {
        public string Category { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public long Amount { get; init; }
        public int Cap { get; init; }
    }

    private List<CurrencyDisplayEntry> BuildCurrencyDisplayEntries()
    {
        var displayCurrencies = cachedCurrencies
            .Select(entry => new CurrencyDisplayEntry
            {
                Category = entry.Category,
                Name = entry.Name,
                Amount = entry.Amount,
                Cap = entry.Cap,
            })
            .ToList();

        int FindCommonCurrencyIndex(string name)
        {
            return displayCurrencies.FindIndex(entry =>
                entry.Category.Equals("Common", StringComparison.OrdinalIgnoreCase)
                && entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        if (cachedFc != null)
        {
            var fcChestEntry = new CurrencyDisplayEntry
            {
                Category = "Common",
                Name = "FC Chest",
                Amount = GetCurrentFcChestGil(),
                Cap = 999_999_999,
            };

            var gilIndex = FindCommonCurrencyIndex("Gil");
            if (gilIndex >= 0)
                displayCurrencies.Insert(gilIndex + 1, fcChestEntry);
            else
                displayCurrencies.Add(fcChestEntry);
        }

        var retainerGil = GetRetainerGilValue();
        if (cachedRetainers.Count == 0 && retainerGil <= 0)
            return displayCurrencies;

        var retainerGilEntry = new CurrencyDisplayEntry
        {
            Category = "Common",
            Name = "Retainer Gil",
            Amount = retainerGil,
            Cap = 999_999_999,
        };

        var fcChestIndex = FindCommonCurrencyIndex("FC Chest");
        if (fcChestIndex >= 0)
            displayCurrencies.Insert(fcChestIndex + 1, retainerGilEntry);
        else
        {
            var gilIndex = FindCommonCurrencyIndex("Gil");
            if (gilIndex >= 0)
                displayCurrencies.Insert(gilIndex + 1, retainerGilEntry);
            else
                displayCurrencies.Add(retainerGilEntry);
        }

        return displayCurrencies;
    }

    private void ApplyFreeCompanyGilOwnership(ulong currentContentId, string currentCharacterName)
    {
        if (cachedFc == null)
            return;

        NormalizeFreeCompanyGilObservation(cachedFc);

        if (FreeCompanyCollector.HasObservedFcChestGil
            && (FreeCompanyCollector.LastObservedFcGilFcId == 0
                || cachedFc.FcId == 0
                || cachedFc.FcId == FreeCompanyCollector.LastObservedFcGilFcId))
        {
            cachedFc.FcGil = FreeCompanyCollector.LastFcGil;
            cachedFc.FcGilObserved = true;
            return;
        }

        if (cachedFc.FcGilObserved)
            return;

        if (!TryRestorePersistedFreeCompanyGil(currentContentId, out var sourceContentId))
            return;

        Plugin.Log.Debug(
            $"[XA] Restored FC chest gil {cachedFc.FcGil:N0} for {currentCharacterName} (cid={currentContentId}, sourceCid={sourceContentId}, fcId={cachedFc.FcId}).");
    }

    private bool IsCurrentCharacterFcMaster(string currentCharacterName)
    {
        if (cachedFc == null || cachedFc.FcId == 0)
            return false;

        var normalizedCurrentName = NormalizeCharacterName(currentCharacterName);
        var normalizedMasterName = NormalizeCharacterName(cachedFc.Master);
        return normalizedCurrentName.Length > 0
            && normalizedCurrentName.Equals(normalizedMasterName, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeCharacterName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim();
        var atIndex = normalized.IndexOf('@');
        if (atIndex >= 0)
            normalized = normalized[..atIndex].Trim();

        return normalized;
    }

    private static void NormalizeFreeCompanyGilObservation(FreeCompanyEntry fc)
    {
        if (fc.FcGil > 0)
            fc.FcGilObserved = true;
    }

    private bool TryRestorePersistedFreeCompanyGil(ulong currentContentId, out ulong sourceContentId)
    {
        sourceContentId = 0;
        if (cachedFc == null || cachedFc.FcId == 0)
            return false;

        XaCharacterSnapshotData? bestSnapshot = null;
        foreach (var snapshot in plugin.SnapshotRepo.GetAllSnapshots())
        {
            var snapshotFc = snapshot.FreeCompany;
            if (snapshotFc == null || snapshotFc.FcId != cachedFc.FcId)
                continue;

            NormalizeFreeCompanyGilObservation(snapshotFc);
            if (!snapshotFc.FcGilObserved)
                continue;

            if (bestSnapshot == null
                || string.CompareOrdinal(snapshot.Row.UpdatedUtc, bestSnapshot.Row.UpdatedUtc) > 0
                || (string.Equals(snapshot.Row.UpdatedUtc, bestSnapshot.Row.UpdatedUtc, StringComparison.Ordinal)
                    && snapshot.Row.ContentId == currentContentId))
            {
                bestSnapshot = snapshot;
            }
        }

        if (bestSnapshot?.FreeCompany == null)
            return false;

        cachedFc.FcGil = bestSnapshot.FreeCompany.FcGil;
        cachedFc.FcGilObserved = bestSnapshot.FreeCompany.FcGilObserved || bestSnapshot.FreeCompany.FcGil > 0;
        sourceContentId = bestSnapshot.Row.ContentId;
        return true;
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
                retainerGil = GetRetainerGilValue(),
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
            var results = SearchSnapshotItemsByName(query, null);
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

    /// <summary>Returns unique Character@World keys whose persisted snapshot contains any exact requested item key.</summary>
    public string GetMatchingCharactersForItems(string itemKeysPayload)
    {
        if (string.IsNullOrWhiteSpace(itemKeysPayload))
            return string.Empty;
        try
        {
            var itemKeys = itemKeysPayload
                .Split(new[] { ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .ToHashSet(StringComparer.Ordinal);
            if (itemKeys.Count == 0)
                return string.Empty;

            var matches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var snapshot in plugin.SnapshotRepo.GetAllSnapshots())
            {
                if (!SnapshotContainsAnyMatchingItem(snapshot, itemKeys))
                    continue;
                matches.Add($"{snapshot.Row.CharacterName}@{snapshot.Row.World}");
            }

            return matches.Count == 0
                ? string.Empty
                : string.Join("\n", matches.OrderBy(key => key, StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[XA] IPC GetMatchingCharactersForItems error: {ex}");
            return string.Empty;
        }
    }

    /// <summary>Current-character scoped item search for automation-safe IPC consumers.</summary>
    public string SearchCurrentCharacterItemsJson(string requestJson)
    {
        var warnings = new List<string>();
        var request = ParseCurrentCharacterItemsRequest(requestJson, warnings);
        var requestItemIds = request.ItemIds ?? new List<uint>();
        var requestSources = request.Sources ?? new List<string>();
        var requestedItemIds = requestItemIds
            .Where(itemId => itemId > 0)
            .Distinct()
            .ToHashSet();
        var requestedSources = requestSources
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Select(source => source.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var includeRetainers = requestedSources.Count == 0
            || requestedSources.Any(source =>
                source.Equals("all", StringComparison.OrdinalIgnoreCase)
                || source.Equals("retainer", StringComparison.OrdinalIgnoreCase)
                || source.Equals("retainers", StringComparison.OrdinalIgnoreCase));

        if (requestedItemIds.Count == 0)
            warnings.Add("No itemIds were supplied; all supported current-character rows will be considered.");

        var unsupportedSources = requestedSources
            .Where(source =>
                !source.Equals("all", StringComparison.OrdinalIgnoreCase)
                && !source.Equals("retainer", StringComparison.OrdinalIgnoreCase)
                && !source.Equals("retainers", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (unsupportedSources.Count > 0)
            warnings.Add($"Unsupported sources ignored: {string.Join(", ", unsupportedSources)}.");

        var ready = Plugin.PlayerState.IsLoaded && Plugin.PlayerState.ContentId != 0;
        var contentId = ready ? Plugin.PlayerState.ContentId : 0UL;
        var characterName = ready ? GetCharacterName() : string.Empty;
        var world = ready ? ResolveCurrentHomeWorldName() : string.Empty;

        if (!ready)
        {
            warnings.Add("XA Database is not ready or no current content ID is available.");
            return SerializeCurrentCharacterItemsResponse(false, contentId, characterName, world, new List<CurrentCharacterItemIpcRow>(), warnings);
        }

        if (!includeRetainers)
        {
            warnings.Add("No supported sources were requested. Supported source: retainers.");
            return SerializeCurrentCharacterItemsResponse(true, contentId, characterName, world, new List<CurrentCharacterItemIpcRow>(), warnings);
        }

        try
        {
            var snapshot = plugin.SnapshotRepo.GetSnapshot(contentId);
            if (snapshot == null)
            {
                warnings.Add("No saved snapshot exists for the current character.");
                return SerializeCurrentCharacterItemsResponse(true, contentId, characterName, world, new List<CurrentCharacterItemIpcRow>(), warnings);
            }

            if (!string.IsNullOrWhiteSpace(snapshot.Row.CharacterName))
                characterName = snapshot.Row.CharacterName;
            if (!string.IsNullOrWhiteSpace(snapshot.Row.World))
                world = snapshot.Row.World;

            var snapshotQuality = ResolveCurrentCharacterItemsSnapshotQuality(contentId, snapshot);
            if (IsSnapshotUpdatedUtcStale(snapshot.Row.UpdatedUtc))
                warnings.Add("Current-character snapshot is stale; retainer inventory may need a fresh save.");

            var normalizedRetainers = XaCharacterSnapshotRepository.NormalizeRetainerPayload(
                snapshot.Retainers,
                snapshot.Listings,
                snapshot.RetainerItems,
                contentId);
            if (normalizedRetainers.Retainers.Count == 0)
                warnings.Add("No current-character retainers are present in the saved snapshot.");
            if (normalizedRetainers.RetainerItems.Count == 0)
                warnings.Add("No current-character retainer inventory rows are present in the saved snapshot.");

            var retainerById = normalizedRetainers.Retainers.ToDictionary(retainer => retainer.RetainerId);
            var rows = new List<CurrentCharacterItemIpcRow>();
            foreach (var item in normalizedRetainers.RetainerItems)
            {
                if (requestedItemIds.Count > 0 && !requestedItemIds.Contains(item.ItemId))
                    continue;
                if (!request.IncludeZeroQuantity && item.Quantity <= 0)
                    continue;
                if (!retainerById.TryGetValue(item.RetainerId, out var retainer))
                    continue;

                rows.Add(new CurrentCharacterItemIpcRow
                {
                    Source = "retainer",
                    OwnerContentId = retainer.OwnerContentId == 0 ? contentId : retainer.OwnerContentId,
                    RetainerId = item.RetainerId,
                    RetainerName = item.RetainerName,
                    ContainerName = $"Retainer: {item.RetainerName}",
                    ItemId = item.ItemId,
                    ItemName = item.ItemName,
                    Quantity = item.Quantity,
                    IsHq = item.IsHq,
                    LastSeenUtc = snapshot.Row.UpdatedUtc,
                    SnapshotQuality = snapshotQuality,
                });
            }

            return SerializeCurrentCharacterItemsResponse(true, contentId, characterName, world, rows, warnings);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[XA] IPC SearchCurrentCharacterItemsJson error: {ex}");
            warnings.Add($"SearchCurrentCharacterItemsJson failed: {ex.Message}");
            return SerializeCurrentCharacterItemsResponse(true, contentId, characterName, world, new List<CurrentCharacterItemIpcRow>(), warnings);
        }
    }

    private static CurrentCharacterItemsIpcRequest ParseCurrentCharacterItemsRequest(string requestJson, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(requestJson))
            return new CurrentCharacterItemsIpcRequest();

        try
        {
            var request = JsonSerializer.Deserialize<CurrentCharacterItemsIpcRequest>(requestJson, CurrentCharacterItemsJsonOptions)
                ?? new CurrentCharacterItemsIpcRequest();
            if (request.Version > IpcContractInfo.CurrentCharacterItemsJsonVersion)
                warnings.Add($"Request version {request.Version} is newer than supported version {IpcContractInfo.CurrentCharacterItemsJsonVersion}; best-effort parsing was used.");
            return request;
        }
        catch (JsonException)
        {
            warnings.Add("Request JSON was invalid; default request was used.");
            return new CurrentCharacterItemsIpcRequest();
        }
    }

    private static string SerializeCurrentCharacterItemsResponse(
        bool ready,
        ulong contentId,
        string characterName,
        string world,
        List<CurrentCharacterItemIpcRow> rows,
        List<string> warnings)
    {
        var response = new CurrentCharacterItemsIpcResponse
        {
            Version = IpcContractInfo.CurrentCharacterItemsJsonVersion,
            IpcContractVersion = IpcContractInfo.CurrentVersion,
            Ready = ready,
            Character = new CurrentCharacterItemsIpcCharacter
            {
                ContentId = contentId,
                Name = characterName,
                World = world,
            },
            Rows = rows,
            Warnings = warnings.Distinct(StringComparer.Ordinal).ToList(),
        };

        return JsonSerializer.Serialize(response, CurrentCharacterItemsJsonOptions);
    }

    private string ResolveCurrentCharacterItemsSnapshotQuality(ulong contentId, XaCharacterSnapshotData snapshot)
    {
        if (lastSnapshotResult != null && lastSnapshotResult.ContentId == contentId)
            return string.IsNullOrWhiteSpace(lastSnapshotResult.Quality)
                ? GetSnapshotQualityLabel(lastSnapshotResult)
                : lastSnapshotResult.Quality;

        if (IsSnapshotUpdatedUtcStale(snapshot.Row.UpdatedUtc))
            return "Stale";

        return string.IsNullOrWhiteSpace(snapshot.Row.UpdatedUtc) ? "No Snapshot" : "Persisted";
    }

    private static bool IsSnapshotUpdatedUtcStale(string updatedUtc)
    {
        if (!DateTime.TryParse(updatedUtc, out var parsed))
            return false;

        var parsedUtc = parsed.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
            : parsed.ToUniversalTime();
        return (DateTime.UtcNow - parsedUtc).TotalMinutes >= SnapshotStaleThresholdMinutes;
    }

    private static string ResolveCurrentHomeWorldName()
    {
        try { return Plugin.PlayerState.HomeWorld.Value.Name.ToString(); }
        catch { }

        try { return Plugin.ObjectTable.LocalPlayer?.HomeWorld.Value.Name.ToString() ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static bool SnapshotContainsAnyMatchingItem(XaCharacterSnapshotData snapshot, HashSet<string> itemKeys)
    {
        foreach (var item in snapshot.AllItems)
        {
            if (itemKeys.Contains(BuildItemMatchKey(item.ItemId, item.IsHq)))
                return true;
        }

        foreach (var item in snapshot.RetainerItems)
        {
            if (itemKeys.Contains(BuildItemMatchKey(item.ItemId, item.IsHq)))
                return true;
        }

        return false;
    }

    private static string BuildItemMatchKey(uint itemId, bool isHq)
    {
        return $"{itemId}:{(isHq ? 1 : 0)}";
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
            {
                ApplySnapshotToCache(snapshot);
                lastLiveContentId = contentId;
            }

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

        if (lastLiveContentId != 0 && lastLiveContentId != playerState.ContentId)
            ResetCharacterScopedCache();

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
            var freshRetainers = RetainerCollector.CollectRetainerList(playerState.ContentId);
            hasAuthoritativeLiveRetainerList = freshRetainers.Count > 0;
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

            cachedCollections = CollectionCollector.Collect(Plugin.DataManager);
            cachedQuests = QuestCollector.CollectActiveQuests(Plugin.DataManager);
            cachedMsqMilestones = QuestCollector.CollectMsqProgress();
            var housing = HousingCollector.CollectPersonalHousing();
            cachedPersonalEstate = housing.PersonalEstate;
            cachedSharedEstates = housing.SharedEstates;
            cachedApartment = housing.Apartment;

            var persistedSnapshot = plugin.SnapshotRepo.GetSnapshot(playerState.ContentId);
            lastPersistedSnapshotContentId = playerState.ContentId;
            lastPersistedSnapshot = persistedSnapshot;
            JournalCollector.SeedPersistedValue(persistedSnapshot?.Currencies);
            JournalCollector.TryCollectFromOpenAddon();
            JournalCollector.ApplyToCurrencies(cachedCurrencies);

            if (persistedSnapshot == null && freshRetainers.Count == 0)
            {
                cachedRetainers.Clear();
                cachedListings.Clear();
                cachedRetainerItems.Clear();
            }

            ApplyPersistedRetainerState(persistedSnapshot);
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
                    FcGil = persistedSnapshot.FreeCompany.FcGil,
                    FcGilObserved = persistedSnapshot.FreeCompany.FcGilObserved || persistedSnapshot.FreeCompany.FcGil > 0,
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
                if (cachedFc.FcGil == 0 && persistedFc.FcGil > 0)
                    cachedFc.FcGil = persistedFc.FcGil;
                if (!cachedFc.FcGilObserved && (persistedFc.FcGilObserved || persistedFc.FcGil > 0))
                    cachedFc.FcGilObserved = true;
                if (string.IsNullOrEmpty(cachedFc.Estate) && !string.IsNullOrEmpty(persistedFc.Estate))
                    cachedFc.Estate = persistedFc.Estate;
            }

            if (cachedFc != null)
            {
                FreeCompanyCollector.SeedPersistedValues(cachedFc.FcPoints, cachedFc.Estate, cachedFc.Name, cachedFc.Tag, cachedFc.Rank, cachedFc.FcGil, cachedFc.FcGilObserved, cachedFc.FcId);
                if (cachedFc.FcPoints == 0 && FreeCompanyCollector.LastFcPoints > 0)
                    cachedFc.FcPoints = FreeCompanyCollector.LastFcPoints;
                if (cachedFc.FcGil == 0 && (FreeCompanyCollector.LastFcGil > 0 || FreeCompanyCollector.HasObservedFcChestGil))
                {
                    cachedFc.FcGil = FreeCompanyCollector.LastFcGil;
                    cachedFc.FcGilObserved = true;
                }
                if (cachedFc.Rank == 0 && FreeCompanyCollector.LastFcRank > 0)
                    cachedFc.Rank = FreeCompanyCollector.LastFcRank;
                if (string.IsNullOrEmpty(cachedFc.Estate) && !string.IsNullOrEmpty(FreeCompanyCollector.LastEstate))
                    cachedFc.Estate = FreeCompanyCollector.LastEstate;
                if (string.IsNullOrEmpty(cachedFc.Name) && !string.IsNullOrEmpty(FreeCompanyCollector.LastFcName))
                    cachedFc.Name = FreeCompanyCollector.LastFcName;
                if (string.IsNullOrEmpty(cachedFc.Tag) && !string.IsNullOrEmpty(FreeCompanyCollector.LastFcTag))
                    cachedFc.Tag = FreeCompanyCollector.LastFcTag;
            }

            ApplyFreeCompanyGilOwnership(playerState.ContentId, playerState.CharacterName.ToString());

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
            ApplyFcMemberRankNames(persistedSnapshot);

            // Load persisted squadron data from DB if not in barracks
            if (cachedSquadron == null && persistedSnapshot != null)
                cachedSquadron = persistedSnapshot.Squadron;

            // Load persisted voyage data from DB if not in workshop
            if (cachedVoyages == null && cachedFc != null && persistedSnapshot != null)
                cachedVoyages = persistedSnapshot.Voyages;

            // Personal housing: no DB fallback needed here — GetOwnedHouseId works from anywhere.
            // The collector already validates sentinel data, so empty = character doesn't own that type.
            // Stale DB data is cleared on save.

            NormalizeCachedRetainerState(playerState.ContentId);

            lastRefreshTime = DateTime.UtcNow;
            lastLiveContentId = playerState.ContentId;
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

    public SaveSnapshotResult QueueRefreshAndSave(SnapshotTrigger trigger = SnapshotTrigger.Manual, string triggerDetail = "Manual refresh")
    {
        refreshAndSaveQueued = true;
        queuedRefreshAndSaveTrigger = trigger;
        queuedRefreshAndSaveDetail = triggerDetail;

        var queuedResult = new SaveSnapshotResult
        {
            Success = false,
            Pending = true,
            Trigger = trigger,
            TriggerDetail = triggerDetail,
            SavedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            Summary = $"Snapshot save queued: {triggerDetail}",
            Quality = "Saving"
        };

        lastSnapshotResult = queuedResult;
        AddTaskLog($"[XA.DB TASK] Queued snapshot save via {trigger}: {triggerDetail}");
        return queuedResult;
    }

    public void ProcessDeferredWork()
    {
        if (!refreshAndSaveQueued)
            return;

        var trigger = queuedRefreshAndSaveTrigger;
        var detail = queuedRefreshAndSaveDetail;
        refreshAndSaveQueued = false;
        queuedRefreshAndSaveDetail = string.Empty;
        RefreshAndSave(trigger, detail);
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
                case "Journal" when trigger.AddonName == "Journal":
                    Plugin.Log.Information("[XA] Journal closing — reading leve allowances via pointer.");
                    JournalCollector.CollectFromAddon(trigger.AddonPtr);
                    break;

                case "FC Members" when trigger.AddonName == "FreeCompany":
                    Plugin.Log.Information("[XA] FreeCompany closing — reading FC points via pointer.");
                    FreeCompanyCollector.CollectFromAddon(trigger.AddonPtr);
                    break;

                case "FC Chest" when trigger.AddonName == "FreeCompanyChest":
                    Plugin.Log.Information("[XA] FreeCompanyChest closing — reading FC chest gil via pointer.");
                    FreeCompanyCollector.CollectChestGilFromAddon(trigger.AddonPtr);
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
        Plugin.Log.Information($"[XA] Addon trigger ({trigger.Category}) — queueing refresh and save.");
        QueueRefreshAndSave(SnapshotTrigger.AddonWatcher, trigger.TriggerDetail);
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
            var persistedSnapshot = lastPersistedSnapshotContentId == contentId
                ? lastPersistedSnapshot
                : plugin.SnapshotRepo.GetSnapshot(contentId);
            JournalCollector.SeedPersistedValue(persistedSnapshot?.Currencies);
            JournalCollector.ApplyToCurrencies(cachedCurrencies);
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
            ApplyFcMemberRankNames(persistedSnapshot);
            if (isOnHomeworld && cachedFc == null && hasReliableLiveCharacterContext)
                ClearPersistedFreeCompanyState();
            ApplyFreeCompanyGilOwnership(contentId, name);
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
            ApplyPersistedRetainerState(persistedSnapshot);
            NormalizeCachedRetainerState(contentId);
            var retainerGil = GetRetainerGilValue();
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
            var normalizedRetainers = XaCharacterSnapshotRepository.NormalizeRetainerPayload(cachedRetainers, cachedListings, cachedRetainerItems, contentId);

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
                    XaCharacterSnapshotRepository.BuildRetainerOwnerReferencesJson(normalizedRetainers.Retainers, contentId),
                    freshnessJson,
                    sections,
                    Schema.CurrentSnapshotVersion,
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

            UpsertKnownCharacterCache(contentId, name, world, datacenter, region, savedAtUtc);
            lastPersistedSnapshotContentId = 0;
            lastPersistedSnapshot = null;
            InvalidateDashboardSnapshotCache();
            RefreshItemTooltipCacheForCurrentSnapshot(contentId, name, world, savedAtUtc);

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

    private void UpsertKnownCharacterCache(
        ulong contentId,
        string name,
        string world,
        string datacenter,
        string region,
        string savedAtUtc)
    {
        var row = knownCharacters.Find(c => c.ContentId == contentId);
        if (row == null)
        {
            row = new CharacterRow
            {
                ContentId = contentId,
                CreatedUtc = savedAtUtc,
            };
            knownCharacters.Add(row);
        }

        row.Name = name;
        row.World = world;
        row.Datacenter = datacenter;
        row.Region = region;
        row.LastSeenUtc = savedAtUtc;
        row.PersonalEstate = cachedPersonalEstate;
        row.SharedEstates = cachedSharedEstates;
        row.Apartment = cachedApartment;

        knownCharacters = knownCharacters
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.World, StringComparer.OrdinalIgnoreCase)
            .ToList();
        charListQueried = true;
        xaCharacterSnapshotCount = Math.Max(xaCharacterSnapshotCount, knownCharacters.Count);
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
            snapshotVersion = Schema.CurrentSnapshotVersion,
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
                retainerGil = GetRetainerGilValue(),
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
                        retainerGil,
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
                    var normalizedRetainers = XaCharacterSnapshotRepository.NormalizeRetainerPayload(retainers, listings, retainerItems, character.ContentId);

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
                        retainerGil,
                        normalizedRetainers.Retainers.Count,
                        XaCharacterSnapshotRepository.GetHighestJobLevel(jobs),
                        XaCharacterSnapshotRepository.BuildRetainerOwnerReferencesJson(normalizedRetainers.Retainers, character.ContentId),
                        JsonSerializer.Serialize(new
                        {
                            exportedUtc,
                            importedFromLegacy = true,
                            source = "legacy_import"
                        }),
                        sections,
                        Schema.CurrentSnapshotVersion,
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
            RefreshItemTooltipCache();
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
            RefreshItemTooltipCache();
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
            lastLiveContentId = 0;
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

    private static bool IsDeleteModifierHeld()
    {
        var io = ImGui.GetIO();
        return io.KeyCtrl && io.KeyShift;
    }

    private static void PushDeleteButtonColors(bool enabled)
    {
        var buttonColor = enabled
            ? new Vector4(0.78f, 0.18f, 0.18f, 1.0f)
            : new Vector4(0.35f, 0.12f, 0.12f, 0.55f);
        var hoveredColor = enabled
            ? new Vector4(0.90f, 0.26f, 0.26f, 1.0f)
            : new Vector4(0.42f, 0.16f, 0.16f, 0.60f);
        var activeColor = enabled
            ? new Vector4(0.65f, 0.14f, 0.14f, 1.0f)
            : new Vector4(0.30f, 0.10f, 0.10f, 0.55f);

        ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(buttonColor));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGui.GetColorU32(hoveredColor));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, ImGui.GetColorU32(activeColor));
    }

    private static void PopDeleteButtonColors()
    {
        ImGui.PopStyleColor(3);
    }

    private bool TryGetCurrentDeleteTarget(out ulong contentId, out string characterLabel)
    {
        if (viewingContentId.HasValue)
        {
            contentId = viewingContentId.Value;
            characterLabel = string.IsNullOrWhiteSpace(viewingCharName) ? contentId.ToString() : viewingCharName;
            return true;
        }

        if (Plugin.PlayerState.IsLoaded)
        {
            contentId = Plugin.PlayerState.ContentId;
            var characterName = Plugin.PlayerState.CharacterName.ToString();
            var worldName = string.Empty;
            try { worldName = Plugin.PlayerState.HomeWorld.Value.Name.ToString(); } catch { }
            characterLabel = string.IsNullOrWhiteSpace(worldName) ? characterName : $"{characterName} @ {worldName}";
            if (string.IsNullOrWhiteSpace(characterLabel))
                characterLabel = contentId.ToString();
            return contentId != 0;
        }

        contentId = 0;
        characterLabel = string.Empty;
        return false;
    }

    private bool DeleteCharacterSnapshot(ulong contentId, string characterLabel, out string errorMessage)
    {
        errorMessage = string.Empty;

        try
        {
            plugin.CharacterRepo.Delete(contentId);

            if (viewingContentId.HasValue && viewingContentId.Value == contentId)
            {
                viewingContentId = null;
                selectedCharacterIndex = -1;
                viewingCharName = string.Empty;
                charSelectorSearch = string.Empty;

                if (Plugin.PlayerState.IsLoaded)
                    RefreshData();
                else
                    ResetCharacterScopedCache();
            }

            knownCharacters = plugin.CharacterRepo.GetAll();
            selectedCharacterIndex = viewingContentId.HasValue
                ? knownCharacters.FindIndex(c => c.ContentId == viewingContentId.Value)
                : -1;
            InvalidateDashboardSnapshotCache();
            RefreshItemTooltipCache();
            RefreshMigrationState();

            Plugin.Log.Information($"[XA] Deleted character snapshot for {characterLabel} (cid={contentId}).");
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            Plugin.Log.Error($"[XA] Failed to delete character snapshot for {characterLabel} (cid={contentId}): {ex}");
            return false;
        }
    }

    private void SetSettingsStatus(string message)
    {
        settingsHousingStatus = message;
        settingsHousingStatusExpiry = DateTime.UtcNow.AddSeconds(8);
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
                QueueRefreshAndSave();
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
            var charSelectorWidth = MathF.Max(Scale(125f), MathF.Min(Scale(210f), ImGui.GetContentRegionAvail().X));
            ImGui.SetNextItemWidth(charSelectorWidth);
            ImGui.SetNextWindowSizeConstraints(new Vector2(charSelectorWidth, 0), new Vector2(charSelectorWidth, Scale(420f)));
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
        var footerHeight = ImGui.GetFrameHeightWithSpacing() + Scale(4f);
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
            ImGui.TextDisabled(lastSnapshotResult.Pending ? "Save queued" : lastSnapshotResult.Success ? lastSnapshotResult.Trigger.ToString() : "Save failed");
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

        if (!string.IsNullOrEmpty(settingsHousingStatus) && DateTime.UtcNow < settingsHousingStatusExpiry)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("|");
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.4f, 1.0f, 0.4f, 1.0f), settingsHousingStatus);
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
    public bool Pending { get; init; }
    public ulong ContentId { get; init; }
    public string CharacterName { get; init; } = string.Empty;
    public string HomeWorld { get; init; } = string.Empty;
    public SnapshotTrigger Trigger { get; init; }
    public string TriggerDetail { get; init; } = string.Empty;
    public string SavedAtUtc { get; init; } = string.Empty;
    public int Gil { get; init; }
    public long RetainerGil { get; init; }
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

internal sealed record SearchItemRequest(uint ItemId, bool IsHq, string ItemName);

internal sealed class CurrentCharacterItemsIpcRequest
{
    public int Version { get; set; } = IpcContractInfo.CurrentCharacterItemsJsonVersion;
    public List<uint> ItemIds { get; set; } = new();
    public List<string> Sources { get; set; } = new();
    public bool IncludeZeroQuantity { get; set; }
}

internal sealed class CurrentCharacterItemsIpcResponse
{
    public int Version { get; set; }
    public int IpcContractVersion { get; set; }
    public bool Ready { get; set; }
    public CurrentCharacterItemsIpcCharacter Character { get; set; } = new();
    public List<CurrentCharacterItemIpcRow> Rows { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

internal sealed class CurrentCharacterItemsIpcCharacter
{
    public ulong ContentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string World { get; set; } = string.Empty;
}

internal sealed class CurrentCharacterItemIpcRow
{
    public string Source { get; set; } = string.Empty;
    public ulong OwnerContentId { get; set; }
    public ulong RetainerId { get; set; }
    public string RetainerName { get; set; } = string.Empty;
    public string ContainerName { get; set; } = string.Empty;
    public uint ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public bool IsHq { get; set; }
    public string LastSeenUtc { get; set; } = string.Empty;
    public string SnapshotQuality { get; set; } = string.Empty;
}
