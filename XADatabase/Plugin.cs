using System;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
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
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IContextMenu ContextMenu { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider GameInterop { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/xadatabase";
    private const string CommandAlias = "/xadb";

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("XA");
    private MainWindow MainWindow { get; init; }

    // Database layer
    public DatabaseService DatabaseService { get; init; }
    public CharacterRepository CharacterRepo { get; init; }
    public XaCharacterSnapshotRepository SnapshotRepo { get; init; }
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
    public IpcProvider IpcProvider { get; init; }
    public ItemLocationTooltipService ItemLocationTooltip { get; init; }
    public ItemSearchContextMenuService ItemSearchContextMenu { get; init; }

    // Framework tick flags — heavy work moved out of Draw() to avoid HITCH warnings
    private bool needsInitialSeed = true;
    private DateTime lastAutoSave = DateTime.MinValue;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        var showVersionInWindowTitleDefaultChanged = false;
        if (!Configuration.ShowVersionInWindowTitleDefaultApplied)
        {
            Configuration.ShowVersionInWindowTitle = true;
            Configuration.ShowVersionInWindowTitleDefaultApplied = true;
            showVersionInWindowTitleDefaultChanged = true;
        }

        // Initialize database
        DatabaseService = new DatabaseService(PluginInterface.GetPluginConfigDirectory());
        DatabaseService.InitializeSchema();
        DatabaseService.RunHealthCheck();
        CharacterRepo = new CharacterRepository(DatabaseService);
        SnapshotRepo = new XaCharacterSnapshotRepository(DatabaseService);
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
        IpcProvider = new IpcProvider(PluginInterface, Log);

        if (showVersionInWindowTitleDefaultChanged)
            Configuration.Save();

        // Prune old currency history on startup

        MainWindow = new MainWindow(this);
        if (Configuration.OpenPluginOnLoad)
            MainWindow.IsOpen = true;
        ItemLocationTooltip = new ItemLocationTooltipService(this, GameInterop, GameGui, Log);
        ItemSearchContextMenu = new ItemSearchContextMenuService(
            ContextMenu,
            Log,
            (itemId, isHq) => MainWindow.OpenSearchForItem(itemId, isHq));

        // Wire IPC handlers now that MainWindow exists
        IpcProvider.Initialize(
            onSave: () => MainWindow.RefreshAndSave(SnapshotTrigger.XASlave, "IPC Save"),
            onRefresh: () => MainWindow.RefreshData(),
            isReady: () => PlayerState.IsLoaded,
            getDbPath: () => DatabaseService.GetDbPath(),
            version: BuildInfo.Version,
            getCharacterName: () => MainWindow.GetCharacterName(),
            getGil: () => MainWindow.GetGil(),
            getRetainerGil: () => MainWindow.GetRetainerGil(),
            getFcInfo: () => MainWindow.GetFcInfo(),
            getFcName: () => MainWindow.GetFcName(),
            getFcTag: () => MainWindow.GetFcTag(),
            getFcPoints: () => MainWindow.GetFcPoints(),
            getPlotInfo: () => MainWindow.GetPlotInfo(),
            getPersonalPlotInfo: () => MainWindow.GetPersonalPlotInfo(),
            getApartment: () => MainWindow.GetApartment(),
            getCharacterSummaryJson: () => MainWindow.GetCharacterSummaryJson(),
            getLastSnapshotResultJson: () => MainWindow.GetLastSnapshotResultJson(),
            searchItems: (query) => MainWindow.SearchItems(query),
            getMatchingCharactersForItems: (itemKeysPayload) => MainWindow.GetMatchingCharactersForItems(itemKeysPayload),
            searchCurrentCharacterItemsJson: (requestJson) => MainWindow.SearchCurrentCharacterItemsJson(requestJson)
        );

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

        // Enable addon watcher — single callback for all transient addon closes
        AddonWatcher.Enable(
            onAddonClose: (trigger) => MainWindow.OnAddonSaveTrigger(trigger),
            onAddonOpen: (trigger) => MainWindow.OnAddonOpenTrigger(trigger)
        );

        Log.Information($"[XA] Plugin loaded successfully.");
    }

    public void Dispose()
    {
        IpcProvider.Dispose();
        AddonWatcher.Dispose();
        ItemLocationTooltip.Dispose();
        ItemSearchContextMenu.Dispose();

        Framework.Update -= OnFrameworkUpdate;

        ClientState.Login -= OnLogin;
        ClientState.Logout -= OnLogout;

        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        WindowSystem.RemoveAllWindows();

        MainWindow.Dispose();

        DatabaseService.Dispose();

        CommandManager.RemoveHandler(CommandName);
        CommandManager.RemoveHandler(CommandAlias);
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!PlayerState.IsLoaded)
            return;

        // Initial seed + refresh — runs once after login/plugin load
        if (needsInitialSeed)
        {
            needsInitialSeed = false;
            MainWindow.DoInitialSeed();
            lastAutoSave = DateTime.UtcNow;
        }

        MainWindow.ProcessDeferredWork();

        // Auto-save timer — moved from Draw() to avoid HITCH warnings
        var autoInterval = Configuration.AutoSaveIntervalMinutes;
        if (autoInterval > 0 && MainWindow.DataCollected)
        {
            var elapsed = DateTime.UtcNow - lastAutoSave;
            if (elapsed.TotalMinutes >= autoInterval)
            {
                MainWindow.QueueRefreshAndSave(SnapshotTrigger.AutoSaveTimer, $"{autoInterval}m interval");
                lastAutoSave = DateTime.UtcNow;
                Log.Information($"[XA] Auto-save queued ({autoInterval}m interval).");
            }
        }
    }

    private void OnLogin()
    {
        Log.Information("[XA] Character logged in — refreshing and saving data.");
        if (Configuration.OpenPluginOnLoad)
            MainWindow.IsOpen = true;
        needsInitialSeed = true;
    }

    private void OnLogout(int type, int code)
    {
        Log.Information("[XA] Character logged out — saving final snapshot.");
        var result = MainWindow.SaveToDatabase(SnapshotTrigger.Logout, "Client logout");
        if (result.Success)
        {
            var checkpointed = DatabaseService.CheckpointWal("FULL", "logout save");
            if (!checkpointed)
                Log.Warning("[XA] Logout checkpoint did not fully merge the WAL into xa.db. External SQLite readers still see the latest data, but file-only copies may lag until the next checkpoint.");
        }
    }

    private void OnCommand(string command, string args)
    {
        MainWindow.Toggle();
    }

    public void ToggleConfigUi() => MainWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
}

internal static class BuildInfo
{
    public const string Version = "0.0.0.38";
}
