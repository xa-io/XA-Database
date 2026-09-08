using XADatabase.Database;

namespace XADatabase.Core.Quality;

public enum SnapshotQuality
{
    None,
    Saving,
    Degraded,
    Stale,
    Partial,
    Fresh,
    Persisted,
    LegacyBasic,
}

public static class SnapshotQualityResolver
{
    public static SnapshotQuality ClassifySave(
        bool exists,
        bool pending,
        bool success,
        bool hasWarnings,
        string savedAtUtc,
        DateTime nowUtc,
        double staleThresholdMinutes)
    {
        if (!exists)
            return SnapshotQuality.None;
        if (pending)
            return SnapshotQuality.Saving;
        if (!success)
            return SnapshotQuality.Degraded;
        if (!SnapshotTime.TryParseUtc(savedAtUtc, out _))
            return SnapshotQuality.Degraded;
        if (IsStale(savedAtUtc, nowUtc, staleThresholdMinutes))
            return SnapshotQuality.Stale;
        return hasWarnings ? SnapshotQuality.Partial : SnapshotQuality.Fresh;
    }

    public static SnapshotQuality ClassifyPersisted(
        string updatedUtc,
        bool hasParseErrors,
        DateTime nowUtc,
        double staleThresholdMinutes)
    {
        if (string.IsNullOrWhiteSpace(updatedUtc))
            return SnapshotQuality.None;
        if (!SnapshotTime.TryParseUtc(updatedUtc, out _))
            return SnapshotQuality.Partial;
        return IsStale(updatedUtc, nowUtc, staleThresholdMinutes)
            ? SnapshotQuality.Stale
            : hasParseErrors
                ? SnapshotQuality.Partial
                : SnapshotQuality.Persisted;
    }

    public static bool IsStale(string timestamp, DateTime nowUtc, double staleThresholdMinutes)
    {
        if (!SnapshotTime.TryParseUtc(timestamp, out var savedAtUtc))
            return false;
        var normalizedNowUtc = nowUtc.Kind switch
        {
            DateTimeKind.Utc => nowUtc,
            DateTimeKind.Local => nowUtc.ToUniversalTime(),
            _ => DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc),
        };
        return (normalizedNowUtc - savedAtUtc).TotalMinutes >= Math.Max(0, staleThresholdMinutes);
    }

    public static string ToLabel(SnapshotQuality quality)
        => quality switch
        {
            SnapshotQuality.None => "No Snapshot",
            SnapshotQuality.Saving => "Saving",
            SnapshotQuality.Degraded => "Degraded",
            SnapshotQuality.Stale => "Stale",
            SnapshotQuality.Partial => "Partial",
            SnapshotQuality.Fresh => "Fresh",
            SnapshotQuality.Persisted => "Persisted",
            SnapshotQuality.LegacyBasic => "legacy-basic",
            _ => "No Snapshot",
        };
}
