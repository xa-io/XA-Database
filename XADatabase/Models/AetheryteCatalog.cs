using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace XADatabase.Models;

public sealed record AetheryteCatalogDestination(
    uint AetheryteId, ushort TerritoryId, string Name, string TerritoryName);

public enum AetheryteAccessStatus
{
    Collected,
    Uncollected,
    Unknown,
}

public sealed record AetheryteCatalogRow(AetheryteEntry Destination, AetheryteAccessStatus Status);

public sealed record AetheryteCatalogView(
    IReadOnlyList<AetheryteCatalogRow> Rows,
    int CatalogTotal,
    int CollectedCatalogIds,
    int ObservedDestinations,
    bool IsKnown);

/// <summary>Projects sheet metadata beside observed access without changing the persisted observation.</summary>
public static class AetheryteCatalog
{
    public static bool IsCatalogDestination(uint id, bool isAetheryte, bool invisible,
        uint territoryId, uint mapId, uint placeNameId, string? name)
        => id != 0 && isAetheryte && !invisible
            && territoryId is > 0 and <= ushort.MaxValue && mapId != 0 && placeNameId != 0
            && !IsResidentialTerritory(territoryId)
            && !string.IsNullOrWhiteSpace(name);

    // Residential sheet rows cannot establish any character's personalized estate access.
    private static bool IsResidentialTerritory(uint territoryId)
        => territoryId is 339 or 340 or 341 or 641 or 979;

    public static AetheryteCatalogView BuildView(AetheryteSnapshot? snapshot,
        IEnumerable<AetheryteCatalogDestination>? definitions, bool includeUncollected, string? filter)
    {
        var observation = AetheryteSnapshot.Normalize(snapshot);
        var catalog = (definitions ?? [])
            .Where(entry => entry != null && entry.AetheryteId != 0 && entry.TerritoryId != 0
                && !IsResidentialTerritory(entry.TerritoryId)
                && !string.IsNullOrWhiteSpace(entry.Name))
            .OrderBy(entry => entry.AetheryteId)
            .ThenBy(entry => entry.Name, StringComparer.Ordinal)
            .DistinctBy(entry => entry.AetheryteId)
            .ToList();
        var observedIds = observation.Destinations.Select(entry => entry.AetheryteId).ToHashSet();
        var collectedCatalogIds = catalog.Count(entry => observedIds.Contains(entry.AetheryteId));
        var rows = observation.Destinations
            .Select(entry => new AetheryteCatalogRow(entry, AetheryteAccessStatus.Collected))
            .ToList();

        if (includeUncollected)
        {
            var missingStatus = observation.IsKnown ? AetheryteAccessStatus.Uncollected : AetheryteAccessStatus.Unknown;
            foreach (var entry in catalog.Where(entry => !observedIds.Contains(entry.AetheryteId)))
            {
                rows.Add(new AetheryteCatalogRow(new AetheryteEntry
                {
                    AetheryteId = entry.AetheryteId,
                    TerritoryId = entry.TerritoryId,
                    Name = entry.Name.Trim(),
                    TerritoryName = string.IsNullOrWhiteSpace(entry.TerritoryName)
                        ? $"Territory {entry.TerritoryId}" : entry.TerritoryName.Trim(),
                }, missingStatus));
            }
        }

        var query = filter?.Trim() ?? string.Empty;
        var visible = rows.Where(row => query.Length == 0
                || row.Destination.AetheryteId.ToString(CultureInfo.InvariantCulture).Contains(query, StringComparison.Ordinal)
                || LocalizedText.Contains(row.Destination.Name, query)
                || LocalizedText.Contains(row.Destination.TerritoryName, query))
            .OrderBy(row => row.Destination.TerritoryName, LocalizedText.Comparer)
            .ThenBy(row => row.Destination.Name, LocalizedText.Comparer)
            .ThenBy(row => row.Destination.AetheryteId)
            .ThenBy(row => row.Destination.SubIndex)
            .ThenBy(row => row.Destination.Ward)
            .ThenBy(row => row.Destination.Plot)
            .ThenBy(row => row.Destination.HouseId)
            .ToList();

        return new AetheryteCatalogView(visible, catalog.Count, collectedCatalogIds,
            observation.Destinations.Count, observation.IsKnown);
    }
}
