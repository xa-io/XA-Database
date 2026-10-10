using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using XADatabase.Services;

namespace XADatabase.Models;

public sealed class StorageItemsRequest
{
    public int Version { get; set; } = IpcContractInfo.StorageItemsJsonVersion;
    public ulong ContentId { get; set; }
    public List<uint>? ItemIds { get; set; }
    public List<string>? Sources { get; set; }
    public bool? IsHq { get; set; }
}

public sealed record StorageItemsObservation(
    ulong OwnerContentId, IReadOnlyList<ContainerItemEntry> Items,
    IReadOnlyList<InventorySummary> Summaries, string Source, string CapturedUtc = "", bool Readable = true);

public sealed class StorageItemsResponse
{
    public int Version { get; init; } = IpcContractInfo.StorageItemsJsonVersion;
    public int IpcContractVersion { get; init; } = IpcContractInfo.CurrentVersion;
    public bool Success { get; init; }
    public ulong ContentId { get; init; }
    public string CharacterName { get; init; } = string.Empty;
    public string World { get; init; } = string.Empty;
    public string SnapshotUpdatedUtc { get; init; } = string.Empty;
    public StorageItemsSection Armoire { get; init; } = new();
    public StorageItemsSection GlamourDresser { get; init; } = new();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed class StorageItemsSection
{
    public bool Requested { get; init; }
    public bool Recorded { get; init; }
    public string Source { get; init; } = "unknown";
    public string CapturedUtc { get; init; } = string.Empty;
    public int? UsedSlots { get; init; }
    public int? TotalSlots { get; init; }
    public IReadOnlyList<StorageItemMatch> Items { get; init; } = Array.Empty<StorageItemMatch>();
}

public sealed record StorageItemMatch(uint ItemId, string ItemName, bool IsHq, long Quantity);

/// <summary>Pure wire projection: storage never joins the ordinary inventory payload.</summary>
public static class StorageItemAccess
{
    public const string ArmoireName = "Armoire";
    public const string DresserName = "Glamour Dresser";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        MaxDepth = 16,
    };

    public static bool TryParse(string json, out StorageItemsRequest request, out string error)
    {
        request = new StorageItemsRequest();
        error = string.Empty;
        if (json is { Length: > 262144 })
        {
            error = "Request exceeds 256 KiB.";
            return false;
        }
        try
        {
            if (!string.IsNullOrWhiteSpace(json))
                request = JsonSerializer.Deserialize<StorageItemsRequest>(json, JsonOptions)
                    ?? throw new JsonException("Expected an object.");
            if (request.Version != IpcContractInfo.StorageItemsJsonVersion)
                error = "Unsupported storage query version.";
            else if (request.ItemIds is { Count: > 8192 } || request.ItemIds?.Any(id => id == 0) == true)
                error = "itemIds must contain at most 8192 nonzero base item IDs.";
            else if (request.Sources?.Any(source => !IsSource(source, "armoire") && !IsSource(source, "glamourDresser")) == true)
                error = "Supported sources are armoire and glamourDresser.";
        }
        catch (JsonException)
        {
            error = "Invalid storage request JSON.";
        }
        return error.Length == 0;
    }

    public static StorageItemsResponse Resolve(StorageItemsRequest request, ulong contentId,
        bool characterAvailable, string name, string world, string snapshotUpdatedUtc,
        IEnumerable<StorageItemsObservation> observations)
    {
        var armoireRequested = WantsSource(request, "armoire");
        var dresserRequested = WantsSource(request, "glamourDresser");
        if (contentId == 0 || !characterAvailable)
            return new StorageItemsResponse
            {
                ContentId = contentId,
                Armoire = new() { Requested = armoireRequested },
                GlamourDresser = new() { Requested = dresserRequested },
                Warnings = ["Character data is unavailable."],
            };

        var candidates = observations.Where(value => value.OwnerContentId == contentId && value.Readable).ToArray();
        var itemIds = request.ItemIds?.ToHashSet() ?? new HashSet<uint>();
        return new StorageItemsResponse
        {
            Success = true, ContentId = contentId, CharacterName = name, World = world,
            SnapshotUpdatedUtc = snapshotUpdatedUtc,
            Armoire = Project(ArmoireName, armoireRequested, request.IsHq, itemIds, candidates),
            GlamourDresser = Project(DresserName, dresserRequested, request.IsHq, itemIds, candidates),
        };
    }

