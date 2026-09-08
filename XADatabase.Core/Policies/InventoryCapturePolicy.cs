namespace XADatabase.Core.Policies;

public static class InventoryCapturePolicy
{
    public static bool CanCapture(bool loggedIn, bool playerLoaded, ulong contentId,
        bool localPlayerPresent, bool zoning)
        => loggedIn && playerLoaded && contentId != 0 && localPlayerPresent && !zoning;

    public static bool IsSameOwner(ulong expected, ulong actual)
        => expected != 0 && expected == actual;

    public static bool PremiumReady(bool windowVisible, bool sameOwner, bool sorterReady, bool buffersReady)
        => windowVisible && sameOwner && sorterReady && buffersReady;
}
