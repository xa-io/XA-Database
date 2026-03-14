using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Component.GUI;
using XADatabase.Models;
using XADatabase.Services;

namespace XADatabase.Collectors;

public static class FreeCompanyCollector
{
    private static readonly string[] GrandCompanyNames =
    {
        "None",
        "Maelstrom",
        "Order of the Twin Adder",
        "Immortal Flames",
    };

    /// <summary>
    /// Rank names extracted from InfoProxyFreeCompany.Ranks — keyed by array index (matches member Sort byte).
    /// Set after Collect() runs successfully.
    /// </summary>
    public static Dictionary<int, string> LastCollectedRankNames { get; private set; } = new();

    /// <summary>
    /// Last collected FC name from addon text node [23] (#8).
    /// Supplements InfoProxyFreeCompany.Name which may return empty via SpanToString.
    /// </summary>
    public static string LastFcName { get; private set; } = string.Empty;

    /// <summary>
    /// Last collected FC tag from addon text node [22] (#9) — e.g. " «Ozma»".
    /// </summary>
    public static string LastFcTag { get; private set; } = string.Empty;

    /// <summary>
    /// Last collected FC points value (persists across refreshes).
    /// Read from FreeCompany addon text node when addon is open.
    /// </summary>
    public static int LastFcPoints { get; private set; }

    /// <summary>
    /// Last collected FC rank level (persists across refreshes).
    /// Read from FreeCompany addon text node [19] (#13) "Rank: 30".
    /// Supplements proxy->Rank as fallback when proxy returns 0.
    /// </summary>
    public static byte LastFcRank { get; private set; }

    /// <summary>
    /// Last collected estate description (persists across refreshes).
    /// E.g. "Plot 4, 9th Ward, Empyreum (Small)"
    /// </summary>
    public static string LastEstate { get; private set; } = string.Empty;

    public static unsafe FreeCompanyEntry? Collect()
    {
        var proxy = InfoProxyFreeCompany.Instance();
        if (proxy == null)
            return null;

        // No FC if ID is 0
        if (proxy->Id == 0)
            return null;

        var gcByte = (int)proxy->GrandCompany;
        var gcName = gcByte >= 0 && gcByte < GrandCompanyNames.Length ? GrandCompanyNames[gcByte] : "Unknown";

        // Read FC name and master from FixedSizeArray spans
        // SpanToString may return empty for Name — fallback to addon-read LastFcName
        var fcName = SpanToString(proxy->Name);
        if (string.IsNullOrEmpty(fcName) && !string.IsNullOrEmpty(LastFcName))
            fcName = LastFcName;
        var fcMaster = SpanToString(proxy->Master);

        // FC tag: prefer FcMemberCollector value, fallback to addon-read LastFcTag
        var fcTag = FcMemberCollector.LastCollectedFcTag;
        if (string.IsNullOrEmpty(fcTag) && !string.IsNullOrEmpty(LastFcTag))
            fcTag = LastFcTag;

        // Custom FC rank names: the Ranks accessor and raw pointer reads at 0x178 both
        // return garbage in our SDK version (Dalamud.NET.Sdk 14.0.2). The struct offsets
        // don't match the live game memory layout. Rank names will use Sort-based fallback
        // ("Master" for Sort 0, "Rank N" for others) until a reliable API is found.
        LastCollectedRankNames = new Dictionary<int, string>();

        return new FreeCompanyEntry
        {
            FcId = proxy->Id,
            Name = fcName,
            Tag = fcTag,
            Master = fcMaster,
            GrandCompany = (byte)proxy->GrandCompany,
            GrandCompanyName = gcName,
            OnlineMembers = proxy->OnlineMembers,
            TotalMembers = proxy->TotalMembers,
            HomeWorldId = proxy->HomeWorldId,
            FcPoints = LastFcPoints,
            Estate = LastEstate,
            Rank = proxy->Rank > 0 ? proxy->Rank : LastFcRank,
        };
    }

    /// <summary>
    /// Seed the static LastFcPoints / LastEstate from persisted DB values.
    /// Called on startup so that Collect() returns non-zero values even before the addon is opened.
    /// </summary>
    public static void SeedPersistedValues(int fcPoints, string estate, string fcName = "", string fcTag = "", byte fcRank = 0)
    {
        if (LastFcPoints == 0 && fcPoints > 0)
            LastFcPoints = fcPoints;
        if (string.IsNullOrEmpty(LastEstate) && !string.IsNullOrEmpty(estate))
            LastEstate = estate;
        if (string.IsNullOrEmpty(LastFcName) && !string.IsNullOrEmpty(fcName))
            LastFcName = fcName;
        if (string.IsNullOrEmpty(LastFcTag) && !string.IsNullOrEmpty(fcTag))
            LastFcTag = fcTag;
        if (LastFcRank == 0 && fcRank > 0)
            LastFcRank = fcRank;
    }

