using System.Collections.Generic;
using XADatabase.Core.Collection;
using XADatabase.Core.Quality;
using XADatabase.Database;

namespace XADatabase.Core.Policies;

public static class LegacyDropProof
{
    public static bool CanDrop(
        IEnumerable<long> legacyContentIds,
        IEnumerable<long> migratedContentIds,
        bool hasExplicitTransaction,
        bool backupExists,
        out IReadOnlyList<long> missingContentIds)
    {
        var migrated = migratedContentIds.ToHashSet();
        missingContentIds = legacyContentIds
            .Where(contentId => !migrated.Contains(contentId))
            .Distinct()
            .OrderBy(static contentId => contentId)
            .ToList();
        return hasExplicitTransaction && backupExists && missingContentIds.Count == 0;
    }
}

public static class SchemaMigrationPolicy
{
    public static bool IsDuplicateColumnError(int sqliteErrorCode, string message)
    {
        return sqliteErrorCode == 1
            && message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase);
    }
}

public static class AuthoritativeSnapshot
{
    public static List<T> MergeByKey<T, TKey>(
        IEnumerable<T> persisted,
        IEnumerable<T> live,
        Func<T, TKey> keySelector,
        bool liveIsAuthoritative)
        where TKey : notnull
    {
        if (liveIsAuthoritative)
            return live.GroupBy(keySelector).Select(static group => group.Last()).ToList();

        var merged = persisted.GroupBy(keySelector).ToDictionary(static group => group.Key, static group => group.Last());
        foreach (var item in live)
            merged[keySelector(item)] = item;
        return merged.Values.ToList();
    }
}

public static class OwnerValidation
{
    public static string? DescribeMismatch(ulong expectedOwnerContentId, ulong actualOwnerContentId, string itemLabel)
    {
        return expectedOwnerContentId != 0 && actualOwnerContentId != expectedOwnerContentId
            ? $"{itemLabel} belongs to a different character; it was excluded from this snapshot."
            : null;
    }
}

public static class CacheOwnerPolicy
{
    public static bool MustReset(ulong currentOwnerContentId, ulong incomingContentId)
    {
        return currentOwnerContentId != 0 && currentOwnerContentId != incomingContentId;
    }
}

public static class SnapshotReplacementPolicy
{
    public static bool CanReplace(
        ulong existingOwnerContentId,
        string? existingTimestamp,
        SnapshotQuality existingQuality,
        ulong candidateOwnerContentId,
        string? candidateTimestamp,
        SnapshotQuality candidateQuality,
        bool candidateRetainsExistingSections = false)
    {
        if (candidateOwnerContentId == 0
            || !SnapshotTime.TryParseUtc(candidateTimestamp, out var candidateUtc)
            || QualityRank(candidateQuality) <= 0)
        {
            return false;
        }

        if (existingOwnerContentId == 0)
            return true;
        if (existingOwnerContentId != candidateOwnerContentId)
            return false;
        var isSafeSectionPreservingPartial = candidateRetainsExistingSections
            && candidateQuality == SnapshotQuality.Partial;
        if (!isSafeSectionPreservingPartial
            && QualityRank(candidateQuality) < QualityRank(existingQuality))
            return false;

        return !SnapshotTime.TryParseUtc(existingTimestamp, out var existingUtc)
            || candidateUtc >= existingUtc;
    }

    private static int QualityRank(SnapshotQuality quality)
        => quality switch
        {
            SnapshotQuality.Fresh => 6,
            SnapshotQuality.Persisted => 5,
            SnapshotQuality.Partial => 4,
            SnapshotQuality.Stale => 3,
            SnapshotQuality.LegacyBasic => 2,
            SnapshotQuality.Degraded => 1,
            _ => 0,
        };
}

public sealed class ScopedSessionCache<TKey, TValue> where TKey : notnull
{
    private TKey? owner;
    private TValue? value;
    private bool hasValue;

    public void Set(TKey newOwner, TValue newValue)
    {
        owner = newOwner;
        value = newValue;
        hasValue = true;
    }

    public bool TryGet(TKey requestedOwner, out TValue? cachedValue)
    {
        if (hasValue && EqualityComparer<TKey>.Default.Equals(owner!, requestedOwner))
        {
            cachedValue = value;
            return true;
        }

        cachedValue = default;
        return false;
    }

    public void Clear()
    {
        owner = default;
        value = default;
        hasValue = false;
    }
}

public sealed record SectionCommit<T>(T Value, IReadOnlyList<string> Warnings, SnapshotQuality Quality);

public static class SectionCommitPolicy
{
    public static SectionCommit<T> Commit<T>(string name, T persisted, SectionResult<T> candidate)
    {
        if (candidate.CanReplacePersisted)
        {
            var warnings = string.IsNullOrWhiteSpace(candidate.Detail)
                ? Array.Empty<string>()
                : new[] { $"{name} was collected with a warning: {candidate.Detail}." };
            return new SectionCommit<T>(
                candidate.Value,
                warnings,
                warnings.Length == 0 ? SnapshotQuality.Fresh : SnapshotQuality.Partial);
        }

        var detail = string.IsNullOrWhiteSpace(candidate.Detail) ? string.Empty : $" ({candidate.Detail})";
        var warning = $"{name} was {candidate.State.ToString().ToLowerInvariant()}; the previous value was kept{detail}.";
        return new SectionCommit<T>(persisted, new[] { warning }, SnapshotQuality.Partial);
    }
}

public static class HousingLanguagePolicy
{
    public static bool MustPreservePersisted(bool hasReliableLiveCharacterContext, bool localizedLabelsResolved)
        => !hasReliableLiveCharacterContext || !localizedLabelsResolved;

    public static IReadOnlyList<string> WarningsFor(string language, bool localizedLabelsResolved)
    {
        if (string.Equals(language, "English", StringComparison.OrdinalIgnoreCase) || localizedLabelsResolved)
            return Array.Empty<string>();
        return new[] { $"Housing text for {language} could not be resolved confidently; the previous value was kept." };
    }
}
