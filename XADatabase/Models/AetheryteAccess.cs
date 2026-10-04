using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using XADatabase.Core.Policies;
using XADatabase.Database;
using XADatabase.Services;

namespace XADatabase.Models;

public sealed record AetheryteAccessObservation(ulong OwnerContentId, AetheryteSnapshot Snapshot, string Source);

public sealed class CharacterAetheryteAccess
{
    public ulong ContentId { get; init; }
    public string CharacterName { get; init; } = string.Empty;
    public string World { get; init; } = string.Empty;
    public bool Known { get; init; }
    public string CapturedUtc { get; init; } = string.Empty;
    public string Source { get; init; } = "unknown";
    public IReadOnlyList<uint> AetheryteIds { get; init; } = Array.Empty<uint>();
    public IReadOnlyList<AetheryteEntry> Destinations { get; init; } = Array.Empty<AetheryteEntry>();
}

public static class AetheryteAccess
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static ulong ResolveContentId(ulong requestedContentId, bool currentCharacterLoaded, ulong currentContentId)
    {
        return requestedContentId != 0 ? requestedContentId : currentCharacterLoaded ? currentContentId : 0;
    }

    // Pass memory candidates before saved data so equal capture times prefer memory.
    public static CharacterAetheryteAccess Resolve(ulong contentId, bool visible, string characterName,
        string world, IEnumerable<AetheryteAccessObservation> observations)
    {
        if (contentId == 0 || !visible)
            return new CharacterAetheryteAccess { ContentId = contentId };

        var latest = observations
            .Where(observation => InventoryCapturePolicy.IsSameOwner(observation.OwnerContentId, contentId)
                && observation.Snapshot.IsKnown)
            .OrderByDescending(observation =>
            {
                SnapshotTime.TryParseUtc(observation.Snapshot.CapturedUtc, out var utc);
                return utc;
            })
            .FirstOrDefault();
        if (latest == null)
            return new CharacterAetheryteAccess { ContentId = contentId, CharacterName = characterName, World = world };

        var snapshot = AetheryteSnapshot.Normalize(latest.Snapshot);
        return new CharacterAetheryteAccess
        {
            ContentId = contentId,
            CharacterName = characterName,
            World = world,
            Known = true,
            CapturedUtc = snapshot.CapturedUtc,
            Source = latest.Source,
            AetheryteIds = snapshot.Destinations.Select(entry => entry.AetheryteId).Distinct().Order().ToArray(),
            Destinations = snapshot.Destinations,
        };
    }

    public static bool HasAccess(CharacterAetheryteAccess character, uint aetheryteId)
    {
        return character.ContentId != 0 && character.Known && aetheryteId != 0
            && character.AetheryteIds.Contains(aetheryteId);
    }

    public static string SerializeCharacter(CharacterAetheryteAccess character)
    {
        return JsonSerializer.Serialize(new
        {
            ipcContractVersion = IpcContractInfo.CurrentVersion,
            aetheryteListVersion = IpcContractInfo.AetheryteListJsonVersion,
            character.ContentId,
            character.CharacterName,
            character.World,
            character.Known,
            character.CapturedUtc,
            character.Source,
            character.AetheryteIds,
            character.Destinations,
        }, JsonOptions);
    }

    public static string SerializeCharacters(IEnumerable<CharacterAetheryteAccess> characters)
    {
        return JsonSerializer.Serialize(new
        {
            ipcContractVersion = IpcContractInfo.CurrentVersion,
            aetheryteListVersion = IpcContractInfo.AetheryteListJsonVersion,
            characters = characters.OrderBy(character => character.ContentId).ToArray(),
        }, JsonOptions);
    }
}
