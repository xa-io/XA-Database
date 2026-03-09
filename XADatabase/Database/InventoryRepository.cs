using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using XADatabase.Models;

namespace XADatabase.Database;

public class InventoryRepository
{
    private readonly DatabaseService db;

    public InventoryRepository(DatabaseService db)
    {
        this.db = db;
    }

    public void SaveSnapshot(ulong contentId, List<InventorySummary> inventories)
    {
        var conn = db.GetConnection();
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        var ownTransaction = !db.HasActiveTransaction;
        var transaction = ownTransaction ? conn.BeginTransaction() : null;
        try
        {
            foreach (var inv in inventories)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO inventory_summaries (content_id, container_name, used_slots, total_slots, updated_utc)
                    VALUES (@cid, @name, @used, @total, @now)
                    ON CONFLICT(content_id, container_name) DO UPDATE SET
                        used_slots = @used,
                        total_slots = @total,
                        updated_utc = @now";
                cmd.Parameters.AddWithValue("@cid", (long)contentId);
                cmd.Parameters.AddWithValue("@name", inv.Name);
                cmd.Parameters.AddWithValue("@used", inv.UsedSlots);
                cmd.Parameters.AddWithValue("@total", inv.TotalSlots);
                cmd.Parameters.AddWithValue("@now", now);
                cmd.ExecuteNonQuery();
            }

            if (ownTransaction) transaction?.Commit();
        }
        catch (Exception ex)
        {
            if (ownTransaction) transaction?.Rollback();
            Plugin.Log.Error($"[XA] Failed to save inventory snapshot: {ex}");
        }
    }

    public List<InventorySummary> GetLatest(ulong contentId)
    {
        var results = new List<InventorySummary>();
        var conn = db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT container_name, used_slots, total_slots
            FROM inventory_summaries
            WHERE content_id = @cid
            ORDER BY container_name";
        cmd.Parameters.AddWithValue("@cid", (long)contentId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new InventorySummary
            {
                Name = reader["container_name"].ToString() ?? "",
                UsedSlots = Convert.ToInt32(reader["used_slots"]),
                TotalSlots = Convert.ToInt32(reader["total_slots"]),
            });
        }
        return results;
    }
}
