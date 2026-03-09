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
/// JobsTab — partial class split from MainWindow.
/// </summary>
public partial class MainWindow
{
    // ───────────────────────────────────────────────
    //  Jobs Tab — Compact single-view, all roles visible
    // ───────────────────────────────────────────────

    // Two-column role layout pairs — all shown in one view
    private static readonly (string Left, string Right)[] JobPairs =
    {
        ("Tank", "Healer"),
        ("Melee DPS", "Ranged DPS"),
        (null!, "Caster DPS"),
        ("Crafter", "Gatherer"),
    };

    private void DrawJobsTab()
    {
        using var tab = ImRaii.TabItem("Jobs");
        if (!tab.Success)
            return;

        ImGui.Spacing();

        var playerState = Plugin.PlayerState;
        if (!playerState.IsLoaded && cachedJobs.Count == 0)
        {
            ImGui.TextColored(new Vector4(1.0f, 0.6f, 0.0f, 1.0f), "Not logged in \u2014 select a character above to view data.");
            return;
        }

        if (cachedJobs.Count == 0)
        {
            ImGui.TextDisabled("No job data collected yet. Click Refresh.");
            return;
        }

        foreach (var (leftCat, rightCat) in JobPairs)
        {
            var leftJobs = leftCat != null ? cachedJobs.Where(j => j.Category == leftCat).ToList() : new List<JobEntry>();
            var rightJobs = rightCat != null ? cachedJobs.Where(j => j.Category == rightCat).ToList() : new List<JobEntry>();
            var maxRows = Math.Max(leftJobs.Count, rightJobs.Count);
            if (maxRows == 0) continue;

            using var pairTable = ImRaii.Table($"JobPair_{leftCat}_{rightCat}", 2, ImGuiTableFlags.None);
            if (!pairTable.Success) continue;

            ImGui.TableSetupColumn("Left", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Right", ImGuiTableColumnFlags.WidthStretch);

            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            if (leftCat != null && leftJobs.Count > 0)
                DrawJobRoleGroup(leftCat, leftJobs);

            ImGui.TableNextColumn();
            if (rightCat != null && rightJobs.Count > 0)
                DrawJobRoleGroup(rightCat, rightJobs);
        }
    }

    private void DrawJobRoleGroup(string category, List<JobEntry> jobs)
    {
        // Role header with icon-like prefix
        var headerColor = category switch
        {
            "Tank" => new Vector4(0.3f, 0.5f, 1.0f, 1.0f),
            "Healer" => new Vector4(0.3f, 0.9f, 0.3f, 1.0f),
            "Melee DPS" => new Vector4(0.9f, 0.3f, 0.3f, 1.0f),
            "Ranged DPS" => new Vector4(0.9f, 0.3f, 0.3f, 1.0f),
            "Caster DPS" => new Vector4(0.9f, 0.3f, 0.3f, 1.0f),
            "Crafter" => new Vector4(0.8f, 0.6f, 0.2f, 1.0f),
            "Gatherer" => new Vector4(0.2f, 0.7f, 0.4f, 1.0f),
            _ => new Vector4(0.8f, 0.8f, 0.8f, 1.0f),
        };

        // Display name mapping to match in-game
        var displayName = category switch
        {
            "Ranged DPS" => "Physical Ranged DPS",
            "Caster DPS" => "Magical Ranged DPS",
            "Crafter" => "Disciples of the Hand",
            "Gatherer" => "Disciples of the Land",
            _ => category,
        };

        ImGui.TextColored(headerColor, displayName);
        ImGui.Spacing();

        foreach (var job in jobs)
        {
            // Job row: name + level right-aligned
            if (job.IsUnlocked)
            {
                ImGui.Text($"  {job.Name}");
                ImGui.SameLine(ImGui.GetContentRegionAvail().X - 30);
                if (job.Level >= 100)
                    ImGui.TextColored(new Vector4(0.3f, 1.0f, 0.5f, 1.0f), $"{job.Level}");
                else
                    ImGui.Text($"{job.Level}");
            }
            else
            {
                ImGui.TextDisabled($"  {job.Name}");
                ImGui.SameLine(ImGui.GetContentRegionAvail().X - 30);
                ImGui.TextDisabled("-");
            }
        }

        ImGui.Spacing();
    }

}
