using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Lumina.Excel.Sheets;
using XADatabase.Database;
using XADatabase.Services;

namespace XADatabase.Windows;

/// <summary>
/// SearchTab - partial class split from MainWindow.
/// </summary>
public partial class MainWindow
{
    private void DrawSearchTab()
    {
        var tabFlags = selectSearchTabOnNextDraw ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
        selectSearchTabOnNextDraw = false;

        using var tab = ImRaii.TabItem("Search", tabFlags);
        if (!tab.Success)
            return;

        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "Item Search");
        ImGui.TextDisabled("Search across all inventories, retainers, and saddlebags for all saved characters.");
        ImGui.Spacing();

        if (activeExactItemSearch != null)
        {
            var exactLabel = activeExactItemSearch.IsHq
                ? $"{activeExactItemSearch.ItemName} (HQ)"
                : activeExactItemSearch.ItemName;
            ImGui.TextDisabled($"Exact item search: {exactLabel}");
            ImGui.SameLine();
            if (ImGui.SmallButton("Clear Exact Search"))
            {
                activeExactItemSearch = null;
                RefreshItemSearchResults();
            }

            ImGui.Spacing();
        }

        ImGui.SetNextItemWidth(Scale(400f));
        if (ImGui.InputTextWithHint("##GlobalItemSearch", "Search items by name (2+ chars)...", ref itemSearchText, 256))
        {
            activeExactItemSearch = null;
            RefreshItemSearchResults();
        }

        ImGui.Spacing();

        if (itemSearchResults.Count > 0)
        {
            var grouped = itemSearchResults
                .GroupBy(r => (r.CharacterName, r.World, Location: SimplifyContainerName(r.ContainerName), r.ItemName, r.ItemId, r.IsHq))
                .Select(g => (g.Key.ItemName, Qty: g.Sum(r => r.Quantity), g.Key.IsHq, Location: g.Key.Location, Char: $"{g.Key.CharacterName} @ {g.Key.World}", g.Key.ItemId))
                .OrderBy(r => r.ItemName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Char, StringComparer.OrdinalIgnoreCase)
                .ToList();

            ImGui.TextDisabled($"{grouped.Count} result(s)");
            ImGui.Spacing();

            using var searchTable = ImRaii.Table(
                "GlobalSearchResults",
                5,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Sortable | ImGuiTableFlags.SortTristate,
                new Vector2(0, MathF.Max(0f, ImGui.GetContentRegionAvail().Y - Scale(30f))));
            if (searchTable.Success)
            {
                ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.DefaultSort);
                ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, Scale(50f));
                ImGui.TableSetupColumn("HQ", ImGuiTableColumnFlags.WidthFixed, Scale(30f));
                ImGui.TableSetupColumn("Location", ImGuiTableColumnFlags.WidthFixed, Scale(180f));
                ImGui.TableSetupColumn("Character", ImGuiTableColumnFlags.WidthFixed, Scale(160f));
                ImGui.TableHeadersRow();

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

                foreach (var result in grouped)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.PushID($"{result.ItemId}:{result.IsHq}:{result.Char}:{result.Location}");
                    ImGui.Text(result.ItemName);
                    DrawSearchItemHoverTooltip(result.ItemId, result.IsHq);
                    ImGui.PopID();

                    ImGui.TableNextColumn();
                    ImGui.Text($"x{result.Qty}");

                    ImGui.TableNextColumn();
                    if (result.IsHq)
                        ImGui.TextColored(new Vector4(0.3f, 1.0f, 0.5f, 1.0f), "HQ");
                    else
                        ImGui.TextDisabled("-");

