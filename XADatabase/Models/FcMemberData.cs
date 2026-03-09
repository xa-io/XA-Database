namespace XADatabase.Models;

public class FcMemberEntry
{
    public ulong ContentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public byte Job { get; set; }
    public string JobName { get; set; } = string.Empty;
    public byte OnlineStatus { get; set; }
    public ushort CurrentWorld { get; set; }
    public string CurrentWorldName { get; set; } = string.Empty;
    public ushort HomeWorld { get; set; }
    public string HomeWorldName { get; set; } = string.Empty;
    public byte GrandCompany { get; set; }
    public byte RankSort { get; set; }
    public string RankName { get; set; } = string.Empty;

    public bool IsOnline => OnlineStatus != 0;
}
