using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using XADatabase.Core.Policies;

namespace XADatabase.Services;

/// <summary>Registers and contains the 21 XA.Database.* IPC contract channels.</summary>
public sealed class IpcProvider : IDisposable
{
    private readonly IPluginLog log;
    private readonly IFramework framework;
    private readonly IpcRegistrationCleanup registrations = new();
    private readonly object lifecycleGate = new();
    private bool initialized;
    private bool disposed;

    private readonly ICallGateProvider<object> saveProvider;
    private readonly ICallGateProvider<object> refreshProvider;
    private readonly ICallGateProvider<bool> isReadyProvider;
    private readonly ICallGateProvider<string> getDbPathProvider;
    private readonly ICallGateProvider<string> getVersionProvider;
    private readonly ICallGateProvider<string> getCharacterNameProvider;
    private readonly ICallGateProvider<int> getGilProvider;
    private readonly ICallGateProvider<long> getRetainerGilProvider;
    private readonly ICallGateProvider<string> getFcInfoProvider;
    private readonly ICallGateProvider<string> getFcNameProvider;
    private readonly ICallGateProvider<string> getFcTagProvider;
    private readonly ICallGateProvider<int> getFcPointsProvider;
    private readonly ICallGateProvider<string> getPlotInfoProvider;
    private readonly ICallGateProvider<string> getPersonalPlotInfoProvider;
    private readonly ICallGateProvider<string> getApartmentProvider;
    private readonly ICallGateProvider<string> getCharacterSummaryJsonProvider;
    private readonly ICallGateProvider<string> getAccountCharacterListJsonProvider;
    private readonly ICallGateProvider<string> getLastSnapshotResultJsonProvider;
    private readonly ICallGateProvider<string, string> searchItemsProvider;
    private readonly ICallGateProvider<string, string> getMatchingCharactersForItemsProvider;
    private readonly ICallGateProvider<string, string> searchCurrentCharacterItemsJsonProvider;

    public IpcProvider(IDalamudPluginInterface pluginInterface, IPluginLog log, IFramework framework)
    {
        this.log = log;
        this.framework = framework;
        saveProvider = pluginInterface.GetIpcProvider<object>(IpcContractInfo.Save);
        refreshProvider = pluginInterface.GetIpcProvider<object>(IpcContractInfo.Refresh);
        isReadyProvider = pluginInterface.GetIpcProvider<bool>(IpcContractInfo.IsReady);
        getDbPathProvider = pluginInterface.GetIpcProvider<string>(IpcContractInfo.GetDbPath);
        getVersionProvider = pluginInterface.GetIpcProvider<string>(IpcContractInfo.GetVersion);
        getCharacterNameProvider = pluginInterface.GetIpcProvider<string>(IpcContractInfo.GetCharacterName);
        getGilProvider = pluginInterface.GetIpcProvider<int>(IpcContractInfo.GetGil);
        getRetainerGilProvider = pluginInterface.GetIpcProvider<long>(IpcContractInfo.GetRetainerGil);
        getFcInfoProvider = pluginInterface.GetIpcProvider<string>(IpcContractInfo.GetFcInfo);
        getFcNameProvider = pluginInterface.GetIpcProvider<string>(IpcContractInfo.GetFcName);
        getFcTagProvider = pluginInterface.GetIpcProvider<string>(IpcContractInfo.GetFcTag);
        getFcPointsProvider = pluginInterface.GetIpcProvider<int>(IpcContractInfo.GetFcPoints);
        getPlotInfoProvider = pluginInterface.GetIpcProvider<string>(IpcContractInfo.GetPlotInfo);
        getPersonalPlotInfoProvider = pluginInterface.GetIpcProvider<string>(IpcContractInfo.GetPersonalPlotInfo);
        getApartmentProvider = pluginInterface.GetIpcProvider<string>(IpcContractInfo.GetApartment);
        getCharacterSummaryJsonProvider = pluginInterface.GetIpcProvider<string>(IpcContractInfo.GetCharacterSummaryJson);
        getAccountCharacterListJsonProvider = pluginInterface.GetIpcProvider<string>(IpcContractInfo.GetAccountCharacterListJson);
        getLastSnapshotResultJsonProvider = pluginInterface.GetIpcProvider<string>(IpcContractInfo.GetLastSnapshotResultJson);
        searchItemsProvider = pluginInterface.GetIpcProvider<string, string>(IpcContractInfo.SearchItems);
        getMatchingCharactersForItemsProvider = pluginInterface.GetIpcProvider<string, string>(IpcContractInfo.GetMatchingCharactersForItems);
        searchCurrentCharacterItemsJsonProvider = pluginInterface.GetIpcProvider<string, string>(IpcContractInfo.SearchCurrentCharacterItemsJson);
    }