    public static void ClearPersistedValues()
    {
        LastCollectedRankNames = new Dictionary<int, string>();
        LastAddonMemberRanks = new Dictionary<string, string>();
        LastFcName = string.Empty;
        LastFcTag = string.Empty;
        LastFcPoints = 0;
        LastFcRank = 0;
        LastEstate = string.Empty;
    }

    /// <summary>
    /// Try reading FC points and estate from currently-open addons (via GetAddonByName).
    /// Safe to call anytime — returns silently if addons are not open.
    /// Used by Refresh+Save so data appears while the windows are still open.
    /// </summary>
    public static unsafe void TryCollectFromOpenAddons()
    {
        try
        {
            var fcAddon = AtkStage.Instance()->RaptureAtkUnitManager->GetAddonByName("FreeCompany");
            if (fcAddon != null && fcAddon->IsVisible)
            {
                Plugin.Log.Debug("[XA] FreeCompany addon is open — reading FC points.");
                CollectFromAddon((nint)fcAddon);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[XA] TryCollect FC error: {ex.Message}");
        }

        // Try reading rank names from FC member list
        TryCollectRankNamesFromAddon();
    }

    /// <summary>
    /// Read FC points and estate info from the FreeCompany addon text nodes.
    /// Call this when the FreeCompany addon is open (PostSetup or on demand).
    /// FreeCompany addon node [15] (#17) = FC points text (e.g. "40,070")
    /// </summary>
    public static unsafe void CollectFromAddon(nint addonPtr)
    {
        try
        {
            if (addonPtr == nint.Zero)
            {
                Plugin.Log.Debug("[XA] FreeCompany addon pointer is zero");
                return;
            }
            var addon = (AtkUnitBase*)addonPtr;
            Plugin.Log.Debug($"[XA] FreeCompany addon: IsVisible={addon->IsVisible}, NodeCount={addon->UldManager.NodeListCount}");

            var allText = AddonTextReader.ReadAllText(addon);

            foreach (var (path, nodeId, text) in allText)
            {
                // FC Name — node [23] (#8) e.g. "We Got Sucked"
                if (path == "[23]" && nodeId == 8 && !string.IsNullOrWhiteSpace(text))
                {
                    LastFcName = text.Trim();
                    Plugin.Log.Information($"[XA] FC name from addon: \"{LastFcName}\"");
                }

                // FC Tag — node [22] (#9) e.g. " «Ozma»"
                if (path == "[22]" && nodeId == 9 && !string.IsNullOrWhiteSpace(text))
                {
                    var tag = text.Trim().Replace("«", "").Replace("»", "").Trim();
                    if (tag.Length > 0)
                    {
                        LastFcTag = tag;
                        Plugin.Log.Information($"[XA] FC tag from addon: \"{LastFcTag}\"");
                    }
                }

                // FC Rank — node [19] (#13) "Rank: 30"
                if (path == "[19]" && nodeId == 13 && text.StartsWith("Rank:", StringComparison.OrdinalIgnoreCase))
                {
                    var rankStr = text.Replace("Rank:", "", StringComparison.OrdinalIgnoreCase).Trim();
                    if (byte.TryParse(rankStr, out var rank) && rank >= 1 && rank <= 30)
                    {
                        LastFcRank = rank;
                        Plugin.Log.Information($"[XA] FC rank from {path} (#{nodeId}): {rank} (raw: \"{text}\")");
                    }
                }

                // FC Points — numeric value > 100
                var cleaned = text.Replace(",", "").Replace(".", "").Trim();
                if (int.TryParse(cleaned, out var points) && points > 100)
                {
                    LastFcPoints = points;
                    Plugin.Log.Information($"[XA] FC points from {path} (#{nodeId}): {points} (raw: \"{text}\")");
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[XA] CollectFromAddon error: {ex.Message}");
        }
    }

    /// <summary>
    /// Last collected member name → rank name mapping from FreeCompanyMember addon.
    /// Keyed by member name (e.g. "Sprite Island" → "Master").
    /// </summary>
    public static Dictionary<string, string> LastAddonMemberRanks { get; private set; } = new();

    /// <summary>
    /// Read FC member rank names from FreeCompanyMember addon text nodes.
    /// This addon opens when the Members tab is clicked in the FreeCompany window.
    ///
    /// Node structure inside NodeList[32] (List Component):
    ///   [32]→[N] = ListItemRenderer per member (N=2,3,4...)
    ///   [32]→[N]→[13] (#23) = Rank name (e.g. "Master", "Member")
    ///   [32]→[N]→[17] (#19) = Location/Last seen (e.g. "Kugane", "6h", "27d")
    ///   [32]→[N]→[23] (#13) = Level (e.g. "90")
    ///   [32]→[N]→[28] (#8) = Character name (e.g. "Sprite Island")
    ///   [32]→[N]→[30]→[4] (#4) = Message (usually "-")
    /// </summary>
    public static unsafe void TryCollectRankNamesFromAddon()
    {
        try
        {
            var memberAddon = AtkStage.Instance()->RaptureAtkUnitManager->GetAddonByName("FreeCompanyMember");
            if (memberAddon == null || !memberAddon->IsVisible) return;

            var allText = AddonTextReader.ReadAllText(memberAddon);

            // Parse member entries: [32]→[N]→[28] (#8) = name, [32]→[N]→[13] (#23) = rank
            // Group by the [N] index to pair name+rank per member
            var memberData = new Dictionary<string, (string? Name, string? Rank)>(); // key = "[N]" index

            foreach (var (path, nodeId, text) in allText)
            {
                if (string.IsNullOrWhiteSpace(text)) continue;

                // Match pattern: [32]→[N]→[child]
                if (!path.StartsWith("[32]→[")) continue;
                var parts = path.Split('→');
                if (parts.Length < 3) continue;

                var memberIdx = parts[1]; // e.g. "[2]", "[3]", "[4]"
                var childPath = string.Join("→", parts.Skip(2)); // e.g. "[13]", "[28]", "[30]→[4]"

                if (!memberData.ContainsKey(memberIdx))
                    memberData[memberIdx] = (null, null);

                var entry = memberData[memberIdx];

                // [32]→[N]→[28] (#8) = Character name
                if (childPath == "[28]" && nodeId == 8)
                    entry.Name = text.Trim();

                // [32]→[N]→[13] (#23) = Rank name
                if (childPath == "[13]" && nodeId == 23)
                    entry.Rank = text.Trim();

                memberData[memberIdx] = entry;
            }

            // Build name → rank mapping (deduplication: skip entries without both name and rank)
            var nameToRank = new Dictionary<string, string>();
            foreach (var (idx, data) in memberData)
            {
                if (!string.IsNullOrEmpty(data.Name) && !string.IsNullOrEmpty(data.Rank))
                {
                    if (!nameToRank.ContainsKey(data.Name))
                    {
                        nameToRank[data.Name] = data.Rank;
                        Plugin.Log.Debug($"[XA] FCMember addon: \"{data.Name}\" → rank \"{data.Rank}\"");
                    }
                }
            }

            if (nameToRank.Count > 0)
            {
                LastAddonMemberRanks = nameToRank;
                Plugin.Log.Information($"[XA] FCMember addon ranks: {string.Join(", ", nameToRank.Select(kv => $"\"{kv.Key}\"=\"{kv.Value}\""))}");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[XA] TryCollectRankNames error: {ex.Message}");
        }
    }

    /// <summary>
    /// Read estate info from the HousingSignBoard addon text nodes.
    /// Node [50] (#21) = estate text (e.g. "Plot 4, 9th Ward, Empyreum (Small)")
    /// </summary>
    public static unsafe void CollectEstateFromAddon(nint addonPtr)
    {
        try
        {
            if (addonPtr == nint.Zero)
            {
                Plugin.Log.Debug("[XA] HousingSignBoard addon pointer is zero");
                return;
            }
            var addon = (AtkUnitBase*)addonPtr;
            Plugin.Log.Debug($"[XA] HousingSignBoard addon: IsVisible={addon->IsVisible}, NodeCount={addon->UldManager.NodeListCount}");

            var allText = AddonTextReader.ReadAllText(addon);

            // Search for text containing "Ward" and a number (the actual address, not the label)
            foreach (var (path, nodeId, text) in allText)
            {
                if (text.Contains("Ward") && (text.Contains("Plot") || text.Contains(",")))
                {
                    // Skip labels like "Address", "Plot Details" — actual address has commas
                    if (text.Contains(","))
                    {
                        LastEstate = text.Trim();
                        Plugin.Log.Information($"[XA] Estate from {path} (#{nodeId}): \"{LastEstate}\"");
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[XA] CollectEstateFromAddon error: {ex.Message}");
        }
    }

    private static string SpanToString(Span<byte> span)
    {
        int len = span.IndexOf((byte)0);
        if (len < 0) len = span.Length;
        if (len == 0) return string.Empty;
        return Encoding.UTF8.GetString(span.Slice(0, len));
    }
}
