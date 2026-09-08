using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using XADatabase.Core.Quality;
using XADatabase.Database;
using XADatabase.Services;

namespace XADatabase.Windows;

public partial class MainWindow
{
    private const int MaxSaveHistoryEntries = 50;
    private const double SnapshotStaleThresholdMinutes = 60.0;
    private bool saveHistoryEnabled;
    private readonly List<string> saveHistoryEntries = new();
    private AddonTriggerEvent? lastAddonTrigger;
    private readonly HashSet<string> pendingCollectorWarnings = new();

    private void DrawDatabaseHealthSection()
    {
        if (ImGui.Button("Run Database Health Check"))
            plugin.DatabaseService.RunHealthCheck();

        var health = plugin.DatabaseService.LastHealthCheck;
        if (string.IsNullOrWhiteSpace(health.CheckedAtUtc))
        {
            ImGui.TextDisabled("No database health check has been recorded yet.");
            return;
        }

        ImGui.SameLine();
        ImGui.TextColored(health.Success ? new Vector4(0.4f, 1.0f, 0.4f, 1.0f) : new Vector4(1.0f, 0.75f, 0.3f, 1.0f),
            health.Success ? "Healthy" : "Check Failed");
        ImGui.TextDisabled($"Last checked: {health.CheckedAtUtc} UTC");
        ImGui.TextWrapped(health.Summary);

        if (!string.IsNullOrWhiteSpace(health.Error))
            ImGui.TextWrapped($"Details: {health.Error}");
    }

    private void DrawLatestSnapshotStatusPanel()
    {
        if (lastSnapshotResult == null)
            return;

        var quality = GetSnapshotQualityLabel(lastSnapshotResult);
        var qualityColor = GetSnapshotQualityColor(lastSnapshotResult);

        ImGui.Spacing();
        ImGui.TextColored(qualityColor, $"Snapshot Status: {quality}");
        ImGui.SameLine();
        ImGui.TextDisabled($"{lastSnapshotResult.Trigger} • {lastSnapshotResult.SavedAtUtc}");
        ImGui.TextWrapped(lastSnapshotResult.Summary);

        if (!string.IsNullOrWhiteSpace(lastSnapshotResult.TriggerDetail))
            ImGui.TextDisabled($"Detail: {lastSnapshotResult.TriggerDetail}");

        if (lastAddonTrigger != null && lastSnapshotResult.Trigger == SnapshotTrigger.AddonWatcher)
            ImGui.TextDisabled($"Addon trigger: {lastAddonTrigger.Category} / {lastAddonTrigger.AddonName}");

        if (lastSnapshotResult.Warnings.Count > 0)
        {
            ImGui.Spacing();
            foreach (var warning in lastSnapshotResult.Warnings)
                ImGui.TextWrapped($"- {warning}");
        }

        ImGui.Spacing();
        ImGui.Separator();
    }

    private void DrawSaveHistorySection()
    {
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "Save History");
        ImGui.Spacing();

        if (ImGui.Checkbox("Enable save history for this session", ref saveHistoryEnabled))
        {
            if (!saveHistoryEnabled)
                saveHistoryEntries.Clear();
        }

        ImGui.TextDisabled("Disabled by default. XA Database keeps the last 50 save results in memory for this plugin session only.");

        if (lastSnapshotResult != null)
        {
            var quality = GetSnapshotQualityLabel(lastSnapshotResult);
            ImGui.TextDisabled($"Latest result: {quality} • {lastSnapshotResult.Trigger} • {lastSnapshotResult.SavedAtUtc}");
        }

        if (!saveHistoryEnabled)
            return;

        if (ImGui.Button("Copy Save History") && saveHistoryEntries.Count > 0)
            ImGui.SetClipboardText(string.Join(Environment.NewLine + Environment.NewLine, saveHistoryEntries));
        ImGui.SameLine();
        if (ImGui.Button("Clear Save History"))
            saveHistoryEntries.Clear();

        using var saveHistoryChild = ImRaii.Child("XaDbSaveHistory", ScaledVector(0f, 170f), true);
        if (!saveHistoryChild.Success)
            return;

        if (saveHistoryEntries.Count == 0)
        {
            ImGui.TextDisabled("No save history entries captured yet.");
            return;
        }

        foreach (var entry in saveHistoryEntries)
        {
            ImGui.TextWrapped(entry);
            ImGui.Spacing();
        }
    }

    private void AddSaveHistoryEntry(SaveSnapshotResult result)
    {
        if (!saveHistoryEnabled)
            return;

        var lines = new List<string>
        {
            $"[{GetSnapshotQualityLabel(result)}] {result.SavedAtUtc} • {result.Trigger} • {result.Summary}"
        };

        if (!string.IsNullOrWhiteSpace(result.TriggerDetail))
            lines.Add($"Detail: {result.TriggerDetail}");

        if (result.Warnings.Count > 0)
        {
            lines.Add($"Warnings ({result.Warnings.Count}):");
            lines.AddRange(result.Warnings.Select(warning => $"- {warning}"));
        }

        saveHistoryEntries.Insert(0, string.Join(Environment.NewLine, lines));
        if (saveHistoryEntries.Count > MaxSaveHistoryEntries)
            saveHistoryEntries.RemoveRange(MaxSaveHistoryEntries, saveHistoryEntries.Count - MaxSaveHistoryEntries);
    }

    private void QueueCollectorWarning(string warning)
    {
        if (!string.IsNullOrWhiteSpace(warning))
            pendingCollectorWarnings.Add(warning);
    }

    private List<string> ConsumePendingCollectorWarnings()
    {
        var warnings = pendingCollectorWarnings.ToList();
        pendingCollectorWarnings.Clear();
        return warnings;
    }

    private static void AppendWarnings(List<string> target, IEnumerable<string> additional)
    {
        foreach (var warning in additional)
        {
            if (!target.Contains(warning))
                target.Add(warning);
        }
    }

    private string GetSnapshotQualityLabel() => GetSnapshotQualityLabel(lastSnapshotResult);

    private string GetSnapshotQualityLabel(SaveSnapshotResult? result)
        => SnapshotQualityResolver.ToLabel(ResolveSnapshotQuality(result));

    private static Vector4 GetSnapshotQualityColor(SaveSnapshotResult? result)
    {
        return ResolveSnapshotQuality(result) switch
        {
            SnapshotQuality.None => new Vector4(0.7f, 0.7f, 0.7f, 1.0f),
            SnapshotQuality.Saving => new Vector4(0.4f, 0.8f, 1.0f, 1.0f),
            SnapshotQuality.Degraded => new Vector4(1.0f, 0.45f, 0.45f, 1.0f),
            SnapshotQuality.Stale => new Vector4(1.0f, 0.75f, 0.3f, 1.0f),
            SnapshotQuality.Partial => new Vector4(1.0f, 0.9f, 0.35f, 1.0f),
            _ => new Vector4(0.4f, 1.0f, 0.4f, 1.0f),
        };
    }

    private static SnapshotQuality ResolveSnapshotQuality(SaveSnapshotResult? result)
        => SnapshotQualityResolver.ClassifySave(
            result != null,
            result?.Pending ?? false,
            result?.Success ?? false,
            result?.Warnings.Count > 0,
            result?.SavedAtUtc ?? string.Empty,
            DateTime.UtcNow,
            SnapshotStaleThresholdMinutes);
}
