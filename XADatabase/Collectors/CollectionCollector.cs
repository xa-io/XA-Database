using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using XADatabase.Models;
using PlayerStateStruct = FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState;

namespace XADatabase.Collectors;

public static class CollectionCollector
{
    public static unsafe List<CollectionSummary> Collect(IDataManager dataManager)
    {
        var results = new List<CollectionSummary>();
        var ps = PlayerStateStruct.Instance();
        var uiState = UIState.Instance();

        // ── Mounts ──
        try
        {
            var mountSheet = dataManager.GetExcelSheet<Mount>();
            if (mountSheet != null && ps != null)
            {
                int total = 0, unlocked = 0;
                foreach (var mount in mountSheet)
                {
                    if (mount.RowId == 0) continue;
                    if (mount.Icon == 0) continue;
                    total++;
                    if (ps->IsMountUnlocked(mount.RowId))
                        unlocked++;
                }
                results.Add(new CollectionSummary { Category = "Mounts", Unlocked = unlocked, Total = total });
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[XA] Error collecting mounts: {ex.Message}");
        }

        // ── Minions ──
        try
        {
            var companionSheet = dataManager.GetExcelSheet<Companion>();
            if (companionSheet != null && uiState != null)
            {
                int total = 0, unlocked = 0;
                foreach (var companion in companionSheet)
                {
                    if (companion.RowId == 0) continue;
                    if (companion.Icon == 0) continue;
                    total++;
                    if (uiState->IsCompanionUnlocked(companion.RowId))
                        unlocked++;
                }
                results.Add(new CollectionSummary { Category = "Minions", Unlocked = unlocked, Total = total });
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[XA] Error collecting minions: {ex.Message}");
        }

        // ── Orchestrion Rolls ──
        try
        {
            var orchestrionSheet = dataManager.GetExcelSheet<Orchestrion>();
            if (orchestrionSheet != null && ps != null)
            {
                int total = 0, unlocked = 0;
                foreach (var roll in orchestrionSheet)
                {
                    if (roll.RowId == 0) continue;
                    var name = roll.Name.ToString();
                    if (string.IsNullOrEmpty(name)) continue;
                    total++;
                    if (ps->IsOrchestrionRollUnlocked(roll.RowId))
                        unlocked++;
                }
                results.Add(new CollectionSummary { Category = "Orchestrion", Unlocked = unlocked, Total = total });
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[XA] Error collecting orchestrion: {ex.Message}");
        }

        // ── Triple Triad Cards ──
        try
        {
            var cardSheet = dataManager.GetExcelSheet<TripleTriadCard>();
            if (cardSheet != null && uiState != null)
            {
                int total = 0, unlocked = 0;
                foreach (var card in cardSheet)
                {
                    if (card.RowId == 0) continue;
                    total++;
                    if (uiState->IsTripleTriadCardUnlocked((ushort)card.RowId))
                        unlocked++;
                }
                results.Add(new CollectionSummary { Category = "TT Cards", Unlocked = unlocked, Total = total });
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[XA] Error collecting TT cards: {ex.Message}");
        }

        return results;
    }
}
