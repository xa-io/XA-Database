using System;
using System.Collections.Generic;

namespace XADatabase.Models;

/// <summary>Comparison-only text policy; never use it for persisted identities.</summary>
public static class LocalizedText
{
    public static IComparer<string> Comparer { get; } = System.Collections.Generic.Comparer<string>.Create(Compare);

    public static int Compare(string? left, string? right)
        => string.Compare(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);

    public static bool Contains(string? value, string? query)
        => value != null && query != null
            && Normalize(value)!.Contains(Normalize(query)!, StringComparison.OrdinalIgnoreCase);

    private static string? Normalize(string? value)
    {
        if (value == null)
            return null;

        // Map only punctuation equivalents. Do not strip marks, fold alphabets,
        // swap decimal/grouping conventions, or mutate the original text.
        char[]? normalized = null;
        for (var i = 0; i < value.Length; i++)
        {
            var replacement = value[i] switch
            {
                '\uFF0E' or '\uFF61' or '\u3002' or '\uFE52' or '\u2024' or '\u066B' => '.',
                '\uFF0C' or '\uFE50' or '\uFE51' or '\u3001' or '\u060C' or '\u066C' => ',',
                _ => value[i],
            };
            if (replacement == value[i])
                continue;

            normalized ??= value.ToCharArray();
            normalized[i] = replacement;
        }

        return normalized == null ? value : new string(normalized);
    }
}
