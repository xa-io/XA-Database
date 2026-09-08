using System;
using System.Collections.Generic;
using XADatabase.Core.Storage;
using XADatabase.Data;
using XADatabase.Models;

namespace XADatabase.Database;

public class FreeCompanyRepository
{
    private readonly DatabaseService db;

    public FreeCompanyRepository(DatabaseService db) => this.db = db;

    public void Save(ulong contentId, FreeCompanyEntry fc)
    {
        var conn = db.GetConnection();
        var now = SnapshotTime.Format(DateTime.UtcNow);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO free_companies (fc_id, content_id, name, tag, master, rank, grand_company, grand_company_name, online_members, total_members, home_world_id, fc_points, estate, updated_utc)
            VALUES (@fcid, @cid, @name, @tag, @master, @rank, @gc, @gcname, @online, @total, @hwid, @pts, @estate, @now)
            ON CONFLICT(fc_id) DO UPDATE SET
                content_id = @cid,
                name = @name,
                tag = @tag,
                master = @master,
                rank = @rank,
                grand_company = @gc,
                grand_company_name = @gcname,
                online_members = @online,
                total_members = @total,
                home_world_id = @hwid,
                fc_points = CASE WHEN @pts > 0 THEN @pts ELSE free_companies.fc_points END,
                estate = CASE WHEN @estate != '' THEN @estate ELSE free_companies.estate END,
                updated_utc = @now";
        cmd.Parameters.AddTypedValue("@fcid", fc.FcId);
        cmd.Parameters.AddTypedValue("@cid", contentId);
        cmd.Parameters.AddTypedValue("@name", fc.Name);
        cmd.Parameters.AddTypedValue("@tag", fc.Tag);
        cmd.Parameters.AddTypedValue("@master", fc.Master);
        cmd.Parameters.AddTypedValue("@rank", (int)fc.Rank);
        cmd.Parameters.AddTypedValue("@gc", (int)fc.GrandCompany);
        cmd.Parameters.AddTypedValue("@gcname", fc.GrandCompanyName);
        cmd.Parameters.AddTypedValue("@online", (int)fc.OnlineMembers);
        cmd.Parameters.AddTypedValue("@total", (int)fc.TotalMembers);
        cmd.Parameters.AddTypedValue("@hwid", (int)fc.HomeWorldId);
        cmd.Parameters.AddTypedValue("@pts", fc.FcPoints);
        cmd.Parameters.AddTypedValue("@estate", HousingPlotSizeData.ApplySizeSuffix(fc.Estate ?? ""));
        cmd.Parameters.AddTypedValue("@now", now);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Get FC data for a character. Returns null if no FC data found.
    /// Multiple characters may be in the same FC — uses content_id to find the FC row.
    /// </summary>
    public FreeCompanyEntry? GetForCharacter(ulong contentId)
    {
        var conn = db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT fc_id, name, tag, master, rank, grand_company, grand_company_name, online_members, total_members, home_world_id, fc_points, estate
            FROM free_companies WHERE content_id = @cid LIMIT 1";
        cmd.Parameters.AddTypedValue("@cid", contentId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;

        return new FreeCompanyEntry
        {
            FcId = SqliteIdentity.Decode((long)reader["fc_id"]),
            Name = reader["name"].ToString() ?? "",
            Tag = reader["tag"].ToString() ?? "",
            Master = reader["master"].ToString() ?? "",
            Rank = (byte)Convert.ToInt32(reader["rank"]),
            GrandCompany = (byte)Convert.ToInt32(reader["grand_company"]),
            GrandCompanyName = reader["grand_company_name"].ToString() ?? "",
            OnlineMembers = (ushort)Convert.ToInt32(reader["online_members"]),
            TotalMembers = (ushort)Convert.ToInt32(reader["total_members"]),
            HomeWorldId = (ushort)Convert.ToInt32(reader["home_world_id"]),
            FcPoints = Convert.ToInt32(reader["fc_points"]),
            Estate = HousingPlotSizeData.ApplySizeSuffix(reader["estate"].ToString() ?? ""),
        };
    }

    /// <summary>
    /// Get all known FCs.
    /// </summary>
    public List<FreeCompanyEntry> GetAll()
    {
        var results = new List<FreeCompanyEntry>();
        var conn = db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT fc_id, name, tag, master, rank, grand_company, grand_company_name, online_members, total_members, home_world_id, fc_points, estate
            FROM free_companies ORDER BY name";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new FreeCompanyEntry
            {
                FcId = SqliteIdentity.Decode((long)reader["fc_id"]),
                Name = reader["name"].ToString() ?? "",
                Tag = reader["tag"].ToString() ?? "",
                Master = reader["master"].ToString() ?? "",
                Rank = (byte)Convert.ToInt32(reader["rank"]),
                GrandCompany = (byte)Convert.ToInt32(reader["grand_company"]),
                GrandCompanyName = reader["grand_company_name"].ToString() ?? "",
                OnlineMembers = (ushort)Convert.ToInt32(reader["online_members"]),
                TotalMembers = (ushort)Convert.ToInt32(reader["total_members"]),
                HomeWorldId = (ushort)Convert.ToInt32(reader["home_world_id"]),
                FcPoints = Convert.ToInt32(reader["fc_points"]),
                Estate = HousingPlotSizeData.ApplySizeSuffix(reader["estate"].ToString() ?? ""),
            });
        }
        return results;
    }
}
