using System.Globalization;
using System.Text.RegularExpressions;

namespace XADatabase.Core.Localization;

/// <summary>
/// Builds a stable comparison key from the language-neutral structure of a
/// residential address. It deliberately does not depend on English Plot/Ward
/// labels; supported client strings put the unit, ward, and district in
/// comma-delimited address segments.
/// </summary>
public static partial class HousingAddressIdentity
{
    [GeneratedRegex(@"\d+", RegexOptions.CultureInvariant)]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"\s*[(\uFF08][^)\uFF09]*[)\uFF09]", RegexOptions.CultureInvariant)]
    private static partial Regex ParentheticalRegex();

    [GeneratedRegex(@"\s*\[[^\]]+\]\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex OwnerSuffixRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    public static bool TryBuildComparisonKey(string? value, out string key)
    {
        key = string.Empty;
        var segments = Split(value);
        if (segments.Length < 3
            || !TryReadFirstNumber(segments[0], out var unit)
            || !TryReadFirstNumber(segments[1], out var ward))
        {
            return false;
        }

        var district = NormalizeSegment(segments[^1]);
        if (unit <= 0 || ward <= 0 || district.Length == 0)
            return false;

        key = $"unit:{unit}|ward:{ward}|district:{district}";
        return true;
    }

    public static bool TryReadResidentialParts(
        string? value,
        out int unit,
        out int ward,
        out string district)
    {
        unit = 0;
        ward = 0;
        district = string.Empty;
        var segments = Split(value);
        if (segments.Length < 3
            || !TryReadFirstNumber(segments[0], out unit)
            || !TryReadFirstNumber(segments[1], out ward))
        {
            return false;
        }

        district = ParentheticalRegex().Replace(segments[^1], string.Empty).Trim().Trim(',', ' ');
        return unit > 0 && ward > 0 && district.Length > 0;
    }

    private static string[] Split(string? value)
    {
        var withoutOwner = OwnerSuffixRegex().Replace(value?.Trim() ?? string.Empty, string.Empty);
        return withoutOwner
            .Split([',', '\uFF0C', '\u3001'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool TryReadFirstNumber(string value, out int number)
    {
        number = 0;
        var match = NumberRegex().Match(value);
        return match.Success
            && int.TryParse(match.Value, NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }

    private static string NormalizeSegment(string value)
    {
        var withoutParenthetical = ParentheticalRegex().Replace(value, string.Empty);
        return WhitespaceRegex().Replace(withoutParenthetical, " ").Trim().Trim(',', ' ').ToLowerInvariant();
    }
}
