using System;

namespace XADatabase.Models;

public class JobEntry
{
    public string Abbreviation { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int Level { get; set; }
    public int LevelCap { get; set; }
    public bool IsUnlocked { get; set; }
}

public static class JobLevelCaps
{
    public const int Default = 100;
    public const int BlueMage = 80;
    public const int Beastmaster = 50;

    public static int ForAbbreviation(string abbreviation, int fallback = Default)
    {
        return abbreviation?.Trim().ToUpperInvariant() switch
        {
            "BLU" => BlueMage,
            "BST" => Beastmaster,
            _ => fallback,
        };
    }

    public static bool IsAtCap(JobEntry job)
    {
        var cap = job.LevelCap > 0 ? job.LevelCap : ForAbbreviation(job.Abbreviation);
        return cap > 0 && job.Level >= cap;
    }
}
