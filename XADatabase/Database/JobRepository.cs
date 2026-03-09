using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using XADatabase.Models;

namespace XADatabase.Database;

public class JobRepository
{
    private readonly DatabaseService db;

    public JobRepository(DatabaseService db)
    {
        this.db = db;
    }

    public void SaveSnapshot(ulong contentId, List<JobEntry> jobs)
    {
        var conn = db.GetConnection();
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        var ownTransaction = !db.HasActiveTransaction;
        var transaction = ownTransaction ? conn.BeginTransaction() : null;
        try
        {
            foreach (var job in jobs)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO job_levels (content_id, abbreviation, name, category, level, is_unlocked, updated_utc)
                    VALUES (@cid, @abbr, @name, @cat, @level, @unlocked, @now)
                    ON CONFLICT(content_id, abbreviation) DO UPDATE SET
                        name = @name,
                        category = @cat,
                        level = @level,
                        is_unlocked = @unlocked,
                        updated_utc = @now";
                cmd.Parameters.AddWithValue("@cid", (long)contentId);
                cmd.Parameters.AddWithValue("@abbr", job.Abbreviation);
                cmd.Parameters.AddWithValue("@name", job.Name);
                cmd.Parameters.AddWithValue("@cat", job.Category);
                cmd.Parameters.AddWithValue("@level", job.Level);
                cmd.Parameters.AddWithValue("@unlocked", job.IsUnlocked ? 1 : 0);
                cmd.Parameters.AddWithValue("@now", now);
                cmd.ExecuteNonQuery();
            }

            if (ownTransaction) transaction?.Commit();
        }
        catch (Exception ex)
        {
            if (ownTransaction) transaction?.Rollback();
            Plugin.Log.Error($"[XA] Failed to save job snapshot: {ex}");
        }
    }

    public List<JobEntry> GetLatest(ulong contentId)
    {
        var results = new List<JobEntry>();
        var conn = db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT abbreviation, name, category, level, is_unlocked
            FROM job_levels
            WHERE content_id = @cid
            ORDER BY category, abbreviation";
        cmd.Parameters.AddWithValue("@cid", (long)contentId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new JobEntry
            {
                Abbreviation = reader["abbreviation"].ToString() ?? "",
                Name = reader["name"].ToString() ?? "",
                Category = reader["category"].ToString() ?? "",
                Level = Convert.ToInt32(reader["level"]),
                IsUnlocked = Convert.ToInt32(reader["is_unlocked"]) == 1,
            });
        }
        return results;
    }
}
