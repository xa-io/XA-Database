using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using XADatabase.Models;

namespace XADatabase.Collectors;

public static class QuestCollector
{
    /// <summary>
    /// MSQ milestone quests — Lumina Quest sheet row IDs.
    /// Grouped by expansion for display. Completion of each indicates story progress.
    /// </summary>
    private static readonly (string Expansion, string Label, uint RowId)[] MsqMilestones =
    {
        // Early unlocks
        ("Unlocks", "Close to Home (Gridania)", 65564),
        ("Unlocks", "Close to Home (Limsa Lominsa)", 65998),
        ("Unlocks", "Close to Home (Ul'dah)", 66131),
        ("Unlocks", "Spirithold Broken (Guildleves/Inn)", 65665),
        ("Unlocks", "Just Deserts (Guildleves/Inn)", 66005),
        ("Unlocks", "Way Down in the Hole (Guildleves/Inn)", 65856),
        ("Unlocks", "It's Probably Pirates (Airships)", 65781),
        ("Unlocks", "The Scions of the Seventh Dawn (Retainers)", 66045),
        ("Unlocks", "Sylph-management (Job Quests)", 66049),

        // A Realm Reborn
        ("A Realm Reborn", "A Realm Reborn", 66060),
        ("A Realm Reborn", "A Realm Awoken", 66729),
        ("A Realm Reborn", "Through the Maelstrom", 66899),
        ("A Realm Reborn", "Defenders of Eorzea", 66996),
        ("A Realm Reborn", "Dreams of Ice", 65625),
        ("A Realm Reborn", "Before the Fall (Part 1)", 65965),
        ("A Realm Reborn", "Before the Fall (Part 2)", 65964),

        // Heavensward
        ("Heavensward", "Heavensward", 67205),
        ("Heavensward", "As Goes Light, So Goes Darkness", 67699),
        ("Heavensward", "The Gears of Change", 67777),
        ("Heavensward", "Revenge of the Horde", 67783),
        ("Heavensward", "Soul Surrender", 67886),
        ("Heavensward", "The Far Edge of Fate (Part 1)", 67891),
        ("Heavensward", "The Far Edge of Fate (Part 2)", 67895),

        // Stormblood
        ("Stormblood", "Stormblood", 68089),
        ("Stormblood", "The Legend Returns", 68508),
        ("Stormblood", "Rise of a New Sun", 68565),
        ("Stormblood", "Under the Moonlight", 68612),
        ("Stormblood", "Prelude in Violet", 68685),
        ("Stormblood", "A Requiem for Heroes (Part 1)", 68719),
        ("Stormblood", "A Requiem for Heroes (Part 2)", 68721),

        // Shadowbringers
        ("Shadowbringers", "Shadowbringers", 69190),
        ("Shadowbringers", "Vows of Virtue, Deeds of Cruelty", 69218),
        ("Shadowbringers", "Echoes of a Fallen Star", 69306),
        ("Shadowbringers", "Reflections in Crystal", 69318),
        ("Shadowbringers", "Futures Rewritten", 69552),
        ("Shadowbringers", "Death Unto Dawn (Part 1)", 69599),
        ("Shadowbringers", "Death Unto Dawn (Part 2)", 69602),

        // Endwalker
        ("Endwalker", "Endwalker", 70000),
        ("Endwalker", "Newfound Adventure", 70062),
        ("Endwalker", "Buried Memory", 70136),
        ("Endwalker", "Gods Revel, Lands Tremble", 70214),
        ("Endwalker", "The Dark Throne", 70279),
        ("Endwalker", "Growing Light (Part 1)", 70286),
        ("Endwalker", "Growing Light (Part 2)", 70289),

        // Dawntrail
        ("Dawntrail", "Dawntrail", 70495),
        ("Dawntrail", "Crossroads", 70786),
        ("Dawntrail", "Seekers of Eternity", 70842),
        ("Dawntrail", "The Promise of Tomorrow", 70909),
        ("Dawntrail", "The Mist", 70970),
    };

    public static unsafe List<ActiveQuestEntry> CollectActiveQuests(IDataManager dataManager)
    {
        var results = new List<ActiveQuestEntry>();

        try
        {
            var questManager = QuestManager.Instance();
            if (questManager == null)
                return results;

            var questSheet = dataManager.GetExcelSheet<Quest>();

            // Iterate normal quests (up to 30 active)
            for (int i = 0; i < questManager->NormalQuests.Length; i++)
            {
                var quest = questManager->NormalQuests[i];
                if (quest.QuestId == 0)
                    continue;

                // Quest IDs in the manager are offset by 65536
                var sheetId = (uint)(quest.QuestId + 65536);
                var name = "Unknown Quest";

                if (questSheet != null)
                {
                    var row = questSheet.GetRowOrDefault(sheetId);
                    if (row.HasValue)
                    {
                        var questName = row.Value.Name.ToString();
                        if (!string.IsNullOrEmpty(questName))
                            name = questName;
                    }
                }

                results.Add(new ActiveQuestEntry
                {
                    QuestId = quest.QuestId,
                    Name = name,
                    Sequence = quest.Sequence,
                });
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[XA] Error collecting active quests: {ex.Message}");
        }

        return results;
    }

    /// <summary>
    /// Check completion status of all MSQ milestone quests.
    /// Uses QuestManager.IsQuestComplete with internal IDs (rowId - 65536).
    /// </summary>
    public static unsafe List<MsqMilestoneEntry> CollectMsqProgress()
    {
        var results = new List<MsqMilestoneEntry>();

        try
        {
            var qm = QuestManager.Instance();
            if (qm == null)
                return results;

            foreach (var (expansion, label, rowId) in MsqMilestones)
            {
                var internalId = (ushort)(rowId - 65536);
                var complete = QuestManager.IsQuestComplete(internalId);

                results.Add(new MsqMilestoneEntry
                {
                    QuestRowId = rowId,
                    Label = label,
                    Expansion = expansion,
                    IsComplete = complete,
                });
            }

            // Close to Home: mutually exclusive starting quests (Gridania / Limsa / Ul'dah).
            // If ANY one is complete, mark ALL three as complete.
            var closeToHomeIds = new uint[] { 65564, 65998, 66131 };
            var anyCloseComplete = results.Exists(m => Array.IndexOf(closeToHomeIds, m.QuestRowId) >= 0 && m.IsComplete);
            if (anyCloseComplete)
            {
                foreach (var m in results)
                {
                    if (Array.IndexOf(closeToHomeIds, m.QuestRowId) >= 0)
                        m.IsComplete = true;
                }
            }

            // Guildleves/Inn: mutually exclusive city openers (Gridania / Limsa / Ul'dah).
            // If ANY one is complete, mark ALL three as complete.
            var guildleveIds = new uint[] { 65665, 66005, 65856 };
            var anyGuildleveComplete = results.Exists(m => Array.IndexOf(guildleveIds, m.QuestRowId) >= 0 && m.IsComplete);
            if (anyGuildleveComplete)
            {
                foreach (var m in results)
                {
                    if (Array.IndexOf(guildleveIds, m.QuestRowId) >= 0)
                        m.IsComplete = true;
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[XA] Error collecting MSQ progress: {ex.Message}");
        }

        return results;
    }
}
