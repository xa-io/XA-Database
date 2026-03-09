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
/// SearchTab — partial class split from MainWindow.
/// </summary>
public partial class MainWindow
{
    // ───────────────────────────────────────────────
    //  Search Tab — cross-inventory, retainer, saddlebag search
    // ───────────────────────────────────────────────
    private void DrawSearchTab()
    {
        using var tab = ImRaii.TabItem("Search");
        if (!tab.Success)
            return;

        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "Item Search");
        ImGui.TextDisabled("Search across all inventories, retainers, and saddlebags for all saved characters.");
        ImGui.Spacing();

        ImGui.SetNextItemWidth(400);
        if (ImGui.InputTextWithHint("##GlobalItemSearch", "Search items by name (2+ chars)...", ref itemSearchText, 256))
        {
            if (itemSearchText.Length >= 2)
                itemSearchResults = SearchSnapshotItemsByName(itemSearchText);
            else
                itemSearchResults.Clear();
        }

        ImGui.Spacing();

        if (itemSearchResults.Count > 0)
        {
            // Group results: combine Inventory1/2/3/4 into "Inventory", Saddlebag1/2 into "Saddlebag", etc.
            var grouped = itemSearchResults
                .GroupBy(r => (r.CharacterName, r.World, Location: SimplifyContainerName(r.ContainerName), r.ItemName, r.ItemId, r.IsHq))
                .Select(g => (g.Key.ItemName, Qty: g.Sum(r => r.Quantity), g.Key.IsHq, Location: g.Key.Location, Char: $"{g.Key.CharacterName} @ {g.Key.World}"))
                .OrderBy(r => r.ItemName).ThenBy(r => r.Char)
                .ToList();

            ImGui.TextDisabled($"{grouped.Count} result(s)");
            ImGui.Spacing();

            using (var searchTable = ImRaii.Table("GlobalSearchResults", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Sortable | ImGuiTableFlags.SortTristate, new Vector2(0, ImGui.GetContentRegionAvail().Y - 30)))
            {
                if (searchTable.Success)
                {
                    ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.DefaultSort);
                    ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 50);
                    ImGui.TableSetupColumn("HQ", ImGuiTableColumnFlags.WidthFixed, 30);
                    ImGui.TableSetupColumn("Location", ImGuiTableColumnFlags.WidthFixed, 180);
                    ImGui.TableSetupColumn("Character", ImGuiTableColumnFlags.WidthFixed, 160);
                    ImGui.TableHeadersRow();

                    // Sort based on clicked column
                    var sortSpecs = ImGui.TableGetSortSpecs();
                    if (sortSpecs.SpecsCount > 0)
                    {
                        unsafe
                        {
                            var spec = sortSpecs.Specs;
                            var asc = spec.SortDirection == ImGuiSortDirection.Ascending;
                            grouped.Sort((a, b) =>
                            {
                                int cmp = spec.ColumnIndex switch
                                {
                                    0 => string.Compare(a.ItemName, b.ItemName, StringComparison.OrdinalIgnoreCase),
                                    1 => a.Qty.CompareTo(b.Qty),
                                    2 => a.IsHq.CompareTo(b.IsHq),
                                    3 => string.Compare(a.Location, b.Location, StringComparison.OrdinalIgnoreCase),
                                    4 => string.Compare(a.Char, b.Char, StringComparison.OrdinalIgnoreCase),
                                    _ => 0,
                                };
                                return asc ? cmp : -cmp;
                            });
                        }
                    }

                    foreach (var r in grouped)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn(); ImGui.Text(r.ItemName);
                        ImGui.TableNextColumn(); ImGui.Text($"x{r.Qty}");
                        ImGui.TableNextColumn();
                        if (r.IsHq) ImGui.TextColored(new Vector4(0.3f, 1.0f, 0.5f, 1.0f), "HQ");
                        else ImGui.TextDisabled("-");
                        ImGui.TableNextColumn(); ImGui.Text(r.Location);
                        ImGui.TableNextColumn(); ImGui.TextDisabled(r.Char);
                    }
                }
            }
        }
        else if (itemSearchText.Length >= 2)
        {
            ImGui.TextDisabled("No results found.");
        }
        else
        {
            ImGui.TextDisabled("Type at least 2 characters to search.");
        }
    }

}
