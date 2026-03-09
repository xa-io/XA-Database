using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;
using XADatabase.Models;

namespace XADatabase.Collectors;

/// <summary>
/// Collects airship and submarine data from the FC workshop.
/// Data is only available when the player is inside the workshop territory.
/// </summary>
public static class VoyageCollector
{
    private const int MaxAirships = 4;
    private const int MaxSubmarines = 4;

    /// <summary>
    /// Static cache: last successful voyage read persists for the session.
    /// Submarine data only exists while the panel is open, so we cache it.
    /// </summary>
    private static VoyageInfo? sessionCache;

    /// <summary>
    /// Collect voyage data. Returns null if not in workshop or data unavailable.
    /// Uses session cache if fresh data is empty but we're in the workshop.
    /// </summary>
    public static unsafe VoyageInfo? Collect()
    {
        try
        {
            var hm = HousingManager.Instance();
            if (hm == null)
                return null;

            if (!hm->IsInWorkshop())
                return null;

            var ws = hm->WorkshopTerritory;
            if (ws == null)
                return null;

            var info = new VoyageInfo();

            // ── Airships ──
            try
            {
                var airshipData = &ws->Airship;
                var count = Math.Min((int)airshipData->AirshipCount, MaxAirships);
                if (count > 0)
                {
                    var span = airshipData->Data;
                    for (int i = 0; i < count && i < span.Length; i++)
                    {
                        try
                        {
                            ref var a = ref span[i];
                            if (a.RegisterTime == 0) continue;

                            var entry = new VoyageEntry
                            {
                                Type = "Airship",
                                Slot = (byte)i,
                                RankId = a.RankId,
                                RegisterTime = a.RegisterTime,
                                ReturnTime = a.ReturnTime,
                                CurrentExp = a.CurrentExp,
                                NextLevelExp = a.NextLevelExp,
                                HullId = a.HullId,
                                SternId = a.SternId,
                                BowId = a.BowId,
                                BridgeId = a.BridgeId,
                                Surveillance = (short)a.Surveillance,
                                Retrieval = (short)a.Retrieval,
                                Speed = (short)a.Speed,
                                Range = (short)a.Range,
                                Favor = (short)a.Favor,
                            };
                            entry.BuildString = VoyagePartLookup.GetBuildString(entry.HullId, entry.SternId, entry.BowId, entry.BridgeId);
                            Plugin.Log.Debug($"[XA] Airship #{i}: Hull={entry.HullId} Stern={entry.SternId} Bow={entry.BowId} Bridge={entry.BridgeId} => {entry.BuildString}");
                            info.Airships.Add(entry);
                        }
                        catch (Exception ex)
                        {
                            Plugin.Log.Error($"[XA] Error reading airship slot {i}: {ex.Message}");
                        }
                    }
                }

                Plugin.Log.Debug($"[XA] VoyageCollector: {info.Airships.Count} airship(s)");
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[XA] Error reading airship data: {ex.Message}");
            }

            // ── Submarines ──
            try
            {
                var subData = &ws->Submersible;
                var span = subData->Data;
                var maxSubs = Math.Min(MaxSubmarines, span.Length);
                for (int i = 0; i < maxSubs; i++)
                {
                    try
                    {
                        ref var s = ref span[i];
                        if (s.RegisterTime == 0) continue;

                        var entry = new VoyageEntry
                        {
                            Type = "Submarine",
                            Slot = (byte)i,
                            RankId = s.RankId,
                            RegisterTime = s.RegisterTime,
                            ReturnTime = s.ReturnTime,
                            CurrentExp = s.CurrentExp,
                            NextLevelExp = s.NextLevelExp,
                            HullId = s.HullId,
                            SternId = s.SternId,
                            BowId = s.BowId,
                            BridgeId = s.BridgeId,
                            Surveillance = (short)s.SurveillanceBase,
                            Retrieval = (short)s.RetrievalBase,
                            Speed = (short)s.SpeedBase,
                            Range = (short)s.RangeBase,
                            Favor = (short)s.FavorBase,
                        };
                        entry.BuildString = VoyagePartLookup.GetBuildString(entry.HullId, entry.SternId, entry.BowId, entry.BridgeId);
                        Plugin.Log.Debug($"[XA] Sub #{i}: Hull={entry.HullId} Stern={entry.SternId} Bow={entry.BowId} Bridge={entry.BridgeId} => {entry.BuildString}");
                        info.Submarines.Add(entry);
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.Error($"[XA] Error reading submarine slot {i}: {ex.Message}");
                    }
                }

                Plugin.Log.Debug($"[XA] VoyageCollector: {info.Submarines.Count} submarine(s)");
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[XA] Error reading submarine data: {ex.Message}");
            }

            // Fresh data found — update session cache
            if (info.Airships.Count > 0 || info.Submarines.Count > 0)
            {
                sessionCache = info;
                Plugin.Log.Debug($"[XA] VoyageCollector: cached {info.Airships.Count} airship(s), {info.Submarines.Count} sub(s)");
                return info;
            }

            // No fresh data but we're in workshop — return session cache if available
            if (sessionCache != null)
            {
                Plugin.Log.Debug("[XA] VoyageCollector: panel not open, returning session cache");
                return sessionCache;
            }

            return null;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[XA] VoyageCollector error: {ex}");
            return null;
        }
    }
}
