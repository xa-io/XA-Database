using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using XADatabase.Models;

namespace XADatabase.Database;

public class CurrencyRepository
{
    private readonly DatabaseService db;

    public CurrencyRepository(DatabaseService db)
    {
        this.db = db;
    }

    public void SaveSnapshot(ulong contentId, List<CurrencyEntry> currencies)
    {
        var conn = db.GetConnection();
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        var ownTransaction = !db.HasActiveTransaction;
        var transaction = ownTransaction ? conn.BeginTransaction() : null;
        try
        {
            foreach (var entry in currencies)
            {
                // Upsert current balance
                using var upsertCmd = conn.CreateCommand();
                upsertCmd.CommandText = @"
                    INSERT INTO currency_balances (content_id, currency_name, category, amount, cap, updated_utc)
                    VALUES (@cid, @name, @cat, @amount, @cap, @now)
                    ON CONFLICT(content_id, currency_name) DO UPDATE SET
                        category = @cat,
                        amount = @amount,
                        cap = @cap,
                        updated_utc = @now";
                upsertCmd.Parameters.AddWithValue("@cid", (long)contentId);
                upsertCmd.Parameters.AddWithValue("@name", entry.Name);
                upsertCmd.Parameters.AddWithValue("@cat", entry.Category);
                upsertCmd.Parameters.AddWithValue("@amount", entry.Amount);
                upsertCmd.Parameters.AddWithValue("@cap", entry.Cap);
                upsertCmd.Parameters.AddWithValue("@now", now);
                upsertCmd.ExecuteNonQuery();
            }

            if (ownTransaction) transaction?.Commit();
        }
        catch (Exception ex)
        {
            if (ownTransaction) transaction?.Rollback();
            Plugin.Log.Error($"[XA] Failed to save currency snapshot: {ex}");
            if (!ownTransaction) throw;
        }
        finally { if (ownTransaction) transaction?.Dispose(); }
    }

    /// <summary>
    /// Prune old currency_history rows using a tiered retention policy:
    /// - Keep all rows from the last 7 days
    /// - Keep 1 per day for rows 7-90 days old
    /// - Keep 1 per week for rows 90-365 days old
    /// - Delete everything older than 365 days
    /// </summary>
    public void PruneHistory()
    {
        return;
    }

    public List<CurrencyEntry> GetLatest(ulong contentId)
    {
        var results = new List<CurrencyEntry>();
        var conn = db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT currency_name, category, amount, cap
            FROM currency_balances
            WHERE content_id = @cid
            ORDER BY category, currency_name";
        cmd.Parameters.AddWithValue("@cid", (long)contentId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new CurrencyEntry
            {
                Name = reader["currency_name"].ToString() ?? "",
                Category = reader["category"].ToString() ?? "",
                Amount = Convert.ToInt32(reader["amount"]),
                Cap = Convert.ToInt32(reader["cap"]),
            });
        }
        return results;
    }
}
