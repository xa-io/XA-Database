using System;
using Dalamud.Game.Command;
using System.Collections.Generic;
using Dalamud.Game.Inventory.InventoryEventArgTypes;
using XADatabase.Collectors;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using XADatabase.Core.Policies;
using XADatabase.Core.Security;
using XADatabase.Database;
using XADatabase.Services;
using XADatabase.Windows;

namespace XADatabase;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IGameInventory GameInventory { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IContextMenu ContextMenu { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider GameInterop { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/xadatabase";
    private const string CommandAlias = "/xadb";

    public Configuration Configuration { get; init; }
    public XaServices Services { get; init; }

    public readonly WindowSystem WindowSystem = new("XA");
    private MainWindow MainWindow { get; init; }

    // Database layer
    public DatabaseService DatabaseService { get; init; }
    public CharacterRepository CharacterRepo { get; init; }
    public XaCharacterSnapshotRepository SnapshotRepo { get; init; }
    public SnapshotStore Snapshots { get; init; }
    public CurrencyRepository CurrencyRepo { get; init; }
    public JobRepository JobRepo { get; init; }
    public InventoryRepository InventoryRepo { get; init; }
    public ContainerItemRepository ContainerItemRepo { get; init; }
    public RetainerRepository RetainerRepo { get; init; }
    public FreeCompanyRepository FcRepo { get; init; }
    public FcMemberRepository FcMemberRepo { get; init; }
    public SquadronRepository SquadronRepo { get; init; }
    public VoyageRepository VoyageRepo { get; init; }
    public CollectionRepository CollectionRepo { get; init; }
    public AddonWatcher AddonWatcher { get; init; }
    public AutoRetainerCharacterVisibilityService CharacterVisibility { get; init; }
    public IpcProvider IpcProvider { get; init; }
    public IpcFacade IpcFacade { get; init; }
    public ItemLocationTooltipService ItemLocationTooltip { get; init; }
    public ItemSearchContextMenuService ItemSearchContextMenu { get; init; }

    // Framework tick flags — heavy work moved out of Draw() to avoid HITCH warnings
    private readonly PluginLifecycleSchedule lifecycleSchedule = new();
    private int consecutiveFrameworkFailures;
    private bool disposed;

