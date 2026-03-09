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
/// DashboardTab — partial class split from MainWindow.
/// </summary>
public partial class MainWindow
{
    private void DrawDashboardTab()
    {
        using var tab = ImRaii.TabItem("Dashboard");
        if (!tab.Success)
            return;

        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "Cross-Character Dashboard");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (knownCharacters.Count == 0)
            knownCharacters = plugin.CharacterRepo.GetAll();

        if (knownCharacters.Count == 0)
        {
            ImGui.TextDisabled("No character data saved yet. Log in and click Refresh + Save.");
            return;
        }

        // Build sortable row data
        var rows = new List<DashRow>(knownCharacters.Count);
        var snapshotMap = new Dictionary<ulong, XaCharacterSnapshotData>();
        long totalGil = 0, totalMarketValue = 0;
        int totalRetainers = 0, totalListings = 0, totalVenturesReady = 0;

        foreach (var ch in knownCharacters)
        {
            var snapshot = plugin.SnapshotRepo.GetSnapshot(ch.ContentId);
            if (snapshot == null)
                continue;

            snapshotMap[ch.ContentId] = snapshot;

            long charGil = snapshot.Row.Gil + snapshot.Row.RetainerGil;
            long marketVal = snapshot.Listings.Sum(l => (long)l.UnitPrice * l.Quantity);
            int venturesReady = snapshot.Retainers.Count(r => r.VentureStatus == "Complete");

            totalGil += charGil;
            totalMarketValue += marketVal;
            totalRetainers += snapshot.Retainers.Count;
            totalListings += snapshot.Listings.Count;
            totalVenturesReady += venturesReady;

            var jobDict = new Dictionary<string, int>();
            foreach (var j in snapshot.Jobs)
            {
                if (!string.IsNullOrEmpty(j.Abbreviation))
                    jobDict[j.Abbreviation.ToUpperInvariant()] = j.Level;
            }

            rows.Add(new DashRow
            {
                Name = snapshot.Row.CharacterName, World = snapshot.Row.World, Server = snapshot.Row.Datacenter, Region = snapshot.Row.Region, Gil = charGil,
                MarketValue = marketVal, Retainers = snapshot.Retainers.Count,
                Listings = snapshot.Listings.Count, VenturesReady = venturesReady,
                FcName = snapshot.FreeCompany?.Name ?? snapshot.Row.FcName ?? "-", LastSeen = snapshot.Row.UpdatedUtc,
                ContentId = ch.ContentId, JobLevels = jobDict,
            });
        }

        var colCount = 11 + DashJobAbbrevs.Length; // 11 base + job columns

