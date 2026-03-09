using System;
using System.Collections.Generic;
using XADatabase.Models;

namespace XADatabase.Database;

public class SquadronRepository
{
    private readonly DatabaseService db;

    public SquadronRepository(DatabaseService db) => this.db = db;

    /// <summary>
    /// Save squadron info + members for a character.
    /// </summary>
    public void Save(ulong contentId, SquadronInfo info)
    {
        var conn = db.GetConnection();
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        var ownTransaction = !db.HasActiveTransaction;
        var transaction = ownTransaction ? conn.BeginTransaction() : null;
        try
        {
            // Upsert squadron_info
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT INTO squadron_info (content_id, progress, current_expedition, expedition_name,
                        bonus_physical, bonus_mental, bonus_tactical, member_count, updated_utc)
                    VALUES (@cid, @prog, @exp, @expname, @phys, @ment, @tact, @mc, @now)
                    ON CONFLICT(content_id) DO UPDATE SET
                        progress = @prog, current_expedition = @exp, expedition_name = @expname,
                        bonus_physical = @phys, bonus_mental = @ment, bonus_tactical = @tact,
                        member_count = @mc, updated_utc = @now";
                cmd.Parameters.AddWithValue("@cid", (long)contentId);
                cmd.Parameters.AddWithValue("@prog", (int)info.Progress);
                cmd.Parameters.AddWithValue("@exp", (int)info.CurrentExpedition);
                cmd.Parameters.AddWithValue("@expname", info.ExpeditionName);
                cmd.Parameters.AddWithValue("@phys", (int)info.BonusPhysical);
                cmd.Parameters.AddWithValue("@ment", (int)info.BonusMental);
                cmd.Parameters.AddWithValue("@tact", (int)info.BonusTactical);
                cmd.Parameters.AddWithValue("@mc", (int)info.MemberCount);
                cmd.Parameters.AddWithValue("@now", now);
                cmd.ExecuteNonQuery();
            }

            // Replace squadron members
            using (var delCmd = conn.CreateCommand())
            {
                delCmd.CommandText = "DELETE FROM squadron_members WHERE content_id = @cid";
                delCmd.Parameters.AddWithValue("@cid", (long)contentId);
                delCmd.ExecuteNonQuery();
            }

            foreach (var m in info.Members)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO squadron_members (content_id, enpc_id, name, race, sex, class_job, class_job_name,
                        level, experience, active_trait, inactive_trait,
                        mastery_independent, mastery_offensive, mastery_defensive, mastery_balanced, updated_utc)
                    VALUES (@cid, @enpc, @name, @race, @sex, @cj, @cjname,
                        @lvl, @exp, @at, @it, @mi, @mo, @md, @mb, @now)";
                cmd.Parameters.AddWithValue("@cid", (long)contentId);
                cmd.Parameters.AddWithValue("@enpc", (long)m.ENpcResidentId);
                cmd.Parameters.AddWithValue("@name", m.Name);
                cmd.Parameters.AddWithValue("@race", (int)m.Race);
                cmd.Parameters.AddWithValue("@sex", (int)m.Sex);
                cmd.Parameters.AddWithValue("@cj", (int)m.ClassJob);
                cmd.Parameters.AddWithValue("@cjname", m.ClassJobName);
                cmd.Parameters.AddWithValue("@lvl", (int)m.Level);
                cmd.Parameters.AddWithValue("@exp", (long)m.Experience);
                cmd.Parameters.AddWithValue("@at", (int)m.ActiveTrait);
                cmd.Parameters.AddWithValue("@it", (int)m.InactiveTrait);
                cmd.Parameters.AddWithValue("@mi", (int)m.MasteryIndependent);
                cmd.Parameters.AddWithValue("@mo", (int)m.MasteryOffensive);
                cmd.Parameters.AddWithValue("@md", (int)m.MasteryDefensive);
                cmd.Parameters.AddWithValue("@mb", (int)m.MasteryBalanced);
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
    /// Load squadron info for a character. Returns null if not found.
    /// </summary>
    public SquadronInfo? GetForCharacter(ulong contentId)
    {
        var conn = db.GetConnection();

        // Load info
        SquadronInfo? info = null;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT progress, current_expedition, expedition_name, bonus_physical, bonus_mental, bonus_tactical, member_count
                FROM squadron_info WHERE content_id = @cid LIMIT 1";
            cmd.Parameters.AddWithValue("@cid", (long)contentId);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                info = new SquadronInfo
                {
                    Progress = (byte)Convert.ToInt32(reader["progress"]),
                    CurrentExpedition = (ushort)Convert.ToInt32(reader["current_expedition"]),
                    ExpeditionName = reader["expedition_name"].ToString() ?? "",
                    BonusPhysical = (ushort)Convert.ToInt32(reader["bonus_physical"]),
                    BonusMental = (ushort)Convert.ToInt32(reader["bonus_mental"]),
                    BonusTactical = (ushort)Convert.ToInt32(reader["bonus_tactical"]),
                    MemberCount = (byte)Convert.ToInt32(reader["member_count"]),
                };
            }
        }

        if (info == null)
            return null;

        // Load members
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT enpc_id, name, race, sex, class_job, class_job_name, level, experience,
                       active_trait, inactive_trait, mastery_independent, mastery_offensive, mastery_defensive, mastery_balanced
                FROM squadron_members WHERE content_id = @cid ORDER BY level DESC, name";
            cmd.Parameters.AddWithValue("@cid", (long)contentId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                info.Members.Add(new SquadronMemberEntry
                {
                    ENpcResidentId = (uint)Convert.ToInt64(reader["enpc_id"]),
                    Name = reader["name"].ToString() ?? "",
                    Race = (byte)Convert.ToInt32(reader["race"]),
                    Sex = (byte)Convert.ToInt32(reader["sex"]),
                    ClassJob = (byte)Convert.ToInt32(reader["class_job"]),
                    ClassJobName = reader["class_job_name"].ToString() ?? "",
                    Level = (byte)Convert.ToInt32(reader["level"]),
                    Experience = (uint)Convert.ToInt64(reader["experience"]),
                    ActiveTrait = (byte)Convert.ToInt32(reader["active_trait"]),
                    InactiveTrait = (byte)Convert.ToInt32(reader["inactive_trait"]),
                    MasteryIndependent = (byte)Convert.ToInt32(reader["mastery_independent"]),
                    MasteryOffensive = (byte)Convert.ToInt32(reader["mastery_offensive"]),
                    MasteryDefensive = (byte)Convert.ToInt32(reader["mastery_defensive"]),
                    MasteryBalanced = (byte)Convert.ToInt32(reader["mastery_balanced"]),
                });
            }
        }

        return info;
    }
}
