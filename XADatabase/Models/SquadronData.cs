using System.Collections.Generic;

namespace XADatabase.Models;

public class SquadronInfo
{
    public byte Progress { get; set; }
    public ushort CurrentExpedition { get; set; }
    public string ExpeditionName { get; set; } = string.Empty;
    public ushort BonusPhysical { get; set; }
    public ushort BonusMental { get; set; }
    public ushort BonusTactical { get; set; }
    public byte MemberCount { get; set; }
    public List<SquadronMemberEntry> Members { get; set; } = new();

    public bool IsUnlocked => Progress > 0;
}

public class SquadronMemberEntry
{
    public uint ENpcResidentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public byte Race { get; set; }
    public byte Sex { get; set; }
    public byte ClassJob { get; set; }
    public string ClassJobName { get; set; } = string.Empty;
    public byte Level { get; set; }
    public uint Experience { get; set; }
    public byte ActiveTrait { get; set; }
    public byte InactiveTrait { get; set; }
    public byte MasteryIndependent { get; set; }
    public byte MasteryOffensive { get; set; }
    public byte MasteryDefensive { get; set; }
    public byte MasteryBalanced { get; set; }
}