        using (var dashTable = ImRaii.Table("DashboardTable", colCount,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.ScrollX
            | ImGuiTableFlags.Sortable | ImGuiTableFlags.SortTristate,
            new Vector2(0, 300)))
        {
            if (dashTable.Success)
            {
                ImGui.TableSetupColumn("Character", ImGuiTableColumnFlags.DefaultSort | ImGuiTableColumnFlags.WidthFixed, 150);
                ImGui.TableSetupColumn("World", ImGuiTableColumnFlags.WidthFixed, 80);
                ImGui.TableSetupColumn("Server", ImGuiTableColumnFlags.WidthFixed, 90);
                ImGui.TableSetupColumn("Region", ImGuiTableColumnFlags.WidthFixed, 70);
                ImGui.TableSetupColumn("Gil", ImGuiTableColumnFlags.WidthFixed, 90);
                ImGui.TableSetupColumn("Market", ImGuiTableColumnFlags.WidthFixed, 90);
                ImGui.TableSetupColumn("Retainers", ImGuiTableColumnFlags.WidthFixed, 65);
                ImGui.TableSetupColumn("Listings", ImGuiTableColumnFlags.WidthFixed, 60);
                ImGui.TableSetupColumn("Ventures", ImGuiTableColumnFlags.WidthFixed, 60);
                ImGui.TableSetupColumn("FC", ImGuiTableColumnFlags.WidthFixed, 120);
                ImGui.TableSetupColumn("Last Seen", ImGuiTableColumnFlags.WidthFixed, 130);
                foreach (var job in DashJobAbbrevs)
                    ImGui.TableSetupColumn(job, ImGuiTableColumnFlags.WidthFixed, 28);
                ImGui.TableSetupScrollFreeze(1, 1);
                ImGui.TableHeadersRow();

                // Apply sorting
                var sortSpecs = ImGui.TableGetSortSpecs();
                if (sortSpecs.SpecsDirty)
                    sortSpecs.SpecsDirty = false;
                if (sortSpecs.SpecsCount > 0)
                {
                    unsafe
                    {
                        var spec = sortSpecs.Specs;
                        var col = spec.ColumnIndex;
                        var asc = spec.SortDirection == ImGuiSortDirection.Ascending;
                        rows.Sort((a, b) =>
                        {
                            int cmp;
                            if (col >= 11 && col < 11 + DashJobAbbrevs.Length)
                            {
                                // Sort by job level
                                var jobName = DashJobAbbrevs[col - 11];
                                var aLv = a.JobLevels != null && a.JobLevels.TryGetValue(jobName, out var av) ? av : 0;
                                var bLv = b.JobLevels != null && b.JobLevels.TryGetValue(jobName, out var bv) ? bv : 0;
                                cmp = aLv.CompareTo(bLv);
                            }
                            else
                            {
                                cmp = col switch
                                {
                                    0 => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
                                    1 => string.Compare(a.World, b.World, StringComparison.OrdinalIgnoreCase),
                                    2 => string.Compare(a.Server, b.Server, StringComparison.OrdinalIgnoreCase),
                                    3 => string.Compare(a.Region, b.Region, StringComparison.OrdinalIgnoreCase),
                                    4 => a.Gil.CompareTo(b.Gil),
                                    5 => a.MarketValue.CompareTo(b.MarketValue),
                                    6 => a.Retainers.CompareTo(b.Retainers),
                                    7 => a.Listings.CompareTo(b.Listings),
                                    8 => a.VenturesReady.CompareTo(b.VenturesReady),
                                    9 => string.Compare(a.FcName, b.FcName, StringComparison.OrdinalIgnoreCase),
                                    10 => string.Compare(a.LastSeen, b.LastSeen, StringComparison.Ordinal),
                                    _ => 0,
                                };
                            }
                            return asc ? cmp : -cmp;
                        });
                    }
                }

                foreach (var row in rows)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(row.Name);
                    ImGui.TableNextColumn(); ImGui.Text(row.World);
                    ImGui.TableNextColumn(); ImGui.Text(row.Server);
                    ImGui.TableNextColumn(); ImGui.Text(row.Region);
                    ImGui.TableNextColumn(); ImGui.Text($"{row.Gil:N0}");
                    ImGui.TableNextColumn();
                    if (row.MarketValue > 0)
                        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), $"{row.MarketValue:N0}");
                    else
                        ImGui.TextDisabled("-");
                    ImGui.TableNextColumn(); ImGui.Text($"{row.Retainers}");
                    ImGui.TableNextColumn(); ImGui.Text($"{row.Listings}");
                    ImGui.TableNextColumn();
                    if (row.VenturesReady > 0)
                        ImGui.TextColored(new Vector4(0.3f, 1.0f, 0.5f, 1.0f), $"{row.VenturesReady}");
                    else
                        ImGui.TextDisabled("0");
                    ImGui.TableNextColumn(); ImGui.Text(row.FcName);
                    ImGui.TableNextColumn(); ImGui.TextDisabled(row.LastSeen);

