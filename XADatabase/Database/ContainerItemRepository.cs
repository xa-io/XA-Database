using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using XADatabase.Models;

namespace XADatabase.Database;

public class ContainerItemRepository
{
    private readonly DatabaseService db;

    public ContainerItemRepository(DatabaseService db)
    {
        this.db = db;
    }

    public void SaveSnapshot(ulong contentId, List<ContainerItemEntry> items)
    {
        var conn = db.GetConnection();
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        var ownTransaction = !db.HasActiveTransaction;
        var transaction = ownTransaction ? conn.BeginTransaction() : null;
        try
        {
            // Clear old items for this character first (full snapshot replace)
            using var deleteCmd = conn.CreateCommand();
            deleteCmd.CommandText = "DELETE FROM container_items WHERE content_id = @cid";
            deleteCmd.Parameters.AddWithValue("@cid", (long)contentId);
            deleteCmd.ExecuteNonQuery();

            foreach (var item in items)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO container_items (content_id, container_name, container_type, slot_index, item_id, item_name, quantity, is_hq, updated_utc)
                    VALUES (@cid, @cname, @ctype, @slot, @itemid, @iname, @qty, @hq, @now)";
                cmd.Parameters.AddWithValue("@cid", (long)contentId);
                cmd.Parameters.AddWithValue("@cname", item.ContainerName);
                cmd.Parameters.AddWithValue("@ctype", item.ContainerType);
                cmd.Parameters.AddWithValue("@slot", item.SlotIndex);
                cmd.Parameters.AddWithValue("@itemid", (long)item.ItemId);
                cmd.Parameters.AddWithValue("@iname", item.ItemName);
                cmd.Parameters.AddWithValue("@qty", item.Quantity);
                cmd.Parameters.AddWithValue("@hq", item.IsHq ? 1 : 0);
                cmd.Parameters.AddWithValue("@now", now);
                cmd.ExecuteNonQuery();
            }

            if (ownTransaction) transaction?.Commit();
        }
        catch (Exception ex)
        {
            if (ownTransaction) transaction?.Rollback();
            Plugin.Log.Error($"[XA] Failed to save container items: {ex}");
        }
    }

    public List<ContainerItemEntry> GetAll(ulong contentId)
    {
        var results = new List<ContainerItemEntry>();
        var conn = db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT container_name, container_type, slot_index, item_id, item_name, quantity, is_hq
            FROM container_items
            WHERE content_id = @cid
            ORDER BY container_name, slot_index";
        cmd.Parameters.AddWithValue("@cid", (long)contentId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new ContainerItemEntry
            {
                ContainerName = reader["container_name"].ToString() ?? "",
                ContainerType = Convert.ToInt32(reader["container_type"]),
                SlotIndex = Convert.ToInt32(reader["slot_index"]),
                ItemId = (uint)Convert.ToInt64(reader["item_id"]),
                ItemName = reader["item_name"].ToString() ?? "",
                Quantity = Convert.ToInt32(reader["quantity"]),
                IsHq = Convert.ToInt32(reader["is_hq"]) == 1,
            });
        }
        return results;
    }

    /// <summary>
    /// Item locator: find all locations of an item across ALL characters.
    /// </summary>
    public List<ItemLocationResult> FindItem(uint itemId)
    {
        var results = new List<ItemLocationResult>();
        var conn = db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT c.name, c.world, ci.container_name, ci.item_name, ci.quantity, ci.is_hq
            FROM container_items ci
            JOIN characters c ON c.content_id = ci.content_id
            WHERE ci.item_id = @itemid
            ORDER BY c.name, ci.container_name";
        cmd.Parameters.AddWithValue("@itemid", (long)itemId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new ItemLocationResult
            {
                CharacterName = reader["name"].ToString() ?? "",
                World = reader["world"].ToString() ?? "",
                ContainerName = reader["container_name"].ToString() ?? "",
                ItemName = reader["item_name"].ToString() ?? "",
                Quantity = Convert.ToInt32(reader["quantity"]),
                IsHq = Convert.ToInt32(reader["is_hq"]) == 1,
            });
        }
        return results;
    }

    /// <summary>
    /// Item locator: search by item name substring across ALL characters.
    /// </summary>
    public List<ItemLocationResult> SearchByName(string searchText)
    {
        var results = new List<ItemLocationResult>();
        var conn = db.GetConnection();

        // Search player inventory
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT c.name, c.world, ci.container_name, ci.item_id, ci.item_name, ci.quantity, ci.is_hq
                FROM container_items ci
                JOIN characters c ON c.content_id = ci.content_id
                WHERE ci.item_name LIKE @search
                ORDER BY ci.item_name, c.name, ci.container_name
                LIMIT 200";
            cmd.Parameters.AddWithValue("@search", $"%{searchText}%");
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new ItemLocationResult
                {
                    CharacterName = reader["name"].ToString() ?? "",
                    World = reader["world"].ToString() ?? "",
                    ContainerName = reader["container_name"].ToString() ?? "",
                    ItemId = (uint)Convert.ToInt64(reader["item_id"]),
                    ItemName = reader["item_name"].ToString() ?? "",
                    Quantity = Convert.ToInt32(reader["quantity"]),
                    IsHq = Convert.ToInt32(reader["is_hq"]) == 1,
                });
            }
        }

        // Also search retainer inventory
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT r.name AS retainer_name, c.name, c.world, ri.item_id, ri.item_name, ri.quantity, ri.is_hq
                FROM retainer_items ri
                JOIN retainers r ON r.retainer_id = ri.retainer_id
                JOIN characters c ON c.content_id = r.content_id
                WHERE ri.item_name LIKE @search
                ORDER BY ri.item_name, c.name, r.name
                LIMIT 200";
            cmd.Parameters.AddWithValue("@search", $"%{searchText}%");
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var retainerName = reader["retainer_name"].ToString() ?? "";
                results.Add(new ItemLocationResult
                {
                    CharacterName = reader["name"].ToString() ?? "",
                    World = reader["world"].ToString() ?? "",
                    ContainerName = $"Retainer: {retainerName}",
                    ItemId = (uint)Convert.ToInt64(reader["item_id"]),
                    ItemName = reader["item_name"].ToString() ?? "",
                    Quantity = Convert.ToInt32(reader["quantity"]),
                    IsHq = Convert.ToInt32(reader["is_hq"]) == 1,
                });
            }
        }

        return results;
    }
}

public class ItemLocationResult
{
    public string CharacterName { get; set; } = string.Empty;
    public string World { get; set; } = string.Empty;
    public string ContainerName { get; set; } = string.Empty;
    public uint ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public bool IsHq { get; set; }
}
