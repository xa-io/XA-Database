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
/// RetainersTab — partial class split from MainWindow.
/// </summary>
public partial class MainWindow
{
    // ───────────────────────────────────────────────
    //  Retainers Tab — Overview + collapsible per-retainer detail
    // ───────────────────────────────────────────────
    private void DrawRetainersTab()
    {
        using var tab = ImRaii.TabItem("Retainers");
        if (!tab.Success)
            return;

        ImGui.Spacing();

        var playerState = Plugin.PlayerState;
        if (!playerState.IsLoaded && cachedRetainers.Count == 0)
        {
            ImGui.TextColored(new Vector4(1.0f, 0.6f, 0.0f, 1.0f), "Not logged in \u2014 select a character above to view data.");
            return;
        }

        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), $"Retainers ({cachedRetainers.Count})");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (cachedRetainers.Count == 0)
        {
            ImGui.TextDisabled("No retainer data available.");
            ImGui.TextDisabled("Open and close a summoning bell to load retainer data.");
            return;
        }

        // ── Retainer overview table ──
        using (var table = ImRaii.Table("RetainerTable", 8, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            if (table.Success)
            {
                ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Lv", ImGuiTableColumnFlags.WidthFixed, 30);
                ImGui.TableSetupColumn("Gil", ImGuiTableColumnFlags.WidthFixed, 80);
                ImGui.TableSetupColumn("Items", ImGuiTableColumnFlags.WidthFixed, 40);
                ImGui.TableSetupColumn("Market", ImGuiTableColumnFlags.WidthFixed, 45);
                ImGui.TableSetupColumn("Town", ImGuiTableColumnFlags.WidthFixed, 90);
                ImGui.TableSetupColumn("Venture", ImGuiTableColumnFlags.WidthFixed, 65);
                ImGui.TableSetupColumn("ETA", ImGuiTableColumnFlags.WidthFixed, 65);
                ImGui.TableHeadersRow();

                foreach (var r in cachedRetainers)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn(); ImGui.Text(r.Name);
                    ImGui.TableNextColumn(); ImGui.Text($"{r.Level}");
                    ImGui.TableNextColumn(); ImGui.TextColored(new Vector4(1.0f, 0.9f, 0.3f, 1.0f), $"{r.Gil:N0}");
                    ImGui.TableNextColumn(); ImGui.Text($"{r.ItemCount}");
                    ImGui.TableNextColumn();
                    if (r.MarketItemCount > 0)
                        ImGui.Text($"{r.MarketItemCount}");
                    else
                        ImGui.TextDisabled("0");
                    ImGui.TableNextColumn(); ImGui.TextDisabled(r.Town);
                    ImGui.TableNextColumn();
                    if (r.VentureStatus == "Complete")
                        ImGui.TextColored(new Vector4(0.3f, 1.0f, 0.5f, 1.0f), "Complete");
                    else if (r.VentureStatus == "Active")
                        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "Active");
                    else
                        ImGui.TextDisabled("Idle");
                    ImGui.TableNextColumn();
                    if (r.VentureEta.Length > 0)
                        ImGui.Text(r.VentureEta);
                    else
                        ImGui.TextDisabled("-");
                }
            }
        }

        // ── Totals row ──
        uint totalRetainerGil = 0;
        foreach (var r in cachedRetainers)
            totalRetainerGil += r.Gil;

        // Get venture coin count from cached currencies
        int ventureCoins = 0;
        foreach (var c in cachedCurrencies)
        {
            if (c.Name == "Venture Coins")
            {
                ventureCoins = c.Amount;
                break;
            }
        }

        ImGui.Spacing();
        ImGui.Text("Total Retainer Gil:");
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(1.0f, 0.9f, 0.3f, 1.0f), $"{totalRetainerGil:N0}");
        ImGui.SameLine();
        ImGui.TextDisabled("  |  ");
        ImGui.SameLine();
        ImGui.Text("Venture Coins:");
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), $"{ventureCoins:N0}");

        // ── Show All Retainers toggle ──
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.Checkbox("Show All Retainers (Inventory + Market)", ref showAllRetainers);

        if (showAllRetainers)
        {
            ImGui.Spacing();

            foreach (var retainer in cachedRetainers)
            {
                // Build header with retainer info
                var retListings = cachedListings.Where(l => l.RetainerId == retainer.RetainerId).ToList();
                var retItems = cachedRetainerItems.Where(i => i.RetainerId == retainer.RetainerId).ToList();
                long retMarketVal = 0;
                foreach (var l in retListings)
                    retMarketVal += (long)l.UnitPrice * l.Quantity;

                var ventureLabel = retainer.VentureStatus == "Complete" ? " [Venture Ready]" : "";
                var headerText = $"{retainer.Name}  (Lv{retainer.Level} — {retainer.Gil:N0} gil — {retItems.Count} items — {retListings.Count} listings){ventureLabel}";

                if (!ImGui.CollapsingHeader(headerText))
                    continue;

                // ── Market Listings sub-section ──
                if (retListings.Count > 0)
                {
                    ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f),
                        $"  Market Listings ({retListings.Count}) — Total Value: {retMarketVal:N0} gil");
                    ImGui.Spacing();

                    using (var listingTable = ImRaii.Table($"RetListing_{retainer.RetainerId}", 5,
                        ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
                        new Vector2(0, Math.Min(retListings.Count * 24 + 30, 200))))
                    {
                        if (listingTable.Success)
                        {
                            ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
                            ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 40);
                            ImGui.TableSetupColumn("HQ", ImGuiTableColumnFlags.WidthFixed, 30);
                            ImGui.TableSetupColumn("Unit Price", ImGuiTableColumnFlags.WidthFixed, 100);
                            ImGui.TableSetupColumn("Total", ImGuiTableColumnFlags.WidthFixed, 100);
                            ImGui.TableHeadersRow();

                            foreach (var l in retListings)
                            {
                                ImGui.TableNextRow();
                                ImGui.TableNextColumn(); ImGui.Text(l.ItemName);
                                ImGui.TableNextColumn(); ImGui.Text($"{l.Quantity}");
                                ImGui.TableNextColumn();
                                if (l.IsHq) ImGui.TextColored(new Vector4(0.3f, 1.0f, 0.5f, 1.0f), "HQ");
                                else ImGui.TextDisabled("-");
                                ImGui.TableNextColumn(); ImGui.Text($"{l.UnitPrice:N0}");
                                ImGui.TableNextColumn(); ImGui.TextColored(new Vector4(1.0f, 0.9f, 0.3f, 1.0f), $"{(ulong)l.UnitPrice * (ulong)l.Quantity:N0}");
                            }
                        }
                    }
                    ImGui.Spacing();
                }
                else
                {
                    ImGui.TextDisabled("  No market listings.");
                }

                // ── Inventory sub-section (stacked: combine duplicate items) ──
                if (retItems.Count > 0)
                {
                    var stackedRetItems = retItems
                        .GroupBy(i => (i.ItemName, i.ItemId, i.IsHq))
                        .Select(g => (Name: g.Key.ItemName, Id: g.Key.ItemId, Hq: g.Key.IsHq, Qty: g.Sum(i => i.Quantity)))
                        .OrderBy(i => i.Name)
                        .ToList();

                    ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f),
                        $"  Inventory ({stackedRetItems.Count} unique items, {retItems.Sum(i => i.Quantity)} total)");
                    ImGui.Spacing();

                    using (var retInvTable = ImRaii.Table($"RetInv_{retainer.RetainerId}", 4,
                        ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
                        new Vector2(0, Math.Min(stackedRetItems.Count * 24 + 30, 200))))
                    {
                        if (retInvTable.Success)
                        {
                            ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
                            ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 50);
                            ImGui.TableSetupColumn("HQ", ImGuiTableColumnFlags.WidthFixed, 30);
                            ImGui.TableSetupColumn("Item ID", ImGuiTableColumnFlags.WidthFixed, 60);
                            ImGui.TableHeadersRow();

                            foreach (var item in stackedRetItems)
                            {
                                ImGui.TableNextRow();
                                ImGui.TableNextColumn(); ImGui.Text(item.Name);
                                ImGui.TableNextColumn(); ImGui.Text($"x{item.Qty}");
                                ImGui.TableNextColumn();
                                if (item.Hq) ImGui.TextColored(new Vector4(0.3f, 1.0f, 0.5f, 1.0f), "HQ");
                                else ImGui.TextDisabled("-");
                                ImGui.TableNextColumn(); ImGui.TextDisabled($"{item.Id}");
                            }
                        }
                    }
                }
                else
                {
                    ImGui.TextDisabled("  No inventory data — visit this retainer at a summoning bell.");
                }

                ImGui.Spacing();
            }
        }
        else
        {
            // ── Flat combined listings + inventory when checkbox is off ──
            if (cachedListings.Count > 0)
            {
                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), $"Market Listings ({cachedListings.Count})");
                ImGui.Spacing();

                using (var listingTable = ImRaii.Table("ListingTable", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY, new Vector2(0, 200)))
                {
                    if (listingTable.Success)
                    {
                        ImGui.TableSetupColumn("Retainer", ImGuiTableColumnFlags.WidthFixed, 120);
                        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
                        ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 40);
                        ImGui.TableSetupColumn("HQ", ImGuiTableColumnFlags.WidthFixed, 30);
                        ImGui.TableSetupColumn("Unit Price", ImGuiTableColumnFlags.WidthFixed, 100);
                        ImGui.TableSetupColumn("Total", ImGuiTableColumnFlags.WidthFixed, 100);
                        ImGui.TableHeadersRow();

                        foreach (var l in cachedListings)
                        {
                            ImGui.TableNextRow();
                            ImGui.TableNextColumn(); ImGui.TextDisabled(l.RetainerName);
                            ImGui.TableNextColumn(); ImGui.Text(l.ItemName);
                            ImGui.TableNextColumn(); ImGui.Text($"{l.Quantity}");
                            ImGui.TableNextColumn();
                            if (l.IsHq) ImGui.TextColored(new Vector4(0.3f, 1.0f, 0.5f, 1.0f), "HQ");
                            else ImGui.TextDisabled("-");
                            ImGui.TableNextColumn(); ImGui.Text($"{l.UnitPrice:N0}");
                            ImGui.TableNextColumn(); ImGui.TextColored(new Vector4(1.0f, 0.9f, 0.3f, 1.0f), $"{(ulong)l.UnitPrice * (ulong)l.Quantity:N0}");
                        }
                    }
                }
            }
            else
            {
                ImGui.Spacing();
                ImGui.TextDisabled("Visit a retainer's selling list to scan market listings.");
            }

            if (cachedRetainerItems.Count > 0)
            {
                var stackedFlatItems = cachedRetainerItems
                    .GroupBy(i => (i.RetainerName, i.ItemName, i.ItemId, i.IsHq))
                    .Select(g => (Retainer: g.Key.RetainerName, Name: g.Key.ItemName, Id: g.Key.ItemId, Hq: g.Key.IsHq, Qty: g.Sum(i => i.Quantity)))
                    .OrderBy(i => i.Retainer).ThenBy(i => i.Name)
                    .ToList();

                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), $"Retainer Inventory Items ({stackedFlatItems.Count} unique, {cachedRetainerItems.Sum(i => i.Quantity)} total)");
                ImGui.Spacing();

                using (var retInvTable = ImRaii.Table("RetainerInvTable", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Sortable | ImGuiTableFlags.SortTristate, new Vector2(0, 250)))
                {
                    if (retInvTable.Success)
                    {
                        ImGui.TableSetupColumn("Retainer", ImGuiTableColumnFlags.WidthFixed, 120);
                        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.DefaultSort);
                        ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 50);
                        ImGui.TableSetupColumn("HQ", ImGuiTableColumnFlags.WidthFixed, 30);
                        ImGui.TableSetupColumn("Item ID", ImGuiTableColumnFlags.WidthFixed, 60);
                        ImGui.TableHeadersRow();

                        var retSortSpecs = ImGui.TableGetSortSpecs();
                        if (retSortSpecs.SpecsCount > 0)
                        {
                            unsafe
                            {
                                var spec = retSortSpecs.Specs;
                                var asc = spec.SortDirection == ImGuiSortDirection.Ascending;
                                stackedFlatItems.Sort((a, b) =>
                                {
                                    int cmp = spec.ColumnIndex switch
                                    {
                                        0 => string.Compare(a.Retainer, b.Retainer, StringComparison.OrdinalIgnoreCase),
                                        1 => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
                                        2 => a.Qty.CompareTo(b.Qty),
                                        3 => a.Hq.CompareTo(b.Hq),
                                        4 => a.Id.CompareTo(b.Id),
                                        _ => 0,
                                    };
                                    return asc ? cmp : -cmp;
                                });
                            }
                        }

                        foreach (var item in stackedFlatItems)
                        {
                            ImGui.TableNextRow();
                            ImGui.TableNextColumn(); ImGui.TextDisabled(item.Retainer);
                            ImGui.TableNextColumn(); ImGui.Text(item.Name);
                            ImGui.TableNextColumn(); ImGui.Text($"x{item.Qty}");
                            ImGui.TableNextColumn();
                            if (item.Hq) ImGui.TextColored(new Vector4(0.3f, 1.0f, 0.5f, 1.0f), "HQ");
                            else ImGui.TextDisabled("-");
                            ImGui.TableNextColumn(); ImGui.TextDisabled($"{item.Id}");
                        }
                    }
                }
            }
            else
            {
                ImGui.Spacing();
                ImGui.TextDisabled("Visit each retainer at a summoning bell to scan their inventory.");
            }
        }
    }

}
