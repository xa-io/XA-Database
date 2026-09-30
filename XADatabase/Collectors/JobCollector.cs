using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using XADatabase.Core.Collection;
using XADatabase.Models;
using XADatabase.Services;

namespace XADatabase.Collectors;

public static class JobCollector
{
    // ClassJob row IDs grouped by role category
    // These map to the ClassJob excel sheet RowId values
    private static readonly (string Category, uint RowId)[] JobDefs =
    {
        // Tanks
        ("Tank", 19),  // PLD
        ("Tank", 21),  // WAR
        ("Tank", 32),  // DRK
        ("Tank", 37),  // GNB

        // Healers
        ("Healer", 24), // WHM
        ("Healer", 28), // SCH
        ("Healer", 33), // AST
        ("Healer", 40), // SGE

        // Melee DPS
        ("Melee DPS", 20), // MNK
        ("Melee DPS", 22), // DRG
        ("Melee DPS", 30), // NIN
        ("Melee DPS", 34), // SAM
        ("Melee DPS", 39), // RPR
        ("Melee DPS", 41), // VPR
        ("Melee DPS", 43), // BST

        // Ranged DPS
        ("Ranged DPS", 23), // BRD
        ("Ranged DPS", 31), // MCH
        ("Ranged DPS", 38), // DNC

        // Caster DPS
        ("Caster DPS", 25), // BLM
        ("Caster DPS", 27), // SMN
        ("Caster DPS", 35), // RDM
        ("Caster DPS", 42), // PCT
        ("Caster DPS", 36), // BLU

        // Crafters
        ("Crafter", 8),  // CRP
        ("Crafter", 9),  // BSM
        ("Crafter", 10), // ARM
        ("Crafter", 11), // GSM
        ("Crafter", 12), // LTW
        ("Crafter", 13), // WVR
        ("Crafter", 14), // ALC
        ("Crafter", 15), // CUL

        // Gatherers
        ("Gatherer", 16), // MIN
        ("Gatherer", 17), // BTN
        ("Gatherer", 18), // FSH
    };

    private static readonly ConditionalWeakTable<IDataManager, List<JobEntry>> Catalogs = new();

    // Sheet metadata is immutable for this plugin instance. Never rescan Item per frame.
    private static List<JobEntry> GetCatalog(IDataManager dataManager) => Catalogs.GetValue(dataManager, BuildCatalog);

    private static List<JobEntry> BuildCatalog(IDataManager dataManager)
    {
        var catalog = new List<JobEntry>();
        var jobs = dataManager.GetExcelSheet<ClassJob>();
        var items = dataManager.GetExcelSheet<Item>();
        foreach (var (category, rowId) in JobDefs)
        {
            if (!jobs.TryGetRow(rowId, out var job))
                continue;
            var abbreviation = job.Abbreviation.ToString().Trim().ToUpperInvariant();
            var definition = new JobEntry
            {
                ClassJobId = rowId,
                Abbreviation = abbreviation,
                Name = job.Name.ToString().Trim().ToUpperInvariant(),
                Category = category,
                LevelCap = JobLevelCaps.ForAbbreviation(abbreviation),
            };
            if (job.ClassJobParent.RowId != 0 && job.ClassJobParent.RowId != rowId &&
                jobs.TryGetRow(job.ClassJobParent.RowId, out var parent))
            {
                definition.ParentClassJobId = parent.RowId;
                definition.ParentAbbreviation = parent.Abbreviation.ToString().Trim().ToUpperInvariant();
                definition.ParentName = parent.Name.ToString().Trim().ToUpperInvariant();
            }
            if (job.ItemSoulCrystal.RowId != 0 && items.TryGetRow(job.ItemSoulCrystal.RowId, out var crystal) &&
                crystal.FilterGroup == 31)
                definition.SoulCrystalItemId = crystal.RowId;
            catalog.Add(definition);
        }
        return catalog;
    }

    /// <summary>Include effective parent classes as well as canonical job IDs.</summary>
    public static Dictionary<string, uint> BuildAbbreviationRowIdMap(IDataManager dataManager)
    {
        var map = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        foreach (var job in GetCatalog(dataManager))
        {
            if (job.Abbreviation.Length > 0)
                map[job.Abbreviation] = job.ClassJobId;
            if (job.ParentAbbreviation.Length > 0)
                map[job.ParentAbbreviation] = job.ParentClassJobId;
        }

        return map;
    }

    public static SectionResult<List<JobEntry>> CollectSection(XaServices services,
        ItemCollectionResult observation, IEnumerable<JobEntry> previous)
    {
        try
        {
            if (!services.PlayerState.IsLoaded)
                return SectionResult<List<Models.JobEntry>>.Unavailable([], "player job data is not loaded");

            var value = Collect(services.PlayerState, services.DataManager, observation, previous);
            return value.Count == 0
                ? SectionResult<List<Models.JobEntry>>.AuthoritativeEmpty(value)
                : SectionResult<List<Models.JobEntry>>.Available(value);
        }
        catch (Exception ex)
        {
            services.Log.Error(ex, "[XA] Job collection failed.");
            return SectionResult<List<Models.JobEntry>>.Failed([], ex.Message);
        }
    }

    public static List<JobEntry> Collect(IPlayerState playerState, IDataManager dataManager,
        ItemCollectionResult observation, IEnumerable<JobEntry> previous)
    {
        var results = new List<Models.JobEntry>();

        if (!playerState.IsLoaded)
            return results;

        var classJobSheet = dataManager.GetExcelSheet<ClassJob>();

        var prior = previous.ToList();
        foreach (var definition in GetCatalog(dataManager))
        {
            if (!classJobSheet.TryGetRow(definition.ClassJobId, out var classJob))
                continue;
            var track = JobAvailability.Normalize(definition);
            track.Level = playerState.GetClassJobLevel(classJob);
            var retained = prior.FirstOrDefault(job => job.ClassJobId == track.ClassJobId);
            results.Add(JobAvailability.Resolve(track, observation.Items, observation.LoadedContainers,
                playerState.ContentId, retained));
        }

        return results;
    }

    /// <summary>Enrich legacy records without treating a historical missing item as proof of absence.</summary>
    public static List<JobEntry> ResolveSavedJobs(IDataManager dataManager, IEnumerable<JobEntry> jobs,
        IEnumerable<ContainerItemEntry> items, ulong ownerContentId)
    {
        var catalog = GetCatalog(dataManager);
        var savedItems = items.Where(item => item != null).ToList();
        var observedContainers = savedItems.Select(item => item.ContainerName).ToHashSet(StringComparer.Ordinal);
        var result = new List<JobEntry>();
        foreach (var saved in jobs)
        {
            if (saved == null)
                continue;
            var definition = catalog.FirstOrDefault(job => saved.ClassJobId != 0
                ? job.ClassJobId == saved.ClassJobId
                : string.Equals(job.Abbreviation, saved.Abbreviation?.Trim(), StringComparison.OrdinalIgnoreCase));
            var track = JobAvailability.Normalize(definition ?? saved);
            // A saved mapping cannot stand in for a missing current sheet definition.
            if (definition == null)
                track.SoulCrystalItemId = 0;
            track.Level = saved.Level;
            result.Add(JobAvailability.Resolve(track, savedItems, observedContainers, ownerContentId, saved,
                canConfirmAbsence: false));
        }
        return result;
    }
}
