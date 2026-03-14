using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace XADatabase.Services;

/// <summary>
/// Exposes IPC calls so other plugins (e.g. XA Slave) can interact with XA Database.
/// All channel names are prefixed with "XA.Database." for namespacing.
///
/// Available IPC calls:
///   XA.Database.Save              (Action)         — Refresh live data + save snapshot to DB
///   XA.Database.Refresh           (Action)         — Refresh live data only (no DB save)
///   XA.Database.IsReady           (Func → bool)    — True when player is loaded
///   XA.Database.GetDbPath         (Func → string)  — Absolute path to xa.db
///   XA.Database.GetVersion        (Func → string)  — Plugin version string
///   XA.Database.GetCharacterName  (Func → string)  — Current character name
///   XA.Database.GetGil            (Func → int)     — Current character gil
///   XA.Database.GetRetainerGil    (Func → int)     — Total retainer gil
///   XA.Database.GetFcInfo         (Func → string)  — FC name|tag|points|rank pipe-delimited
///   XA.Database.GetPlotInfo       (Func → string)  — FC estate info
///   XA.Database.GetPersonalPlotInfo (Func → string) — Personal estate + apartment pipe-delimited
///   XA.Database.SearchItems       (Func(string) → string) — Cross-character item search
/// </summary>
public sealed class IpcProvider : IDisposable
{
    private readonly IPluginLog log;

    // Core IPC channels
    private readonly ICallGateProvider<object> saveProvider;
    private readonly ICallGateProvider<object> refreshProvider;
    private readonly ICallGateProvider<bool> isReadyProvider;
    private readonly ICallGateProvider<string> getDbPathProvider;
    private readonly ICallGateProvider<string> getVersionProvider;

    // Data query IPC channels
    private readonly ICallGateProvider<string> getCharacterNameProvider;
    private readonly ICallGateProvider<int> getGilProvider;
    private readonly ICallGateProvider<int> getRetainerGilProvider;
    private readonly ICallGateProvider<string> getFcInfoProvider;
    private readonly ICallGateProvider<string> getFcNameProvider;
    private readonly ICallGateProvider<string> getFcTagProvider;
    private readonly ICallGateProvider<int> getFcPointsProvider;
    private readonly ICallGateProvider<string> getPlotInfoProvider;
    private readonly ICallGateProvider<string> getPersonalPlotInfoProvider;
    private readonly ICallGateProvider<string> getApartmentProvider;
    private readonly ICallGateProvider<string> getCharacterSummaryJsonProvider;
    private readonly ICallGateProvider<string> getLastSnapshotResultJsonProvider;
    private readonly ICallGateProvider<string, string> searchItemsProvider;

    public IpcProvider(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.log = log;

        // Core channels
        saveProvider = pluginInterface.GetIpcProvider<object>("XA.Database.Save");
        refreshProvider = pluginInterface.GetIpcProvider<object>("XA.Database.Refresh");
        isReadyProvider = pluginInterface.GetIpcProvider<bool>("XA.Database.IsReady");
        getDbPathProvider = pluginInterface.GetIpcProvider<string>("XA.Database.GetDbPath");
        getVersionProvider = pluginInterface.GetIpcProvider<string>("XA.Database.GetVersion");

        // Data query channels
        getCharacterNameProvider = pluginInterface.GetIpcProvider<string>("XA.Database.GetCharacterName");
        getGilProvider = pluginInterface.GetIpcProvider<int>("XA.Database.GetGil");
        getRetainerGilProvider = pluginInterface.GetIpcProvider<int>("XA.Database.GetRetainerGil");
        getFcInfoProvider = pluginInterface.GetIpcProvider<string>("XA.Database.GetFcInfo");
        getFcNameProvider = pluginInterface.GetIpcProvider<string>("XA.Database.GetFcName");
        getFcTagProvider = pluginInterface.GetIpcProvider<string>("XA.Database.GetFcTag");
        getFcPointsProvider = pluginInterface.GetIpcProvider<int>("XA.Database.GetFcPoints");
        getPlotInfoProvider = pluginInterface.GetIpcProvider<string>("XA.Database.GetPlotInfo");
        getPersonalPlotInfoProvider = pluginInterface.GetIpcProvider<string>("XA.Database.GetPersonalPlotInfo");
        getApartmentProvider = pluginInterface.GetIpcProvider<string>("XA.Database.GetApartment");
        getCharacterSummaryJsonProvider = pluginInterface.GetIpcProvider<string>("XA.Database.GetCharacterSummaryJson");
        getLastSnapshotResultJsonProvider = pluginInterface.GetIpcProvider<string>("XA.Database.GetLastSnapshotResultJson");
        searchItemsProvider = pluginInterface.GetIpcProvider<string, string>("XA.Database.SearchItems");

        log.Information("[XA] IPC provider registered (XA.Database.*).");
    }