    public void Initialize(IpcFacade facade)
    {
        lock (lifecycleGate)
        {
            if (disposed || initialized || registrations.IsDisposed)
                return;

            try
            {
                saveProvider.RegisterAction(() => RunSafe(IpcContractInfo.Save, () =>
                {
                    log.Information("[XA] IPC: Save requested by external plugin.");
                    facade.Save();
                }));
                registrations.Add(IpcContractInfo.Save, saveProvider.UnregisterAction);
                refreshProvider.RegisterAction(() => RunSafe(IpcContractInfo.Refresh, () =>
                {
                    log.Information("[XA] IPC: Refresh requested by external plugin.");
                    facade.Refresh();
                }));
                registrations.Add(IpcContractInfo.Refresh, refreshProvider.UnregisterAction);

                Register(isReadyProvider, IpcContractInfo.IsReady, facade.IsReady, false);
                Register(getDbPathProvider, IpcContractInfo.GetDbPath, facade.GetDbPath, string.Empty);
                Register(getVersionProvider, IpcContractInfo.GetVersion, () => facade.Version, string.Empty);
                Register(getCharacterNameProvider, IpcContractInfo.GetCharacterName, facade.GetCharacterName, string.Empty);
                Register(getGilProvider, IpcContractInfo.GetGil, facade.GetGil, 0);
                Register(getRetainerGilProvider, IpcContractInfo.GetRetainerGil, facade.GetRetainerGil, 0L);
                Register(getFcInfoProvider, IpcContractInfo.GetFcInfo, facade.GetFcInfo, string.Empty);
                Register(getFcNameProvider, IpcContractInfo.GetFcName, facade.GetFcName, string.Empty);
                Register(getFcTagProvider, IpcContractInfo.GetFcTag, facade.GetFcTag, string.Empty);
                Register(getFcPointsProvider, IpcContractInfo.GetFcPoints, facade.GetFcPoints, 0);
                Register(getPlotInfoProvider, IpcContractInfo.GetPlotInfo, facade.GetPlotInfo, string.Empty);
                Register(getPersonalPlotInfoProvider, IpcContractInfo.GetPersonalPlotInfo, facade.GetPersonalPlotInfo, string.Empty);
                Register(getApartmentProvider, IpcContractInfo.GetApartment, facade.GetApartment, string.Empty);
                Register(getCharacterSummaryJsonProvider, IpcContractInfo.GetCharacterSummaryJson, facade.GetCharacterSummaryJson, string.Empty);
                Register(getAccountCharacterListJsonProvider, IpcContractInfo.GetAccountCharacterListJson, facade.GetAccountCharacterListJson, string.Empty);
                Register(getLastSnapshotResultJsonProvider, IpcContractInfo.GetLastSnapshotResultJson, facade.GetLastSnapshotResultJson, string.Empty);
                Register(searchItemsProvider, IpcContractInfo.SearchItems, facade.SearchItems, string.Empty);
                Register(getMatchingCharactersForItemsProvider, IpcContractInfo.GetMatchingCharactersForItems, facade.GetMatchingCharactersForItems, string.Empty);
                Register(searchCurrentCharacterItemsJsonProvider, IpcContractInfo.SearchCurrentCharacterItemsJson, facade.SearchCurrentCharacterItemsJson, string.Empty);
                initialized = true;
                log.Information($"[XA] IPC handlers initialized ({IpcContractInfo.ChannelCount} channels, contract v{IpcContractInfo.CurrentVersion}).");
            }
            catch (Exception ex)
            {
                log.Error(ex, "[XA] IPC initialization failed; partial registrations are being removed.");
                registrations.Dispose(ReportCleanupFailure);
            }
        }
    }

    public void Dispose()
    {
        lock (lifecycleGate)
        {
            if (disposed)
                return;
            disposed = true;
        }

        registrations.Dispose(ReportCleanupFailure);
        log.Information("[XA] IPC provider disposed.");
    }

    private void Register<T>(ICallGateProvider<T> provider, string channel, Func<T> function, T fallback)
    {
        provider.RegisterFunc(() => RunSafe(channel, function, fallback));
        registrations.Add(channel, provider.UnregisterFunc);
    }

    private void Register<TArg, TResult>(
        ICallGateProvider<TArg, TResult> provider,
        string channel,
        Func<TArg, TResult> function,
        TResult fallback)
    {
        provider.RegisterFunc(argument => RunSafe(channel, () => function(argument), fallback));
        registrations.Add(channel, provider.UnregisterFunc);
    }

    private void RunSafe(string channel, Action action)
        => IpcInvocationPolicy.RunSafe(
            channel,
            () => framework.RunOnFrameworkThread(action).GetAwaiter().GetResult(),
            ReportInvocationFailure);

    private T RunSafe<T>(string channel, Func<T> function, T fallback)
        => IpcInvocationPolicy.RunSafe(
            channel,
            () => framework.RunOnFrameworkThread(function).GetAwaiter().GetResult(),
            fallback,
            ReportInvocationFailure);

    private void ReportInvocationFailure(string channel, Exception error)
        => log.Error(error, $"[XA] IPC channel {channel} failed; returning its safe contract fallback.");

    private void ReportCleanupFailure(string channel, Exception error)
        => log.Warning(error, $"[XA] IPC channel {channel} could not be unregistered cleanly.");
}
