using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Lumina.Excel.Sheets;
using XADatabase.Collectors;
using XADatabase.Database;
using XADatabase.Models;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using XADatabase.Services;

namespace XADatabase.Windows;

/// <summary>
/// ProgressTab — partial class split from MainWindow.
/// </summary>
public partial class MainWindow
{
    // ───────────────────────────────────────────────
    //  Progress Tab
    // ───────────────────────────────────────────────
    private void DrawProgressTab()
    {
        using var tab = ImRaii.TabItem("Progress");
        if (!tab.Success)
            return;

        ImGui.Spacing();

        // ── Collections ──
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "Collections");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (cachedCollections.Count > 0)
        {
            using (var collTable = ImRaii.Table("CollectionsTable", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
            {
                if (collTable.Success)
                {
                    ImGui.TableSetupColumn("Category", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("Unlocked", ImGuiTableColumnFlags.WidthFixed, 80);
                    ImGui.TableSetupColumn("Total", ImGuiTableColumnFlags.WidthFixed, 80);
                    ImGui.TableSetupColumn("Progress", ImGuiTableColumnFlags.WidthFixed, 120);
                    ImGui.TableHeadersRow();

                    foreach (var c in cachedCollections)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn(); ImGui.Text(c.Category);
                        ImGui.TableNextColumn(); ImGui.Text($"{c.Unlocked}");
                        ImGui.TableNextColumn(); ImGui.TextDisabled($"{c.Total}");
                        ImGui.TableNextColumn();
                        if (c.Total > 0)
                        {
                            var pct = (float)c.Unlocked / c.Total;
                            ImGui.ProgressBar(pct, new Vector2(-1, 0), $"{pct:P1}");
                        }
                        else
                        {
                            ImGui.TextDisabled("-");
                        }
                    }
                }
            }
        }
        else
        {
            ImGui.TextDisabled("No collection data. Click Refresh + Save to scan.");
        }

        // ── Active Quests ──
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), $"Active Quests ({cachedQuests.Count})");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (cachedQuests.Count > 0)
        {
            using (var questTable = ImRaii.Table("QuestTable", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY, new Vector2(0, 300)))
            {
                if (questTable.Success)
                {
                    ImGui.TableSetupColumn("Quest", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("Step", ImGuiTableColumnFlags.WidthFixed, 50);
                    ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed, 60);
                    ImGui.TableHeadersRow();

                    foreach (var q in cachedQuests)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn(); ImGui.Text(q.Name);
                        ImGui.TableNextColumn(); ImGui.Text($"{q.Sequence}");
                        ImGui.TableNextColumn(); ImGui.TextDisabled($"{q.QuestId}");
                    }
                }
            }
        }
        else
        {
            ImGui.TextDisabled("No active quests detected. Click Refresh + Save to scan.");
        }

        if (cachedQuests.Count > 0 && viewingContentId.HasValue)
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.3f, 1.0f),
                "Showing last saved quests for this character.");
        }

        // ── MSQ Progress ──
        ImGui.Spacing();
        ImGui.Spacing();
        if (cachedMsqMilestones.Count > 0)
        {
            var completed = cachedMsqMilestones.Count(m => m.IsComplete);
            var total = cachedMsqMilestones.Count;
            var pct = total > 0 ? (float)completed / total : 0f;

            ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), $"MSQ Progress ({completed}/{total})");
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.ProgressBar(pct, new Vector2(-1, 0), $"{pct:P1}");
            ImGui.Spacing();

            // Group by expansion
            string? currentExpansion = null;
            using (var msqTable = ImRaii.Table("MsqTable", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY, new Vector2(0, 350)))
            {
                if (msqTable.Success)
                {
                    ImGui.TableSetupColumn("Milestone", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("Expansion", ImGuiTableColumnFlags.WidthFixed, 120);
                    ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 60);
                    ImGui.TableHeadersRow();

                    foreach (var m in cachedMsqMilestones)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        if (m.IsComplete)
                            ImGui.TextColored(new Vector4(0.2f, 1.0f, 0.2f, 1.0f), m.Label);
                        else
                            ImGui.TextDisabled(m.Label);

                        ImGui.TableNextColumn();
                        if (m.Expansion != currentExpansion)
                        {
                            ImGui.Text(m.Expansion);
                            currentExpansion = m.Expansion;
                        }

                        ImGui.TableNextColumn();
                        if (m.IsComplete)
                            ImGui.TextColored(new Vector4(0.2f, 1.0f, 0.2f, 1.0f), "Done");
                        else
                            ImGui.TextDisabled("---");
                    }
                }
            }
        }
        else
        {
            ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "MSQ Progress");
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.TextDisabled("No MSQ data. Click Refresh + Save to scan.");
        }
    }

    // ───────────────────────────────────────────────
    //  Dashboard Tab (Cross-Character)
    // ───────────────────────────────────────────────

    // Dashboard row cache for sorting
    private struct DashRow
    {
        public string Name, World, Server, Region, FcName, LastSeen;
        public long Gil, RetainerGil, MarketValue;
        public int Retainers, Listings, VenturesReady;
        public ulong ContentId;
        public Dictionary<string, int> JobLevels;
    }

    // Job abbreviations for dashboard columns — matches in-game order
    private static readonly string[] DashJobAbbrevs = {
        "PLD", "WAR", "DRK", "GNB",
        "WHM", "SCH", "AST", "SGE",
        "MNK", "DRG", "NIN", "SAM", "RPR", "VPR",
        "BRD", "MCH", "DNC",
        "BLM", "SMN", "RDM", "PCT",
        "CRP", "BSM", "ARM", "GSM", "LTW", "WVR", "ALC", "CUL",
        "MIN", "BTN", "FSH",
    };

    private struct CollRow
    {
        public string Location, Name;
        public int MountsU, MountsT, MinionsU, MinionsT, OrchU, OrchT, TtU, TtT;
    }

    private struct MsqRow
    {
        public string Location, Name;
        public int Completed, Total;
        public float Percent;
    }

    private bool HasAutoRetainer()
    {
        try
        {
            return Plugin.PluginInterface.InstalledPlugins
                .Any(p => p.InternalName == "AutoRetainer" && p.IsLoaded);
        }
        catch { return false; }
    }

}