    /// <summary>
    /// Wire up the IPC handlers. Called after MainWindow is ready so the callbacks have valid targets.
    /// </summary>
    public void Initialize(
        Action onSave,
        Action onRefresh,
        Func<bool> isReady,
        Func<string> getDbPath,
        string version,
        Func<string> getCharacterName,
        Func<int> getGil,
        Func<int> getRetainerGil,
        Func<string> getFcInfo,
        Func<string> getFcName,
        Func<string> getFcTag,
        Func<int> getFcPoints,
        Func<string> getPlotInfo,
        Func<string> getPersonalPlotInfo,
        Func<string> getApartment,
        Func<string> getCharacterSummaryJson,
        Func<string> getLastSnapshotResultJson,
        Func<string, string> searchItems)
    {
        // Core actions
        saveProvider.RegisterAction(() =>
        {
            log.Information("[XA] IPC: Save requested by external plugin.");
            onSave();
        });

        refreshProvider.RegisterAction(() =>
        {
            log.Information("[XA] IPC: Refresh requested by external plugin.");
            onRefresh();
        });

        // Core queries
        isReadyProvider.RegisterFunc(() => isReady());
        getDbPathProvider.RegisterFunc(() => getDbPath());
        getVersionProvider.RegisterFunc(() => version);

        // Data queries
        getCharacterNameProvider.RegisterFunc(() => getCharacterName());
        getGilProvider.RegisterFunc(() => getGil());
        getRetainerGilProvider.RegisterFunc(() => getRetainerGil());
        getFcInfoProvider.RegisterFunc(() => getFcInfo());
        getFcNameProvider.RegisterFunc(() => getFcName());
        getFcTagProvider.RegisterFunc(() => getFcTag());
        getFcPointsProvider.RegisterFunc(() => getFcPoints());
        getPlotInfoProvider.RegisterFunc(() => getPlotInfo());
        getPersonalPlotInfoProvider.RegisterFunc(() => getPersonalPlotInfo());
        getApartmentProvider.RegisterFunc(() => getApartment());
        getCharacterSummaryJsonProvider.RegisterFunc(() => getCharacterSummaryJson());
        getLastSnapshotResultJsonProvider.RegisterFunc(() => getLastSnapshotResultJson());
        searchItemsProvider.RegisterFunc((query) => searchItems(query));

        log.Information($"[XA] IPC handlers initialized ({IpcContractInfo.ChannelCount} channels).");
    }

    public void Dispose()
    {
        saveProvider.UnregisterAction();
        refreshProvider.UnregisterAction();
        isReadyProvider.UnregisterFunc();
        getDbPathProvider.UnregisterFunc();
        getVersionProvider.UnregisterFunc();
        getCharacterNameProvider.UnregisterFunc();
        getGilProvider.UnregisterFunc();
        getRetainerGilProvider.UnregisterFunc();
        getFcInfoProvider.UnregisterFunc();
        getFcNameProvider.UnregisterFunc();
        getFcTagProvider.UnregisterFunc();
        getFcPointsProvider.UnregisterFunc();
        getPlotInfoProvider.UnregisterFunc();
        getPersonalPlotInfoProvider.UnregisterFunc();
        getApartmentProvider.UnregisterFunc();
        getCharacterSummaryJsonProvider.UnregisterFunc();
        getLastSnapshotResultJsonProvider.UnregisterFunc();
        searchItemsProvider.UnregisterFunc();

        log.Information("[XA] IPC provider disposed.");
    }
}
