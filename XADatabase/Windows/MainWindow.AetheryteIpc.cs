using System.Collections.Generic;
using System.Linq;
using XADatabase.Core.Policies;
using XADatabase.Models;

namespace XADatabase.Windows;

public partial class MainWindow
{
    public string GetCharacterAetherytesJson(ulong contentId)
    {
        return AetheryteAccess.SerializeCharacter(ResolveCharacterAetherytes(contentId));
    }

    public string GetAllCharacterAetherytesJson()
    {
        var currentContentId = GetLoadedAetheryteCharacterId();
        var contentIds = plugin.Snapshots.All().Keys
            .Append(currentContentId)
            .Where(contentId => contentId != 0 && plugin.IsCharacterVisible(contentId))
            .Distinct();
        return AetheryteAccess.SerializeCharacters(contentIds.Select(ResolveCharacterAetherytes));
    }

    public bool HasAetheryte(uint aetheryteId)
    {
        return CharacterHasAetheryte(0, aetheryteId);
    }

    public bool CharacterHasAetheryte(ulong contentId, uint aetheryteId)
    {
        return aetheryteId != 0 && AetheryteAccess.HasAccess(ResolveCharacterAetherytes(contentId), aetheryteId);
    }

    private ulong GetLoadedAetheryteCharacterId()
    {
        return plugin.Services.ClientState.IsLoggedIn && plugin.Services.PlayerState.IsLoaded
            ? plugin.Services.PlayerState.ContentId : 0;
    }

    private CharacterAetheryteAccess ResolveCharacterAetherytes(ulong requestedContentId)
    {
        var currentContentId = GetLoadedAetheryteCharacterId();
        var contentId = AetheryteAccess.ResolveContentId(requestedContentId, currentContentId != 0, currentContentId);
        if (contentId == 0 || !plugin.IsCharacterVisible(contentId))
            return AetheryteAccess.Resolve(contentId, false, string.Empty, string.Empty, []);

        var saved = plugin.Snapshots.Get(contentId);
        var name = saved?.Row.CharacterName ?? string.Empty;
        var world = saved?.Row.World ?? string.Empty;
        var candidates = new List<AetheryteAccessObservation>();
        if (InventoryCapturePolicy.IsSameOwner(contentId, currentContentId))
        {
            name = plugin.Services.PlayerState.CharacterName.ToString();
            world = ResolveCurrentHomeWorldName();
            if (hasLiveAetheryteCapture && InventoryCapturePolicy.IsSameOwner(cacheOwnerContentId, contentId))
                candidates.Add(new AetheryteAccessObservation(cacheOwnerContentId, cachedAetherytes, "memory"));
            if (retainedLiveCharacterCache is { HasLiveAetheryteCapture: true } retained
                && InventoryCapturePolicy.IsSameOwner(retained.OwnerContentId, contentId))
                candidates.Add(new AetheryteAccessObservation(retained.OwnerContentId, retained.Aetherytes, "memory"));
        }

        if (saved != null)
            candidates.Add(new AetheryteAccessObservation(saved.Row.ContentId, saved.Aetherytes, "saved"));
        return AetheryteAccess.Resolve(contentId, true, name, world, candidates);
    }
}
