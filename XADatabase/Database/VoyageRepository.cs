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
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        var ownTransaction = !db.HasActiveTransaction;
        var transaction = ownTransaction ? conn.BeginTransaction() : null;
        try
        {
            // Delete existing voyage entries for this FC
            using (var delCmd = conn.CreateCommand())
            {
                delCmd.CommandText = "DELETE FROM voyages WHERE fc_id = @fcid";
                delCmd.Parameters.AddWithValue("@fcid", (long)fcId);
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
                cmd.Parameters.AddWithValue("@fcid", (long)fcId);
                cmd.Parameters.AddWithValue("@type", v.Type);
                cmd.Parameters.AddWithValue("@slot", (int)v.Slot);
                cmd.Parameters.AddWithValue("@rank", (int)v.RankId);
                cmd.Parameters.AddWithValue("@reg", (long)v.RegisterTime);
                cmd.Parameters.AddWithValue("@ret", (long)v.ReturnTime);
                cmd.Parameters.AddWithValue("@cexp", (long)v.CurrentExp);
                cmd.Parameters.AddWithValue("@nexp", (long)v.NextLevelExp);
                cmd.Parameters.AddWithValue("@hull", (int)v.HullId);
                cmd.Parameters.AddWithValue("@stern", (int)v.SternId);
                cmd.Parameters.AddWithValue("@bow", (int)v.BowId);
                cmd.Parameters.AddWithValue("@bridge", (int)v.BridgeId);
                cmd.Parameters.AddWithValue("@surv", (int)v.Surveillance);
                cmd.Parameters.AddWithValue("@retr", (int)v.Retrieval);
                cmd.Parameters.AddWithValue("@spd", (int)v.Speed);
                cmd.Parameters.AddWithValue("@rng", (int)v.Range);
                cmd.Parameters.AddWithValue("@fav", (int)v.Favor);
                cmd.Parameters.AddWithValue("@build", v.BuildString ?? "");
                cmd.Parameters.AddWithValue("@now", now);
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
        cmd.Parameters.AddWithValue("@fcid", (long)fcId);

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