                    // Job level columns
                    foreach (var job in DashJobAbbrevs)
                    {
                        ImGui.TableNextColumn();
                        var lv = row.JobLevels != null && row.JobLevels.TryGetValue(job, out var v) ? v : 0;
                        if (lv >= 100)
                            ImGui.TextColored(new Vector4(1.0f, 0.85f, 0.0f, 1.0f), $"{lv}");
                        else if (lv > 0)
                            ImGui.Text($"{lv}");
                        else
                            ImGui.TextDisabled("-");
                    }
                }

                // Totals row
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextColored(new Vector4(1.0f, 0.9f, 0.4f, 1.0f), "TOTAL");
                ImGui.TableNextColumn(); ImGui.Text($"{knownCharacters.Count}");
                ImGui.TableNextColumn(); ImGui.Text("");
                ImGui.TableNextColumn(); ImGui.Text("");
                ImGui.TableNextColumn();
                ImGui.TextColored(new Vector4(1.0f, 0.9f, 0.4f, 1.0f), $"{totalGil:N0}");
                ImGui.TableNextColumn();
                ImGui.TextColored(new Vector4(1.0f, 0.9f, 0.4f, 1.0f), $"{totalMarketValue:N0}");
                ImGui.TableNextColumn();
                ImGui.TextColored(new Vector4(1.0f, 0.9f, 0.4f, 1.0f), $"{totalRetainers}");
                ImGui.TableNextColumn();
                ImGui.TextColored(new Vector4(1.0f, 0.9f, 0.4f, 1.0f), $"{totalListings}");
                ImGui.TableNextColumn();
                if (totalVenturesReady > 0)
                    ImGui.TextColored(new Vector4(0.3f, 1.0f, 0.5f, 1.0f), $"{totalVenturesReady}");
                else
                    ImGui.TextDisabled("0");
                ImGui.TableNextColumn(); ImGui.Text(""); // FC
                ImGui.TableNextColumn(); ImGui.Text(""); // Last Seen
                // Empty job cells in totals row
                for (int j = 0; j < DashJobAbbrevs.Length; j++)
                    ImGui.TableNextColumn();
            }
        }

        // ── MSQ Comparison ──
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "MSQ Progress Comparison");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        using (var msqCompTable = ImRaii.Table("MsqCompTable", 3,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            if (msqCompTable.Success)
            {
                ImGui.TableSetupColumn("Character", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Progress", ImGuiTableColumnFlags.WidthFixed, 100);
                ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 140);
                ImGui.TableHeadersRow();

                foreach (var ch in knownCharacters)
                {
                    if (!snapshotMap.TryGetValue(ch.ContentId, out var snapshot))
                        continue;

                    var milestones = snapshot.MsqMilestones;
                    if (milestones.Count == 0) continue;
                    var completed = milestones.Count(m => m.IsComplete);
                    var total = milestones.Count;
                    var pct = total > 0 ? (float)completed / total : 0f;

                    ImGui.TableNextRow();
                    ImGui.TableNextColumn(); ImGui.Text(ch.Name);
                    ImGui.TableNextColumn(); ImGui.Text($"{completed}/{total}");
                    ImGui.TableNextColumn(); ImGui.ProgressBar(pct, new Vector2(-1, 0), $"{pct:P0}");
                }
            }
        }

        // ── Collection Comparison ──
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "Collection Comparison");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Build sortable collection rows
        var collRows = new List<CollRow>(knownCharacters.Count);
        foreach (var ch in knownCharacters)
        {
            if (!snapshotMap.TryGetValue(ch.ContentId, out var snapshot))
                continue;

            var cols = snapshot.Collections;
            var mounts = cols.Find(c => c.Category == "Mounts");
            var minions = cols.Find(c => c.Category == "Minions");
            var orch = cols.Find(c => c.Category == "Orchestrion");
            var tt = cols.Find(c => c.Category == "TT Cards");

            collRows.Add(new CollRow
            {
                Name = ch.Name,
                MountsU = mounts?.Unlocked ?? 0, MountsT = mounts?.Total ?? 0,
                MinionsU = minions?.Unlocked ?? 0, MinionsT = minions?.Total ?? 0,
                OrchU = orch?.Unlocked ?? 0, OrchT = orch?.Total ?? 0,
                TtU = tt?.Unlocked ?? 0, TtT = tt?.Total ?? 0,
            });
        }

        using (var collCompTable = ImRaii.Table("CollCompTable", 5,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg
            | ImGuiTableFlags.Sortable | ImGuiTableFlags.SortTristate))
        {
            if (collCompTable.Success)
            {
                ImGui.TableSetupColumn("Character", ImGuiTableColumnFlags.DefaultSort | ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Mounts", ImGuiTableColumnFlags.WidthFixed, 70);
                ImGui.TableSetupColumn("Minions", ImGuiTableColumnFlags.WidthFixed, 70);
                ImGui.TableSetupColumn("Orchestrion", ImGuiTableColumnFlags.WidthFixed, 80);
                ImGui.TableSetupColumn("TT Cards", ImGuiTableColumnFlags.WidthFixed, 70);
                ImGui.TableHeadersRow();

                // Apply sorting — always sort since row data is rebuilt each frame
                var sortSpecs = ImGui.TableGetSortSpecs();
                if (sortSpecs.SpecsDirty)
                    sortSpecs.SpecsDirty = false;
                if (sortSpecs.SpecsCount > 0)
                {
                    unsafe
                    {
                        var spec = sortSpecs.Specs;
                        var col = spec.ColumnIndex;
                        var asc = spec.SortDirection == ImGuiSortDirection.Ascending;
                        collRows.Sort((a, b) =>
                        {
                            int cmp = col switch
                            {
                                0 => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
                                1 => a.MountsU.CompareTo(b.MountsU),
                                2 => a.MinionsU.CompareTo(b.MinionsU),
                                3 => a.OrchU.CompareTo(b.OrchU),
                                4 => a.TtU.CompareTo(b.TtU),
                                _ => 0,
                            };
                            return asc ? cmp : -cmp;
                        });
                    }
                }

                foreach (var cr in collRows)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn(); ImGui.Text(cr.Name);
                    ImGui.TableNextColumn(); ImGui.Text(cr.MountsT > 0 ? $"{cr.MountsU}/{cr.MountsT}" : "-");
                    ImGui.TableNextColumn(); ImGui.Text(cr.MinionsT > 0 ? $"{cr.MinionsU}/{cr.MinionsT}" : "-");
                    ImGui.TableNextColumn(); ImGui.Text(cr.OrchT > 0 ? $"{cr.OrchU}/{cr.OrchT}" : "-");
                    ImGui.TableNextColumn(); ImGui.Text(cr.TtT > 0 ? $"{cr.TtU}/{cr.TtT}" : "-");
                }
            }
        }
    }

}
