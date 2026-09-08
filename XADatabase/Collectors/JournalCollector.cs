using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using FFXIVClientStructs.FFXIV.Component.GUI;
using XADatabase.Core.Collection;
using XADatabase.Core.Localization;
using XADatabase.Models;
using XADatabase.Services;

namespace XADatabase.Collectors;

public static class JournalCollector
{
    private const string JournalAddonName = "Journal";
    private const string LeveAllowanceCategory = "Common";
    private const string LeveAllowanceName = "Leve Allowances";
    private const int LeveAllowanceCap = 100;
    private static readonly Regex FirstNumberRegex = new(@"\d+", RegexOptions.Compiled);

    public static int? LastLeveAllowances { get; private set; }

    public static void ClearPersistedValues()
    {
        LastLeveAllowances = null;
    }

    public static void SeedPersistedValue(IEnumerable<CurrencyEntry>? currencies)
    {
        var persistedValue = GetLeveAllowances(currencies);
        if (persistedValue.HasValue)
            LastLeveAllowances = persistedValue.Value;
    }

    public static int? GetLeveAllowances(IEnumerable<CurrencyEntry>? currencies)
    {
        if (currencies == null)
            return null;

        foreach (var entry in currencies)
        {
            if (entry == null || !IsLeveAllowanceEntry(entry))
                continue;

            return Math.Clamp(entry.Amount, 0, LeveAllowanceCap);
        }

        return null;
    }

    public static void ApplyToCurrencies(List<CurrencyEntry> currencies)
    {
        if (currencies == null || !LastLeveAllowances.HasValue)
            return;

        currencies.RemoveAll(IsLeveAllowanceEntry);

        var leveEntry = new CurrencyEntry
        {
            Key = CurrencyIdentity.LeveAllowances,
            Category = LeveAllowanceCategory,
            Name = LeveAllowanceName,
            Amount = Math.Clamp(LastLeveAllowances.Value, 0, LeveAllowanceCap),
            Cap = LeveAllowanceCap,
        };

        var firstNonCommonIndex = currencies.FindIndex(entry => !string.Equals(entry.Category, LeveAllowanceCategory, StringComparison.OrdinalIgnoreCase));
        if (firstNonCommonIndex < 0)
            firstNonCommonIndex = currencies.Count;

        var insertIndex = 0;
        while (insertIndex < firstNonCommonIndex
               && string.Compare(currencies[insertIndex].Name, LeveAllowanceName, StringComparison.OrdinalIgnoreCase) < 0)
        {
            insertIndex++;
        }

        currencies.Insert(insertIndex, leveEntry);
    }

    public static unsafe SectionResult<int?> CollectSection(XaServices services)
    {
        try
        {
            var stage = AtkStage.Instance();
            if (stage == null || stage->RaptureAtkUnitManager == null)
                return SectionResult<int?>.Unavailable(LastLeveAllowances, "journal addon manager is not ready");

            var addon = stage->RaptureAtkUnitManager->GetAddonByName(JournalAddonName);
            if (addon == null || !addon->IsVisible || !addon->IsReady)
                return SectionResult<int?>.Unavailable(LastLeveAllowances, "journal addon is not loaded");

            var allText = AddonTextReader.ReadAllText(addon);
            if (!TryResolveLeveAllowances(allText, out var value))
                return SectionResult<int?>.Failed(LastLeveAllowances, "leve allowances could not be parsed from the loaded journal");

            LastLeveAllowances = value;
            return SectionResult<int?>.Available(value);
        }
        catch (Exception ex)
        {
            services.Log.Error(ex, "[XA] Journal collection failed.");
            return SectionResult<int?>.Failed(LastLeveAllowances, ex.Message);
        }
    }

    public static unsafe void TryCollectFromOpenAddon(XaServices services)
    {
        var stage = AtkStage.Instance();
        if (stage == null)
            return;

        var unitManager = stage->RaptureAtkUnitManager;
        if (unitManager == null)
            return;

        var addon = unitManager->GetAddonByName(JournalAddonName);
        if (addon == null || !addon->IsVisible || !addon->IsReady)
            return;

        CollectFromAddon(addon, services);
    }

    public static unsafe void CollectFromAddon(XaServices services, nint addonPtr)
    {
        if (addonPtr == nint.Zero)
            return;

        CollectFromAddon((AtkUnitBase*)addonPtr, services);
    }

    private static unsafe void CollectFromAddon(AtkUnitBase* addon, XaServices services)
    {
        if (addon == null)
            return;

        var allText = AddonTextReader.ReadAllText(addon);
        if (!TryResolveLeveAllowances(allText, out var leveAllowances))
            return;

        if (LastLeveAllowances != leveAllowances)
        {
            LastLeveAllowances = leveAllowances;
            services.Log.Information($"[XA] Journal leve allowances from addon: {leveAllowances}");
        }
    }

    private static bool IsLeveAllowanceEntry(CurrencyEntry? entry)
    {
        if (entry == null)
            return false;
        if (entry.Key.Equals(CurrencyIdentity.LeveAllowances, StringComparison.Ordinal))
            return true;

        // Read compatibility for snapshots written before synthetic currencies had a key.
        return string.IsNullOrWhiteSpace(entry.Key)
            && (entry.Name.Equals(LeveAllowanceName, StringComparison.OrdinalIgnoreCase)
                || entry.Name.Equals("Leve Allowance", StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryResolveLeveAllowances(List<(string Path, uint NodeId, string Text)> allText, out int leveAllowances)
    {
        foreach (var entry in allText)
        {
            if (!entry.Path.Equals("[6]→[2]", StringComparison.Ordinal))
                continue;

            if (entry.NodeId != 2)
                continue;

            if (TryParseAllowance(entry.Text, out leveAllowances))
                return true;
        }

        foreach (var entry in allText)
        {
            if (!entry.Path.StartsWith("[6]", StringComparison.Ordinal))
                continue;

            if (entry.NodeId != 2)
                continue;

            if (TryParseAllowance(entry.Text, out leveAllowances))
                return true;
        }

        leveAllowances = 0;
        return false;
    }

    private static bool TryParseAllowance(string text, out int leveAllowances)
    {
        leveAllowances = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var match = FirstNumberRegex.Match(text);
        if (!match.Success || !int.TryParse(match.Value, out var parsedValue))
            return false;

        leveAllowances = Math.Clamp(parsedValue, 0, LeveAllowanceCap);
        return true;
    }
}
