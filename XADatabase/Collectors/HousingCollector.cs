using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

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
    public static unsafe (string PersonalEstate, string Apartment) CollectPersonalHousing()
    {
        var personalEstate = string.Empty;
        var apartment = string.Empty;

        try
        {
            // Personal Estate
            var personalHouseId = HousingManager.GetOwnedHouseId(EstateType.PersonalEstate);
            if (IsValidHouseId(personalHouseId))
            {
                var plot = personalHouseId.Unit.PlotIndex + 1;
                var ward = personalHouseId.WardIndex + 1;
                var district = DistrictNames[personalHouseId.TerritoryTypeId];
                var size = GetPlotSize(personalHouseId.Unit.PlotIndex);
                var sizeStr = !string.IsNullOrEmpty(size) ? $" ({size})" : "";

                personalEstate = $"Plot {plot}, Ward {ward}, {district}{sizeStr}";
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
                var division = aptHouseId.Unit.ApartmentDivision == 1 ? " (Subdivision)" : "";

                apartment = $"Room {room}, Ward {ward}, {district}{division}";
                Plugin.Log.Information($"[XA] Apartment: {apartment} (TerritoryType={aptHouseId.TerritoryTypeId}, World={aptHouseId.WorldId})");
            }
            else
            {
                Plugin.Log.Debug($"[XA] No apartment (Id={aptHouseId.Id:X}, Territory={aptHouseId.TerritoryTypeId})");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[XA] Error collecting personal housing: {ex.Message}");
        }

        return (personalEstate, apartment);
    }
}
