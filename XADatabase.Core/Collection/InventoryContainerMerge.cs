namespace XADatabase.Core.Collection;

/// <summary>Replaces observed containers, including empty ones, without erasing unavailable storage.</summary>
public static class InventoryContainerMerge
{
    private static readonly string[][] CoherentGroups =
    [
        ["Inventory 1", "Inventory 2", "Inventory 3", "Inventory 4"],
        ["Saddlebag 1", "Saddlebag 2"],
        ["Premium Saddlebag 1", "Premium Saddlebag 2"],
    ];

    public static HashSet<string> SelectReadableContainers(IEnumerable<string> loaded)
    {
        var readable = new HashSet<string>(loaded, StringComparer.OrdinalIgnoreCase);
        foreach (var group in CoherentGroups)
        {
            // Transfers between pages must not combine a new source with an old destination.
            if (!group.All(readable.Contains))
                readable.ExceptWith(group);
        }
        return readable;
    }

    public static List<T> Merge<T>(IEnumerable<T> previous, IEnumerable<T> observed,
        IReadOnlySet<string> readable, Func<T, string> containerName)
    {
        return previous.Where(item => !readable.Contains(containerName(item)))
            .Concat(observed.Where(item => readable.Contains(containerName(item))))
            .ToList();
    }
}