    public Plugin()
    {
        Services = new XaServices(
            PluginInterface,
            ClientState,
            PlayerState,
            DataManager,
            ObjectTable,
            Condition,
            Framework,
            ChatGui,
            Log,
            GameGui);
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        var configurationChanged = false;
        if (!Configuration.ShowVersionInWindowTitleDefaultApplied)
        {
            Configuration.ShowVersionInWindowTitle = true;
            Configuration.ShowVersionInWindowTitleDefaultApplied = true;
            configurationChanged = true;
        }
        if (string.IsNullOrWhiteSpace(Configuration.FcMemberContentIdSalt))
        {
            Configuration.FcMemberContentIdSalt = LocalContentIdHash.CreateSalt();
            configurationChanged = true;
        }

        // Initialize database
        DatabaseService = new DatabaseService(PluginInterface.GetPluginConfigDirectory());
        DatabaseService.InitializeSchema();
        DatabaseService.RunHealthCheck();
        CharacterRepo = new CharacterRepository(DatabaseService);
        SnapshotRepo = new XaCharacterSnapshotRepository(DatabaseService);
        Snapshots = new SnapshotStore(SnapshotRepo);
        CurrencyRepo = new CurrencyRepository(DatabaseService);
        JobRepo = new JobRepository(DatabaseService);
        InventoryRepo = new InventoryRepository(DatabaseService);
        ContainerItemRepo = new ContainerItemRepository(DatabaseService);
        RetainerRepo = new RetainerRepository(DatabaseService);
        FcRepo = new FreeCompanyRepository(DatabaseService);
        FcMemberRepo = new FcMemberRepository(DatabaseService);
        SquadronRepo = new SquadronRepository(DatabaseService);
        VoyageRepo = new VoyageRepository(DatabaseService);
        CollectionRepo = new CollectionRepository(DatabaseService);
        AddonWatcher = new AddonWatcher(AddonLifecycle, Log);
        CharacterVisibility = new AutoRetainerCharacterVisibilityService(PluginInterface, Log);
        IpcProvider = new IpcProvider(PluginInterface, Log, Framework);

        if (configurationChanged)
            Configuration.Save();

        // Prune old currency history on startup

        MainWindow = new MainWindow(this);
        IpcFacade = new IpcFacade(MainWindow, DatabaseService, Services.PlayerState, Services.ClientState);
        if (Configuration.OpenPluginOnLoad)
            MainWindow.IsOpen = true;
        ItemLocationTooltip = new ItemLocationTooltipService(this, GameInterop, GameGui, Log);
        ItemSearchContextMenu = new ItemSearchContextMenuService(
            ContextMenu,
            Log,
            (itemId, isHq) => MainWindow.OpenSearchForItem(itemId, isHq));

        // Wire IPC handlers now that MainWindow exists
        IpcProvider.Initialize(IpcFacade);

        WindowSystem.AddWindow(MainWindow);

        if (Configuration.SearchItemContextMenuEnabled && !ItemSearchContextMenu.SetEnabled(true))
            Log.Warning("[XA] Search item context menu could not be enabled.");

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the XA Database window",
            AllowedInMacros = true,
        });
        CommandManager.AddHandler(CommandAlias, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the XA Database window (alias)",
            AllowedInMacros = true,
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        Framework.Update += OnFrameworkUpdate;

        ClientState.Login += OnLogin;
        ClientState.Logout += OnLogout;
        ClientState.TerritoryChanged += OnTerritoryChanged;
        GameInventory.InventoryChanged += OnInventoryChanged;

        // Enable addon watcher — single callback for all transient addon closes
        AddonWatcher.Enable(
            onAddonClose: (trigger) => MainWindow.OnAddonSaveTrigger(trigger),
            onAddonOpen: (trigger) => MainWindow.OnAddonOpenTrigger(trigger)
        );

        Log.Information($"[XA] Plugin loaded successfully.");
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        // Stop every public entry point before releasing hook, IPC, window, or database owners.
        TryCleanup("Framework.Update -= OnFrameworkUpdate", () => Framework.Update -= OnFrameworkUpdate);
        TryCleanup("ClientState.Login -= OnLogin", () => ClientState.Login -= OnLogin);
        TryCleanup("ClientState.Logout -= OnLogout", () => ClientState.Logout -= OnLogout);
        TryCleanup("ClientState.TerritoryChanged -= OnTerritoryChanged", () => ClientState.TerritoryChanged -= OnTerritoryChanged);
        TryCleanup("GameInventory.InventoryChanged -= OnInventoryChanged", () => GameInventory.InventoryChanged -= OnInventoryChanged);
        TryCleanup("UiBuilder.Draw -= WindowSystem.Draw", () => PluginInterface.UiBuilder.Draw -= WindowSystem.Draw);
        TryCleanup("UiBuilder.OpenConfigUi -= ToggleConfigUi", () => PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi);
        TryCleanup("UiBuilder.OpenMainUi -= ToggleMainUi", () => PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi);
        TryCleanup($"CommandManager.RemoveHandler({CommandName})", () => CommandManager.RemoveHandler(CommandName));
        TryCleanup($"CommandManager.RemoveHandler({CommandAlias})", () => CommandManager.RemoveHandler(CommandAlias));
        TryCleanup("WindowSystem.RemoveAllWindows", WindowSystem.RemoveAllWindows);

        TryDispose("IpcProvider", IpcProvider);
        TryDispose("AddonWatcher", AddonWatcher);
        TryDispose("CharacterVisibility", CharacterVisibility);
        TryDispose("ItemLocationTooltip", ItemLocationTooltip);
        TryDispose("ItemSearchContextMenu", ItemSearchContextMenu);
        TryDispose("MainWindow", MainWindow);
        TryDispose("DatabaseService", DatabaseService);
    }

    private void TryDispose(string label, IDisposable? disposable)
    {
        if (disposable == null)
            return;

        TryCleanup(label, disposable.Dispose);
    }

    private void TryCleanup(string label, Action cleanup)
    {
        try
        {
            cleanup();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, $"[XA] Dispose cleanup failed for {label}.");
        }
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (disposed)
            return;

