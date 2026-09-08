using System;
using System.Globalization;

namespace XADatabase.Database;

public static class SnapshotTime
{
    public const string StorageFormat = "yyyy-MM-dd HH:mm:ss";
    public const string LocalOffsetFormat = "yyyy-MM-dd HH:mm:ss zzz";

    private static readonly string[] AcceptedExactFormats =
    {
        StorageFormat,
        "O",
        "s",
    };

    public static string Format(DateTime value)
    {
        var utc = NormalizeUtc(value);
        return utc.ToString(StorageFormat, CultureInfo.InvariantCulture);
    }

    public static string FormatLocal(DateTime value)
    {
        var local = new DateTimeOffset(NormalizeUtc(value)).ToLocalTime();
        return local.ToString(LocalOffsetFormat, CultureInfo.InvariantCulture);
    }

    public static bool TryParseUtc(string? value, out DateTime parsedUtc)
    {
        parsedUtc = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (DateTime.TryParseExact(
                value.Trim(),
                AcceptedExactFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out parsedUtc))
        {
            parsedUtc = DateTime.SpecifyKind(parsedUtc, DateTimeKind.Utc);
            return true;
        }

        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsedOffset))
        {
            return false;
        }

        parsedUtc = parsedOffset.UtcDateTime;
        return true;
    }

    public static string NormalizeOrFallback(string? value, DateTime fallbackUtc)
    {
        return TryParseUtc(value, out var parsedUtc)
            ? Format(parsedUtc)
            : Format(fallbackUtc);
    }

    public static bool IsSameOrNewer(string? existingValue, string? candidateValue)
    {
        if (string.IsNullOrWhiteSpace(candidateValue))
            return true;
        if (string.IsNullOrWhiteSpace(existingValue))
            return false;

        var existingParsed = TryParseUtc(existingValue, out var existingUtc);
        var candidateParsed = TryParseUtc(candidateValue, out var candidateUtc);
        if (existingParsed && candidateParsed)
            return existingUtc >= candidateUtc;
        if (existingParsed)
            return true;
        if (candidateParsed)
            return false;

        // Two malformed values have no trustworthy ordering. Preserve the existing
        // value rather than replacing it from an arbitrary ordinal comparison.
        return true;
    }

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
