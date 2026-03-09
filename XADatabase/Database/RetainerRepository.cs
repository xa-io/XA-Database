using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using XADatabase.Models;

namespace XADatabase.Database;

public class RetainerRepository
{
    private readonly DatabaseService db;

    public RetainerRepository(DatabaseService db)
    {
        this.db = db;
    }

    public void SaveRetainers(ulong contentId, List<RetainerEntry> retainers)
    {
        var conn = db.GetConnection();
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        var ownTransaction = !db.HasActiveTransaction;
        var transaction = ownTransaction ? conn.BeginTransaction() : null;
        try
        {
            foreach (var r in retainers)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO retainers (retainer_id, content_id, name, class_job, level, gil, item_count, market_item_count, town, venture_id, venture_complete_unix, venture_status, venture_eta, updated_utc)
                    VALUES (@rid, @cid, @name, @cj, @lvl, @gil, @ic, @mic, @town, @vid, @vcomplete, @vstatus, @veta, @now)
                    ON CONFLICT(retainer_id) DO UPDATE SET
                        content_id = @cid,
                        name = @name,
                        class_job = @cj,
                        level = @lvl,
                        gil = @gil,
                        item_count = @ic,
                        market_item_count = @mic,
                        town = @town,
                        venture_id = @vid,
                        venture_complete_unix = @vcomplete,
                        venture_status = @vstatus,
                        venture_eta = @veta,
                        updated_utc = @now";
                cmd.Parameters.AddWithValue("@rid", (long)r.RetainerId);
                cmd.Parameters.AddWithValue("@cid", (long)contentId);
                cmd.Parameters.AddWithValue("@name", r.Name);
                cmd.Parameters.AddWithValue("@cj", (int)r.ClassJob);
                cmd.Parameters.AddWithValue("@lvl", (int)r.Level);
                cmd.Parameters.AddWithValue("@gil", (long)r.Gil);
                cmd.Parameters.AddWithValue("@ic", (int)r.ItemCount);
                cmd.Parameters.AddWithValue("@mic", (int)r.MarketItemCount);
                cmd.Parameters.AddWithValue("@town", r.Town);
                cmd.Parameters.AddWithValue("@vid", (int)r.VentureId);
                cmd.Parameters.AddWithValue("@vcomplete", (long)r.VentureCompleteUnix);
                cmd.Parameters.AddWithValue("@vstatus", r.VentureStatus);
                cmd.Parameters.AddWithValue("@veta", r.VentureEta);
                cmd.Parameters.AddWithValue("@now", now);
                cmd.ExecuteNonQuery();
            }

            if (ownTransaction) transaction?.Commit();
        }
        catch (Exception ex)
        {
            if (ownTransaction) transaction?.Rollback();
            Plugin.Log.Error($"[XA] Failed to save retainers: {ex}");
        }
    }

    public List<RetainerEntry> GetRetainers(ulong contentId)
    {
        var results = new List<RetainerEntry>();
        var conn = db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT retainer_id, name, class_job, level, gil, item_count, market_item_count, town,
                   venture_id, venture_complete_unix, venture_status, venture_eta
            FROM retainers WHERE content_id = @cid ORDER BY name";
        cmd.Parameters.AddWithValue("@cid", (long)contentId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new RetainerEntry
            {
                RetainerId = (ulong)(long)reader["retainer_id"],
                Name = reader["name"].ToString() ?? "",
                ClassJob = (byte)Convert.ToInt32(reader["class_job"]),
                Level = (byte)Convert.ToInt32(reader["level"]),
                Gil = (uint)Convert.ToInt64(reader["gil"]),
                ItemCount = (byte)Convert.ToInt32(reader["item_count"]),
                MarketItemCount = (byte)Convert.ToInt32(reader["market_item_count"]),
                Town = reader["town"].ToString() ?? "",
                VentureId = (ushort)Convert.ToInt32(reader["venture_id"]),
                VentureCompleteUnix = (uint)Convert.ToInt64(reader["venture_complete_unix"]),
                VentureStatus = reader["venture_status"].ToString() ?? "",
                VentureEta = reader["venture_eta"].ToString() ?? "",
            });
        }
        return results;
    }

    public void SaveListings(ulong retainerId, List<RetainerListingEntry> listings)
    {
        var conn = db.GetConnection();
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        // Infer sales before replacing listings
        InferSales(retainerId, listings, now);

        var ownTransaction = !db.HasActiveTransaction;
        var transaction = ownTransaction ? conn.BeginTransaction() : null;
        try
        {
            // Clear old listings for this retainer
            using var deleteCmd = conn.CreateCommand();
            deleteCmd.CommandText = "DELETE FROM retainer_listings WHERE retainer_id = @rid";
            deleteCmd.Parameters.AddWithValue("@rid", (long)retainerId);
            deleteCmd.ExecuteNonQuery();

            foreach (var l in listings)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO retainer_listings (retainer_id, slot_index, item_id, item_name, quantity, is_hq, unit_price, updated_utc)
                    VALUES (@rid, @slot, @itemid, @iname, @qty, @hq, @price, @now)";
                cmd.Parameters.AddWithValue("@rid", (long)retainerId);
                cmd.Parameters.AddWithValue("@slot", l.SlotIndex);
                cmd.Parameters.AddWithValue("@itemid", (long)l.ItemId);
                cmd.Parameters.AddWithValue("@iname", l.ItemName);
                cmd.Parameters.AddWithValue("@qty", l.Quantity);
                cmd.Parameters.AddWithValue("@hq", l.IsHq ? 1 : 0);
                cmd.Parameters.AddWithValue("@price", (long)l.UnitPrice);
                cmd.Parameters.AddWithValue("@now", now);
                cmd.ExecuteNonQuery();
            }

            if (ownTransaction) transaction?.Commit();
        }
        catch (Exception ex)
        {
            if (ownTransaction) transaction?.Rollback();
            Plugin.Log.Error($"[XA] Failed to save retainer listings: {ex}");
        }
    }

    /// <summary>
    /// Compare previous listings with new listings for a retainer.
    /// Items that disappeared (or had quantity reduced) are inferred as sales.
    /// </summary>
    private void InferSales(ulong retainerId, List<RetainerListingEntry> newListings, string now)
    {
        try
        {
            var oldListings = GetListings(retainerId);
            if (oldListings.Count == 0)
                return;

            var conn = db.GetConnection();

            // Build lookup: (item_id, is_hq, unit_price) → total qty in new listings
            var newLookup = new Dictionary<(uint, bool, uint), int>();
            foreach (var nl in newListings)
            {
                var key = (nl.ItemId, nl.IsHq, nl.UnitPrice);
                if (newLookup.ContainsKey(key))
                    newLookup[key] += nl.Quantity;
                else
                    newLookup[key] = nl.Quantity;
            }

            // Check each old listing — if it disappeared or qty decreased, infer sale
            var oldLookup = new Dictionary<(uint, bool, uint), (int Qty, string Name)>();
            foreach (var ol in oldListings)
            {
                var key = (ol.ItemId, ol.IsHq, ol.UnitPrice);
                if (oldLookup.ContainsKey(key))
                    oldLookup[key] = (oldLookup[key].Qty + ol.Quantity, ol.ItemName);
                else
                    oldLookup[key] = (ol.Quantity, ol.ItemName);
            }

            foreach (var (key, oldVal) in oldLookup)
            {
                var (itemId, isHq, unitPrice) = key;
                var newQty = newLookup.ContainsKey(key) ? newLookup[key] : 0;
                var soldQty = oldVal.Qty - newQty;

                if (soldQty > 0)
                {
                    var totalGil = (long)unitPrice * soldQty;
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                        INSERT INTO retainer_sales (retainer_id, item_id, item_name, quantity, is_hq, unit_price, total_gil, sold_utc)
                        VALUES (@rid, @itemid, @iname, @qty, @hq, @price, @total, @now)";
                    cmd.Parameters.AddWithValue("@rid", (long)retainerId);
                    cmd.Parameters.AddWithValue("@itemid", (long)itemId);
                    cmd.Parameters.AddWithValue("@iname", oldVal.Name);
                    cmd.Parameters.AddWithValue("@qty", soldQty);
                    cmd.Parameters.AddWithValue("@hq", isHq ? 1 : 0);
                    cmd.Parameters.AddWithValue("@price", (long)unitPrice);
                    cmd.Parameters.AddWithValue("@total", totalGil);
                    cmd.Parameters.AddWithValue("@now", now);
                    cmd.ExecuteNonQuery();

                    Plugin.Log.Information($"[XA] Inferred sale: {soldQty}x {oldVal.Name} @ {unitPrice:N0} = {totalGil:N0} gil");
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[XA] Sale inference error: {ex.Message}");
        }
    }

    /// <summary>
    /// Get recent inferred sales for a character's retainers.
    /// </summary>
    public List<RetainerSaleEntry> GetRecentSales(ulong contentId, int limit = 50)
    {
        var results = new List<RetainerSaleEntry>();
        var conn = db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT rs.retainer_id, r.name AS retainer_name, rs.item_id, rs.item_name, rs.quantity, rs.is_hq, rs.unit_price, rs.total_gil, rs.sold_utc
            FROM retainer_sales rs
            JOIN retainers r ON r.retainer_id = rs.retainer_id
            WHERE r.content_id = @cid
            ORDER BY rs.sold_utc DESC
            LIMIT @limit";
        cmd.Parameters.AddWithValue("@cid", (long)contentId);
        cmd.Parameters.AddWithValue("@limit", limit);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new RetainerSaleEntry
            {
                RetainerId = (ulong)(long)reader["retainer_id"],
                RetainerName = reader["retainer_name"].ToString() ?? "",
                ItemId = (uint)Convert.ToInt64(reader["item_id"]),
                ItemName = reader["item_name"].ToString() ?? "",
                Quantity = Convert.ToInt32(reader["quantity"]),
                IsHq = Convert.ToInt32(reader["is_hq"]) == 1,
                UnitPrice = (uint)Convert.ToInt64(reader["unit_price"]),
                TotalGil = Convert.ToInt64(reader["total_gil"]),
                SoldUtc = reader["sold_utc"].ToString() ?? "",
            });
        }
        return results;
    }

    public List<RetainerListingEntry> GetListings(ulong retainerId)
    {
        var results = new List<RetainerListingEntry>();
        var conn = db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT slot_index, item_id, item_name, quantity, is_hq, unit_price
            FROM retainer_listings WHERE retainer_id = @rid ORDER BY slot_index";
        cmd.Parameters.AddWithValue("@rid", (long)retainerId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new RetainerListingEntry
            {
                RetainerId = retainerId,
                SlotIndex = Convert.ToInt32(reader["slot_index"]),
                ItemId = (uint)Convert.ToInt64(reader["item_id"]),
                ItemName = reader["item_name"].ToString() ?? "",
                Quantity = Convert.ToInt32(reader["quantity"]),
                IsHq = Convert.ToInt32(reader["is_hq"]) == 1,
                UnitPrice = (uint)Convert.ToInt64(reader["unit_price"]),
            });
        }
        return results;
    }

    /// <summary>
    /// Load ALL listings for ALL retainers belonging to a character.
    /// </summary>
    public List<RetainerListingEntry> GetAllListings(ulong contentId)
    {
        var results = new List<RetainerListingEntry>();
        var conn = db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT rl.retainer_id, r.name AS retainer_name, rl.slot_index, rl.item_id, rl.item_name, rl.quantity, rl.is_hq, rl.unit_price
            FROM retainer_listings rl
            JOIN retainers r ON r.retainer_id = rl.retainer_id
            WHERE r.content_id = @cid
            ORDER BY r.name, rl.slot_index";
        cmd.Parameters.AddWithValue("@cid", (long)contentId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new RetainerListingEntry
            {
                RetainerId = (ulong)(long)reader["retainer_id"],
                RetainerName = reader["retainer_name"].ToString() ?? "",
                SlotIndex = Convert.ToInt32(reader["slot_index"]),
                ItemId = (uint)Convert.ToInt64(reader["item_id"]),
                ItemName = reader["item_name"].ToString() ?? "",
                Quantity = Convert.ToInt32(reader["quantity"]),
                IsHq = Convert.ToInt32(reader["is_hq"]) == 1,
                UnitPrice = (uint)Convert.ToInt64(reader["unit_price"]),
            });
        }
        return results;
    }

    /// <summary>
    /// Load ALL retainer inventory items for ALL retainers belonging to a character.
    /// </summary>
    public List<RetainerInventoryItem> GetAllRetainerItems(ulong contentId)
    {
        var results = new List<RetainerInventoryItem>();
        var conn = db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT r.retainer_id, r.name AS retainer_name, ri.item_id, ri.item_name, ri.quantity, ri.is_hq
            FROM retainer_items ri
            JOIN retainers r ON r.retainer_id = ri.retainer_id
            WHERE r.content_id = @cid
            ORDER BY r.name, ri.item_name";
        cmd.Parameters.AddWithValue("@cid", (long)contentId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new RetainerInventoryItem
            {
                RetainerId = (ulong)(long)reader["retainer_id"],
                RetainerName = reader["retainer_name"].ToString() ?? "",
                ItemId = (uint)Convert.ToInt64(reader["item_id"]),
                ItemName = reader["item_name"].ToString() ?? "",
                Quantity = Convert.ToInt32(reader["quantity"]),
                IsHq = Convert.ToInt32(reader["is_hq"]) == 1,
            });
        }
        return results;
    }

    public void SaveRetainerInventory(ulong retainerId, List<ContainerItemEntry> items)
    {
        var conn = db.GetConnection();
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        var ownTransaction = !db.HasActiveTransaction;
        var transaction = ownTransaction ? conn.BeginTransaction() : null;
        try
        {
            // Clear old retainer inventory
            using var deleteCmd = conn.CreateCommand();
            deleteCmd.CommandText = "DELETE FROM retainer_items WHERE retainer_id = @rid";
            deleteCmd.Parameters.AddWithValue("@rid", (long)retainerId);
            deleteCmd.ExecuteNonQuery();

            foreach (var item in items)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO retainer_items (retainer_id, container_type, slot_index, item_id, item_name, quantity, is_hq, updated_utc)
                    VALUES (@rid, @ctype, @slot, @itemid, @iname, @qty, @hq, @now)";
                cmd.Parameters.AddWithValue("@rid", (long)retainerId);
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
            Plugin.Log.Error($"[XA] Failed to save retainer inventory: {ex}");
        }
    }
}