        try
        {
            var nowUtc = DateTime.UtcNow;
            if (CharacterVisibility.Update(Configuration.HonorAutoRetainerExclusions, nowUtc))
                MainWindow.OnCharacterVisibilityChanged();

            if (!ClientState.IsLoggedIn || !InventoryReadiness.CanCapture(Services))
                return;

            if (lifecycleSchedule.ShouldAttemptInitialSeed(nowUtc, TimeSpan.FromSeconds(5)))
            {
                lifecycleSchedule.MarkInitialSeedAttempted(nowUtc);
                if (MainWindow.DoInitialSeed())
                    lifecycleSchedule.MarkInitialSeedSucceeded(nowUtc);
            }

            MainWindow.ProcessDeferredWork();

            var autoInterval = Configuration.AutoSaveIntervalMinutes;
            if (lifecycleSchedule.ShouldQueueAutoSave(nowUtc, autoInterval, MainWindow.DataCollected))
            {
                MainWindow.QueueRefreshAndSave(SnapshotTrigger.AutoSaveTimer, $"{autoInterval}m interval");
                lifecycleSchedule.MarkAutoSaveQueued(nowUtc);
                Log.Information($"[XA] Auto-save queued ({autoInterval}m interval).");
            }

            if (lifecycleSchedule.ShouldRunPeriodicCheckpoint(nowUtc, TimeSpan.FromMinutes(30)))
            {
                lifecycleSchedule.MarkCheckpointAttempted(nowUtc);
                LogCheckpointOutcome(DatabaseService.CheckpointWal("PASSIVE", "periodic timer"), "Periodic");
            }

            consecutiveFrameworkFailures = 0;
        }
        catch (Exception ex)
        {
            consecutiveFrameworkFailures++;
            if (consecutiveFrameworkFailures <= 3)
                Log.Error(ex, "[XA] Framework update failed.");
            else if (consecutiveFrameworkFailures == 4)
                Log.Error("[XA] Framework update has failed four consecutive times; further errors are suppressed until a framework update succeeds.");
        }
    }

    private void OnLogin()
    {
        if (disposed)
            return;

        Log.Information("[XA] Character logged in — refreshing and saving data.");
        if (Configuration.OpenPluginOnLoad)
            MainWindow.IsOpen = true;
        lifecycleSchedule.MarkLogin();
        MainWindow.ClearPendingCaptureAndSave();
    }

    private void OnInventoryChanged(IReadOnlyCollection<InventoryEventArgs> events)
    {
        if (disposed || events.Count == 0 || !ClientState.IsLoggedIn)
            return;

        // Dalamud delivers a coalesced change batch on the framework thread.
        // Update memory now, before automation can log out; do not force a disk save.
        try { MainWindow.CaptureLiveInventory(); }
        catch (Exception ex) { Log.Error(ex, "[XA] Inventory change capture failed; previous cache retained."); }
    }

    private void OnTerritoryChanged(uint territory)
    {
        if (!disposed)
            MainWindow.QueueInventoryCapture();
    }

    private void OnLogout(int type, int code)
    {
        if (disposed)
            return;

        var result = MainWindow.SaveForLogout("Client logout");
        if (result.Success)
            Log.Information("[XA] Character logged out — final snapshot saved.");
        else
            Log.Warning($"[XA] Logout save did not run: {result.Summary}");

        LogCheckpointOutcome(DatabaseService.CheckpointWal("FULL", "logout"), "Logout");
    }

    internal static void LogCheckpointOutcome(WalCheckpointOutcome outcome, string context)
    {
        switch (outcome)
        {
            case WalCheckpointOutcome.Merged:
                return;
            case WalCheckpointOutcome.Partial:
            case WalCheckpointOutcome.Blocked:
                Log.Warning($"[XA] {context} checkpoint did not fully merge the WAL into xa.db. Readers opening xa.db together with xa.db-wal still see committed data; a copy of xa.db alone may lag.");
                return;
            case WalCheckpointOutcome.SkippedTransactionActive:
                Log.Error($"[XA] {context} checkpoint was skipped because a transaction was still active. The final snapshot may not be committed.");
                return;
            case WalCheckpointOutcome.Failed:
                Log.Error($"[XA] {context} checkpoint failed. The state of xa.db could not be confirmed; run the database health check on next login.");
                return;
        }
    }

    private void OnCommand(string command, string args)
    {
        if (disposed)
            return;

        MainWindow.Toggle();
    }

    public void ToggleConfigUi()
    {
        if (!disposed)
            MainWindow.Toggle();
    }

    public void ToggleMainUi()
    {
        if (!disposed)
            MainWindow.Toggle();
    }

    internal ulong ProtectOtherPlayerContentId(ulong contentId)
    {
        if (Services.PlayerState.IsLoaded && contentId == Services.PlayerState.ContentId)
            return contentId;
        return LocalContentIdHash.Hash(contentId, Configuration.FcMemberContentIdSalt);
    }

    internal bool IsCharacterVisible(ulong contentId)
        => CharacterVisibility.IsVisible(contentId);
}

internal static class BuildInfo
{
    public const string Version = "0.0.0.42";
}
