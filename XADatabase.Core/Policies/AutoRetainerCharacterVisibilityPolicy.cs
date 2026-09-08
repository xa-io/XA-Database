using System.Reflection;

namespace XADatabase.Core.Policies;

public static class AutoRetainerIpcContract
{
    public const string AssemblyName = "AutoRetainerAPI";
    public const string OfflineCharacterDataTypeName = "AutoRetainerAPI.Configuration.OfflineCharacterData";

    public static Type? FindOfflineCharacterDataType(IEnumerable<Assembly>? assemblies)
    {
        if (assemblies == null)
            return null;

        foreach (var assembly in assemblies)
        {
            if (!string.Equals(assembly.GetName().Name, AssemblyName, StringComparison.Ordinal))
                continue;

            var candidate = assembly.GetType(OfflineCharacterDataTypeName, throwOnError: false, ignoreCase: false);
            if (candidate != null && HasExcludeRetainerMember(candidate))
                return candidate;
        }

        return null;
    }

    public static bool HasExcludeRetainerMember(Type candidate)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
        var property = candidate.GetProperty("ExcludeRetainer", flags);
        if (property?.CanRead == true && property.PropertyType == typeof(bool))
            return true;

        return candidate.GetField("ExcludeRetainer", flags)?.FieldType == typeof(bool);
    }
}

public sealed class AutoRetainerCharacterVisibilitySnapshot
{
    private readonly HashSet<ulong> visibleContentIds;

    private AutoRetainerCharacterVisibilitySnapshot(
        bool isAuthoritative,
        HashSet<ulong>? visibleContentIds = null,
        int registeredCount = 0,
        int excludedCount = 0)
    {
        IsAuthoritative = isAuthoritative;
        this.visibleContentIds = visibleContentIds ?? new HashSet<ulong>();
        RegisteredCount = registeredCount;
        ExcludedCount = excludedCount;
    }

    public bool IsAuthoritative { get; }
    public int RegisteredCount { get; }
    public int ExcludedCount { get; }
    public int VisibleCount => visibleContentIds.Count;

    public static AutoRetainerCharacterVisibilitySnapshot FailOpen()
        => new(false);

    public static bool TryCreate(
        IEnumerable<ulong>? registeredContentIds,
        Func<ulong, object?> getOfflineCharacterData,
        out AutoRetainerCharacterVisibilitySnapshot snapshot)
    {
        snapshot = FailOpen();
        if (registeredContentIds == null || getOfflineCharacterData == null)
            return false;

        var registered = registeredContentIds.Where(static contentId => contentId != 0).Distinct().ToList();
        var visible = new HashSet<ulong>();
        var excludedCount = 0;
        foreach (var contentId in registered)
        {
            if (!TryReadExcludeRetainer(getOfflineCharacterData(contentId), out var excluded))
                return false;

            if (excluded)
                excludedCount++;
            else
                visible.Add(contentId);
        }

        snapshot = new(true, visible, registered.Count, excludedCount);
        return true;
    }

    public bool IsVisible(ulong contentId)
        => !IsAuthoritative || visibleContentIds.Contains(contentId);

    public bool HasSameVisibilityAs(AutoRetainerCharacterVisibilitySnapshot other)
        => IsAuthoritative == other.IsAuthoritative
            && (!IsAuthoritative || visibleContentIds.SetEquals(other.visibleContentIds));

    public static bool TryReadExcludeRetainer(object? offlineCharacterData, out bool excluded)
    {
        excluded = false;
        if (offlineCharacterData == null)
            return false;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
        var type = offlineCharacterData.GetType();
        var property = type.GetProperty("ExcludeRetainer", flags);
        if (property?.CanRead == true && property.PropertyType == typeof(bool))
        {
            excluded = (bool)(property.GetValue(offlineCharacterData) ?? false);
            return true;
        }

        var field = type.GetField("ExcludeRetainer", flags);
        if (field?.FieldType != typeof(bool))
            return false;

        excluded = (bool)(field.GetValue(offlineCharacterData) ?? false);
        return true;
    }
}
