using System;
using System.Collections.Generic;
using XADatabase.Collectors;
using XADatabase.Core.Collection;
using XADatabase.Core.Policies;
using XADatabase.Models;

namespace XADatabase.Windows;

public partial class MainWindow
{
    // Independent of the selected stored view, including before the first full live snapshot.
    private JournalLeveObservation liveJournalObservation = JournalLeveObservation.Empty;

    public void BeginCharacterSession()
    {
        liveJournalObservation = JournalLeveObservation.Empty;
    }

    private void SeedLiveJournal(ulong owner, IEnumerable<CurrencyEntry>? persistedCurrencies)
    {
        JournalCollector.RestoreState(liveJournalObservation.BindOwner(owner));
        JournalCollector.SeedPersistedValue(owner, persistedCurrencies);
        liveJournalObservation = JournalCollector.CaptureState();
    }

    private void CaptureLiveJournal(nint addonPtr)
    {
        if (!InventoryReadiness.CanCapture(plugin.Services))
            return;

        var owner = plugin.Services.PlayerState.ContentId;
        var viewedState = viewingContentId.HasValue ? CaptureCharacterCacheState() : null;
        var previousCollectorState = JournalCollector.CaptureState();
        var restoredLiveCache = false;
        try
        {
            JournalCollector.RestoreState(liveJournalObservation.BindOwner(owner));
            if (!JournalCollector.CollectFromAddon(plugin.Services, addonPtr))
                return;
            liveJournalObservation = JournalCollector.CaptureState();

            if (viewedState != null)
            {
                if (retainedLiveCharacterCache is not { DataCollected: true } retained
                    || !InventoryCapturePolicy.IsSameOwner(retained.OwnerContentId, owner))
                    return; // Keep the observation for the live owner's first full refresh.

                RestoreCharacterCacheState(retained);
                restoredLiveCache = true;
            }

            if (!InventoryCapturePolicy.IsSameOwner(cacheOwnerContentId, owner))
                return;

            JournalCollector.RestoreState(liveJournalObservation);
            JournalCollector.ApplyToCurrencies(owner, cachedCurrencies);
            lastCollectorSectionStates["Journal"] = SectionState.Available;
            if (DataCollected)
            {
                lastRefreshTime = DateTime.UtcNow;
                retainedLiveCharacterCache = CaptureCharacterCacheState();
            }
        }
        catch (Exception ex)
        {
            plugin.Services.Log.Warning(ex, "[XA] Journal close capture failed; previous allowances were retained.");
            QueueCollectorWarning("Journal close capture failed; previous allowances were retained.");
        }
        finally
        {
            if (viewedState != null && restoredLiveCache)
                RestoreCharacterCacheState(viewedState);
            else if (viewedState != null || !InventoryCapturePolicy.IsSameOwner(cacheOwnerContentId, owner))
                JournalCollector.RestoreState(previousCollectorState);
        }
    }
}

