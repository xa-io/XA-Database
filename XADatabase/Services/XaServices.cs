using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace XADatabase.Services;

/// <summary>Injected host-service context for application and collector code.</summary>
public sealed record XaServices(
    IDalamudPluginInterface PluginInterface,
    IClientState ClientState,
    IPlayerState PlayerState,
    IDataManager DataManager,
    IObjectTable ObjectTable,
    ICondition Condition,
    IFramework Framework,
    IChatGui ChatGui,
    IPluginLog Log,
    IGameGui GameGui);
