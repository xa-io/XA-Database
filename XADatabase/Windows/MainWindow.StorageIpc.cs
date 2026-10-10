using System.Collections.Generic;
using XADatabase.Core.Policies;
using XADatabase.Models;

namespace XADatabase.Windows;

public partial class MainWindow
{
    private readonly StorageObservationCache liveStorageObservations = new();

    public string SearchCharacterStorageItemsJson(string requestJson)
    {
        if (!StorageItemAccess.TryParse(requestJson, out var request, out var error))
            return StorageItemAccess.Failure(error);

        var currentContentId = plugin.Services.ClientState.IsLoggedIn && plugin.Services.PlayerState.IsLoaded
            ? plugin.Services.PlayerState.ContentId : 0;
        var contentId = request.ContentId != 0 ? request.ContentId : currentContentId;
        if (contentId == 0 || !plugin.IsCharacterVisible(contentId))
            return StorageItemAccess.Serialize(StorageItemAccess.Resolve(request, contentId, false, "", "", "", []));

        var saved = plugin.Snapshots.Get(contentId);
        var isCurrent = InventoryCapturePolicy.IsSameOwner(contentId, currentContentId);
        var name = isCurrent ? plugin.Services.PlayerState.CharacterName.ToString() : saved?.Row.CharacterName ?? "";
        var world = isCurrent ? ResolveCurrentHomeWorldName() : saved?.Row.World ?? "";
        var observations = new List<StorageItemsObservation>();
        if (isCurrent)
            observations.AddRange(liveStorageObservations.ForOwner(contentId));
        if (saved != null)
            observations.Add(new StorageItemsObservation(saved.Row.ContentId, saved.AllItems, saved.InventorySummaries,
                "saved", Readable: !saved.ParseErrors.ContainsKey("items_json") && !saved.ParseErrors.ContainsKey("inventory_summaries_json")));

        return StorageItemAccess.Serialize(StorageItemAccess.Resolve(request, contentId, isCurrent || saved != null,
            name, world, saved?.Row.UpdatedUtc ?? "", observations));
    }
}