                    ImGui.TableNextColumn();
                    ImGui.Text(result.Location);
                    ImGui.TableNextColumn();
                    ImGui.TextDisabled(result.Char);
                }
            }
        }
        else if (activeExactItemSearch != null || itemSearchText.Length >= 2)
        {
            ImGui.TextDisabled("No results found.");
        }
        else
        {
            ImGui.TextDisabled("Type at least 2 characters to search.");
        }
    }

    public void OpenSearchForItem(uint rawItemId, bool isHq)
    {
        var itemId = NormalizeSearchItemId(rawItemId);
        var itemName = ResolveSearchItemName(itemId);
        if (string.IsNullOrWhiteSpace(itemName))
            itemName = $"Item #{itemId}";

        activeExactItemSearch = new SearchItemRequest(itemId, isHq, itemName);
        itemSearchText = itemName;
        itemSearchResults = SearchSnapshotItemsByExactMatch(activeExactItemSearch, null);
        selectSearchTabOnNextDraw = true;
        IsOpen = true;
    }

    private void RefreshItemSearchResults()
    {
        if (activeExactItemSearch != null)
        {
            itemSearchResults = SearchSnapshotItemsByExactMatch(activeExactItemSearch, null);
            return;
        }

        itemSearchResults = itemSearchText.Length >= 2
            ? SearchSnapshotItemsByName(itemSearchText, null)
            : new List<ItemLocationResult>();
    }

    private List<ItemLocationResult> SearchSnapshotItemsByExactMatch(SearchItemRequest request, int? maxResults)
    {
        var results = new List<ItemLocationResult>();

        foreach (var snapshot in plugin.SnapshotRepo.GetAllSnapshots())
        {
            foreach (var item in snapshot.AllItems)
            {
                if (item.ItemId != request.ItemId || item.IsHq != request.IsHq)
                    continue;

                results.Add(BuildItemLocationResult(snapshot, item.ContainerName, item.ItemId, item.ItemName, item.Quantity, item.IsHq));
            }

            foreach (var item in snapshot.RetainerItems)
            {
                if (item.ItemId != request.ItemId || item.IsHq != request.IsHq)
                    continue;

                results.Add(BuildItemLocationResult(snapshot, $"Retainer: {item.RetainerName}", item.ItemId, item.ItemName, item.Quantity, item.IsHq));
            }
        }

        var orderedResults = results
            .OrderByDescending(r => r.UpdatedUtc, StringComparer.Ordinal)
            .ThenBy(r => r.CharacterName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.ContainerName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return maxResults.HasValue ? orderedResults.Take(maxResults.Value).ToList() : orderedResults;
    }

    private static ItemLocationResult BuildItemLocationResult(
        XaCharacterSnapshotData snapshot,
        string containerName,
        uint itemId,
        string itemName,
        int quantity,
        bool isHq)
    {
        return new ItemLocationResult
        {
            ContentId = snapshot.Row.ContentId,
            CharacterName = snapshot.Row.CharacterName,
            World = snapshot.Row.World,
            UpdatedUtc = snapshot.Row.UpdatedUtc,
            ContainerName = containerName,
            ItemId = itemId,
            ItemName = itemName,
            Quantity = quantity,
            IsHq = isHq,
        };
    }

    private void DrawSearchItemHoverTooltip(uint itemId, bool isHq)
    {
        if (!plugin.Configuration.SearchHoverTooltipEnabled || !ImGui.IsItemHovered())
            return;

        if (!plugin.ItemLocationTooltip.TryGetSummary(itemId, isHq, out var summary) || summary.TotalQuantity <= 0)
            return;

        var tooltipLines = ItemLocationTooltipService.BuildTooltipLines(summary, plugin.Configuration.SearchHoverTooltipCharacterLimit);
        var itemLabel = string.IsNullOrWhiteSpace(summary.ItemName)
            ? ResolveSearchItemName(itemId)
            : summary.ItemName;

        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(Scale(520f));
        ImGui.TextUnformatted(isHq ? $"{itemLabel} (HQ)" : itemLabel);
        ImGui.Separator();
        foreach (var line in tooltipLines)
            ImGui.TextUnformatted(line);

        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }

    private static uint NormalizeSearchItemId(uint rawItemId)
    {
        return rawItemId >= 2_000_000 ? rawItemId : rawItemId % 500_000;
    }

    private string ResolveSearchItemName(uint itemId)
    {
        if (itemId >= 2_000_000)
        {
            var eventItems = Plugin.DataManager.GetExcelSheet<EventItem>();
            if (eventItems != null && eventItems.TryGetRow(itemId, out var eventItem))
                return eventItem.Singular.ToString();
        }

        var items = Plugin.DataManager.GetExcelSheet<Item>();
        return items != null && items.TryGetRow(itemId, out var item)
            ? item.Name.ToString()
            : string.Empty;
    }
}
