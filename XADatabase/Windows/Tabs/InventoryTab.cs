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
using Dalamud.Interface.Textures;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using XADatabase.Services;

namespace XADatabase.Windows;

/// <summary>
/// InventoryTab — partial class split from MainWindow.
/// </summary>
public partial class MainWindow
{
    // ───────────────────────────────────────────────
    //  Inventory Tab — Sectioned by container group
    // ───────────────────────────────────────────────

    // Section definitions: display name → list of container name prefixes that belong to it
    private static readonly (string Section, string[] Prefixes)[] InventorySections =
    {
        ("Equipped", new[] { "Equipped" }),
        ("Inventory", new[] { "Inventory 1", "Inventory 2", "Inventory 3", "Inventory 4" }),
        ("Armoury - Main Hand", new[] { "Armoury - Main Hand" }),
        ("Armoury - Off Hand", new[] { "Armoury - Off Hand" }),
        ("Armoury - Head", new[] { "Armoury - Head" }),
        ("Armoury - Body", new[] { "Armoury - Body" }),
        ("Armoury - Hands", new[] { "Armoury - Hands" }),
        ("Armoury - Legs", new[] { "Armoury - Legs" }),
        ("Armoury - Feet", new[] { "Armoury - Feet" }),
        ("Armoury - Accessories", new[] { "Armoury - Earring", "Armoury - Necklace", "Armoury - Bracelet", "Armoury - Ring" }),
        ("Armoury - Soul Crystal", new[] { "Armoury - Soul Crystal" }),
        ("Crystals", new[] { "Crystals" }),
        ("Saddlebag", new[] { "Saddlebag 1", "Saddlebag 2" }),
        ("Premium Saddlebag", new[] { "Premium Saddlebag 1", "Premium Saddlebag 2" }),
        (StorageItemCollector.ArmoireName, new[] { StorageItemCollector.ArmoireName }),
        (StorageItemCollector.DresserName, new[] { StorageItemCollector.DresserName }),
    };

    private void DrawInventoryTab()
    {
        using var tab = ImRaii.TabItem("Inventory");
        if (!tab.Success)
            return;

        ImGui.Spacing();

        var playerState = plugin.Services.PlayerState;
        if (!playerState.IsLoaded && !viewingContentId.HasValue && cachedInventory.Count == 0)
        {
            ImGui.TextColored(new Vector4(1.0f, 0.6f, 0.0f, 1.0f), "Not logged in \u2014 select a character above to view data.");
            return;
        }

        // ── Item Search (cross-character) ──
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "Item Locator");
        ImGui.Spacing();
        ImGui.SetNextItemWidth(Scale(300f));
        if (ImGui.InputTextWithHint("##ItemSearch", "Search items across all characters...", ref itemSearchText, 256))
        {
            if (itemSearchText.Length >= 2)
                itemSearchResults = SearchSnapshotItemsByName(itemSearchText);
            else
                itemSearchResults.Clear();
        }

