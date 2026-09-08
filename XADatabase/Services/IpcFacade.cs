using Dalamud.Plugin.Services;
using XADatabase.Database;
using XADatabase.Windows;

namespace XADatabase.Services;

/// <summary>Stable application boundary for the XA.Database.* IPC contract.</summary>
public sealed class IpcFacade
{
    private readonly MainWindow mainWindow;
    private readonly DatabaseService database;
    private readonly IPlayerState playerState;
    private readonly IClientState clientState;

    public IpcFacade(MainWindow mainWindow, DatabaseService database, IPlayerState playerState, IClientState clientState)
    {
        this.mainWindow = mainWindow;
        this.database = database;
        this.playerState = playerState;
        this.clientState = clientState;
    }

    public string Version => BuildInfo.Version;
    public void Save() => mainWindow.SaveFromIpc();
    public void Refresh() => mainWindow.RefreshData();
    public bool IsReady() => clientState.IsLoggedIn && playerState.IsLoaded;
    public string GetDbPath() => database.GetDbPath();
    public string GetCharacterName() => mainWindow.GetCharacterName();
    public int GetGil() => mainWindow.GetGil();
    public long GetRetainerGil() => mainWindow.GetRetainerGil();
    public string GetFcInfo() => mainWindow.GetFcInfo();
    public string GetFcName() => mainWindow.GetFcName();
    public string GetFcTag() => mainWindow.GetFcTag();
    public int GetFcPoints() => mainWindow.GetFcPoints();
    public string GetPlotInfo() => mainWindow.GetPlotInfo();
    public string GetPersonalPlotInfo() => mainWindow.GetPersonalPlotInfo();
    public string GetApartment() => mainWindow.GetApartment();
    public string GetCharacterSummaryJson() => mainWindow.GetCharacterSummaryJson();
    public string GetAccountCharacterListJson() => mainWindow.GetAccountCharacterListJson();
    public string GetLastSnapshotResultJson() => mainWindow.GetLastSnapshotResultJson();
    public string SearchItems(string query) => mainWindow.SearchItems(query);
    public string GetMatchingCharactersForItems(string payload) => mainWindow.GetMatchingCharactersForItems(payload);
    public string SearchCurrentCharacterItemsJson(string requestJson) => mainWindow.SearchCurrentCharacterItemsJson(requestJson);
}
