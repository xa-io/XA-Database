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
/// SettingsTab — partial class split from MainWindow.
/// </summary>
public partial class MainWindow
{
    // ───────────────────────────────────────────────
    //  Settings Tab
    // ───────────────────────────────────────────────
    private void DrawSettingsTab()
    {
        using var tab = ImRaii.TabItem("Settings");
        if (!tab.Success)
            return;

        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "Settings");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("Database");
        var dbPath = plugin.DatabaseService.GetDbPath();
        ImGui.TextDisabled(dbPath);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "Utility");
        ImGui.Spacing();

        var openPluginOnLoad = plugin.Configuration.OpenPluginOnLoad;
        if (ImGui.Checkbox("Open Plugin on Load", ref openPluginOnLoad))
        {
            plugin.Configuration.OpenPluginOnLoad = openPluginOnLoad;
            plugin.Configuration.Save();
        }
        ImGui.TextDisabled("Opens XA Database when the plugin loads and when the character logs in.");

        // ── Auto-Save ──
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "Auto-Save");
        ImGui.Spacing();

        var autoSaveMin = plugin.Configuration.AutoSaveIntervalMinutes;
        ImGui.SetNextItemWidth(120);
        if (ImGui.InputInt("Interval (minutes)", ref autoSaveMin))
        {
            if (autoSaveMin < 0) autoSaveMin = 0;
            if (autoSaveMin > 120) autoSaveMin = 120;
            plugin.Configuration.AutoSaveIntervalMinutes = autoSaveMin;
            plugin.Configuration.Save();
        }
        ImGui.TextDisabled("Set to 0 to disable. Data always saves on login, logout, and manual Refresh+Save.");
        if (autoSaveMin > 0)
        {
            ImGui.TextDisabled("Auto-save is active.");
        }

        // ── Echo Notification ──
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "Echo Notification");
        ImGui.Spacing();

        var echoOnSave = plugin.Configuration.EchoOnSave;
        if (ImGui.Checkbox("Show echo message on save", ref echoOnSave))
        {
            plugin.Configuration.EchoOnSave = echoOnSave;
            plugin.Configuration.Save();
        }
        ImGui.TextDisabled("Prints \"[XA] Database has been saved.\" in chat each time data is saved.");

        // ── Session Task Log ──
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "XA DB Task Log");
        ImGui.Spacing();

        if (ImGui.Checkbox("Enable task log for this session", ref taskLogEnabled))
        {
            if (!taskLogEnabled)
                taskLogEntries.Clear();
        }
        ImGui.TextDisabled("Disabled by default. XA Database keeps the last 50 task lines in memory for this plugin session only.");

        if (taskLogEnabled)
        {
            if (ImGui.Button("Copy Task Log") && taskLogEntries.Count > 0)
                ImGui.SetClipboardText(string.Join(Environment.NewLine, taskLogEntries));
            ImGui.SameLine();
            if (ImGui.Button("Clear Task Log"))
                taskLogEntries.Clear();

            using var taskLogChild = ImRaii.Child("XaDbTaskLog", new Vector2(0, 160), true);
            if (taskLogChild.Success)
            {
                if (taskLogEntries.Count == 0)
                {
                    ImGui.TextDisabled("No task log entries captured yet.");
                }
                else
                {
                    foreach (var entry in taskLogEntries)
                        ImGui.TextWrapped(entry);
                }
            }
        }

        // ── Addon Watcher ──
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "Addon Watcher (Auto-Save on Window Close)");
        ImGui.Spacing();

        var addonEnabled = plugin.Configuration.AddonWatcherEnabled;
        if (ImGui.Checkbox("Enable Addon Watcher", ref addonEnabled))
        {
            plugin.Configuration.AddonWatcherEnabled = addonEnabled;
            plugin.Configuration.Save();
        }
        ImGui.TextDisabled("Passive save triggers only. XA Database tracks addon closes and saves current data when supported windows close.");

        // Debug: show open addons
        ImGui.Spacing();
        var openAddons = plugin.AddonWatcher.GetOpenAddons();
        var transientOpen = openAddons.Where(a => !AddonWatcher.IsPersistent(a)).ToList();
        var persistentOpen = openAddons.Where(a => AddonWatcher.IsPersistent(a)).ToList();

        if (transientOpen.Count > 0)
        {
            ImGui.TextColored(new Vector4(0.4f, 1.0f, 0.4f, 1.0f), $"Open addons ({transientOpen.Count}):");
            foreach (var a in transientOpen)
                ImGui.BulletText(a);
        }
        else
        {
            ImGui.TextDisabled("No tracked addons currently open.");
        }

        if (persistentOpen.Count > 0)
        {
            ImGui.TextDisabled($"Always loaded ({persistentOpen.Count}): {string.Join(", ", persistentOpen)}");
            ImGui.TextDisabled("(Inventory data is captured on every save — no close trigger needed.)");
        }

        ImGui.Spacing();
        if (ImGui.TreeNode("Tracked Addons"))
        {
            // Persistent groups
            foreach (var (category, addons) in AddonWatcher.GetPersistentGroups())
            {
                ImGui.TextDisabled($"{category}:");
                foreach (var a in addons)
                {
                    var isOpen = openAddons.Contains(a);
                    if (isOpen)
                        ImGui.TextDisabled($"    {a} (loaded)");
                    else
                        ImGui.TextDisabled($"    {a}");
                }
            }

            // Transient groups
            foreach (var (category, addons) in AddonWatcher.GetTransientGroups())
            {
                ImGui.Text($"{category}:");
                foreach (var a in addons)
                {
                    var isOpen = openAddons.Contains(a);
                    if (isOpen)
                        ImGui.TextColored(new Vector4(0.4f, 1.0f, 0.4f, 1.0f), $"    {a} (open)");
                    else
                        ImGui.TextDisabled($"    {a}");
                }
            }
            ImGui.TreePop();
        }

        // ── Export ──
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "Export");
        ImGui.Spacing();

        var charLabel = viewingContentId.HasValue ? viewingCharName : "Current Character";

        if (ImGui.Button("Export Current CSV"))
        {
            try
            {
                var basePath = plugin.DatabaseService.GetDbDirectory();
                ExportService.WriteExport(basePath, charLabel, "currencies.csv", ExportService.ExportCurrenciesCsv(cachedCurrencies));
                ExportService.WriteExport(basePath, charLabel, "jobs.csv", ExportService.ExportJobsCsv(cachedJobs));
                ExportService.WriteExport(basePath, charLabel, "inventory.csv", ExportService.ExportInventoryCsv(cachedInventory));
                ExportService.WriteExport(basePath, charLabel, "items.csv", ExportService.ExportItemsCsv(cachedItems));
                ExportService.WriteExport(basePath, charLabel, "retainers.csv", ExportService.ExportRetainersCsv(cachedRetainers));
                ExportService.WriteExport(basePath, charLabel, "listings.csv", ExportService.ExportListingsCsv(cachedListings));
                ExportService.WriteExport(basePath, charLabel, "collections.csv", ExportService.ExportCollectionsCsv(cachedCollections));
                ExportService.WriteExport(basePath, charLabel, "quests.csv", ExportService.ExportQuestsCsv(cachedQuests));
                SetExportStatus($"CSV exported for {charLabel}");
            }
            catch (Exception ex)
            {
                SetExportStatus($"Export error: {ex.Message}");
                Plugin.Log.Error($"[XA] CSV export error: {ex}");
            }
        }

        ImGui.SameLine();

        if (ImGui.Button("Export JSON Snapshot"))
        {
            try
            {
                var basePath = plugin.DatabaseService.GetDbDirectory();
                var worldName = "Unknown";
                try { worldName = Plugin.PlayerState.HomeWorld.Value.Name.ToString(); } catch { }
                var json = ExportService.ExportFullJson(
                    charLabel, worldName,
                    cachedCurrencies, cachedJobs, cachedInventory, cachedItems,
                    cachedRetainers, cachedListings, cachedRetainerItems,
                    cachedFc, cachedCollections, cachedQuests);
                ExportService.WriteExport(basePath, charLabel, "snapshot.json", json);
                SetExportStatus($"JSON exported for {charLabel}");
            }
            catch (Exception ex)
            {
                SetExportStatus($"Export error: {ex.Message}");
                Plugin.Log.Error($"[XA] JSON export error: {ex}");
            }
        }

        ImGui.Spacing();

        if (ImGui.Button("Export All Characters CSV"))
        {
            try
            {
                var basePath = plugin.DatabaseService.GetDbDirectory();
                var chars = plugin.CharacterRepo.GetAll();

                // Collect all character data for master CSVs
                var allCurr = new List<(string Name, string World, List<CurrencyEntry> Data)>();
                var allJobs = new List<(string Name, string World, List<JobEntry> Data)>();
                var allInv = new List<(string Name, string World, List<InventorySummary> Data)>();
                var allItems = new List<(string Name, string World, List<ContainerItemEntry> Data)>();
                var allRet = new List<(string Name, string World, List<RetainerEntry> Data)>();
                var allList = new List<(string Name, string World, List<RetainerListingEntry> Data)>();
                var allColl = new List<(string Name, string World, List<CollectionSummary> Data)>();
                var allQuest = new List<(string Name, string World, List<ActiveQuestEntry> Data)>();

                foreach (var ch in chars)
                {
                    var snapshot = plugin.SnapshotRepo.GetSnapshot(ch.ContentId);
                    if (snapshot == null)
                        continue;

                    allCurr.Add((snapshot.Row.CharacterName, snapshot.Row.World, snapshot.Currencies));
                    allJobs.Add((snapshot.Row.CharacterName, snapshot.Row.World, snapshot.Jobs));
                    allInv.Add((snapshot.Row.CharacterName, snapshot.Row.World, snapshot.InventorySummaries));
                    allItems.Add((snapshot.Row.CharacterName, snapshot.Row.World, snapshot.AllItems));
                    allRet.Add((snapshot.Row.CharacterName, snapshot.Row.World, snapshot.Retainers));
                    allList.Add((snapshot.Row.CharacterName, snapshot.Row.World, snapshot.Listings));
                    allColl.Add((snapshot.Row.CharacterName, snapshot.Row.World, snapshot.Collections));
                    allQuest.Add((snapshot.Row.CharacterName, snapshot.Row.World, snapshot.ActiveQuests));
                }

                ExportService.WriteExport(basePath, "all_characters", "currencies.csv",
                    ExportService.BuildMasterCsv("Category,Name,Amount,Cap", allCurr, ExportService.FmtCurrency));
                ExportService.WriteExport(basePath, "all_characters", "jobs.csv",
                    ExportService.BuildMasterCsv("Abbreviation,Name,Category,Level,IsUnlocked", allJobs, ExportService.FmtJob));
                ExportService.WriteExport(basePath, "all_characters", "inventory.csv",
                    ExportService.BuildMasterCsv("Container,UsedSlots,TotalSlots", allInv, ExportService.FmtInventory));
                ExportService.WriteExport(basePath, "all_characters", "items.csv",
                    ExportService.BuildMasterCsv("Container,ItemName,ItemId,Quantity,IsHq,Slot", allItems, ExportService.FmtItem));
                ExportService.WriteExport(basePath, "all_characters", "retainers.csv",
                    ExportService.BuildMasterCsv("Name,Level,Gil,Items,MarketItems,Town,VentureStatus,VentureEta", allRet, ExportService.FmtRetainer));
                ExportService.WriteExport(basePath, "all_characters", "listings.csv",
                    ExportService.BuildMasterCsv("Retainer,ItemName,ItemId,Quantity,IsHq,UnitPrice,Total", allList, ExportService.FmtListing));
                ExportService.WriteExport(basePath, "all_characters", "collections.csv",
                    ExportService.BuildMasterCsv("Category,Unlocked,Total,Percent", allColl, ExportService.FmtCollection));
                ExportService.WriteExport(basePath, "all_characters", "quests.csv",
                    ExportService.BuildMasterCsv("QuestId,Name,Sequence", allQuest, ExportService.FmtQuest));

                SetExportStatus($"Master CSV exported for {chars.Count} characters");
            }
            catch (Exception ex)
            {
                SetExportStatus($"Export error: {ex.Message}");
                Plugin.Log.Error($"[XA] Export all CSV error: {ex}");
            }
        }

        ImGui.SameLine();

        if (ImGui.Button("Open Folder"))
        {
            try
            {
                var exportDir = ExportService.GetExportDir(plugin.DatabaseService.GetDbDirectory());
                Process.Start(new ProcessStartInfo
                {
                    FileName = exportDir,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                SetExportStatus($"Could not open folder: {ex.Message}");
            }
        }

        // Show export status message
        if (!string.IsNullOrEmpty(exportStatusMessage) && DateTime.UtcNow < exportStatusExpiry)
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.4f, 1.0f, 0.4f, 1.0f), exportStatusMessage);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextDisabled($"XA Database v{PluginVersion}");
        ImGui.TextDisabled("https://github.com/xa-io");
    }

    private void SetExportStatus(string message)
    {
        exportStatusMessage = message;
        exportStatusExpiry = DateTime.UtcNow.AddSeconds(8);
    }

}
