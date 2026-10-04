using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using XADatabase.Core.Collection;
using XADatabase.Core.Policies;

namespace XADatabase.Models;

/// <summary>A destination available to one character at the last successful teleport-list capture.</summary>
public sealed class AetheryteEntry
{
    public uint AetheryteId { get; set; }
    public ushort TerritoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TerritoryName { get; set; } = string.Empty;
    public byte SubIndex { get; set; }
    public byte Ward { get; set; }
    public byte Plot { get; set; }
    public int EstateType { get; set; }
    public ulong HouseId { get; set; }

    [JsonIgnore]
    public bool IsSharedHouse => Ward > 0 && Plot > 0;

    [JsonIgnore]
    public bool IsApartment => SubIndex == 128 && !IsSharedHouse;
}

/// <summary>Managed snapshot validation and stable destination identity; no native state is retained.</summary>
public static class AetheryteData
{
    public const int MaximumDestinations = 4096;

    public static string FormatLocation(AetheryteEntry entry)
    {
        if (entry.IsSharedHouse)
            return $"Shared Estate - Ward {entry.Ward}, Plot {entry.Plot}";
        if (entry.IsApartment)
            return "Apartment";

        // EstateType is not a housing flag: even zero denotes an FC estate.
        // Interpret it only in the residential districts used by HousingCollector.
        if (entry.TerritoryId is not (339 or 340 or 341 or 641 or 979))
            return "-";

        return entry.EstateType switch
        {
            0 => "FC Estate",
            1 => "Private Chambers",
            2 => "Private Estate",
            4 => "Shared Estate",
            5 => "Apartment Building",
            6 => "Apartment",
            _ => "Housing",
        };
    }

    public static List<AetheryteEntry> Normalize(IEnumerable<AetheryteEntry>? entries)
        => (entries ?? [])
            .Where(entry => entry != null && entry.AetheryteId != 0 && entry.TerritoryId != 0)
            .Select(Clone)
            .OrderBy(entry => entry.AetheryteId)
            .ThenBy(entry => entry.TerritoryId)
            .ThenBy(entry => entry.SubIndex)
            .ThenBy(entry => entry.Ward)
            .ThenBy(entry => entry.Plot)
            .ThenBy(entry => entry.EstateType)
            .ThenBy(entry => entry.HouseId)
            .ThenBy(entry => entry.Name, StringComparer.Ordinal)
            .ThenBy(entry => entry.TerritoryName, StringComparer.Ordinal)
            .DistinctBy(entry => (entry.AetheryteId, entry.TerritoryId, entry.SubIndex,
                entry.Ward, entry.Plot, entry.EstateType, entry.HouseId))
            .ToList();

    public static AetheryteEntry Clone(AetheryteEntry entry) => new()
    {
        AetheryteId = entry.AetheryteId,
        TerritoryId = entry.TerritoryId,
        Name = string.IsNullOrWhiteSpace(entry.Name) ? $"Aetheryte {entry.AetheryteId}" : entry.Name.Trim(),
        TerritoryName = string.IsNullOrWhiteSpace(entry.TerritoryName)
            ? $"Territory {entry.TerritoryId}" : entry.TerritoryName.Trim(),
        SubIndex = entry.SubIndex,
        Ward = entry.Ward,
        Plot = entry.Plot,
        EstateType = entry.EstateType,
        HouseId = entry.HouseId,
    };

    public static SectionResult<List<AetheryteEntry>> CreateObservation(ulong expectedOwnerContentId,
        ulong observedOwnerBefore, ulong observedOwnerAfter, int expectedNativeCount,
        IReadOnlyCollection<AetheryteEntry> copiedEntries)
    {
        if (!InventoryCapturePolicy.IsSameOwner(expectedOwnerContentId, observedOwnerBefore)
            || !InventoryCapturePolicy.IsSameOwner(expectedOwnerContentId, observedOwnerAfter))
            return SectionResult<List<AetheryteEntry>>.Unavailable([], "teleport-list owner or readiness changed");

        // A zero-length list does not establish that native character teleport data has loaded.
        if (expectedNativeCount == 0 && copiedEntries.Count == 0)
            return SectionResult<List<AetheryteEntry>>.Unavailable([], "teleport destinations are not ready");

        if (expectedNativeCount < 1 || expectedNativeCount > MaximumDestinations
            || copiedEntries.Count != expectedNativeCount)
            return SectionResult<List<AetheryteEntry>>.Failed([], "teleport-list capture was incomplete");

        if (copiedEntries.Any(entry => entry == null || entry.AetheryteId == 0 || entry.TerritoryId == 0))
            return SectionResult<List<AetheryteEntry>>.Failed([], "teleport-list capture contained an invalid destination");

        return SectionResult<List<AetheryteEntry>>.Available(Normalize(copiedEntries));
    }
}