    private static StorageItemsSection Project(string container, bool requested, bool? isHq,
        HashSet<uint> itemIds, IEnumerable<StorageItemsObservation> candidates)
    {
        if (!requested)
            return new StorageItemsSection();
        // The host supplies current-session observations before saved fallback.
        foreach (var candidate in candidates)
        {
            var summaries = candidate.Summaries.Where(value => string.Equals(value.Name, container, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (summaries.Length != 1)
                continue;
            var summary = summaries[0];
            var rows = candidate.Items.Where(value => string.Equals(value.ContainerName, container, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (summary.UsedSlots < 0 || summary.TotalSlots <= 0 || summary.UsedSlots > summary.TotalSlots
                || rows.Any(value => value.ItemId == 0 || value.Quantity <= 0)
                || (summary.UsedSlots == 0) != (rows.Length == 0)
                || rows.Length < summary.UsedSlots
                || (container == ArmoireName && rows.Length != summary.UsedSlots))
                continue;

            return new StorageItemsSection
            {
                Requested = true, Recorded = true, Source = candidate.Source, CapturedUtc = candidate.CapturedUtc,
                UsedSlots = summary.UsedSlots, TotalSlots = summary.TotalSlots,
                Items = rows.Where(value => (itemIds.Count == 0 || itemIds.Contains(value.ItemId))
                        && (!isHq.HasValue || value.IsHq == isHq.Value))
                    .GroupBy(value => (value.ItemId, value.IsHq))
                    .Select(group => new StorageItemMatch(group.Key.ItemId, group.First().ItemName,
                        group.Key.IsHq, group.Sum(value => (long)value.Quantity)))
                    .OrderBy(value => value.ItemId).ThenBy(value => value.IsHq).ToArray(),
            };
        }
        return new StorageItemsSection { Requested = true };
    }

    private static bool WantsSource(StorageItemsRequest request, string source)
        => request.Sources == null || request.Sources.Count == 0 || request.Sources.Any(value => IsSource(value, source));

    private static bool IsSource(string? value, string source)
        => string.Equals(value?.Trim(), source, StringComparison.OrdinalIgnoreCase);

    public static string Serialize(StorageItemsResponse response) => JsonSerializer.Serialize(response, JsonOptions);

    public static string Failure(string error)
        => Serialize(new StorageItemsResponse { Warnings = [error] });
}

/// <summary>Only complete native observations enter this per-login cache.</summary>
public sealed class StorageObservationCache
{
    private ulong ownerContentId;
    private readonly Dictionary<string, StorageItemsObservation> observations = new(StringComparer.OrdinalIgnoreCase);

    public void Observe(ulong owner, IReadOnlyList<ContainerItemEntry> items,
        IReadOnlyList<InventorySummary> summaries, IReadOnlySet<string> readable, string capturedUtc)
    {
        if (owner == 0)
            return;
        if (ownerContentId != owner)
            Clear();
        ownerContentId = owner;
        foreach (var name in new[] { StorageItemAccess.ArmoireName, StorageItemAccess.DresserName })
        {
            if (!readable.Contains(name))
                continue;
            observations[name] = new StorageItemsObservation(owner,
                items.Where(value => value.ContainerName == name).Select(value => new ContainerItemEntry
                {
                    ContainerName = value.ContainerName, ContainerType = value.ContainerType,
                    SlotIndex = value.SlotIndex, ItemId = value.ItemId, ItemName = value.ItemName,
                    Quantity = value.Quantity, IsHq = value.IsHq,
                }).ToArray(),
                summaries.Where(value => value.Name == name).Select(value => new InventorySummary
                {
                    Name = value.Name, UsedSlots = value.UsedSlots, TotalSlots = value.TotalSlots,
                }).ToArray(), "memory", capturedUtc);
        }
    }

    public IReadOnlyList<StorageItemsObservation> ForOwner(ulong owner)
        => owner != 0 && owner == ownerContentId ? observations.Values.ToArray() : Array.Empty<StorageItemsObservation>();

    public void Clear()
    {
        ownerContentId = 0;
        observations.Clear();
    }
}