        if (itemSearchResults.Count > 0)
        {
            var invGrouped = itemSearchResults
                .GroupBy(r => (r.CharacterName, r.World, Location: SimplifyContainerName(r.ContainerName), r.ItemName, r.ItemId, r.IsHq))
                .Select(g => (g.Key.ItemName, Qty: g.Sum(r => r.Quantity), g.Key.IsHq, Location: g.Key.Location, Char: $"{g.Key.CharacterName} @ {g.Key.World}"))
                .OrderBy(r => r.ItemName, LocalizedText.Comparer).ThenBy(r => r.Char, LocalizedText.Comparer)
                .ToList();

            ImGui.Spacing();
            using (var searchTable = ImRaii.Table("ItemSearchResults", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Sortable, ScaledVector(0f, 150f)))
            {
                if (searchTable.Success)
                {
                    ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, Scale(50f));
                    ImGui.TableSetupColumn("HQ", ImGuiTableColumnFlags.WidthFixed, Scale(30f));
                    ImGui.TableSetupColumn("Location", ImGuiTableColumnFlags.WidthFixed, Scale(160f));
                    ImGui.TableSetupColumn("Character", ImGuiTableColumnFlags.WidthFixed, Scale(140f));
                    ImGui.TableHeadersRow();

                    var sortSpecs = ImGui.TableGetSortSpecs();
                    if (sortSpecs.SpecsCount > 0)
                    {
                        unsafe
                        {
                            var spec = sortSpecs.Specs;
                            var asc = spec.SortDirection == ImGuiSortDirection.Ascending;
                            invGrouped.Sort((a, b) =>
                            {
                                int cmp = spec.ColumnIndex switch
                                {
                                    0 => LocalizedText.Compare(a.ItemName, b.ItemName),
                                    1 => a.Qty.CompareTo(b.Qty),
                                    2 => a.IsHq.CompareTo(b.IsHq),
                                    3 => LocalizedText.Compare(a.Location, b.Location),
                                    4 => LocalizedText.Compare(a.Char, b.Char),
                                    _ => 0,
                                };
                                return asc ? cmp : -cmp;
                            });
                        }
                    }

                    foreach (var r in invGrouped)
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

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // ── Sectioned inventory display ──
        foreach (var (sectionName, prefixes) in InventorySections)
        {
            // Gather items and slot summary for this section
            var sectionItems = cachedItems.Where(i => prefixes.Any(p => i.ContainerName.StartsWith(p))).ToList();
            var sectionSlots = cachedInventory.Where(inv => prefixes.Any(p => inv.Name.StartsWith(p))).ToList();

            int usedTotal = 0, slotTotal = 0;
            foreach (var s in sectionSlots) { usedTotal += s.UsedSlots; slotTotal += s.TotalSlots; }
            var pct = slotTotal > 0 ? (float)usedTotal / slotTotal * 100f : 0f;

            // Section header with slot usage
            var isStorage = sectionName is StorageItemCollector.ArmoireName or StorageItemCollector.DresserName;
            var recorded = sectionSlots.Count > 0;
            var headerLabel = isStorage && !recorded
                ? $"{sectionName}  (Not recorded)"
                : sectionItems.Count > 0
                ? $"{sectionName}  ({usedTotal}/{slotTotal} — {pct:F0}%)"
                : $"{sectionName}  ({usedTotal}/{slotTotal})";

            if (!ImGui.CollapsingHeader($"{headerLabel}###InventorySection_{sectionName}"))
                continue;

            if (sectionName == StorageItemCollector.DresserName && recorded)
                ImGui.TextDisabled("  Usage counts physical slots; stored outfit pieces appear as individual items.");

            if (sectionItems.Count == 0)
            {
                ImGui.TextDisabled(isStorage
                    ? recorded ? "  Recorded empty." : "  Not recorded. Open this storage in game, then save."
                    : "  No items.");
                continue;
            }

            // Keep distinct item IDs separate even when localized names match.
            var aggregated = sectionItems
                .GroupBy(i => (i.ItemId, i.ItemName, i.IsHq))
                .Select(g => new { g.Key.ItemId, Name = g.Key.ItemName, IsHq = g.Key.IsHq, Qty = g.Sum(x => (long)x.Quantity) })
                .OrderBy(a => a.Name, LocalizedText.Comparer)
                .ToList();

            using var itemTable = ImRaii.Table($"InvSection_{sectionName}", 3,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
                new Vector2(0, Math.Min(aggregated.Count * Scale(24f) + Scale(30f), Scale(250f))));
            if (!itemTable.Success) continue;

            ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, Scale(60f));
            ImGui.TableSetupColumn("HQ", ImGuiTableColumnFlags.WidthFixed, Scale(30f));
            ImGui.TableHeadersRow();

            var sheet = plugin.Services.DataManager.GetExcelSheet<Item>();
            foreach (var item in aggregated)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                if (sheet != null && sheet.TryGetRow(item.ItemId, out var itemRow)
                    && Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(itemRow.Icon, item.IsHq)).TryGetWrap(out var icon, out _))
                {
                    ImGui.Image(icon.Handle, ScaledVector(20f, 20f));
                    ImGui.SameLine();
                }
                ImGui.Text(item.Name);
                DrawSearchItemHoverTooltip(item.ItemId, item.IsHq);
                ImGui.TableNextColumn(); ImGui.Text($"{item.Qty:N0}");
                ImGui.TableNextColumn();
                if (item.IsHq) ImGui.TextColored(new Vector4(0.3f, 1.0f, 0.5f, 1.0f), "HQ");
                else ImGui.TextDisabled("-");
            }
        }
    }

}
