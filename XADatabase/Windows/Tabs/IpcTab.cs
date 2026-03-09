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
/// IpcTab — partial class split from MainWindow.
/// </summary>
public partial class MainWindow
{
    // ───────────────────────────────────────────────
    //  [?] Tab — IPC Reference
    // ───────────────────────────────────────────────
    private void DrawIpcTab()
    {
        using var tab = ImRaii.TabItem("[?]");
        if (!tab.Success)
            return;

        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "IPC Calls Available");
        ImGui.TextDisabled("Other plugins can call these IPC channels to interact with XA Database.");
        ImGui.TextDisabled("All channels use the \"XA.Database.\" prefix.");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // ── Actions ──
        ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.3f, 1.0f), "Actions");
        ImGui.Spacing();

        if (ImGui.BeginTable("IpcActions", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Channel", ImGuiTableColumnFlags.WidthFixed, 220);
            ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("Description");
            ImGui.TableHeadersRow();

            DrawIpcRow("XA.Database.Save", "Action", "Refresh live data + save snapshot to database");
            DrawIpcRow("XA.Database.Refresh", "Action", "Refresh live data into memory (no DB write)");

            ImGui.EndTable();
        }

        ImGui.Spacing();

        // ── Queries ──
        ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.3f, 1.0f), "Queries");
        ImGui.Spacing();

        if (ImGui.BeginTable("IpcQueries", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Channel", ImGuiTableColumnFlags.WidthFixed, 260);
            ImGui.TableSetupColumn("Returns", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("Description");
            ImGui.TableHeadersRow();

            DrawIpcRow("XA.Database.IsReady", "bool", "True when player is loaded");
            DrawIpcRow("XA.Database.GetVersion", "string", "Plugin version (e.g. \"0.0.0.21\")");
            DrawIpcRow("XA.Database.GetDbPath", "string", "Absolute path to xa.db");
            DrawIpcRow("XA.Database.GetCharacterName", "string", "Current character name");
            DrawIpcRow("XA.Database.GetGil", "int", "Current character's gil");
            DrawIpcRow("XA.Database.GetRetainerGil", "int", "Total gil across all retainers");
            DrawIpcRow("XA.Database.GetFcInfo", "string", "Legacy FC Name|Tag|Points|Rank compatibility payload");
            DrawIpcRow("XA.Database.GetFcName", "string", "Current Free Company name");
            DrawIpcRow("XA.Database.GetFcTag", "string", "Current Free Company tag");
            DrawIpcRow("XA.Database.GetFcPoints", "int", "Current Free Company credits / points");
            DrawIpcRow("XA.Database.GetPlotInfo", "string", "FC estate location");
            DrawIpcRow("XA.Database.GetPersonalPlotInfo", "string", "Personal Estate|Apartment (pipe-delimited)");
            DrawIpcRow("XA.Database.GetApartment", "string", "Apartment string only");
            DrawIpcRow("XA.Database.GetCharacterSummaryJson", "string", "Structured JSON summary for the current character snapshot");
            DrawIpcRow("XA.Database.GetLastSnapshotResultJson", "string", "Structured JSON payload describing the last save result");
            DrawIpcRow("XA.Database.SearchItems", "string", "Cross-character item search (takes query string, returns pipe-delimited results)");

            ImGui.EndTable();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "Usage Example (C#)");
        ImGui.Spacing();
        ImGui.TextDisabled("var save = pluginInterface.GetIpcSubscriber<object>(\"XA.Database.Save\");");
        ImGui.TextDisabled("var isReady = pluginInterface.GetIpcSubscriber<bool>(\"XA.Database.IsReady\");");
        ImGui.TextDisabled("if (isReady.InvokeFunc()) save.InvokeAction();");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextDisabled($"XA Database v{PluginVersion} — {18} IPC channels registered");
    }

    private static void DrawIpcRow(string channel, string type, string description)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextColored(new Vector4(0.6f, 0.9f, 0.6f, 1.0f), channel);
        ImGui.TableNextColumn();
        ImGui.Text(type);
        ImGui.TableNextColumn();
        ImGui.TextWrapped(description);
    }

    /// <summary>
    /// Simplifies container names for grouped display.
    /// Combines Inventory1/2/3/4 → "Inventory", SaddleBag1/2 → "Saddlebag",
    /// RetainerPage1-7 → "Retainer Inventory", ArmouryXxx → "Armoury", etc.
    /// </summary>
    private static string SimplifyContainerName(string containerName)
    {
        if (string.IsNullOrEmpty(containerName)) return "Unknown";
        var lower = containerName.ToLowerInvariant();
        if (lower.StartsWith("inventory") && !lower.Contains("retainer") && !lower.Contains("buddy"))
            return "Inventory";
        if (lower.StartsWith("saddlebag") || lower.Contains("inventorybuddy"))
            return "Saddlebag";
        if (lower.StartsWith("premiumsaddlebag"))
            return "Premium Saddlebag";
        if (lower.StartsWith("retainerpage") || lower.StartsWith("retainer page"))
            return "Retainer Inventory";
        if (lower.StartsWith("retainermarket"))
            return "Retainer Market";
        if (lower.StartsWith("armoury") || lower.StartsWith("armory"))
            return "Armoury";
        if (lower.StartsWith("equipped"))
            return "Equipped";
        if (lower == "crystals")
            return "Crystals";
        return containerName;
    }

}
