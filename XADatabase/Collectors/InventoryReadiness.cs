using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using XADatabase.Core.Policies;
using XADatabase.Services;

namespace XADatabase.Collectors;

internal static class InventoryReadiness
{
    internal static bool CanCapture(XaServices services)
        => InventoryCapturePolicy.CanCapture(services.ClientState.IsLoggedIn,
            services.PlayerState.IsLoaded, services.PlayerState.ContentId,
            services.ObjectTable.LocalPlayer != null,
            services.Condition[ConditionFlag.BetweenAreas] || services.Condition[ConditionFlag.BetweenAreas51]);

    internal static bool IsSaddlebagVisible(XaServices services)
        => services.GameGui.GetAddonByName("InventoryBuddy", 1).IsVisible
            || services.GameGui.GetAddonByName("InventoryBuddy2", 1).IsVisible;

    internal static unsafe bool IsReadable(InventoryManager* manager, InventoryType type,
        ulong contentId, bool saddlebagVisible)
    {
        var container = manager->GetInventoryContainer(type);
        if (container == null || container->Items == null || container->Size <= 0)
            return false;

        if (type is not (InventoryType.PremiumSaddleBag1 or InventoryType.PremiumSaddleBag2))
            return container->IsLoaded;

        // Premium IsLoaded is not reliable. Like Allagan Tools, require an open
        // Buddy window, premium-specific sort data and both allocated pages.
        // This is read readiness, not proof of a subscription (or its absence).
        var order = ItemOrderModule.Instance();
        var first = manager->GetInventoryContainer(InventoryType.PremiumSaddleBag1);
        var second = manager->GetInventoryContainer(InventoryType.PremiumSaddleBag2);
        return InventoryCapturePolicy.PremiumReady(saddlebagVisible,
            order != null && InventoryCapturePolicy.IsSameOwner(contentId, order->CharacterContentId),
            order != null && order->PremiumSaddleBagSorter != null
                && order->PremiumSaddleBagSorter->Items.LongCount == 70,
            first != null && second != null && first->Items != null && second->Items != null
                && first->Size == 35 && second->Size == 35);
    }
}
