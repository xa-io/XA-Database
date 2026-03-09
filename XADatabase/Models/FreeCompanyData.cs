namespace XADatabase.Models;

public class FreeCompanyEntry
{
    public ulong FcId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public string Master { get; set; } = string.Empty;
    public byte Rank { get; set; }
    public byte GrandCompany { get; set; }
    public string GrandCompanyName { get; set; } = string.Empty;
    public ushort OnlineMembers { get; set; }
    public ushort TotalMembers { get; set; }
    public ushort HomeWorldId { get; set; }
    public int FcPoints { get; set; }
    public string Estate { get; set; } = string.Empty;
}
