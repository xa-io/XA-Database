using System;
using System.Collections.Generic;
using System.Linq;
using XADatabase.Core.Policies;
using XADatabase.Core.Quality;

namespace XADatabase.Database;

public sealed class SnapshotStore
{
    private const double StaleThresholdMinutes = 60.0;
    private readonly Func<List<XaCharacterSnapshotData>> loadAll;
    private readonly Func<List<XaCharacterRosterData>> loadRoster;
    private readonly Func<List<XaCharacterItemsData>> loadItemSections;
    private readonly Func<ulong, XaCharacterSnapshotData?> loadSnapshot;
    private readonly object gate = new();
    private Dictionary<ulong, XaCharacterSnapshotData>? allSnapshots;
    private List<XaCharacterRosterData>? roster;
    private List<XaCharacterItemsData>? itemSections;

    public SnapshotStore(XaCharacterSnapshotRepository repository)
        : this(repository.GetAllSnapshots, repository.GetRoster, repository.GetAllItemSections, repository.GetSnapshot)
    {
    }

    internal SnapshotStore(
        Func<List<XaCharacterSnapshotData>> loadAll,
        Func<List<XaCharacterRosterData>> loadRoster,
        Func<List<XaCharacterItemsData>> loadItemSections,
        Func<ulong, XaCharacterSnapshotData?> loadSnapshot)
    {
        this.loadAll = loadAll;
        this.loadRoster = loadRoster;
        this.loadItemSections = loadItemSections;
        this.loadSnapshot = loadSnapshot;
    }

    public IReadOnlyDictionary<ulong, XaCharacterSnapshotData> All()
    {
        lock (gate)
            return allSnapshots ??= loadAll().ToDictionary(snapshot => snapshot.Row.ContentId);
    }

    public XaCharacterSnapshotData? Get(ulong contentId)
    {
        lock (gate)
        {
            allSnapshots ??= loadAll().ToDictionary(snapshot => snapshot.Row.ContentId);
            return allSnapshots.GetValueOrDefault(contentId);
        }
    }

    public IReadOnlyList<XaCharacterRosterData> Roster()
    {
        lock (gate)
            return roster ??= loadRoster();
    }

    public IReadOnlyList<XaCharacterItemsData> ItemSections()
    {
        lock (gate)
            return itemSections ??= loadItemSections();
    }

    public bool Upsert(ulong contentId)
    {
        var candidate = loadSnapshot(contentId);
        if (candidate == null)
            return false;

        lock (gate)
        {
            if (allSnapshots != null
                && allSnapshots.TryGetValue(contentId, out var existing)
                && !CanReplace(existing, candidate))
            {
                return false;
            }

            if (allSnapshots != null)
                allSnapshots[contentId] = candidate;

            roster = null;
            itemSections = null;
            return true;
        }
    }

    public void Invalidate()
    {
        lock (gate)
        {
            allSnapshots = null;
            roster = null;
            itemSections = null;
        }
    }

    private static bool CanReplace(XaCharacterSnapshotData existing, XaCharacterSnapshotData candidate)
    {
        var nowUtc = DateTime.UtcNow;
        var existingQuality = SnapshotQualityResolver.ClassifyPersisted(
            existing.Row.UpdatedUtc,
            existing.ParseErrors.Count > 0,
            nowUtc,
            StaleThresholdMinutes);
        var candidateQuality = SnapshotQualityResolver.ClassifyPersisted(
            candidate.Row.UpdatedUtc,
            candidate.ParseErrors.Count > 0,
            nowUtc,
            StaleThresholdMinutes);
        return SnapshotReplacementPolicy.CanReplace(
            existing.Row.ContentId,
            existing.Row.UpdatedUtc,
            existingQuality,
            candidate.Row.ContentId,
            candidate.Row.UpdatedUtc,
            candidateQuality);
    }
}
