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
/// FcHousingTab — partial class split from MainWindow.
/// </summary>
public partial class MainWindow
{
    // ───────────────────────────────────────────────
    //  FC / Housing Tab
    // ───────────────────────────────────────────────
    private void DrawFcHousingTab()
    {
        using var tab = ImRaii.TabItem("FC / Housing");
        if (!tab.Success)
            return;

        ImGui.Spacing();

        // ── Free Company Info ──
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "Free Company");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Detect homeworld status for messaging
        bool isOnHomeworld = true;
        string currentWorldName = string.Empty;
        string homeWorldName = string.Empty;
        var localPlayer = Plugin.ObjectTable.LocalPlayer;
        if (localPlayer != null)
        {
            var currentWorldId = localPlayer.CurrentWorld.RowId;
            var homeWorldId = localPlayer.HomeWorld.RowId;
            isOnHomeworld = currentWorldId == homeWorldId;
            currentWorldName = localPlayer.CurrentWorld.Value.Name.ToString();
            homeWorldName = localPlayer.HomeWorld.Value.Name.ToString();
        }

        if (cachedFc != null)
        {
            // Show world-visit note if not on homeworld
            if (!isOnHomeworld && localPlayer != null)
            {
                ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.3f, 1.0f),
                    $"Currently visiting {currentWorldName} — showing saved FC data from {homeWorldName}.");
                ImGui.Spacing();
            }

            using (var fcTable = ImRaii.Table("FcInfoTable", 2, ImGuiTableFlags.None))
            {
                if (fcTable.Success)
                {
                    ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed, 140);
                    ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);

                    ImGui.TableNextRow();
                    ImGui.TableNextColumn(); ImGui.TextDisabled("FC Name");
                    ImGui.TableNextColumn(); ImGui.Text(cachedFc.Name);

                    if (cachedFc.Tag.Length > 0)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn(); ImGui.TextDisabled("Tag");
                        ImGui.TableNextColumn(); ImGui.Text($"«{cachedFc.Tag}»");
                    }

                    ImGui.TableNextRow();
                    ImGui.TableNextColumn(); ImGui.TextDisabled("Rank");
                    ImGui.TableNextColumn(); ImGui.Text($"{cachedFc.Rank}");

                    ImGui.TableNextRow();
                    ImGui.TableNextColumn(); ImGui.TextDisabled("Grand Company");
                    ImGui.TableNextColumn(); ImGui.Text(cachedFc.GrandCompanyName);

                    ImGui.TableNextRow();
                    ImGui.TableNextColumn(); ImGui.TextDisabled("Members");
                    ImGui.TableNextColumn();
                    ImGui.TextColored(new Vector4(0.3f, 1.0f, 0.5f, 1.0f), $"{cachedFc.OnlineMembers}");
                    ImGui.SameLine();
                    ImGui.TextDisabled($"/ {cachedFc.TotalMembers}");
                    ImGui.SameLine();
                    ImGui.TextDisabled("(online / total)");

                    ImGui.TableNextRow();
                    ImGui.TableNextColumn(); ImGui.TextDisabled("FC Master");
                    ImGui.TableNextColumn(); ImGui.Text(cachedFc.Master);

                    if (cachedFc.FcPoints > 0)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn(); ImGui.TextDisabled("FC Points");
                        ImGui.TableNextColumn(); ImGui.Text($"{cachedFc.FcPoints:N0}");
                    }

                    if (!string.IsNullOrEmpty(cachedFc.Estate))
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn(); ImGui.TextDisabled("Estate");
                        ImGui.TableNextColumn(); ImGui.Text(cachedFc.Estate);
                    }

                    ImGui.TableNextRow();
                    ImGui.TableNextColumn(); ImGui.TextDisabled("FC ID");
                    ImGui.TableNextColumn(); ImGui.TextDisabled($"{cachedFc.FcId}");
                }
            }
        }
        else
        {
            if (!isOnHomeworld && localPlayer != null)
            {
                ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.3f, 1.0f),
                    $"Currently visiting {currentWorldName} (homeworld: {homeWorldName}).");
                ImGui.TextDisabled("FC data is only available on your homeworld.");
                ImGui.TextDisabled("Return to your homeworld and click Refresh + Save to collect FC info.");
            }
            else
            {
                ImGui.TextDisabled("No Free Company data found for this character.");
                ImGui.TextDisabled("This character may not be in a Free Company.");
            }
        }

        // ── Personal Housing ──
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "Personal Housing");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var sharedEstateEntries = cachedSharedEstates
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(entry => entry.Trim())
            .Where(entry => entry.Length > 0)
            .GroupBy(entry => XaCharacterSnapshotRepository.StripHousingOwnerSuffix(entry), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        var personalEstateDisplay = XaCharacterSnapshotRepository.StripHousingOwnerSuffix(cachedPersonalEstate);
        var apartmentDisplay = XaCharacterSnapshotRepository.StripHousingOwnerSuffix(cachedApartment);

        if (!string.IsNullOrEmpty(personalEstateDisplay) || sharedEstateEntries.Count > 0 || !string.IsNullOrEmpty(apartmentDisplay))
        {
            using (var phTable = ImRaii.Table("PersonalHousingTable", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
            {
                if (phTable.Success)
                {
                    ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, 120);
                    ImGui.TableSetupColumn("Location", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableHeadersRow();

                    if (!string.IsNullOrEmpty(personalEstateDisplay))
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn(); ImGui.Text("Personal Estate");
                        ImGui.TableNextColumn(); ImGui.Text(personalEstateDisplay);
                    }

                    foreach (var sharedEstate in sharedEstateEntries)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn(); ImGui.Text("Shared Estate");
                        ImGui.TableNextColumn(); ImGui.Text(sharedEstate);
                    }

                    if (!string.IsNullOrEmpty(apartmentDisplay))
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn(); ImGui.Text("Apartment");
                        ImGui.TableNextColumn(); ImGui.Text(apartmentDisplay);
                    }
                }
            }
        }
        else
        {
            ImGui.TextDisabled("No personal housing data. Click Refresh + Save to scan.");
        }

        // ── FC Members ──
        ImGui.Spacing();
        ImGui.Spacing();
        // Detect current player's content ID for self-online override
        var selfContentId = (!viewingContentId.HasValue && Plugin.PlayerState.IsLoaded)
            ? Plugin.PlayerState.ContentId : 0UL;
        var onlineCount = cachedFcMembers.Count(m => m.IsOnline || m.ContentId == selfContentId);
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f),
            cachedFcMembers.Count > 0
                ? $"FC Members ({onlineCount} online / {cachedFcMembers.Count} total)"
                : "FC Members");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (cachedFcMembers.Count > 0)
        {
            using (var memberTable = ImRaii.Table("FcMembersTable", 5,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Sortable,
                new Vector2(0, 250)))
            {
                if (memberTable.Success)
                {
                    ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.DefaultSort | ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("Rank", ImGuiTableColumnFlags.WidthFixed, 90);
                    ImGui.TableSetupColumn("Job", ImGuiTableColumnFlags.WidthFixed, 40);
                    ImGui.TableSetupColumn("World", ImGuiTableColumnFlags.WidthFixed, 90);
                    ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 55);
                    ImGui.TableSetupScrollFreeze(0, 1);
                    ImGui.TableHeadersRow();

                    // Sort members
                    var sortedMembers = new List<FcMemberEntry>(cachedFcMembers);
                    var sortSpecs = ImGui.TableGetSortSpecs();
                    if (sortSpecs.SpecsDirty) sortSpecs.SpecsDirty = false;
                    if (sortSpecs.SpecsCount > 0)
                    {
                        unsafe
                        {
                            var spec = sortSpecs.Specs;
                            var col = spec.ColumnIndex;
                            var asc = spec.SortDirection == ImGuiSortDirection.Ascending;
                            sortedMembers.Sort((a, b) =>
                            {
                                int cmp = col switch
                                {
                                    0 => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
                                    1 => a.RankSort.CompareTo(b.RankSort),
                                    2 => string.Compare(a.JobName, b.JobName, StringComparison.OrdinalIgnoreCase),
                                    3 => string.Compare(a.HomeWorldName, b.HomeWorldName, StringComparison.OrdinalIgnoreCase),
                                    4 => a.OnlineStatus.CompareTo(b.OnlineStatus),
                                    _ => 0,
                                };
                                return asc ? cmp : -cmp;
                            });
                        }
                    }

                    foreach (var m in sortedMembers)
                    {
                        var isSelf = selfContentId != 0 && m.ContentId == selfContentId;
                        var isOnline = m.IsOnline || isSelf;

                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        if (isOnline)
                            ImGui.Text(m.Name);
                        else
                            ImGui.TextDisabled(m.Name);

                        ImGui.TableNextColumn();
                        ImGui.TextDisabled(m.RankName.Length > 0 ? m.RankName : $"Rank {m.RankSort + 1}");

                        ImGui.TableNextColumn();
                        if (isOnline && m.JobName.Length > 0)
                            ImGui.Text(m.JobName);
                        else
                            ImGui.TextDisabled(m.JobName.Length > 0 ? m.JobName : "-");

                        ImGui.TableNextColumn();
                        if (m.CurrentWorldName != m.HomeWorldName && m.CurrentWorldName.Length > 0)
                            ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.3f, 1.0f), m.CurrentWorldName);
                        else
                            ImGui.TextDisabled(m.HomeWorldName);

                        ImGui.TableNextColumn();
                        if (isOnline)
                            ImGui.TextColored(new Vector4(0.3f, 1.0f, 0.5f, 1.0f), "Online");
                        else
                            ImGui.TextDisabled("Offline");
                    }
                }
            }
        }
        else
        {
            ImGui.TextDisabled("No FC member data available.");
            ImGui.TextDisabled("Open the FC member list in-game, then click Refresh + Save.");
        }

        // ── Squadron ──
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "Squadron");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (cachedSquadron != null && cachedSquadron.Members.Count > 0)
        {
            // Squadron summary
            using (var sqInfoTable = ImRaii.Table("SquadronInfoTable", 2, ImGuiTableFlags.None))
            {
                if (sqInfoTable.Success)
                {
                    ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed, 140);
                    ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);

                    ImGui.TableNextRow();
                    ImGui.TableNextColumn(); ImGui.TextDisabled("Members");
                    ImGui.TableNextColumn(); ImGui.Text($"{cachedSquadron.MemberCount}");

                    if (cachedSquadron.CurrentExpedition > 0)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn(); ImGui.TextDisabled("Current Mission");
                        ImGui.TableNextColumn();
                        ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.3f, 1.0f),
                            cachedSquadron.ExpeditionName.Length > 0 ? cachedSquadron.ExpeditionName : $"#{cachedSquadron.CurrentExpedition}");
                    }

                    ImGui.TableNextRow();
                    ImGui.TableNextColumn(); ImGui.TextDisabled("Bonuses");
                    ImGui.TableNextColumn();
                    ImGui.Text($"Phys {cachedSquadron.BonusPhysical}  Mental {cachedSquadron.BonusMental}  Tact {cachedSquadron.BonusTactical}");
                }
            }

            ImGui.Spacing();

            // Squadron member table
            using (var sqTable = ImRaii.Table("SquadronMembersTable", 4,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg, new Vector2(0, 0)))
            {
                if (sqTable.Success)
                {
                    ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("Job", ImGuiTableColumnFlags.WidthFixed, 40);
                    ImGui.TableSetupColumn("Level", ImGuiTableColumnFlags.WidthFixed, 45);
                    ImGui.TableSetupColumn("Mastery", ImGuiTableColumnFlags.WidthFixed, 120);
                    ImGui.TableHeadersRow();

                    foreach (var m in cachedSquadron.Members)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn(); ImGui.Text(m.Name.Length > 0 ? m.Name : $"NPC#{m.ENpcResidentId}");
                        ImGui.TableNextColumn(); ImGui.Text(m.ClassJobName.Length > 0 ? m.ClassJobName : $"{m.ClassJob}");
                        ImGui.TableNextColumn();
                        if (m.Level >= 60)
                            ImGui.TextColored(new Vector4(0.3f, 1.0f, 0.5f, 1.0f), $"{m.Level}");
                        else
                            ImGui.Text($"{m.Level}");
                        ImGui.TableNextColumn();
                        ImGui.TextDisabled($"I:{m.MasteryIndependent} O:{m.MasteryOffensive} D:{m.MasteryDefensive} B:{m.MasteryBalanced}");
                    }
                }
            }
        }
        else
        {
            ImGui.TextDisabled("No squadron data available.");
            ImGui.TextDisabled("Visit the GC barracks in-game, then click Refresh + Save.");
        }

        // ── Airships / Submarines ──
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "Airships / Submarines");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (cachedVoyages != null && (cachedVoyages.Airships.Count > 0 || cachedVoyages.Submarines.Count > 0))
        {
            DrawVoyageTable("Airships", cachedVoyages.Airships);
            if (cachedVoyages.Airships.Count > 0 && cachedVoyages.Submarines.Count > 0)
                ImGui.Spacing();
            DrawVoyageTable("Submarines", cachedVoyages.Submarines);
        }
        else
        {
            ImGui.TextDisabled("No airship or submarine data available.");
            ImGui.TextDisabled("Enter the FC workshop in-game, then click Refresh + Save.");
        }
    }

    private void DrawVoyageTable(string label, List<VoyageEntry> entries)
    {
        if (entries.Count == 0) return;

        ImGui.Text($"{label} ({entries.Count})");
        ImGui.Spacing();

        using var table = ImRaii.Table($"{label}VoyageTable", 9,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit);
        if (!table.Success) return;

        ImGui.TableSetupColumn("Slot", ImGuiTableColumnFlags.WidthFixed, 40);
        ImGui.TableSetupColumn("Rank", ImGuiTableColumnFlags.WidthFixed, 40);
        ImGui.TableSetupColumn("Build", ImGuiTableColumnFlags.WidthFixed, 80);
        ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 100);
        ImGui.TableSetupColumn("Surv", ImGuiTableColumnFlags.WidthFixed, 45);
        ImGui.TableSetupColumn("Retr", ImGuiTableColumnFlags.WidthFixed, 45);
        ImGui.TableSetupColumn("Spd", ImGuiTableColumnFlags.WidthFixed, 45);
        ImGui.TableSetupColumn("Rng", ImGuiTableColumnFlags.WidthFixed, 45);
        ImGui.TableSetupColumn("Favor", ImGuiTableColumnFlags.WidthFixed, 45);
        ImGui.TableHeadersRow();

        foreach (var v in entries)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.Text($"#{v.Slot + 1}");
            ImGui.TableNextColumn(); ImGui.Text($"{v.RankId}");
            ImGui.TableNextColumn();
            if (!string.IsNullOrEmpty(v.BuildString) && !v.BuildString.Contains("?"))
                ImGui.Text(v.BuildString);
            else if (!string.IsNullOrEmpty(v.BuildString))
                ImGui.TextDisabled(v.BuildString);
            else
                ImGui.TextDisabled("—");

            ImGui.TableNextColumn();
            if (v.ReturnTime > 0)
            {
                var returnDt = DateTimeOffset.FromUnixTimeSeconds(v.ReturnTime);
                var remaining = returnDt - DateTimeOffset.UtcNow;
                if (remaining.TotalSeconds > 0)
                {
                    ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.2f, 1.0f),
                        $"{remaining.Hours}h {remaining.Minutes}m");
                }
                else
                {
                    ImGui.TextColored(new Vector4(0.2f, 1.0f, 0.2f, 1.0f), "Returned");
                }
            }
            else
            {
                ImGui.TextDisabled("Idle");
            }

            ImGui.TableNextColumn(); ImGui.Text($"{v.Surveillance}");
            ImGui.TableNextColumn(); ImGui.Text($"{v.Retrieval}");
            ImGui.TableNextColumn(); ImGui.Text($"{v.Speed}");
            ImGui.TableNextColumn(); ImGui.Text($"{v.Range}");
            ImGui.TableNextColumn(); ImGui.Text($"{v.Favor}");
        }
    }

}
