using System;
using XADatabase.Collectors;
using XADatabase.Core.Collection;
using XADatabase.Core.Policies;
using XADatabase.Database;

namespace XADatabase.Windows;

public partial class MainWindow
{
    private ulong inventoryCaptureOwner;
    private int inventoryCaptureAttempts;
    private DateTime nextInventoryCaptureUtc;

    private void ApplyInventoryCapture(SectionResult<ItemCollectionResult> items)
    {
        var readable = items.Value.LoadedContainers;
        ApplyCollectedSection("Items", items, value => cachedItems = InventoryContainerMerge.Merge(
            cachedItems, value.Items, readable, item => item.ContainerName));
        ApplyCollectedSection("Inventory summaries", InventoryCollector.CollectSection(plugin.Services, items),
            value => cachedInventory = InventoryContainerMerge.Merge(cachedInventory, value, readable, summary => summary.Name));
    }

    public void CaptureLiveInventory(bool saddlebagClosing = false)
    {
        if (!InventoryReadiness.CanCapture(plugin.Services))
            return;

        var owner = plugin.Services.PlayerState.ContentId;
        var retained = retainedLiveCharacterCache;
        if (viewingContentId.HasValue
            ? retained == null || !retained.DataCollected || !InventoryCapturePolicy.IsSameOwner(retained.OwnerContentId, owner)
            : !DataCollected || !InventoryCapturePolicy.IsSameOwner(cacheOwnerContentId, owner))
            return;

        var observed = ItemCollector.CollectSection(plugin.Services, saddlebagClosing);
        if (!observed.CanReplacePersisted)
            return;

        // Browsing a stored character must not freeze the live owner's cache or replace the view.
        var viewedState = viewingContentId.HasValue ? CaptureCharacterCacheState() : null;
        try
        {
            if (viewedState != null)
                RestoreCharacterCacheState(retained!);

            ApplyInventoryCapture(observed);
            try
            {
                MergeActiveRetainerInventory();
                MergeActiveRetainerListings();
            }
            catch (Exception ex)
            {
                plugin.Services.Log.Warning(ex, "[XA] Active retainer capture failed; readable character bags were still captured.");
                QueueCollectorWarning("Active retainer capture failed during an inventory change.");
            }
            lastRefreshTime = DateTime.UtcNow;
            retainedLiveCharacterCache = CaptureCharacterCacheState();
        }
        finally
        {
            if (viewedState != null)
                RestoreCharacterCacheState(viewedState);
        }
    }

    public void QueueInventoryCapture()
    {
        if (!plugin.Services.ClientState.IsLoggedIn)
            return;
        inventoryCaptureOwner = plugin.Services.PlayerState.ContentId;
        inventoryCaptureAttempts = inventoryCaptureOwner != 0 ? 3 : 0;
        nextInventoryCaptureUtc = DateTime.UtcNow.AddMilliseconds(200);
    }

    private void ProcessPendingInventoryCapture()
    {
        if (inventoryCaptureAttempts == 0 || DateTime.UtcNow < nextInventoryCaptureUtc)
            return;
        if (!InventoryCapturePolicy.IsSameOwner(inventoryCaptureOwner, plugin.Services.PlayerState.ContentId))
        {
            inventoryCaptureAttempts = 0;
            return;
        }
        if (!InventoryReadiness.CanCapture(plugin.Services))
            return;

        // Bounded post-open/zone retries allow server-populated bags to arrive; no background scanner.
        inventoryCaptureAttempts--;
        nextInventoryCaptureUtc = DateTime.UtcNow.AddMilliseconds(400);
        CaptureLiveInventory();
    }

    public void ClearPendingCaptureAndSave()
    {
        inventoryCaptureAttempts = 0;
        inventoryCaptureOwner = 0;
        refreshAndSaveQueued = false;
        queuedSaveContentId = 0;
        queuedRefreshAndSaveDetail = string.Empty;
    }

    public SaveSnapshotResult SaveFromIpc()
    {
        // IpcProvider already marshals and contains this invocation on the framework thread.
        // Never leave an unowned IPC request waiting for the next character's login.
        if (!plugin.Services.ClientState.IsLoggedIn)
            return lastSnapshotResult is { Success: true, Trigger: SnapshotTrigger.Logout }
                ? lastSnapshotResult
                : SaveForLogout("IPC Save after logout");
        if (!InventoryReadiness.CanCapture(plugin.Services))
            return QueueRefreshAndSave(SnapshotTrigger.XASlave, "IPC Save");

        refreshAndSaveQueued = false;
        queuedSaveContentId = 0;
        queuedRefreshAndSaveDetail = string.Empty;
        return RefreshAndSave(SnapshotTrigger.XASlave, "IPC Save");
    }

    private SaveSnapshotResult FinishCaptureSkipped(SnapshotTrigger trigger, string detail, string summary)
    {
        var now = DateTime.UtcNow;
        var result = new SaveSnapshotResult
        {
            Trigger = trigger,
            TriggerDetail = detail,
            SavedAtUtc = SnapshotTime.Format(now),
            SavedAtLocal = SnapshotTime.FormatLocal(now),
            Summary = summary,
            Quality = "Degraded",
        };
        lastSnapshotResult = result;
        AddSaveHistoryEntry(result);
        return result;
    }
}
