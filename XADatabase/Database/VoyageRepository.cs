using System;
using System.Collections.Generic;
using XADatabase.Models;

namespace XADatabase.Database;

public class VoyageRepository
{
    private readonly DatabaseService db;

    public VoyageRepository(DatabaseService db) => this.db = db;

    /// <summary>
    /// Save voyage data (airships + submarines) for an FC.
    /// Replaces all existing entries for the FC.
    /// </summary>
    public void Save(ulong fcId, VoyageInfo info)
    {
        var conn = db.GetConnection();
        var now = SnapshotTime.Format(DateTime.UtcNow);

        var ownTransaction = !db.HasActiveTransaction;
        var transaction = ownTransaction ? conn.BeginTransaction() : null;
        try
        {
            // Delete existing voyage entries for this FC
            using (var delCmd = conn.CreateCommand())
            {
                delCmd.CommandText = "DELETE FROM voyages WHERE fc_id = @fcid";
                delCmd.Parameters.AddTypedValue("@fcid", (long)fcId);
                delCmd.ExecuteNonQuery();
            }

            // Insert all entries
            var all = new List<VoyageEntry>();
            all.AddRange(info.Airships);
            all.AddRange(info.Submarines);

            foreach (var v in all)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO voyages (fc_id, type, slot, rank_id, register_time, return_time,
                        current_exp, next_level_exp, hull_id, stern_id, bow_id, bridge_id,
                        surveillance, retrieval, speed, range, favor, build_string, updated_utc)
                    VALUES (@fcid, @type, @slot, @rank, @reg, @ret,
                        @cexp, @nexp, @hull, @stern, @bow, @bridge,
                        @surv, @retr, @spd, @rng, @fav, @build, @now)";
                cmd.Parameters.AddTypedValue("@fcid", (long)fcId);
                cmd.Parameters.AddTypedValue("@type", v.Type);
                cmd.Parameters.AddTypedValue("@slot", (int)v.Slot);
                cmd.Parameters.AddTypedValue("@rank", (int)v.RankId);
                cmd.Parameters.AddTypedValue("@reg", (long)v.RegisterTime);
                cmd.Parameters.AddTypedValue("@ret", (long)v.ReturnTime);
                cmd.Parameters.AddTypedValue("@cexp", (long)v.CurrentExp);
                cmd.Parameters.AddTypedValue("@nexp", (long)v.NextLevelExp);
                cmd.Parameters.AddTypedValue("@hull", (int)v.HullId);
                cmd.Parameters.AddTypedValue("@stern", (int)v.SternId);
                cmd.Parameters.AddTypedValue("@bow", (int)v.BowId);
                cmd.Parameters.AddTypedValue("@bridge", (int)v.BridgeId);
                cmd.Parameters.AddTypedValue("@surv", (int)v.Surveillance);
                cmd.Parameters.AddTypedValue("@retr", (int)v.Retrieval);
                cmd.Parameters.AddTypedValue("@spd", (int)v.Speed);
                cmd.Parameters.AddTypedValue("@rng", (int)v.Range);
                cmd.Parameters.AddTypedValue("@fav", (int)v.Favor);
                cmd.Parameters.AddTypedValue("@build", v.BuildString ?? "");
                cmd.Parameters.AddTypedValue("@now", now);
                cmd.ExecuteNonQuery();
            }

            if (ownTransaction) transaction?.Commit();
        }
        catch
        {
            if (ownTransaction) transaction?.Rollback();
            throw;
        }
    }

    /// <summary>
    /// Load voyage data for an FC. Returns null if no data found.
    /// </summary>
    public VoyageInfo? GetForFc(ulong fcId)
    {
        var conn = db.GetConnection();
        var info = new VoyageInfo();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT type, slot, rank_id, register_time, return_time,
                   current_exp, next_level_exp, hull_id, stern_id, bow_id, bridge_id,
                   surveillance, retrieval, speed, range, favor, build_string
            FROM voyages WHERE fc_id = @fcid ORDER BY type, slot";
        cmd.Parameters.AddTypedValue("@fcid", (long)fcId);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var entry = new VoyageEntry
            {
                Type = reader["type"].ToString() ?? "",
                Slot = (byte)Convert.ToInt32(reader["slot"]),
                RankId = (byte)Convert.ToInt32(reader["rank_id"]),
                RegisterTime = (uint)Convert.ToInt64(reader["register_time"]),
                ReturnTime = (uint)Convert.ToInt64(reader["return_time"]),
                CurrentExp = (uint)Convert.ToInt64(reader["current_exp"]),
                NextLevelExp = (uint)Convert.ToInt64(reader["next_level_exp"]),
                HullId = (ushort)Convert.ToInt32(reader["hull_id"]),
                SternId = (ushort)Convert.ToInt32(reader["stern_id"]),
                BowId = (ushort)Convert.ToInt32(reader["bow_id"]),
                BridgeId = (ushort)Convert.ToInt32(reader["bridge_id"]),
                Surveillance = (short)Convert.ToInt32(reader["surveillance"]),
                Retrieval = (short)Convert.ToInt32(reader["retrieval"]),
                Speed = (short)Convert.ToInt32(reader["speed"]),
                Range = (short)Convert.ToInt32(reader["range"]),
                Favor = (short)Convert.ToInt32(reader["favor"]),
                BuildString = reader["build_string"]?.ToString() ?? "",
            };

            if (entry.Type == "Airship")
                info.Airships.Add(entry);
            else
                info.Submarines.Add(entry);
        }

        if (info.Airships.Count == 0 && info.Submarines.Count == 0)
            return null;

        return info;
    }
}
