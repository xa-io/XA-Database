using System;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;

namespace XADatabase.Services;

public sealed class ItemSearchContextMenuService : IDisposable
{
    private readonly IContextMenu contextMenu;
    private readonly IPluginLog log;
    private readonly Action<uint, bool> openSearchForItem;

    private bool enabled;
    private bool subscribed;

    public ItemSearchContextMenuService(
        IContextMenu contextMenu,
        IPluginLog log,
        Action<uint, bool> openSearchForItem)
    {
        this.contextMenu = contextMenu;
        this.log = log;
        this.openSearchForItem = openSearchForItem;
    }

    public string StatusText { get; private set; } = "Disabled";

    public bool SetEnabled(bool value)
    {
        if (value == enabled)
            return enabled;

        if (!value)
        {
            enabled = false;
            Unsubscribe();
            StatusText = "Disabled";
            return false;
        }

        Subscribe();
        enabled = true;
        StatusText = "Enabled - inventory context menus can route exact items into XA Database search.";
        return true;
    }

    public void Dispose()
    {
        enabled = false;
        Unsubscribe();
    }

    private void Subscribe()
    {
        if (subscribed)
            return;

        contextMenu.OnMenuOpened += OnMenuOpened;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed)
            return;

        contextMenu.OnMenuOpened -= OnMenuOpened;
        subscribed = false;
    }

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (!enabled || args.MenuType != ContextMenuType.Inventory || args.Target is not MenuTargetInventory target)
            return;

        if (target.TargetItem is not { } item || item.ItemId == 0)
            return;

        args.AddMenuItem(new MenuItem
        {
            Name = new SeString(new TextPayload("Search For Item")),
            PrefixChar = 'X',
            PrefixColor = 539,
            UseDefaultPrefix = false,
            OnClicked = _ => SearchForItem(item.ItemId, item.IsHq),
        });
    }

    private void SearchForItem(uint rawItemId, bool isHq)
    {
        try
        {
            var normalizedItemId = rawItemId >= 2_000_000 ? rawItemId : rawItemId % 500_000;
            openSearchForItem(normalizedItemId, isHq);
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[XA] Failed to open search from inventory context menu.");
        }
    }
}
