namespace XADatabase.Core.Quality;

public static class SnapshotFreshnessPolicy
{
    public static DateTime ResolveSnapshotUpdatedUtc(
        bool usedCachedLogoutIdentity,
        DateTime lastRefreshUtc,
        DateTime savedAtUtc)
    {
        var normalizedSavedAtUtc = NormalizeUtc(savedAtUtc);
        if (!usedCachedLogoutIdentity)
            return normalizedSavedAtUtc;

        if (lastRefreshUtc <= DateTime.MinValue)
            return DateTime.MinValue;

        var normalizedRefreshUtc = NormalizeUtc(lastRefreshUtc);
        return normalizedRefreshUtc > normalizedSavedAtUtc
            ? normalizedSavedAtUtc
            : normalizedRefreshUtc;
    }

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
