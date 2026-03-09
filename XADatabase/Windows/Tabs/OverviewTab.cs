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
/// OverviewTab — partial class split from MainWindow.
/// </summary>
public partial class MainWindow
{
    // ───────────────────────────────────────────────
    //  Overview Tab
    // ───────────────────────────────────────────────
    private void DrawOverviewTab()
    {
        using var tab = ImRaii.TabItem("Overview");
        if (!tab.Success)
            return;

        ImGui.Spacing();

        var playerState = Plugin.PlayerState;
        if (!playerState.IsLoaded && !viewingContentId.HasValue)
        {
            ImGui.TextColored(new Vector4(1.0f, 0.6f, 0.0f, 1.0f), "Not logged in \u2014 select a character above to view data.");
            return;
        }

        // Character info header
        string charName;
        if (viewingContentId.HasValue)
        {
            charName = viewingCharName;
        }
        else
        {
            charName = playerState.CharacterName.ToString();
        }
        if (charName.Length > 0)
        {
            ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), charName);
            ImGui.SameLine();
            ImGui.TextDisabled(viewingContentId.HasValue ? "(DB Snapshot)" : "(Overview)");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Two-column layout
        using (var table = ImRaii.Table("OverviewTable", 2, ImGuiTableFlags.None))
        {
            if (table.Success)
            {
                ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed, 140);
                ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);

                // Character name
                if (charName.Length > 0)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text("Character");
                    ImGui.TableNextColumn();
                    ImGui.Text(charName);
                }

                // Live-only sections (World, Job, Location, Content ID)
                if (playerState.IsLoaded)
                {
                    if (playerState.HomeWorld.IsValid)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.Text("World");
                        ImGui.TableNextColumn();
                        ImGui.Text($"{playerState.HomeWorld.Value.Name}");
                    }

                    if (playerState.ClassJob.IsValid)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.Text("Job");
                        ImGui.TableNextColumn();
                        ImGui.Text($"{playerState.ClassJob.Value.Abbreviation} Lv.{playerState.Level}");
                    }

                    var territoryId = Plugin.ClientState.TerritoryType;
                    if (Plugin.DataManager.GetExcelSheet<TerritoryType>().TryGetRow(territoryId, out var territoryRow))
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.Text("Location");
                        ImGui.TableNextColumn();
                        ImGui.Text($"{territoryRow.PlaceName.Value.Name}");
                    }

                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text("Content ID");
                    ImGui.TableNextColumn();
                    ImGui.TextDisabled($"{playerState.ContentId}");
                }
                else if (viewingContentId.HasValue)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text("Content ID");
                    ImGui.TableNextColumn();
                    ImGui.TextDisabled($"{viewingContentId.Value}");
                }

                // Gil from currency collector
                var gilEntry = cachedCurrencies.Find(c => c.Name == "Gil");
                if (gilEntry != null)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text("Gil");
                    ImGui.TableNextColumn();
                    ImGui.TextColored(new Vector4(1.0f, 0.9f, 0.3f, 1.0f), $"{gilEntry.Amount:N0}");
                }

                // FC info in overview (separate rows for downstream data use)
                if (cachedFc != null && !string.IsNullOrEmpty(cachedFc.Name))
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text("FC Name");
                    ImGui.TableNextColumn();
                    ImGui.Text(cachedFc.Name);

                    if (!string.IsNullOrEmpty(cachedFc.Tag))
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.Text("FC Tag");
                        ImGui.TableNextColumn();
                        ImGui.Text($"«{cachedFc.Tag}»");
                    }

                    if (cachedFc.FcPoints > 0)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.Text("FC Points");
                        ImGui.TableNextColumn();
                        ImGui.Text($"{cachedFc.FcPoints:N0}");
                    }
                }
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Inventory summary in overview — aggregate split bags into display groups
        if (cachedInventory.Count > 0)
        {
            ImGui.Text("Inventory Usage");
            ImGui.Spacing();

            // Aggregate groups for overview display
            var overviewGroups = new (string Label, string[] Prefixes)[]
            {
                ("Main Inventory", new[] { "Inventory 1", "Inventory 2", "Inventory 3", "Inventory 4" }),
                ("Equipped", new[] { "Equipped" }),
                ("Armoury", new[] { "Armoury -" }),
                ("Crystals", new[] { "Crystals" }),
                ("Saddlebag", new[] { "Saddlebag 1", "Saddlebag 2" }),
                ("Premium Saddlebag", new[] { "Premium Saddlebag 1", "Premium Saddlebag 2" }),
            };

            using (var invTable = ImRaii.Table("OverviewInvTable", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
            {
                if (invTable.Success)
                {
                    ImGui.TableSetupColumn("Container", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("Used / Total", ImGuiTableColumnFlags.WidthFixed, 100);
                    ImGui.TableSetupColumn("%%", ImGuiTableColumnFlags.WidthFixed, 50);
                    ImGui.TableHeadersRow();

                    foreach (var (label, prefixes) in overviewGroups)
                    {
                        var matching = cachedInventory.Where(inv => prefixes.Any(p => inv.Name.StartsWith(p))).ToList();
                        int used = matching.Sum(m => m.UsedSlots);
                        int total = matching.Sum(m => m.TotalSlots);

                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.Text(label);
                        ImGui.TableNextColumn();
                        ImGui.Text($"{used} / {total}");
                        ImGui.TableNextColumn();
                        var pct = total > 0 ? (float)used / total * 100f : 0f;
                        if (pct > 90f)
                            ImGui.TextColored(new Vector4(1.0f, 0.3f, 0.3f, 1.0f), $"{pct:F0}%%");
                        else if (pct > 70f)
                            ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.3f, 1.0f), $"{pct:F0}%%");
                        else
                            ImGui.Text($"{pct:F0}%%");
                    }
                }
            }
        }
    }

}
