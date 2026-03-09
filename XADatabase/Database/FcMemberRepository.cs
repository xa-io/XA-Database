using System;
using System.Collections.Generic;
using XADatabase.Models;

namespace XADatabase.Database;

public class FcMemberRepository
{
    private readonly DatabaseService db;

    public FcMemberRepository(DatabaseService db) => this.db = db;

    /// <summary>
    /// Save FC member list — replaces all members for the given FC.
    /// </summary>
    public void SaveMembers(ulong fcId, List<FcMemberEntry> members)
    {
        var conn = db.GetConnection();
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        var ownTransaction = !db.HasActiveTransaction;
        var transaction = ownTransaction ? conn.BeginTransaction() : null;
        try
        {
            // Delete existing members for this FC
            using (var delCmd = conn.CreateCommand())
            {
                delCmd.CommandText = "DELETE FROM fc_members WHERE fc_id = @fcid";
                delCmd.Parameters.AddWithValue("@fcid", (long)fcId);
                delCmd.ExecuteNonQuery();
            }

            // Insert all current members
            foreach (var m in members)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO fc_members (fc_id, member_content_id, name, job, job_name, online_status,
                        current_world, current_world_name, home_world, home_world_name, grand_company, rank_sort, rank_name, updated_utc)
                    VALUES (@fcid, @mcid, @name, @job, @jobname, @online,
                        @cw, @cwname, @hw, @hwname, @gc, @rank, @rankname, @now)";
                cmd.Parameters.AddWithValue("@fcid", (long)fcId);
                cmd.Parameters.AddWithValue("@mcid", (long)m.ContentId);
                cmd.Parameters.AddWithValue("@name", m.Name);
                cmd.Parameters.AddWithValue("@job", (int)m.Job);
                cmd.Parameters.AddWithValue("@jobname", m.JobName);
                cmd.Parameters.AddWithValue("@online", (int)m.OnlineStatus);
                cmd.Parameters.AddWithValue("@cw", (int)m.CurrentWorld);
                cmd.Parameters.AddWithValue("@cwname", m.CurrentWorldName);
                cmd.Parameters.AddWithValue("@hw", (int)m.HomeWorld);
                cmd.Parameters.AddWithValue("@hwname", m.HomeWorldName);
                cmd.Parameters.AddWithValue("@gc", (int)m.GrandCompany);
                cmd.Parameters.AddWithValue("@rank", (int)m.RankSort);
                cmd.Parameters.AddWithValue("@rankname", m.RankName);
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
    /// Get all members for an FC.
    /// </summary>
    public List<FcMemberEntry> GetMembers(ulong fcId)
    {
        var results = new List<FcMemberEntry>();
        var conn = db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT member_content_id, name, job, job_name, online_status,
                   current_world, current_world_name, home_world, home_world_name, grand_company, rank_sort, rank_name
            FROM fc_members WHERE fc_id = @fcid ORDER BY rank_sort, name";
        cmd.Parameters.AddWithValue("@fcid", (long)fcId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new FcMemberEntry
            {
                ContentId = (ulong)(long)reader["member_content_id"],
                Name = reader["name"].ToString() ?? "",
                Job = (byte)Convert.ToInt32(reader["job"]),
                JobName = reader["job_name"].ToString() ?? "",
                OnlineStatus = (byte)Convert.ToInt32(reader["online_status"]),
                CurrentWorld = (ushort)Convert.ToInt32(reader["current_world"]),
                CurrentWorldName = reader["current_world_name"].ToString() ?? "",
                HomeWorld = (ushort)Convert.ToInt32(reader["home_world"]),
                HomeWorldName = reader["home_world_name"].ToString() ?? "",
                GrandCompany = (byte)Convert.ToInt32(reader["grand_company"]),
                RankSort = (byte)Convert.ToInt32(reader["rank_sort"]),
                RankName = reader["rank_name"].ToString() ?? "",
            });
        }
        return results;
    }
}
