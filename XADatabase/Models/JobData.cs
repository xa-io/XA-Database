namespace XADatabase.Models;

public class JobEntry
{
    public string Abbreviation { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int Level { get; set; }
    public bool IsUnlocked { get; set; }
}
