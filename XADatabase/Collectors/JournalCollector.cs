using System;
using System.Collections.Generic;
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
    private const int LeveAllowanceCap = JournalLeveObservation.AllowanceCap;
    private static JournalLeveObservation observation = JournalLeveObservation.Empty;

    public static int? LastLeveAllowances => observation.Allowances;

    public static JournalLeveObservation CaptureState() => observation;

    public static void RestoreState(JournalLeveObservation state)
    {
        observation = state;
    }

    public static void ClearPersistedValues()
    {
        observation = JournalLeveObservation.Empty;
    }

    public static void SeedPersistedValue(ulong ownerContentId, IEnumerable<CurrencyEntry>? currencies)
    {
        observation = observation.SeedPersistedFallback(ownerContentId, GetLeveAllowances(currencies));
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

    public static void ApplyToCurrencies(ulong ownerContentId, List<CurrencyEntry> currencies)
    {
        var allowances = observation.GetAllowances(ownerContentId);
        if (currencies == null || !allowances.HasValue)
            return;

        currencies.RemoveAll(IsLeveAllowanceEntry);

        var leveEntry = new CurrencyEntry
        {
            Key = CurrencyIdentity.LeveAllowances,
            Category = LeveAllowanceCategory,
            Name = LeveAllowanceName,
            Amount = allowances.Value,
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
            if (!TryGetLiveOwner(services, out var ownerContentId))
                return SectionResult<int?>.Unavailable(null, "journal owner is not loaded");

            observation = observation.BindOwner(ownerContentId);
            var stage = AtkStage.Instance();
            if (stage == null || stage->RaptureAtkUnitManager == null)
                return SectionResult<int?>.Unavailable(LastLeveAllowances, "journal addon manager is not ready");

            var addon = stage->RaptureAtkUnitManager->GetAddonByName(JournalAddonName);
            if (addon == null || !addon->IsVisible || !addon->IsReady)
                return SectionResult<int?>.Unavailable(LastLeveAllowances, "journal addon is not loaded");

            var allText = AddonTextReader.ReadAllText(addon);
            if (!JournalLeveParser.TryResolveLeveAllowances(allText, out var value))
                return SectionResult<int?>.Failed(LastLeveAllowances, "leve allowances could not be parsed from the loaded journal");

            if (!TryGetLiveOwner(services, out var currentOwnerContentId) || currentOwnerContentId != ownerContentId)
                return SectionResult<int?>.Unavailable(null, "journal owner changed during collection");

            observation = observation.Observe(ownerContentId, value, DateTimeOffset.UtcNow);
            return SectionResult<int?>.Available(value);
        }
        catch (Exception ex)
        {
            services.Log.Error(ex, "[XA] Journal collection failed.");
            return SectionResult<int?>.Failed(LastLeveAllowances, ex.Message);
        }
    }

    public static bool TryCollectFromOpenAddon(XaServices services)
    {
        return CollectSection(services).CanReplacePersisted;
    }

    public static unsafe bool CollectFromAddon(XaServices services, nint addonPtr)
    {
        if (addonPtr == nint.Zero || !TryGetLiveOwner(services, out var ownerContentId))
            return false;

        try
        {
            // A PreClose/PreFinalize pointer can remain readable after visibility clears.
            // Keep this separate from the visible, ready open-window path above.
            var allText = AddonTextReader.ReadAllText((AtkUnitBase*)addonPtr);
            if (!JournalLeveParser.TryResolveLeveAllowances(allText, out var leveAllowances)
                || !TryGetLiveOwner(services, out var currentOwnerContentId)
                || currentOwnerContentId != ownerContentId)
                return false;

            var previous = observation.GetAllowances(ownerContentId);
            observation = observation.Observe(ownerContentId, leveAllowances, DateTimeOffset.UtcNow);
            if (previous != leveAllowances)
                services.Log.Information($"[XA] Journal leve allowances from addon: {leveAllowances}");
            return true;
        }
        catch (Exception ex)
        {
            services.Log.Error(ex, "[XA] Journal close collection failed.");
            return false;
        }
    }

    private static bool TryGetLiveOwner(XaServices services, out ulong ownerContentId)
    {
        ownerContentId = services.PlayerState.ContentId;
        return services.ClientState.IsLoggedIn && services.PlayerState.IsLoaded && ownerContentId != 0;
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
}
