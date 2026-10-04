using System.Collections.Generic;
using System.Text.Json.Serialization;
using XADatabase.Database;

namespace XADatabase.Models;

/// <summary>An owner-scoped teleport observation with its own capture time.</summary>
public sealed class AetheryteSnapshot
{
    public string CapturedUtc { get; set; } = string.Empty;
    public List<AetheryteEntry> Destinations { get; set; } = new();

    [JsonIgnore]
    public bool IsKnown => SnapshotTime.TryParseUtc(CapturedUtc, out var capturedUtc)
        && capturedUtc != System.DateTime.MinValue;

    public static AetheryteSnapshot Normalize(AetheryteSnapshot? snapshot)
    {
        if (snapshot == null || !snapshot.IsKnown)
            return new AetheryteSnapshot();

        SnapshotTime.TryParseUtc(snapshot.CapturedUtc, out var capturedUtc);
        return new AetheryteSnapshot
        {
            CapturedUtc = SnapshotTime.Format(capturedUtc),
            Destinations = AetheryteData.Normalize(snapshot.Destinations),
        };
    }
}
