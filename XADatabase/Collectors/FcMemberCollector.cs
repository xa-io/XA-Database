using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Lumina.Excel.Sheets;
using XADatabase.Models;

namespace XADatabase.Collectors;

public static class FcMemberCollector
{
    /// <summary>
    /// FC tag extracted from first member's CharacterData.FCTagString.
    /// Set after Collect() runs successfully.
    /// </summary>
    public static string LastCollectedFcTag { get; private set; } = string.Empty;

    public static void ClearPersistedValues()
    {
        LastCollectedFcTag = string.Empty;
    }

    /// <summary>
    /// Collect FC member list from InfoProxyFreeCompanyMember.
    /// Returns empty list if FC member data hasn't been loaded by the client
    /// (e.g. the FC member list window hasn't been opened yet this session).
    /// </summary>
    public static unsafe List<FcMemberEntry> Collect(IDataManager dataManager)
    {
        var results = new List<FcMemberEntry>();

        var proxy = InfoProxyFreeCompanyMember.Instance();
        if (proxy == null)
            return results;

        var count = proxy->GetEntryCount();
        if (count == 0)
            return results;

        var classJobSheet = dataManager.GetExcelSheet<ClassJob>();
        var worldSheet = dataManager.GetExcelSheet<World>();

        // Grab FC tag from first entry
        LastCollectedFcTag = string.Empty;
        try
        {
            var firstEntry = proxy->GetEntry(0);
            if (firstEntry != null)
                LastCollectedFcTag = firstEntry->FCTagString ?? string.Empty;
        }
        catch { }

        for (uint i = 0; i < count; i++)
        {
            try
            {
                var entry = proxy->GetEntry(i);
                if (entry == null) continue;

                var name = entry->NameString;
                if (string.IsNullOrEmpty(name)) continue;

                // Resolve ClassJob name
                var jobName = string.Empty;
                if (entry->Job > 0 && classJobSheet != null)
                {
                    try
                    {
                        var row = classJobSheet.GetRow(entry->Job);
                        jobName = row.Abbreviation.ToString();
                    }
                    catch { }
                }

                // Resolve world names
                var currentWorldName = string.Empty;
                var homeWorldName = string.Empty;
                if (worldSheet != null)
                {
                    try
                    {
                        if (entry->CurrentWorld > 0)
                            currentWorldName = worldSheet.GetRow(entry->CurrentWorld).Name.ToString();
                        if (entry->HomeWorld > 0)
                            homeWorldName = worldSheet.GetRow(entry->HomeWorld).Name.ToString();
                    }
                    catch { }
                }

                results.Add(new FcMemberEntry
                {
                    ContentId = entry->ContentId,
                    Name = name,
                    Job = entry->Job,
                    JobName = jobName,
                    OnlineStatus = (byte)entry->State,
                    CurrentWorld = entry->CurrentWorld,
                    CurrentWorldName = currentWorldName,
                    HomeWorld = entry->HomeWorld,
                    HomeWorldName = homeWorldName,
                    GrandCompany = (byte)entry->GrandCompany,
                    RankSort = entry->Sort,
                });
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[XA] Error reading FC member {i}: {ex}");
            }
        }

        return results;
    }
}
