using System.Collections.Generic;

namespace XADatabase.Models;

/// <summary>
/// Container for all FC workshop voyage data (airships + submarines).
/// </summary>
public class VoyageInfo
{
    public List<VoyageEntry> Airships { get; set; } = new();
    public List<VoyageEntry> Submarines { get; set; } = new();
}

/// <summary>
/// A single airship or submarine entry.
/// </summary>
public class VoyageEntry
{
    /// <summary>"Airship" or "Submarine"</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Slot index (0-3)</summary>
    public byte Slot { get; set; }

    /// <summary>Rank / level of the vessel</summary>
    public byte RankId { get; set; }

    /// <summary>Unix timestamp when vessel was registered</summary>
    public uint RegisterTime { get; set; }

    /// <summary>Unix timestamp when vessel returns from voyage (0 = idle)</summary>
    public uint ReturnTime { get; set; }

    public uint CurrentExp { get; set; }
    public uint NextLevelExp { get; set; }

    // Parts
    public ushort HullId { get; set; }
    public ushort SternId { get; set; }
    public ushort BowId { get; set; }
    public ushort BridgeId { get; set; }

    // Stats
    public short Surveillance { get; set; }
    public short Retrieval { get; set; }
    public short Speed { get; set; }
    public short Range { get; set; }
    public short Favor { get; set; }

    /// <summary>Build shortcode string (e.g. "SSUC", "WSUC++"). Computed from part IDs.</summary>
    public string BuildString { get; set; } = string.Empty;

    /// <summary>True if ReturnTime is in the future (vessel on voyage)</summary>
    public bool IsOnVoyage => ReturnTime > 0;
}

/// <summary>
/// Maps submarine/airship part row IDs (from SubmarinePart game sheet) to class shortcodes.
/// Row IDs are 1-indexed, grouped by class in sets of 4: Bow, Bridge, Hull, Stern (alphabetical).
/// Class 0=Shark(S), 1=Unkiu(U), 2=Whale(W), 3=Coelacanth(C), 4=Syldra(Y),
/// 5=ModShark(S+), 6=ModUnkiu(U+), 7=ModWhale(W+), 8=ModCoelacanth(C+), 9=ModSyldra(Y+).
/// </summary>
public static class VoyagePartLookup
{
    private static readonly string[] ClassShortcodes =
    {
        "S",    // 0: Shark       (rows  1- 4)
        "U",    // 1: Unkiu       (rows  5- 8)
        "W",    // 2: Whale       (rows  9-12)
        "C",    // 3: Coelacanth  (rows 13-16)
        "Y",    // 4: Syldra      (rows 17-20)
        "S+",   // 5: Mod Shark   (rows 21-24)
        "U+",   // 6: Mod Unkiu   (rows 25-28)
        "W+",   // 7: Mod Whale   (rows 29-32)
        "C+",   // 8: Mod Coel    (rows 33-36)
        "Y+",   // 9: Mod Syldra  (rows 37-40)
    };

    /// <summary>
    /// Convert a SubmarinePart row ID to its class shortcode.
    /// Returns "?" if the ID is out of range.
    /// </summary>
    public static string ShortenPartId(int rowId)
    {
        if (rowId < 1) return "?";
        int classIndex = (rowId - 1) / 4;
        if (classIndex >= ClassShortcodes.Length) return "?";
        return ClassShortcodes[classIndex];
    }

    /// <summary>
    /// Build the shortcode string from 4 part row IDs (hull, stern, bow, bridge).
    /// E.g. "SSUC", "W+S+U+C+".
    /// </summary>
    public static string GetBuildString(ushort hullId, ushort sternId, ushort bowId, ushort bridgeId)
    {
        var h = ShortenPartId(hullId);
        var s = ShortenPartId(sternId);
        var b = ShortenPartId(bowId);
        var br = ShortenPartId(bridgeId);
        return $"{h}{s}{b}{br}";
    }
}
