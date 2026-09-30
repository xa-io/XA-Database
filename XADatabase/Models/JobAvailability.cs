using System;
using System.Collections.Generic;
using System.Linq;

namespace XADatabase.Models;

/// <summary>A presentation of a canonical job track without changing its persisted key.</summary>
public sealed record JobDisplayEntry
{
    public uint ClassJobId { get; init; }
    public string Abbreviation { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public int Level { get; init; }
    public int LevelCap { get; init; }
    public bool IsAvailable { get; init; }
    public bool IsUnknown { get; init; }
    public bool IsBaseClass { get; init; }
    public uint CanonicalClassJobId { get; init; }
    public string CanonicalAbbreviation { get; init; } = string.Empty;
    public bool IsJobUnlocked { get; init; }
    public JobUnlockEvidence UnlockEvidence { get; init; }
}

/// <summary>Pure owner-bound soul crystal policy shared by collection, storage and presentation.</summary>
public static class JobAvailability
{
    private static readonly string[] CrystalContainers =
    [
        "Inventory 1", "Inventory 2", "Inventory 3", "Inventory 4",
        "Equipped", "Armoury - Soul Crystal",
    ];

    /// <summary>Copy a saved record while rejecting legacy level-derived combat unlock flags.</summary>
    public static JobEntry Normalize(JobEntry job)
    {
        ArgumentNullException.ThrowIfNull(job);
        var result = new JobEntry
        {
            ClassJobId = job.ClassJobId,
            ParentClassJobId = job.ParentClassJobId,
            Abbreviation = job.Abbreviation ?? string.Empty,
            Name = job.Name ?? string.Empty,
            ParentAbbreviation = job.ParentAbbreviation ?? string.Empty,
            ParentName = job.ParentName ?? string.Empty,
            Category = job.Category ?? string.Empty,
            Level = job.Level,
            LevelCap = job.LevelCap,
            SoulCrystalItemId = job.SoulCrystalItemId,
            UnlockEvidence = job.UnlockEvidence,
            EvidenceOwnerContentId = job.EvidenceOwnerContentId,
        };

        if (IsNonCombat(result))
        {
            result.UnlockEvidence = JobUnlockEvidence.NotRequired;
            result.EvidenceOwnerContentId = 0;
            result.IsUnlocked = result.Level > 0;
            return result;
        }

        if (!HasCrystalDefinition(result) || result.EvidenceOwnerContentId == 0 ||
            result.UnlockEvidence is not (JobUnlockEvidence.CrystalObserved or JobUnlockEvidence.CrystalAbsent))
        {
            result.UnlockEvidence = JobUnlockEvidence.Unknown;
            result.EvidenceOwnerContentId = 0;
        }

        result.IsUnlocked = result.UnlockEvidence == JobUnlockEvidence.CrystalObserved;
        return result;
    }

    /// <summary>Resolve one fresh observation. Callers must supply items belonging to ownerContentId.</summary>
    public static JobEntry Resolve(
        JobEntry definitionWithLevel,
        IEnumerable<ContainerItemEntry> items,
        IReadOnlySet<string> loadedContainers,
        ulong ownerContentId,
        JobEntry? previous = null,
        bool canConfirmAbsence = true)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(loadedContainers);
        var result = Normalize(definitionWithLevel);
        if (IsNonCombat(result))
            return result;

        result.IsUnlocked = false;
        result.UnlockEvidence = JobUnlockEvidence.Unknown;
        result.EvidenceOwnerContentId = 0;
        if (ownerContentId == 0 || !HasCrystalDefinition(result))
            return result;

        var retained = previous is null ? null : Normalize(previous);
        var sameEvidenceOwner = retained is not null &&
            retained.EvidenceOwnerContentId == ownerContentId &&
            retained.ClassJobId == result.ClassJobId &&
            retained.SoulCrystalItemId == result.SoulCrystalItemId;

