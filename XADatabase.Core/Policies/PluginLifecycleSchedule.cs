namespace XADatabase.Core.Policies;

public sealed class PluginLifecycleSchedule
{
    public bool NeedsInitialSeed { get; private set; } = true;
    public DateTime LastInitialSeedAttemptUtc { get; private set; } = DateTime.MinValue;
    public DateTime LastAutoSaveUtc { get; private set; } = DateTime.MinValue;
    public DateTime LastCheckpointUtc { get; private set; } = DateTime.MinValue;

    public void MarkLogin()
    {
        NeedsInitialSeed = true;
        LastInitialSeedAttemptUtc = DateTime.MinValue;
    }

    public bool ShouldAttemptInitialSeed(DateTime nowUtc, TimeSpan retryInterval)
    {
        if (!NeedsInitialSeed)
            return false;
        if (LastInitialSeedAttemptUtc == DateTime.MinValue)
            return true;
        return NormalizeUtc(nowUtc) - LastInitialSeedAttemptUtc >= retryInterval;
    }

    public void MarkInitialSeedAttempted(DateTime nowUtc)
    {
        LastInitialSeedAttemptUtc = NormalizeUtc(nowUtc);
    }

    public void MarkInitialSeedSucceeded(DateTime nowUtc)
    {
        var normalizedUtc = NormalizeUtc(nowUtc);
        NeedsInitialSeed = false;
        LastInitialSeedAttemptUtc = normalizedUtc;
        LastAutoSaveUtc = normalizedUtc;
        LastCheckpointUtc = normalizedUtc;
    }

    public static bool IsInitialSeedComplete(bool saveSucceeded, bool retainedExistingSnapshot)
    {
        return saveSucceeded || retainedExistingSnapshot;
    }

    public static bool CanUseRetainedLiveCacheForLogout(
        bool viewingStoredCharacter,
        bool retainedDataCollected,
        ulong retainedContentId,
        ulong currentContentId)
    {
        return viewingStoredCharacter
            && retainedDataCollected
            && retainedContentId != 0
            && (currentContentId == 0 || currentContentId == retainedContentId);
    }

    public bool ShouldQueueAutoSave(DateTime nowUtc, int intervalMinutes, bool dataCollected)
    {
        if (intervalMinutes <= 0 || !dataCollected || LastAutoSaveUtc == DateTime.MinValue)
            return false;
        return NormalizeUtc(nowUtc) - LastAutoSaveUtc >= TimeSpan.FromMinutes(intervalMinutes);
    }

    public void MarkAutoSaveQueued(DateTime nowUtc)
    {
        LastAutoSaveUtc = NormalizeUtc(nowUtc);
    }

    public bool ShouldRunPeriodicCheckpoint(DateTime nowUtc, TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero || LastCheckpointUtc == DateTime.MinValue)
            return false;
        return NormalizeUtc(nowUtc) - LastCheckpointUtc >= interval;
    }

    public void MarkCheckpointAttempted(DateTime nowUtc)
    {
        LastCheckpointUtc = NormalizeUtc(nowUtc);
    }

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
