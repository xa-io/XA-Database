using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Lumina.Excel.Sheets;
using XADatabase.Core.Collection;
using XADatabase.Core.Policies;
using XADatabase.Models;
using XADatabase.Services;

namespace XADatabase.Collectors;

public static class FcMemberCollector
{
    public static unsafe SectionResult<List<FcMemberEntry>> CollectSection(
        XaServices services,
        Func<ulong, ulong> protectContentId)
    {
        try
        {
            ClearPersistedValues();
            var proxy = InfoProxyFreeCompanyMember.Instance();
            if (proxy == null || !FreeCompanyCollector.TryGetCurrentFcId(out var fcId)
                || !FreeCompanyIdentityPolicy.CanReuse(fcId, proxy->FreeCompanyId))
                return SectionResult<List<FcMemberEntry>>.Unavailable([], "FC member list belongs to an unknown or different free company");
            if (proxy->GetEntryCount() == 0)
                return SectionResult<List<FcMemberEntry>>.Unavailable([], "FC member list is not loaded");

            var value = Collect(services, protectContentId);
            return value.Count == 0
                ? SectionResult<List<FcMemberEntry>>.AuthoritativeEmpty(value)
                : SectionResult<List<FcMemberEntry>>.Available(value);
        }
        catch (Exception ex)
        {
            services.Log.Error(ex, "[XA] FC member collection failed.");
            return SectionResult<List<FcMemberEntry>>.Failed([], ex.Message);
        }
    }

    /// <summary>
    /// FC tag extracted from first member's CharacterData.FCTagString.
    /// Set after Collect() runs successfully.
    /// </summary>
    public static string LastCollectedFcTag { get; private set; } = string.Empty;
    public static ulong LastCollectedFcId { get; private set; }

    public static void ClearPersistedValues()
    {
        LastCollectedFcTag = string.Empty;
        LastCollectedFcId = 0;
    }

    /// <summary>
    /// Collect FC member list from InfoProxyFreeCompanyMember.
    /// Returns empty list if FC member data hasn't been loaded by the client
    /// (e.g. the FC member list window hasn't been opened yet this session).
    /// </summary>
    public static unsafe List<FcMemberEntry> Collect(
        XaServices services,
        Func<ulong, ulong> protectContentId)
    {
        var results = new List<FcMemberEntry>();
        ClearPersistedValues();

        var proxy = InfoProxyFreeCompanyMember.Instance();
        if (proxy == null || !FreeCompanyCollector.TryGetCurrentFcId(out var fcId)
            || !FreeCompanyIdentityPolicy.CanReuse(fcId, proxy->FreeCompanyId))
            return results;

        var count = proxy->GetEntryCount();
        if (count == 0)
            return results;

        var classJobSheet = services.DataManager.GetExcelSheet<ClassJob>();
        var worldSheet = services.DataManager.GetExcelSheet<World>();

        // Grab FC tag from first entry
        LastCollectedFcId = fcId;
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
                    ContentId = protectContentId(entry->ContentId),
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
                services.Log.Error($"[XA] Error reading FC member {i}: {ex}");
            }
        }

        return results;
    }
}
