using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using XADatabase.Core.Collection;
using XADatabase.Models;
using XADatabase.Services;

namespace XADatabase.Collectors;

public static class SquadronCollector
{
    /// <summary>
    /// Collect GC Squadron data from GcArmyManager.
    /// Returns null if squadron is not unlocked or data unavailable.
    /// </summary>
    public static unsafe SectionResult<SquadronInfo?> CollectSection(XaServices services)
    {
        try
        {
            var manager = GcArmyManager.Instance();
            if (manager == null || manager->GetMemberCount() == 0)
                return SectionResult<SquadronInfo?>.Unavailable(null, "squadron data is not loaded");

            return SectionResult<SquadronInfo?>.Available(Collect(services));
        }
        catch (Exception ex)
        {
            services.Log.Error(ex, "[XA] Squadron collection failed.");
            return SectionResult<SquadronInfo?>.Failed(null, ex.Message);
        }
    }

    public static unsafe SquadronInfo? Collect(XaServices services)
    {
        var mgr = GcArmyManager.Instance();
        if (mgr == null)
            return null;

        var memberCount = mgr->GetMemberCount();
        if (memberCount == 0)
            return null;

        var classJobSheet = services.DataManager.GetExcelSheet<ClassJob>();
        var enpcSheet = services.DataManager.GetExcelSheet<ENpcResident>();

        var members = new List<SquadronMemberEntry>();
        for (uint i = 0; i < memberCount; i++)
        {
            try
            {
                var m = mgr->GetMember(i);
                if (m == null) continue;

                // Resolve NPC name from ENpcResident sheet
                var name = string.Empty;
                if (m->ENpcResidentId > 0 && enpcSheet != null)
                {
                    try
                    {
                        var row = enpcSheet.GetRow(m->ENpcResidentId);
                        name = row.Singular.ToString();
                    }
                    catch { }
                }

                // Resolve ClassJob name
                var jobName = string.Empty;
                if (m->ClassJob > 0 && classJobSheet != null)
                {
                    try
                    {
                        var row = classJobSheet.GetRow(m->ClassJob);
                        jobName = row.Abbreviation.ToString();
                    }
                    catch { }
                }

                members.Add(new SquadronMemberEntry
                {
                    ENpcResidentId = m->ENpcResidentId,
                    Name = name,
                    Race = m->Race,
                    Sex = m->Sex,
                    ClassJob = m->ClassJob,
                    ClassJobName = jobName,
                    Level = m->Level,
                    Experience = m->Experience,
                    ActiveTrait = m->ActiveTrait,
                    InactiveTrait = m->InactiveTrait,
                    MasteryIndependent = m->MasteryIndependent,
                    MasteryOffensive = m->MasteryOffensive,
                    MasteryDefensive = m->MasteryDefensive,
                    MasteryBalanced = m->MasteryBalanced,
                });
            }
            catch (Exception ex)
            {
                services.Log.Error($"[XA] Error reading squadron member {i}: {ex}");
            }
        }

        // Read expedition name if available
        var expeditionName = string.Empty;
        try
        {
            if (mgr->Data != null && mgr->Data->CurrentExpedition > 0)
            {
                var expSheet = services.DataManager.GetExcelSheet<GcArmyExpedition>();
                if (expSheet != null)
                {
                    var row = expSheet.GetRow(mgr->Data->CurrentExpedition);
                    expeditionName = row.Name.ToString();
                }
            }
        }
        catch { }

        return new SquadronInfo
        {
            Progress = mgr->Data != null ? mgr->Data->Progress : (byte)0,
            CurrentExpedition = mgr->Data != null ? mgr->Data->CurrentExpedition : (ushort)0,
            ExpeditionName = expeditionName,
            BonusPhysical = mgr->Data != null ? mgr->Data->BonusPhysical : (ushort)0,
            BonusMental = mgr->Data != null ? mgr->Data->BonusMental : (ushort)0,
            BonusTactical = mgr->Data != null ? mgr->Data->BonusTactical : (ushort)0,
            MemberCount = (byte)memberCount,
            Members = members,
        };
    }
}
