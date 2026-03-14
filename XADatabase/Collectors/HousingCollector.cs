using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using XADatabase.Database;
using XADatabase.Services;

namespace XADatabase.Collectors;

public static class HousingCollector
{
    // Known housing outdoor territory type IDs → district names
    private static readonly Dictionary<ushort, string> DistrictNames = new()
    {
        { 339, "Mist" },
        { 340, "The Lavender Beds" },
        { 341, "The Goblet" },
        { 641, "Shirogane" },
        { 979, "Empyreum" },
    };

    public static string LastPersonalEstateFromAddon { get; private set; } = string.Empty;
    public static string LastApartmentFromAddon { get; private set; } = string.Empty;
    public static string LastSharedEstatesFromAddon => string.Join("\n", LastSharedEstateAddressesFromAddon);
    private static HousingSignBoardContext LastObservedEstateContext = HousingSignBoardContext.Unknown;
    private static readonly List<string> LastSharedEstateAddressesFromAddon = new();

    // Plot size ranges (0-indexed plot numbers)
    // Plots 0-29: Small(0-14 main, 15-29 sub), Medium/Large vary by ward layout
    // Simplified: plots 0-7 in each half = Small, 8-11 = Medium, 12-14 = Large (per ward)
    private static string GetPlotSize(byte plotIndex)
    {
        var localPlot = plotIndex % 30; // normalize for subdivision
        return localPlot switch
        {
            >= 0 and <= 7 => "Small",
            >= 8 and <= 11 => "Medium",
            >= 12 and <= 14 => "Large",
            >= 15 and <= 22 => "Small",  // subdivision small
            >= 23 and <= 26 => "Medium", // subdivision medium
            >= 27 and <= 29 => "Large",  // subdivision large
            _ => "",
        };
    }

    /// <summary>
    /// Checks whether a HouseId contains valid housing data.
    /// The game returns sentinel/max values when a character doesn't own that type:
    ///   PlotIndex=127, WardIndex=63, TerritoryTypeId=65535, RoomNumber=1023
    /// We validate by checking TerritoryTypeId against known housing districts.
    /// </summary>
    private static bool IsValidHouseId(HouseId id)
    {
        return id.Id != 0
            && id.TerritoryTypeId != 0
            && id.TerritoryTypeId != 0xFFFF
            && DistrictNames.ContainsKey(id.TerritoryTypeId);
    }

    /// <summary>
    /// Collect personal housing information from HousingManager.
    /// Returns formatted strings for personal estate and apartment.
    /// Can be called from anywhere — does not require being in a housing zone.
    /// </summary>
    public static unsafe (string PersonalEstate, string SharedEstates, string Apartment) CollectPersonalHousing()
    {
        var personalEstate = string.Empty;
        var sharedEstates = string.Empty;
        var apartment = string.Empty;

        try
        {
            TryCollectFromOpenAddons();

            // Personal Estate
            var personalHouseId = HousingManager.GetOwnedHouseId(EstateType.PersonalEstate);
            if (IsValidHouseId(personalHouseId))
            {
                var plot = personalHouseId.Unit.PlotIndex + 1;
                var ward = personalHouseId.WardIndex + 1;
                var district = DistrictNames[personalHouseId.TerritoryTypeId];

                personalEstate = $"Plot {plot}, Ward {ward}, {district}";
                Plugin.Log.Information($"[XA] Personal Estate: {personalEstate} (TerritoryType={personalHouseId.TerritoryTypeId}, World={personalHouseId.WorldId})");
            }
            else
            {
                Plugin.Log.Debug($"[XA] No personal estate (Id={personalHouseId.Id:X}, Territory={personalHouseId.TerritoryTypeId})");
            }

            // Apartment
            var aptHouseId = HousingManager.GetOwnedHouseId(EstateType.ApartmentRoom);
            if (IsValidHouseId(aptHouseId))
            {
                var ward = aptHouseId.WardIndex + 1;
                var room = aptHouseId.RoomNumber;
                var district = DistrictNames[aptHouseId.TerritoryTypeId];
                apartment = XaCharacterSnapshotRepository.NormalizeApartmentDisplayValue($"Room {room}, Ward {ward}, {district}");
                Plugin.Log.Information($"[XA] Apartment: {apartment} (TerritoryType={aptHouseId.TerritoryTypeId}, World={aptHouseId.WorldId})");
            }
            else
            {
                Plugin.Log.Debug($"[XA] No apartment (Id={aptHouseId.Id:X}, Territory={aptHouseId.TerritoryTypeId})");
            }

            if (!string.IsNullOrEmpty(personalEstate) && !string.IsNullOrEmpty(LastPersonalEstateFromAddon))
                personalEstate = XaCharacterSnapshotRepository.PreferSizedPersonalEstateValue(personalEstate, LastPersonalEstateFromAddon);
            if (!string.IsNullOrEmpty(personalEstate) && LastSharedEstateAddressesFromAddon.Count > 0)
            {
                foreach (var sharedEstateAddress in LastSharedEstateAddressesFromAddon.ToList())
                {
                    var promotedPersonalEstate = XaCharacterSnapshotRepository.PreferSizedPersonalEstateValue(personalEstate, sharedEstateAddress);
                    if (promotedPersonalEstate.Equals(personalEstate, StringComparison.Ordinal))
                        continue;

                    personalEstate = promotedPersonalEstate;
                    RemoveSharedEstateFromAddon(sharedEstateAddress);
                    Plugin.Log.Information($"[XA] Reclassified addon housing address as Personal Estate: \"{personalEstate}\"");
                    break;
                }
            }
            if (LastSharedEstateAddressesFromAddon.Count > 0)
                sharedEstates = LastSharedEstatesFromAddon;
            if (!string.IsNullOrEmpty(apartment) && !string.IsNullOrEmpty(LastApartmentFromAddon))
                apartment = XaCharacterSnapshotRepository.NormalizeApartmentDisplayValue(LastApartmentFromAddon);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[XA] Error collecting personal housing: {ex.Message}");
        }

        return (personalEstate, sharedEstates, apartment);
    }

