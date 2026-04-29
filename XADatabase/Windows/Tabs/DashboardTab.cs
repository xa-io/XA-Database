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
    private const float DashboardComparisonSectionHeight = 300f;

    private void DrawDashboardTab()
    {
        var comparisonSectionHeight = Scale(DashboardComparisonSectionHeight);

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

        static string BuildLocationLabel(XaCharacterSnapshotData snapshot)
        {
            var parts = new[] { snapshot.Row.Region, snapshot.Row.Datacenter, snapshot.Row.World }
                .Where(part => !string.IsNullOrWhiteSpace(part));
            var label = string.Join(" / ", parts);
            return string.IsNullOrWhiteSpace(label) ? "-" : label;
        }

        // Build sortable row data
        var snapshotLookup = GetDashboardSnapshotCache();
        var rows = new List<DashRow>(knownCharacters.Count);
        var snapshotMap = new Dictionary<ulong, XaCharacterSnapshotData>();
        long totalGil = 0, totalRetainerGil = 0, totalFcChestGil = 0, totalMarketValue = 0;
        int totalRetainers = 0, totalListings = 0, totalVenturesReady = 0, totalLeveAllowances = 0;

        foreach (var ch in knownCharacters)
        {
            if (!snapshotLookup.TryGetValue(ch.ContentId, out var snapshot))
                continue;

            snapshotMap[ch.ContentId] = snapshot;

            long charGil = snapshot.Row.Gil;
            long retainerGil = snapshot.Row.RetainerGil;
            long fcChestGil = snapshot.FreeCompany?.FcGil ?? 0;
            long marketVal = snapshot.Listings.Sum(l => (long)l.UnitPrice * l.Quantity);
            int venturesReady = snapshot.Retainers.Count(r => r.VentureStatus == "Complete");

            totalGil += charGil;
            totalRetainerGil += retainerGil;
            totalFcChestGil += fcChestGil;
            totalMarketValue += marketVal;
            totalRetainers += snapshot.Retainers.Count;
            totalListings += snapshot.Listings.Count;
            totalVenturesReady += venturesReady;
            totalLeveAllowances += JournalCollector.GetLeveAllowances(snapshot.Currencies) ?? 0;

            var jobDict = new Dictionary<string, int>();
            foreach (var j in snapshot.Jobs)
            {
                if (!string.IsNullOrEmpty(j.Abbreviation))
                    jobDict[j.Abbreviation.ToUpperInvariant()] = j.Level;
            }

            rows.Add(new DashRow
            {
                Name = snapshot.Row.CharacterName, World = snapshot.Row.World, Server = snapshot.Row.Datacenter, Region = snapshot.Row.Region, Gil = charGil, RetainerGil = retainerGil, FcChestGil = fcChestGil,
                MarketValue = marketVal, Retainers = snapshot.Retainers.Count,
                Listings = snapshot.Listings.Count, VenturesReady = venturesReady,
                LeveAllowances = JournalCollector.GetLeveAllowances(snapshot.Currencies) ?? 0,
                FcName = snapshot.FreeCompany?.Name ?? snapshot.Row.FcName ?? "-", LastSeen = snapshot.Row.UpdatedUtc,
                ContentId = ch.ContentId, JobLevels = jobDict,
            });
        }

        var colCount = 14 + DashJobAbbrevs.Length;
        var deleteModifierHeld = IsDeleteModifierHeld();
        ulong? pendingDeleteContentId = null;
        string pendingDeleteLabel = string.Empty;

        using (var dashTable = ImRaii.Table("DashboardTable", colCount,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.ScrollX
            | ImGuiTableFlags.Sortable | ImGuiTableFlags.SortTristate,
            new Vector2(0, comparisonSectionHeight)))
        {
            if (dashTable.Success)
            {
                ImGui.TableSetupColumn("Character", ImGuiTableColumnFlags.DefaultSort | ImGuiTableColumnFlags.WidthFixed, Scale(150f));
                ImGui.TableSetupColumn("World", ImGuiTableColumnFlags.WidthFixed, Scale(80f));
                ImGui.TableSetupColumn("Server", ImGuiTableColumnFlags.WidthFixed, Scale(90f));
                ImGui.TableSetupColumn("Region", ImGuiTableColumnFlags.WidthFixed, Scale(70f));
                ImGui.TableSetupColumn("Gil", ImGuiTableColumnFlags.WidthFixed, Scale(90f));
                ImGui.TableSetupColumn("Retainer Gil", ImGuiTableColumnFlags.WidthFixed, Scale(100f));
                ImGui.TableSetupColumn("FC Chest Gil", ImGuiTableColumnFlags.WidthFixed, Scale(110f));
                ImGui.TableSetupColumn("Market", ImGuiTableColumnFlags.WidthFixed, Scale(90f));
                ImGui.TableSetupColumn("Retainers", ImGuiTableColumnFlags.WidthFixed, Scale(65f));
                ImGui.TableSetupColumn("Listings", ImGuiTableColumnFlags.WidthFixed, Scale(60f));
                ImGui.TableSetupColumn("Ventures", ImGuiTableColumnFlags.WidthFixed, Scale(60f));
                ImGui.TableSetupColumn("Leve A", ImGuiTableColumnFlags.WidthFixed, Scale(60f));
                ImGui.TableSetupColumn("FC", ImGuiTableColumnFlags.WidthFixed, Scale(120f));
                ImGui.TableSetupColumn("Last Seen", ImGuiTableColumnFlags.WidthFixed, Scale(130f));
                foreach (var job in DashJobAbbrevs)
                    ImGui.TableSetupColumn(job, ImGuiTableColumnFlags.WidthFixed, Scale(28f));
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
                            if (col >= 14 && col < 14 + DashJobAbbrevs.Length)
                            {
                                var jobName = DashJobAbbrevs[col - 14];
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
                                    5 => a.RetainerGil.CompareTo(b.RetainerGil),
                                    6 => a.FcChestGil.CompareTo(b.FcChestGil),
                                    7 => a.MarketValue.CompareTo(b.MarketValue),
                                    8 => a.Retainers.CompareTo(b.Retainers),
                                    9 => a.Listings.CompareTo(b.Listings),
                                    10 => a.VenturesReady.CompareTo(b.VenturesReady),
                                    11 => a.LeveAllowances.CompareTo(b.LeveAllowances),
                                    12 => string.Compare(a.FcName, b.FcName, StringComparison.OrdinalIgnoreCase),
                                    13 => string.Compare(a.LastSeen, b.LastSeen, StringComparison.Ordinal),
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
                    var cellStartX = ImGui.GetCursorPosX();
                    var cellWidth = ImGui.GetContentRegionAvail().X;
                    var deleteButtonWidth = ImGui.CalcTextSize("X").X + (ImGui.GetStyle().FramePadding.X * 2f);
                    ImGui.Text(row.Name);
                    var deleteButtonX = cellStartX + MathF.Max(0f, cellWidth - deleteButtonWidth);
                    ImGui.SameLine(deleteButtonX);
                    PushDeleteButtonColors(deleteModifierHeld);
                    ImGui.BeginDisabled(!deleteModifierHeld);
                    if (ImGui.SmallButton($"X##DashboardDelete{row.ContentId}"))
                    {
                        pendingDeleteContentId = row.ContentId;
                        pendingDeleteLabel = $"{row.Name} @ {row.World}";
                    }
                    ImGui.EndDisabled();
                    PopDeleteButtonColors();
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    {
                        if (!deleteModifierHeld)
                            ImGui.SetTooltip("Hold Ctrl+Shift and click to select.");
                        else
                            ImGui.SetTooltip($"Delete {row.Name} @ {row.World} from the database.");
                    }
                    ImGui.TableNextColumn(); ImGui.Text(row.World);
                    ImGui.TableNextColumn(); ImGui.Text(row.Server);
                    ImGui.TableNextColumn(); ImGui.Text(row.Region);
                    ImGui.TableNextColumn(); ImGui.Text($"{row.Gil:N0}");
                    ImGui.TableNextColumn(); ImGui.Text($"{row.RetainerGil:N0}");
                    ImGui.TableNextColumn();
                    if (row.FcChestGil > 0)
                        ImGui.TextColored(new Vector4(1.0f, 0.9f, 0.3f, 1.0f), $"{row.FcChestGil:N0}");
                    else
                        ImGui.TextDisabled("0");
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
                    ImGui.TableNextColumn();
                    if (row.LeveAllowances > 0)
                        ImGui.Text($"{row.LeveAllowances}");
                    else
                        ImGui.TextDisabled("0");
                    ImGui.TableNextColumn(); ImGui.Text(row.FcName);
                    ImGui.TableNextColumn(); ImGui.TextDisabled(row.LastSeen);

                    // Job level columns
                    foreach (var job in DashJobAbbrevs)
                    {
                        ImGui.TableNextColumn();
                        var lv = row.JobLevels != null && row.JobLevels.TryGetValue(job, out var v) ? v : 0;
                        var levelCap = JobLevelCaps.ForAbbreviation(job);
                        if ((levelCap > 0 && lv >= levelCap) || lv >= 100)
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
                ImGui.TextColored(new Vector4(1.0f, 0.9f, 0.4f, 1.0f), $"{totalRetainerGil:N0}");
                ImGui.TableNextColumn();
                if (totalFcChestGil > 0)
                    ImGui.TextColored(new Vector4(1.0f, 0.9f, 0.4f, 1.0f), $"{totalFcChestGil:N0}");
                else
                    ImGui.TextDisabled("0");
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
                ImGui.TableNextColumn();
                if (totalLeveAllowances > 0)
                    ImGui.TextColored(new Vector4(1.0f, 0.9f, 0.4f, 1.0f), $"{totalLeveAllowances}");
                else
                    ImGui.TextDisabled("0");
                ImGui.TableNextColumn(); ImGui.Text(""); // FC
                ImGui.TableNextColumn(); ImGui.Text(""); // Last Seen
                for (int j = 0; j < DashJobAbbrevs.Length; j++)
                    ImGui.TableNextColumn();
            }
        }

        if (pendingDeleteContentId.HasValue)
        {
            if (DeleteCharacterSnapshot(pendingDeleteContentId.Value, pendingDeleteLabel, out var errorMessage))
                SetSettingsStatus($"Deleted {pendingDeleteLabel} from the database.");
            else
                SetSettingsStatus($"Delete failed: {errorMessage}");
        }

        // ── MSQ Comparison ──
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "MSQ Progress Comparison");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var msqRows = new List<MsqRow>(knownCharacters.Count);
        foreach (var ch in knownCharacters)
        {
            if (!snapshotMap.TryGetValue(ch.ContentId, out var snapshot))
                continue;

            var milestones = snapshot.MsqMilestones;
            if (milestones.Count == 0)
                continue;

            var completed = milestones.Count(m => m.IsComplete);
            var total = milestones.Count;
            var pct = total > 0 ? (float)completed / total : 0f;

            msqRows.Add(new MsqRow
            {
                Location = BuildLocationLabel(snapshot),
                Name = snapshot.Row.CharacterName,
                Completed = completed,
                Total = total,
                Percent = pct,
            });
        }

        using (var msqCompTable = ImRaii.Table("MsqCompTable", 4,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY
            | ImGuiTableFlags.Sortable | ImGuiTableFlags.SortTristate,
            new Vector2(0, comparisonSectionHeight)))
        {
            if (msqCompTable.Success)
            {
                ImGui.TableSetupColumn("Region / Server / World", ImGuiTableColumnFlags.WidthFixed, Scale(200f));
                ImGui.TableSetupColumn("Character", ImGuiTableColumnFlags.DefaultSort | ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Progress", ImGuiTableColumnFlags.WidthFixed, Scale(100f));
                ImGui.TableSetupColumn("Percent Complete", ImGuiTableColumnFlags.WidthFixed, Scale(140f));
                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableHeadersRow();

                var msqSortSpecs = ImGui.TableGetSortSpecs();
                if (msqSortSpecs.SpecsDirty)
                    msqSortSpecs.SpecsDirty = false;
                if (msqSortSpecs.SpecsCount > 0)
                {
                    unsafe
                    {
                        var spec = msqSortSpecs.Specs;
                        var asc = spec.SortDirection == ImGuiSortDirection.Ascending;
                        msqRows.Sort((a, b) =>
                        {
                            int cmp = spec.ColumnIndex switch
                            {
                                0 => string.Compare(a.Location, b.Location, StringComparison.OrdinalIgnoreCase),
                                1 => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
                                2 => a.Completed != b.Completed ? a.Completed.CompareTo(b.Completed) : a.Total.CompareTo(b.Total),
                                3 => a.Percent.CompareTo(b.Percent),
                                _ => 0,
                            };
                            return asc ? cmp : -cmp;
                        });
                    }
                }

                foreach (var row in msqRows)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn(); ImGui.Text(row.Location);
                    ImGui.TableNextColumn(); ImGui.Text(row.Name);
                    ImGui.TableNextColumn(); ImGui.Text($"{row.Completed}/{row.Total}");
                    ImGui.TableNextColumn(); ImGui.ProgressBar(row.Percent, new Vector2(-1, 0), $"{row.Percent:P0}");
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
                Location = BuildLocationLabel(snapshot),
                Name = ch.Name,
                MountsU = mounts?.Unlocked ?? 0, MountsT = mounts?.Total ?? 0,
                MinionsU = minions?.Unlocked ?? 0, MinionsT = minions?.Total ?? 0,
                OrchU = orch?.Unlocked ?? 0, OrchT = orch?.Total ?? 0,
                TtU = tt?.Unlocked ?? 0, TtT = tt?.Total ?? 0,
            });
        }

        using (var collCompTable = ImRaii.Table("CollCompTable", 6,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY
            | ImGuiTableFlags.Sortable | ImGuiTableFlags.SortTristate,
            new Vector2(0, comparisonSectionHeight)))
        {
            if (collCompTable.Success)
            {
                ImGui.TableSetupColumn("Region / Server / World", ImGuiTableColumnFlags.WidthFixed, Scale(200f));
                ImGui.TableSetupColumn("Character", ImGuiTableColumnFlags.DefaultSort | ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Mounts", ImGuiTableColumnFlags.WidthFixed, Scale(70f));
                ImGui.TableSetupColumn("Minions", ImGuiTableColumnFlags.WidthFixed, Scale(70f));
                ImGui.TableSetupColumn("Orchestrion", ImGuiTableColumnFlags.WidthFixed, Scale(80f));
                ImGui.TableSetupColumn("TT Cards", ImGuiTableColumnFlags.WidthFixed, Scale(70f));
                ImGui.TableSetupScrollFreeze(0, 1);
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
                                0 => string.Compare(a.Location, b.Location, StringComparison.OrdinalIgnoreCase),
                                1 => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
                                2 => a.MountsU.CompareTo(b.MountsU),
                                3 => a.MinionsU.CompareTo(b.MinionsU),
                                4 => a.OrchU.CompareTo(b.OrchU),
                                5 => a.TtU.CompareTo(b.TtU),
                                _ => 0,
                            };
                            return asc ? cmp : -cmp;
                        });
                    }
                }

                foreach (var cr in collRows)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn(); ImGui.Text(cr.Location);
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
