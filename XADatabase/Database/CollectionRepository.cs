using System;
using System.Collections.Generic;
using XADatabase.Models;

namespace XADatabase.Database;

public class CollectionRepository
{
    private readonly DatabaseService db;

    public CollectionRepository(DatabaseService db) => this.db = db;

    public void SaveSnapshot(ulong contentId, List<CollectionSummary> collections)
    {
        var conn = db.GetConnection();
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        foreach (var c in collections)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO collection_summaries (content_id, category, unlocked, total, updated_utc)
                VALUES (@cid, @cat, @unlocked, @total, @now)
                ON CONFLICT(content_id, category) DO UPDATE SET
                    unlocked = @unlocked,
                    total = @total,
                    updated_utc = @now";
            cmd.Parameters.AddWithValue("@cid", (long)contentId);
            cmd.Parameters.AddWithValue("@cat", c.Category);
            cmd.Parameters.AddWithValue("@unlocked", c.Unlocked);
            cmd.Parameters.AddWithValue("@total", c.Total);
            cmd.Parameters.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
    }

    public List<CollectionSummary> GetLatest(ulong contentId)
    {
        var results = new List<CollectionSummary>();
        var conn = db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT category, unlocked, total
            FROM collection_summaries WHERE content_id = @cid ORDER BY category";
        cmd.Parameters.AddWithValue("@cid", (long)contentId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new CollectionSummary
            {
                Category = reader["category"].ToString() ?? "",
                Unlocked = Convert.ToInt32(reader["unlocked"]),
                Total = Convert.ToInt32(reader["total"]),
            });
        }
        return results;
    }

    // ── Active Quests ──

    public void SaveQuests(ulong contentId, List<ActiveQuestEntry> quests)
    {
        var conn = db.GetConnection();
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        // Delete old quests for this character, then insert fresh
        using (var delCmd = conn.CreateCommand())
        {
            delCmd.CommandText = "DELETE FROM active_quests WHERE content_id = @cid";
            delCmd.Parameters.AddWithValue("@cid", (long)contentId);
            delCmd.ExecuteNonQuery();
        }

        foreach (var q in quests)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO active_quests (content_id, quest_id, name, sequence, updated_utc)
                VALUES (@cid, @qid, @name, @seq, @now)";
            cmd.Parameters.AddWithValue("@cid", (long)contentId);
            cmd.Parameters.AddWithValue("@qid", (int)q.QuestId);
            cmd.Parameters.AddWithValue("@name", q.Name);
            cmd.Parameters.AddWithValue("@seq", (int)q.Sequence);
            cmd.Parameters.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
    }

    public List<ActiveQuestEntry> GetQuests(ulong contentId)
    {
        var results = new List<ActiveQuestEntry>();
        var conn = db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT quest_id, name, sequence
            FROM active_quests WHERE content_id = @cid ORDER BY name";
        cmd.Parameters.AddWithValue("@cid", (long)contentId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new ActiveQuestEntry
            {
                QuestId = (ushort)Convert.ToInt32(reader["quest_id"]),
                Name = reader["name"].ToString() ?? "",
                Sequence = (byte)Convert.ToInt32(reader["sequence"]),
            });
        }
        return results;
    }

    // ── MSQ Milestones ──

    public void SaveMsqMilestones(ulong contentId, List<MsqMilestoneEntry> milestones)
    {
        var conn = db.GetConnection();
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        // Delete old milestones for this character, then insert fresh.
        // Prevents stale rows from previous milestone lists inflating the count.
        using (var delCmd = conn.CreateCommand())
        {
            delCmd.CommandText = "DELETE FROM msq_milestones WHERE content_id = @cid";
            delCmd.Parameters.AddWithValue("@cid", (long)contentId);
            delCmd.ExecuteNonQuery();
        }

        foreach (var m in milestones)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO msq_milestones (content_id, quest_row_id, label, expansion, is_complete, updated_utc)
                VALUES (@cid, @qrid, @label, @exp, @done, @now)";
            cmd.Parameters.AddWithValue("@cid", (long)contentId);
            cmd.Parameters.AddWithValue("@qrid", (long)m.QuestRowId);
            cmd.Parameters.AddWithValue("@label", m.Label);
            cmd.Parameters.AddWithValue("@exp", m.Expansion);
            cmd.Parameters.AddWithValue("@done", m.IsComplete ? 1 : 0);
            cmd.Parameters.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
    }

    public List<MsqMilestoneEntry> GetMsqMilestones(ulong contentId)
    {
        var results = new List<MsqMilestoneEntry>();
        var conn = db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT quest_row_id, label, expansion, is_complete
            FROM msq_milestones WHERE content_id = @cid ORDER BY quest_row_id";
        cmd.Parameters.AddWithValue("@cid", (long)contentId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new MsqMilestoneEntry
            {
                QuestRowId = (uint)Convert.ToInt64(reader["quest_row_id"]),
                Label = reader["label"].ToString() ?? "",
                Expansion = reader["expansion"].ToString() ?? "",
                IsComplete = Convert.ToInt32(reader["is_complete"]) != 0,
            });
        }
        return results;
    }
}
