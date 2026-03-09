namespace XADatabase.Models;

public class CollectionSummary
{
    public string Category { get; set; } = string.Empty;
    public int Unlocked { get; set; }
    public int Total { get; set; }
}

public class ActiveQuestEntry
{
    public ushort QuestId { get; set; }
    public string Name { get; set; } = string.Empty;
    public byte Sequence { get; set; }
}

/// <summary>
/// Tracks completion of a specific MSQ or unlock milestone quest.
/// </summary>
public class MsqMilestoneEntry
{
    /// <summary>Lumina Quest sheet row ID (e.g. 66060)</summary>
    public uint QuestRowId { get; set; }

    /// <summary>Human-readable milestone label</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Expansion or category grouping</summary>
    public string Expansion { get; set; } = string.Empty;

    /// <summary>True if the quest has been completed by this character</summary>
    public bool IsComplete { get; set; }
}
