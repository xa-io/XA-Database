using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using XADatabase.Core.Collection;
using XADatabase.Core.Policies;
using XADatabase.Models;
using XADatabase.Services;

namespace XADatabase.Collectors;

public static class AetheryteCollector
{
    public static unsafe SectionResult<List<AetheryteEntry>> CollectSection(
        XaServices services, ulong expectedContentId)
    {
        try
        {
            if (!CanCapture(services, expectedContentId))
                return SectionResult<List<AetheryteEntry>>.Unavailable([], "teleport-list player context is not ready");

            var observedOwnerBefore = services.PlayerState.ContentId;
            var telepo = Telepo.Instance();
            if (telepo == null)
                return SectionResult<List<AetheryteEntry>>.Unavailable([], "teleport manager is not ready");

            // Exactly one refresh per capture, never an enumeration that refreshes on each index.
            if (telepo->UpdateAetheryteList() == null)
                return SectionResult<List<AetheryteEntry>>.Unavailable([], "teleport-list refresh is not ready");

            if (!CanCapture(services, expectedContentId))
                return SectionResult<List<AetheryteEntry>>.Unavailable([], "teleport-list owner or readiness changed");

            var first = telepo->TeleportList.First;
            var last = telepo->TeleportList.Last;
            var end = telepo->TeleportList.End;
            var count = telepo->TeleportList.LongCount;
            if (count < 0 || count > AetheryteData.MaximumDestinations
                || (count > 0 && (first == null || last == null || end == null || last > end)))
                return SectionResult<List<AetheryteEntry>>.Failed([], "teleport-list bounds are invalid");

            // Copy native structs before sheet lookup or any other host work.
            var copied = new TeleportInfo[(int)count];
            for (var index = 0; index < copied.Length; index++)
                copied[index] = first[index];

            var observationStable = CanCapture(services, expectedContentId)
                && Telepo.Instance() == telepo
                && telepo->TeleportList.First == first
                && telepo->TeleportList.Last == last
                && telepo->TeleportList.End == end;
            if (!observationStable)
                return SectionResult<List<AetheryteEntry>>.Unavailable([], "teleport-list owner or readiness changed");

            var rows = new List<AetheryteEntry>(copied.Length);
            var aetherytes = services.DataManager.GetExcelSheet<Aetheryte>();
            var territories = services.DataManager.GetExcelSheet<TerritoryType>();
            var placeNames = services.DataManager.GetExcelSheet<PlaceName>();
            foreach (var native in copied)
            {
                rows.Add(new AetheryteEntry
                {
                    AetheryteId = native.AetheryteId,
                    TerritoryId = native.TerritoryId,
                    Name = aetherytes.TryGetRow(native.AetheryteId, out var aetheryte)
                        && placeNames.TryGetRow(aetheryte.PlaceName.RowId, out var name)
                        ? name.Name.ToString() : string.Empty,
                    TerritoryName = territories.TryGetRow(native.TerritoryId, out var territory)
                        && placeNames.TryGetRow(territory.PlaceName.RowId, out var territoryName)
                        ? territoryName.Name.ToString() : string.Empty,
                    SubIndex = native.SubIndex,
                    Ward = native.Ward,
                    Plot = native.Plot,
                    EstateType = (int)native.EstateType,
                    HouseId = native.HouseId.Id,
                });
            }

            return AetheryteData.CreateObservation(expectedContentId, observedOwnerBefore,
                CanCapture(services, expectedContentId) ? services.PlayerState.ContentId : 0,
                copied.Length, rows);
        }
        catch (Exception ex)
        {
            services.Log.Error(ex, "[XA] Aetheryte collection failed.");
            return SectionResult<List<AetheryteEntry>>.Failed([], ex.Message);
        }
    }

    private static bool CanCapture(XaServices services, ulong expectedContentId)
        => services.Framework.IsInFrameworkUpdateThread
            && !services.Framework.IsFrameworkUnloading
            && InventoryReadiness.CanCapture(services)
            && InventoryCapturePolicy.IsSameOwner(expectedContentId, services.PlayerState.ContentId);
}
