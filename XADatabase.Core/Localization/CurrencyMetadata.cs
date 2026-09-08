namespace XADatabase.Core.Localization;

/// <summary>Stable curation categories; display names come from game data.</summary>
public enum CurrencyCategory
{
    Common,
    Battle,
    Other,
    Societies,
}

public readonly record struct CurrencyDefinition(CurrencyCategory Category, int Cap, bool AlwaysShow);

public static class CurrencyIdentity
{
    public const string LeveAllowances = "leve-allowances";
    public const string GilItemKey = "item:1";

    public static string CategoryName(CurrencyCategory category) => category.ToString();

    public static string UnknownItemName(uint itemId) => $"Item #{itemId}";

    public static string ResolveKey(uint itemId, string? syntheticKey, string? legacyDisplayName)
    {
        if (itemId != 0)
            return $"item:{itemId}";
        if (!string.IsNullOrWhiteSpace(syntheticKey))
            return syntheticKey.Trim();
        return string.Equals(legacyDisplayName, "Gil", StringComparison.OrdinalIgnoreCase)
            ? GilItemKey
            : string.Empty;
    }

    public static bool IsGil(uint itemId, string? syntheticKey, string? legacyDisplayName)
        => string.Equals(
            ResolveKey(itemId, syntheticKey, legacyDisplayName),
            GilItemKey,
            StringComparison.Ordinal);
}