    public static unsafe void TryCollectFromOpenAddons()
    {
        try
        {
            var hsb = AtkStage.Instance()->RaptureAtkUnitManager->GetAddonByName("HousingSignBoard");
            if (hsb != null && hsb->IsVisible)
                CollectFromAddon((nint)hsb);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[XA] TryCollect HousingSignBoard error: {ex.Message}");
        }
    }

    public static void ResetPersonalHousingState()
    {
        LastPersonalEstateFromAddon = string.Empty;
        LastApartmentFromAddon = string.Empty;
        LastObservedEstateContext = HousingSignBoardContext.Unknown;
        LastSharedEstateAddressesFromAddon.Clear();
    }

    public static void ObserveEstateAddonDetail(string addonName, string addonDetail)
    {
        var context = ResolveContextFromAddonDetail(addonName, addonDetail);
        if (context != HousingSignBoardContext.Unknown)
        {
            LastObservedEstateContext = context;
            return;
        }

        if (addonName.Equals("HousingMenu", StringComparison.OrdinalIgnoreCase)
            || addonName.Equals("HousingSelectHouse", StringComparison.OrdinalIgnoreCase))
        {
            LastObservedEstateContext = HousingSignBoardContext.Unknown;
        }
    }

    public static unsafe void CollectFromAddon(nint addonPtr)
    {
        try
        {
            if (addonPtr == nint.Zero)
            {
                Plugin.Log.Debug("[XA] HousingSignBoard addon pointer is zero");
                return;
            }

            var addon = (AtkUnitBase*)addonPtr;
            var allText = AddonTextReader.ReadAllText(addon);
            var title = string.Empty;
            var ownerName = string.Empty;
            var address = string.Empty;

            for (var i = 0; i < allText.Count; i++)
            {
                var trimmed = NormalizeHousingText(allText[i].Text);
                if (trimmed.Length == 0)
                    continue;

                if (title.Length == 0 && (trimmed.Contains("Details", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("Profile", StringComparison.OrdinalIgnoreCase)))
                    title = trimmed;

                if (ownerName.Length == 0 && trimmed.Equals("Owner", StringComparison.OrdinalIgnoreCase))
                {
                    ownerName = ExtractOwnerName(allText, i);
                    continue;
                }

                if (address.Length == 0 && LooksLikeHousingAddress(trimmed))
                    address = trimmed;
            }

            if (address.Length == 0)
            {
                Plugin.Log.Debug("[XA] HousingSignBoard collector found no address text.");
                return;
            }

            var context = DetectHousingContext(title, ownerName, address);
            if (context != HousingSignBoardContext.Unknown)
                LastObservedEstateContext = context;

            switch (context)
            {
                case HousingSignBoardContext.Apartment:
                    LastApartmentFromAddon = XaCharacterSnapshotRepository.NormalizeApartmentDisplayValue(address);
                    Plugin.Log.Information($"[XA] Apartment from addon: \"{LastApartmentFromAddon}\"");
                    break;

                case HousingSignBoardContext.PersonalEstate:
                    LastPersonalEstateFromAddon = XaCharacterSnapshotRepository.StripHousingOwnerSuffix(address);
                    RemoveSharedEstateFromAddon(address);
                    Plugin.Log.Information($"[XA] Personal estate from addon: \"{LastPersonalEstateFromAddon}\"");
                    break;

                case HousingSignBoardContext.FreeCompanyEstate:
                    FreeCompanyCollector.CollectEstateFromAddon(addonPtr);
                    break;

                case HousingSignBoardContext.SharedEstate:
                    var sharedEstateDisplayValue = BuildSharedEstateDisplayValue(address, ownerName);
                    AddSharedEstateFromAddon(sharedEstateDisplayValue);
                    if (HousingDisplayValuesMatch(LastPersonalEstateFromAddon, sharedEstateDisplayValue))
                        LastPersonalEstateFromAddon = string.Empty;
                    Plugin.Log.Information($"[XA] Shared estate from addon: \"{sharedEstateDisplayValue}\"");
                    break;

                default:
                    if (address.Contains("Room #", StringComparison.OrdinalIgnoreCase) || title.Contains("Apartment", StringComparison.OrdinalIgnoreCase))
                    {
                        LastApartmentFromAddon = XaCharacterSnapshotRepository.NormalizeApartmentDisplayValue(address);
                        Plugin.Log.Information($"[XA] Apartment from addon (fallback): \"{LastApartmentFromAddon}\"");
                    }
                    else if (IsFreeCompanyHousingContext())
                    {
                        FreeCompanyCollector.CollectEstateFromAddon(addonPtr);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[XA] CollectFromAddon housing error: {ex.Message}");
        }
    }

    private static void AddSharedEstateFromAddon(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return;

        var normalized = address.Trim();
        for (var i = 0; i < LastSharedEstateAddressesFromAddon.Count; i++)
        {
            var existing = LastSharedEstateAddressesFromAddon[i];
            if (!HousingDisplayValuesMatch(existing, normalized))
                continue;

            if (HasHousingOwnerSuffix(normalized) && !HasHousingOwnerSuffix(existing))
                LastSharedEstateAddressesFromAddon[i] = normalized;

            return;
        }

        LastSharedEstateAddressesFromAddon.Add(normalized);
    }

    private static void RemoveSharedEstateFromAddon(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return;

        LastSharedEstateAddressesFromAddon.RemoveAll(existing => HousingDisplayValuesMatch(existing, address));
    }

    private static string BuildSharedEstateDisplayValue(string address, string ownerName)
    {
        var baseAddress = XaCharacterSnapshotRepository.StripHousingOwnerSuffix(address);
        if (baseAddress.Length == 0)
            return string.Empty;

        var normalizedOwner = NormalizeHousingText(ownerName);
        var currentPlayerName = NormalizeHousingText(Plugin.PlayerState.CharacterName.ToString());
        if (normalizedOwner.Length == 0 || normalizedOwner.Equals(currentPlayerName, StringComparison.OrdinalIgnoreCase))
            return baseAddress;

        return $"{baseAddress} [{normalizedOwner}]";
    }

    private static bool HousingDisplayValuesMatch(string left, string right)
    {
        return XaCharacterSnapshotRepository.HousingDisplayValuesMatch(left, right);
    }

    private static bool HasHousingOwnerSuffix(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        var stripped = XaCharacterSnapshotRepository.StripHousingOwnerSuffix(normalized);
        return normalized.Length > 0 && !normalized.Equals(stripped, StringComparison.Ordinal);
    }

    private static HousingSignBoardContext DetectHousingContext(string title, string ownerName, string address)
    {
        if (title.Contains("Apartment", StringComparison.OrdinalIgnoreCase)
            || address.Contains("Room #", StringComparison.OrdinalIgnoreCase)
            || address.Contains(" Wing ", StringComparison.OrdinalIgnoreCase))
            return HousingSignBoardContext.Apartment;

        if (TryReadOpenAddonTexts("HousingSubmenu", out var submenuTexts))
        {
            if (ContainsText(submenuTexts, "Apartment Options") || ContainsText(submenuTexts, "View Room Details"))
                return HousingSignBoardContext.Apartment;
            if (ContainsText(submenuTexts, "Shared Estate", contains: true))
                return HousingSignBoardContext.SharedEstate;
            if (ContainsText(submenuTexts, "Private Estate"))
                return HousingSignBoardContext.PersonalEstate;
            if (ContainsText(submenuTexts, "Free Company Estate"))
                return HousingSignBoardContext.FreeCompanyEstate;
        }

        if (IsFreeCompanyHousingContext())
            return HousingSignBoardContext.FreeCompanyEstate;

        var currentPlayerName = NormalizeHousingText(Plugin.PlayerState.CharacterName.ToString());
        if (ownerName.Length > 0 && currentPlayerName.Length > 0)
        {
            if (ownerName.Equals(currentPlayerName, StringComparison.OrdinalIgnoreCase))
                return HousingSignBoardContext.PersonalEstate;

            if (address.Contains("Plot ", StringComparison.OrdinalIgnoreCase))
                return HousingSignBoardContext.SharedEstate;
        }

        if (LastObservedEstateContext != HousingSignBoardContext.Unknown)
            return LastObservedEstateContext;

        if (address.Contains("Plot ", StringComparison.OrdinalIgnoreCase))
            return HousingSignBoardContext.PersonalEstate;

        return HousingSignBoardContext.Unknown;
    }

    private static string ExtractOwnerName(List<(string Path, uint NodeId, string Text)> texts, int ownerLabelIndex)
    {
        const int ownerSearchRadius = 3;
        string bestCandidate = string.Empty;
        var bestDistance = int.MaxValue;

        for (var i = Math.Max(0, ownerLabelIndex - ownerSearchRadius); i <= Math.Min(texts.Count - 1, ownerLabelIndex + ownerSearchRadius); i++)
        {
            if (i == ownerLabelIndex)
                continue;

            var trimmed = NormalizeHousingText(texts[i].Text);
            if (trimmed.Length == 0)
                continue;
            if (IsHousingMetadataLabel(trimmed) || LooksLikeHousingAddress(trimmed))
                continue;
            if (trimmed.Contains("Details", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("Profile", StringComparison.OrdinalIgnoreCase))
                continue;

            var distance = Math.Abs(i - ownerLabelIndex);
            if (distance >= bestDistance)
                continue;

            bestCandidate = trimmed;
            bestDistance = distance;
        }

        return bestCandidate;
    }

    private static HousingSignBoardContext ResolveContextFromAddonDetail(string addonName, string addonDetail)
    {
        var detail = NormalizeHousingText(addonDetail);
        if (detail.Length == 0)
            return HousingSignBoardContext.Unknown;
        if (detail.Contains("Apartment", StringComparison.OrdinalIgnoreCase))
            return HousingSignBoardContext.Apartment;
        if (detail.Contains("Shared", StringComparison.OrdinalIgnoreCase))
            return HousingSignBoardContext.SharedEstate;
        if (detail.Contains("FreeCompany", StringComparison.OrdinalIgnoreCase) || detail.Contains("Free Company", StringComparison.OrdinalIgnoreCase))
            return HousingSignBoardContext.FreeCompanyEstate;
        if (detail.Contains("Personal", StringComparison.OrdinalIgnoreCase) || detail.Contains("Private", StringComparison.OrdinalIgnoreCase))
            return HousingSignBoardContext.PersonalEstate;

        return HousingSignBoardContext.Unknown;
    }

    private static unsafe bool IsFreeCompanyHousingContext()
    {
        return IsAddonVisible("FreeCompanyStatus") || IsAddonVisible("FreeCompany");
    }

    private static unsafe bool IsAddonVisible(string addonName)
    {
        var addon = AtkStage.Instance()->RaptureAtkUnitManager->GetAddonByName(addonName);
        return addon != null && addon->IsVisible;
    }

    private static unsafe bool TryReadOpenAddonTexts(string addonName, out List<(string Path, uint NodeId, string Text)> texts)
    {
        texts = new List<(string Path, uint NodeId, string Text)>();
        var addon = AtkStage.Instance()->RaptureAtkUnitManager->GetAddonByName(addonName);
        if (addon == null || !addon->IsVisible)
            return false;

        texts = AddonTextReader.ReadAllText(addon);
        return texts.Count > 0;
    }

    private static bool ContainsText(List<(string Path, uint NodeId, string Text)> texts, string value, bool contains = false)
    {
        foreach (var (_, _, text) in texts)
        {
            var trimmed = NormalizeHousingText(text);
            if (trimmed.Length == 0)
                continue;

            if (contains)
            {
                if (trimmed.Contains(value, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else if (trimmed.Equals(value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsHousingMetadataLabel(string text)
    {
        return text.Equals("Owner", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Address", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Greeting", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Name", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Tag", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Main", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Sub 1", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Sub 2", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Plot Details", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Plot Size", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Price", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Next Devaluation", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Cancel", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Purchase Land", StringComparison.OrdinalIgnoreCase)
            || text.Equals("None", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeHousingAddress(string text)
    {
        return text.Contains("Ward", StringComparison.OrdinalIgnoreCase) && text.Contains(",", StringComparison.Ordinal);
    }

    private static string NormalizeHousingText(string text)
    {
        return string.Join(" ", (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private enum HousingSignBoardContext
    {
        Unknown,
        Apartment,
        PersonalEstate,
        FreeCompanyEstate,
        SharedEstate,
    }
}
