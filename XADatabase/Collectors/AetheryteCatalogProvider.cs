using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using XADatabase.Models;

namespace XADatabase.Collectors;

public sealed record AetheryteCatalogSource(IReadOnlyList<AetheryteCatalogDestination> Destinations, string Error = "");

/// <summary>Localized sheet metadata cached once for the injected data-manager instance.</summary>
public static class AetheryteCatalogProvider
{
    private static readonly ConditionalWeakTable<IDataManager, AetheryteCatalogSource> Catalogs = new();

    public static AetheryteCatalogSource GetCatalog(IDataManager dataManager)
        => Catalogs.GetValue(dataManager, BuildCatalog);

    private static AetheryteCatalogSource BuildCatalog(IDataManager dataManager)
    {
        try
        {
            var destinations = new List<AetheryteCatalogDestination>();
            var aetherytes = dataManager.GetExcelSheet<Aetheryte>();
            var territories = dataManager.GetExcelSheet<TerritoryType>();
            var maps = dataManager.GetExcelSheet<Map>();
            var placeNames = dataManager.GetExcelSheet<PlaceName>();
            foreach (var row in aetherytes)
            {
                // Housing placeholders and aethernet shards are not standard attunements.
                // Personalized estates are supplied only by the captured teleport list.
                if (!row.IsAetheryte || row.Invisible
                    || !territories.TryGetRow(row.Territory.RowId, out var territory)
                    || !maps.TryGetRow(row.Map.RowId, out _)
                    || !placeNames.TryGetRow(row.PlaceName.RowId, out var placeName))
                    continue;

                var name = placeName.Name.ToString().Trim();
                if (!AetheryteCatalog.IsCatalogDestination(row.RowId, row.IsAetheryte, row.Invisible,
                        row.Territory.RowId, row.Map.RowId, row.PlaceName.RowId, name))
                    continue;

                var territoryName = placeNames.TryGetRow(territory.PlaceName.RowId, out var zone)
                    ? zone.Name.ToString().Trim() : string.Empty;
                destinations.Add(new AetheryteCatalogDestination(row.RowId,
                    (ushort)row.Territory.RowId, name, territoryName));
            }

            return new AetheryteCatalogSource(destinations.AsReadOnly());
        }
        catch (Exception ex)
        {
            // Cache failure as well, preventing sheet retries and exceptions on every draw.
            return new AetheryteCatalogSource(Array.Empty<AetheryteCatalogDestination>(), ex.Message);
        }
    }
}
