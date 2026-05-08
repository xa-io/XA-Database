using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace XADatabase.Data;

public static class HousingPlotSizeData
{
    private static readonly Regex PlotNumberRegex = new(@"\bPlot\s+(?<plot>\d+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SizeSuffixRegex = new(@"\s*\((?:Small|Medium|Large)\)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex OwnerSuffixRegex = new(@"\s*(?<owner>\[[^\]]+\])\s*$", RegexOptions.Compiled);

    private static readonly Dictionary<string, string> DistrictAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Mist", "Mist" },
        { "The Mist", "Mist" },
        { "Goblet", "Goblet" },
        { "The Goblet", "Goblet" },
        { "Lavender Beds", "Lavender Beds" },
        { "The Lavender Beds", "Lavender Beds" },
        { "LB", "Lavender Beds" },
        { "Shirogane", "Shirogane" },
        { "Empyreum", "Empyreum" },
    };

    private static readonly Dictionary<string, string[]> PlotSizesByDistrict = new(StringComparer.OrdinalIgnoreCase)
    {
        {
            "Mist",
            new[] { "Medium", "Large", "Small", "Medium", "Large", "Medium", "Medium", "Small", "Small", "Small", "Small", "Small", "Small", "Medium", "Large", "Small", "Small", "Small", "Small", "Small", "Small", "Small", "Small", "Small", "Small", "Small", "Small", "Small", "Medium", "Medium", "Medium", "Large", "Small", "Medium", "Large", "Medium", "Medium", "Small", "Small", "Small", "Small", "Small", "Small", "Medium", "Large", "Small", "Small", "Small", "Small", "Small", "Small", "Small", "Small", "Small", "Small", "Small", "Small", "Small", "Medium", "Medium" }
        },
        {
            "Goblet",
            new[] { "Small", "Small", "Small", "Medium", "Large", "Medium", "Small", "Medium", "Small", "Small", "Medium", "Medium", "Large", "Small", "Small", "Small", "Small", "Small", "Medium", "Small", "Small", "Small", "Small", "Small", "Medium", "Small", "Small", "Small", "Small", "Large", "Small", "Small", "Small", "Medium", "Large", "Medium", "Small", "Medium", "Small", "Small", "Medium", "Medium", "Large", "Small", "Small", "Small", "Small", "Small", "Medium", "Small", "Small", "Small", "Small", "Small", "Medium", "Small", "Small", "Small", "Small", "Large" }
        },
        {
            "Lavender Beds",
            new[] { "Medium", "Small", "Large", "Small", "Medium", "Large", "Small", "Small", "Small", "Small", "Medium", "Small", "Small", "Small", "Small", "Medium", "Small", "Small", "Small", "Small", "Medium", "Small", "Small", "Small", "Small", "Small", "Medium", "Large", "Small", "Medium", "Medium", "Small", "Large", "Small", "Medium", "Large", "Small", "Small", "Small", "Small", "Medium", "Small", "Small", "Small", "Small", "Medium", "Small", "Small", "Small", "Small", "Medium", "Small", "Small", "Small", "Small", "Small", "Medium", "Large", "Small", "Medium" }
        },
        {
            "Shirogane",
            new[] { "Medium", "Small", "Small", "Small", "Small", "Small", "Large", "Medium", "Small", "Small", "Small", "Small", "Medium", "Small", "Medium", "Large", "Small", "Small", "Medium", "Small", "Small", "Small", "Small", "Medium", "Small", "Small", "Small", "Medium", "Small", "Large", "Medium", "Small", "Small", "Small", "Small", "Small", "Large", "Medium", "Small", "Small", "Small", "Small", "Medium", "Small", "Medium", "Large", "Small", "Small", "Medium", "Small", "Small", "Small", "Small", "Medium", "Small", "Small", "Small", "Medium", "Small", "Large" }
        },
        {
            "Empyreum",
            new[] { "Small", "Medium", "Small", "Small", "Small", "Small", "Medium", "Medium", "Small", "Small", "Small", "Large", "Small", "Small", "Small", "Small", "Medium", "Medium", "Small", "Small", "Medium", "Large", "Small", "Small", "Small", "Medium", "Small", "Small", "Small", "Large", "Small", "Medium", "Small", "Small", "Small", "Small", "Medium", "Medium", "Small", "Small", "Small", "Large", "Small", "Small", "Small", "Small", "Medium", "Medium", "Small", "Small", "Medium", "Large", "Small", "Small", "Small", "Medium", "Small", "Small", "Small", "Large" }
        },
    };

    public static string GetPlotSize(string districtName, int plotNumber)
    {
        if (plotNumber < 1 || plotNumber > 60)
            return string.Empty;

        if (!TryCanonicalizeDistrict(districtName, out var canonicalDistrict))
            return string.Empty;

        return PlotSizesByDistrict.TryGetValue(canonicalDistrict, out var sizes)
            ? sizes[plotNumber - 1]
            : string.Empty;
    }

    public static string ApplySizeSuffix(string estateValue)
    {
        var trimmed = estateValue?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return string.Empty;

        var ownerSuffix = string.Empty;
        var ownerMatch = OwnerSuffixRegex.Match(trimmed);
        if (ownerMatch.Success)
        {
            ownerSuffix = " " + ownerMatch.Groups["owner"].Value.Trim();
            trimmed = trimmed[..ownerMatch.Index].Trim();
        }

        if (!TryParseResidentialPlot(trimmed, out var districtName, out var plotNumber))
            return (trimmed + ownerSuffix).Trim();

        var plotSize = GetPlotSize(districtName, plotNumber);
        if (plotSize.Length == 0)
            return (trimmed + ownerSuffix).Trim();

        var withoutSize = SizeSuffixRegex.Replace(trimmed, string.Empty).Trim();
        return $"{withoutSize} ({plotSize}){ownerSuffix}".Trim();
    }

    private static bool TryParseResidentialPlot(string estateValue, out string districtName, out int plotNumber)
    {
        districtName = string.Empty;
        plotNumber = 0;

        var plotMatch = PlotNumberRegex.Match(estateValue);
        if (!plotMatch.Success || !int.TryParse(plotMatch.Groups["plot"].Value, out plotNumber))
            return false;

        var segments = estateValue
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => SizeSuffixRegex.Replace(segment.Trim(), string.Empty).Trim())
            .Where(segment => segment.Length > 0)
            .ToArray();
        if (segments.Length == 0)
            return false;

        return TryCanonicalizeDistrict(segments[^1], out districtName);
    }

    private static bool TryCanonicalizeDistrict(string districtName, out string canonicalDistrict)
    {
        canonicalDistrict = string.Empty;
        var normalizedDistrict = (districtName ?? string.Empty).Trim();
        if (normalizedDistrict.Length == 0)
            return false;

        if (DistrictAliases.TryGetValue(normalizedDistrict, out var resolvedDistrict))
        {
            canonicalDistrict = resolvedDistrict;
            return true;
        }

        return false;
    }
}
