using System.Collections.Generic;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

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

    public static List<Models.JobEntry> Collect(IPlayerState playerState, IDataManager dataManager)
    {
        var results = new List<Models.JobEntry>();

        if (!playerState.IsLoaded)
            return results;

        var classJobSheet = dataManager.GetExcelSheet<ClassJob>();

        foreach (var (category, rowId) in JobDefs)
        {
            if (!classJobSheet.TryGetRow(rowId, out var classJob))
                continue;

            var abbr = classJob.Abbreviation.ToString();
            var name = classJob.Name.ToString();

            short level = playerState.GetClassJobLevel(classJob);
            bool isUnlocked = level > 0;

            results.Add(new Models.JobEntry
            {
                Abbreviation = abbr,
                Name = name,
                Category = category,
                Level = level,
                IsUnlocked = isUnlocked,
            });
        }

        return results;
    }
}
