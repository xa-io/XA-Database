using System.Collections.Generic;

namespace XADatabase.Services;

internal static class IpcContractInfo
{
    public const int CurrentVersion = 6;
    public const int CharacterSummaryJsonVersion = 2;
    public const int AccountCharacterListJsonVersion = 1;
    public const int LastSnapshotResultJsonVersion = 4;
    public const int CurrentCharacterItemsJsonVersion = 1;
    public const int ChannelCount = 21;

    public const string Save = "XA.Database.Save";
    public const string Refresh = "XA.Database.Refresh";
    public const string IsReady = "XA.Database.IsReady";
    public const string GetDbPath = "XA.Database.GetDbPath";
    public const string GetVersion = "XA.Database.GetVersion";
    public const string GetCharacterName = "XA.Database.GetCharacterName";
    public const string GetGil = "XA.Database.GetGil";
    public const string GetRetainerGil = "XA.Database.GetRetainerGil";
    public const string GetFcInfo = "XA.Database.GetFcInfo";
    public const string GetFcName = "XA.Database.GetFcName";
    public const string GetFcTag = "XA.Database.GetFcTag";
    public const string GetFcPoints = "XA.Database.GetFcPoints";
    public const string GetPlotInfo = "XA.Database.GetPlotInfo";
    public const string GetPersonalPlotInfo = "XA.Database.GetPersonalPlotInfo";
    public const string GetApartment = "XA.Database.GetApartment";
    public const string GetCharacterSummaryJson = "XA.Database.GetCharacterSummaryJson";
    public const string GetAccountCharacterListJson = "XA.Database.GetAccountCharacterListJson";
    public const string GetLastSnapshotResultJson = "XA.Database.GetLastSnapshotResultJson";
    public const string SearchItems = "XA.Database.SearchItems";
    public const string GetMatchingCharactersForItems = "XA.Database.GetMatchingCharactersForItems";
    public const string SearchCurrentCharacterItemsJson = "XA.Database.SearchCurrentCharacterItemsJson";

    public static IReadOnlyList<string> ChannelNames { get; } = new[]
    {
        Save, Refresh, IsReady, GetDbPath, GetVersion, GetCharacterName, GetGil,
        GetRetainerGil, GetFcInfo, GetFcName, GetFcTag, GetFcPoints, GetPlotInfo,
        GetPersonalPlotInfo, GetApartment, GetCharacterSummaryJson,
        GetAccountCharacterListJson, GetLastSnapshotResultJson, SearchItems,
        GetMatchingCharactersForItems, SearchCurrentCharacterItemsJson,
    };
}
