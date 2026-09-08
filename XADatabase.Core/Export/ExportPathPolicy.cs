using System.Globalization;

namespace XADatabase.Core.Export;

public static class ExportPathPolicy
{
    public static string BuildPath(string exportDirectory, string characterName, string suffix, DateTime timestampUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exportDirectory);

        var root = Path.GetFullPath(exportDirectory);
        var rootWithSeparator = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var suffixFileName = Path.GetFileName(suffix ?? string.Empty);
        var extension = SanitizeExtension(Path.GetExtension(suffixFileName));
        var suffixName = SanitizeComponent(Path.GetFileNameWithoutExtension(suffixFileName), "export");
        var safeCharacterName = SanitizeComponent(characterName, "character").Replace(' ', '_').Trim('_');
        if (safeCharacterName.Length == 0)
            safeCharacterName = "character";

        var timestamp = timestampUtc.ToUniversalTime().ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
        var candidate = Path.GetFullPath(Path.Combine(root, $"{safeCharacterName}_{suffixName}_{timestamp}{extension}"));
        if (!candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The resolved export path escaped the export directory.");

        return candidate;
    }

    public static string SanitizeComponent(string? value, string fallback)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var chars = (value ?? string.Empty)
            .Select(character => invalid.Contains(character) || char.IsControl(character) ? '_' : character)
            .ToArray();
        var sanitized = new string(chars).Trim().Trim('.', '_');
        return sanitized.Length == 0 ? fallback : sanitized;
    }

    private static string SanitizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return string.Empty;

        return extension.Length <= 16
            && extension[0] == '.'
            && extension.Skip(1).All(char.IsLetterOrDigit)
                ? extension.ToLowerInvariant()
                : string.Empty;
    }
}