        // Soul crystals cannot be discarded. Retaining owner-bound positive proof also
        // prevents a transfer between separately read containers from appearing as a relock.
        if ((sameEvidenceOwner && retained!.UnlockEvidence == JobUnlockEvidence.CrystalObserved) ||
            items.Any(item => item is not null && item.ItemId == result.SoulCrystalItemId &&
                item.Quantity > 0 && IsCrystalContainer(item.ContainerName) &&
                loadedContainers.Contains(item.ContainerName)))
        {
            result.IsUnlocked = true;
            result.UnlockEvidence = JobUnlockEvidence.CrystalObserved;
            result.EvidenceOwnerContentId = ownerContentId;
        }
        else if ((canConfirmAbsence && CrystalContainers.All(loadedContainers.Contains)) ||
            (sameEvidenceOwner && retained!.UnlockEvidence == JobUnlockEvidence.CrystalAbsent))
        {
            result.UnlockEvidence = JobUnlockEvidence.CrystalAbsent;
            result.EvidenceOwnerContentId = ownerContentId;
        }

        return result;
    }

    public static JobDisplayEntry GetDisplay(JobEntry job)
    {
        var normalized = Normalize(job);
        // Arcanist's base progression belongs in Summoner's caster track. Scholar keeps
        // its own healer row so a missing Scholar crystal cannot duplicate Arcanist.
        var isScholar = normalized.ClassJobId == 28 ||
            (normalized.ClassJobId == 0 && string.Equals(normalized.Abbreviation, "SCH", StringComparison.OrdinalIgnoreCase));
        var showParent = !normalized.IsUnlocked && normalized.Level > 0 && !isScholar &&
            normalized.ParentClassJobId > 0 && normalized.ParentClassJobId != normalized.ClassJobId &&
            !string.IsNullOrWhiteSpace(normalized.ParentAbbreviation) && !string.IsNullOrWhiteSpace(normalized.ParentName);

        return new JobDisplayEntry
        {
            ClassJobId = showParent ? normalized.ParentClassJobId : normalized.ClassJobId,
            Abbreviation = showParent ? normalized.ParentAbbreviation : normalized.Abbreviation,
            Name = showParent ? normalized.ParentName : normalized.Name,
            Category = normalized.Category,
            Level = normalized.Level,
            LevelCap = normalized.LevelCap,
            IsAvailable = showParent || normalized.IsUnlocked,
            IsUnknown = normalized.UnlockEvidence == JobUnlockEvidence.Unknown,
            IsBaseClass = showParent,
            CanonicalClassJobId = normalized.ClassJobId,
            CanonicalAbbreviation = normalized.Abbreviation,
            IsJobUnlocked = normalized.IsUnlocked,
            UnlockEvidence = normalized.UnlockEvidence,
        };
    }

    public static List<JobDisplayEntry> Project(IEnumerable<JobEntry> jobs)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        var result = new List<JobDisplayEntry>();
        var positions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var job in jobs)
        {
            if (job is null)
                continue;

            var display = GetDisplay(job);
            var key = display.ClassJobId > 0 ? $"id:{display.ClassJobId}" : $"abbr:{display.Abbreviation}";
            if (!positions.TryGetValue(key, out var position))
            {
                positions[key] = result.Count;
                result.Add(display);
            }
            else if (DisplayRank(display) > DisplayRank(result[position]) ||
                (DisplayRank(display) == DisplayRank(result[position]) && display.Level > result[position].Level))
            {
                result[position] = display;
            }
        }

        return result;
    }

    private static bool IsNonCombat(JobEntry job)
        => string.Equals(job.Category, "Crafter", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(job.Category, "Gatherer", StringComparison.OrdinalIgnoreCase);

    private static bool HasCrystalDefinition(JobEntry job) => job.ClassJobId > 0 && job.SoulCrystalItemId > 0;

    private static bool IsCrystalContainer(string name)
        => Array.IndexOf(CrystalContainers, name) >= 0;

    private static int DisplayRank(JobDisplayEntry job)
        => job.IsJobUnlocked ? 3 : job.IsAvailable ? 2 : !job.IsUnknown ? 1 : 0;
}
