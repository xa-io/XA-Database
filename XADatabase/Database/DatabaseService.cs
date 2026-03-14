using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using XADatabase.Data;
using XADatabase.Models;

namespace XADatabase.Database;

public sealed class DatabaseService : IDisposable
{
    private static readonly string[] LegacyTables =
    {
        "retainer_listings",
        "retainer_sales",
        "retainer_items",
        "container_items",
        "currency_history",
        "fc_members",
        "squadron_members",
        "voyages",
        "msq_milestones",
        "active_quests",
        "collection_summaries",
        "currency_balances",
        "job_levels",
        "inventory_summaries",
        "squadron_info",
        "free_companies",
        "retainers",
        "characters",
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly string dbPath;
    private SqliteConnection? connection;
    public DatabaseHealthCheckResult LastHealthCheck { get; private set; } = new();

    public DatabaseService(string pluginConfigDir)
    {
        Directory.CreateDirectory(pluginConfigDir);
        dbPath = Path.Combine(pluginConfigDir, "xa.db");
    }

    public SqliteConnection GetConnection()
    {
        if (connection == null)
        {
            connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();

            using var walCmd = connection.CreateCommand();
            walCmd.CommandText = "PRAGMA journal_mode=WAL";
            walCmd.ExecuteNonQuery();
        }

        if (connection.State != System.Data.ConnectionState.Open)
            connection.Open();

        return connection;
    }

    public DatabaseHealthCheckResult RunHealthCheck()
    {
        var result = new DatabaseHealthCheckResult
        {
            DbPath = dbPath,
            CheckedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
        };

        if (ActiveTransaction != null)
        {
            result.Summary = "Database health check skipped because a transaction is currently active.";
            LastHealthCheck = result;
            Plugin.Log.Information($"[XA] {result.Summary}");
            return result;
        }

        try
        {
            var conn = GetConnection();

            using (var readCmd = conn.CreateCommand())
            {
                readCmd.CommandText = "SELECT COUNT(*) FROM sqlite_master";
                readCmd.ExecuteScalar();
                result.ReadOk = true;
            }

            using (var integrityCmd = conn.CreateCommand())
            {
                integrityCmd.CommandText = "PRAGMA quick_check(1)";
                var integrity = integrityCmd.ExecuteScalar()?.ToString() ?? string.Empty;
                result.IntegrityOk = string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase);
                if (!result.IntegrityOk)
                    result.Error = string.IsNullOrWhiteSpace(integrity) ? "quick_check returned an empty result." : integrity;
            }

            using (var beginCmd = conn.CreateCommand())
            {
                beginCmd.CommandText = "BEGIN IMMEDIATE";
                beginCmd.ExecuteNonQuery();
            }

            using (var rollbackCmd = conn.CreateCommand())
            {
                rollbackCmd.CommandText = "ROLLBACK";
                rollbackCmd.ExecuteNonQuery();
            }

            result.WriteOk = true;
            result.Success = result.ReadOk && result.WriteOk && result.IntegrityOk;
            result.Summary = result.Success
                ? "Database read/write health check passed."
                : $"Database integrity check returned: {result.Error}";
        }
        catch (Exception ex)
        {
            try
            {
                var conn = GetConnection();
                using var rollbackCmd = conn.CreateCommand();
                rollbackCmd.CommandText = "ROLLBACK";
                rollbackCmd.ExecuteNonQuery();
            }
            catch
            {
            }

            result.Error = ex.Message;
            result.Success = false;
            result.Summary = $"Database health check failed: {ex.Message}";
        }

        LastHealthCheck = result;

        if (result.Success)
            Plugin.Log.Information($"[XA] {result.Summary} ({dbPath})");
        else
            Plugin.Log.Error($"[XA] {result.Summary} ({dbPath})");

        return result;
    }

    public void InitializeSchema()
    {
        var conn = GetConnection();
        var currentVersion = GetSchemaVersion();
        var needsXaUpgrade = NeedsXaCharactersUpgrade();
        var needsRegionUpgrade = TableExists("xa_characters") && !ColumnExists("xa_characters", "region");
        var needsSharedEstatesUpgrade = TableExists("xa_characters") && !ColumnExists("xa_characters", "shared_estates");
        var needsLegacyCleanup = GetXaCharacterCount() > 0 && HasLegacyTables();

        if (currentVersion >= Schema.CurrentVersion && !needsXaUpgrade && !needsRegionUpgrade && !needsSharedEstatesUpgrade && !needsLegacyCleanup)
        {
            Plugin.Log.Information($"[XA] Database schema is up to date (v{currentVersion}).");
            return;
        }

        using var transaction = conn.BeginTransaction();
        try
        {
            ExecuteSchemaStatements(conn, transaction);

            if (currentVersion < 3 && TableExists("retainers"))
            {
                TryExecuteNonQuery(conn, transaction, "ALTER TABLE retainers ADD COLUMN venture_id INTEGER NOT NULL DEFAULT 0");
                TryExecuteNonQuery(conn, transaction, "ALTER TABLE retainers ADD COLUMN venture_complete_unix INTEGER NOT NULL DEFAULT 0");
                TryExecuteNonQuery(conn, transaction, "ALTER TABLE retainers ADD COLUMN venture_status TEXT NOT NULL DEFAULT ''");
                TryExecuteNonQuery(conn, transaction, "ALTER TABLE retainers ADD COLUMN venture_eta TEXT NOT NULL DEFAULT ''");
                Plugin.Log.Information("[XA] Applied schema migration v2 → v3 (retainer venture columns)");
            }

            if (currentVersion < 10 && TableExists("fc_members"))
            {
                TryExecuteNonQuery(conn, transaction, "ALTER TABLE fc_members ADD COLUMN rank_name TEXT NOT NULL DEFAULT ''");
                Plugin.Log.Information("[XA] Applied schema migration v7 → v8 (fc_members rank_name column)");
            }

            if (currentVersion < 12 && TableExists("voyages"))
            {
                TryExecuteNonQuery(conn, transaction, "ALTER TABLE voyages ADD COLUMN build_string TEXT NOT NULL DEFAULT ''");
                Plugin.Log.Information("[XA] Applied schema migration v11 → v12 (voyages build_string column)");
            }

            if (currentVersion < 13 && TableExists("free_companies"))
            {
                TryExecuteNonQuery(conn, transaction, "ALTER TABLE free_companies ADD COLUMN fc_points INTEGER NOT NULL DEFAULT 0");
                TryExecuteNonQuery(conn, transaction, "ALTER TABLE free_companies ADD COLUMN estate TEXT NOT NULL DEFAULT ''");
                Plugin.Log.Information("[XA] Applied schema migration v12 → v13 (fc_points, estate columns)");
            }

            if (currentVersion < 14)
                Plugin.Log.Information("[XA] Applied schema migration v13 → v14 (msq_milestones table)");

            if (currentVersion < 15 && TableExists("characters"))
            {
                TryExecuteNonQuery(conn, transaction, "ALTER TABLE characters ADD COLUMN personal_estate TEXT NOT NULL DEFAULT ''");
                TryExecuteNonQuery(conn, transaction, "ALTER TABLE characters ADD COLUMN apartment TEXT NOT NULL DEFAULT ''");
                Plugin.Log.Information("[XA] Applied schema migration v14 → v15 (personal_estate, apartment columns)");
            }

            if (needsXaUpgrade)
                UpgradeXaCharactersTable(conn, transaction);

            if (currentVersion < 17 || needsXaUpgrade)
                Plugin.Log.Information("[XA] Applied schema migration v16 → v17 (xa_characters per-section snapshot table)");

            if (needsRegionUpgrade)
                AddXaCharacterRegionColumn(conn, transaction);

            if (currentVersion < 18 || needsRegionUpgrade)
                Plugin.Log.Information("[XA] Applied schema migration v17 → v18 (xa_characters region column)");

            if (needsSharedEstatesUpgrade)
                AddXaCharacterSharedEstatesColumn(conn, transaction);

            if (currentVersion < 19 || needsSharedEstatesUpgrade)
                Plugin.Log.Information("[XA] Applied schema migration v18 → v19 (xa_characters shared_estates column)");

            if (GetXaCharacterCount() > 0 && HasLegacyTables())
                DropLegacyTablesInternal(conn, transaction);

            UpsertSchemaVersion(conn, transaction);

            transaction.Commit();
            Plugin.Log.Information($"[XA] Database schema initialized to v{Schema.CurrentVersion} at {dbPath}");
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            Plugin.Log.Error($"[XA] Failed to initialize database schema: {ex}");
            throw;
        }
    }

    public SqliteTransaction? ActiveTransaction { get; set; }

    public bool HasActiveTransaction => ActiveTransaction != null;

    public SqliteTransaction BeginTransaction()
    {
        var tx = GetConnection().BeginTransaction();
        ActiveTransaction = tx;
        return tx;
    }

    public void CommitTransaction()
    {
        ActiveTransaction?.Commit();
        ActiveTransaction = null;
    }

    public void RollbackTransaction()
    {
        try { ActiveTransaction?.Rollback(); } catch { }
        ActiveTransaction = null;
    }

    public int GetSchemaVersion()
    {
        var conn = GetConnection();
        try
        {
            using var checkCmd = conn.CreateCommand();
            checkCmd.CommandText = "SELECT version FROM schema_version LIMIT 1";
            var result = checkCmd.ExecuteScalar();
            if (result != null)
                return Convert.ToInt32(result);
        }
        catch (SqliteException)
        {
        }

        return 0;
    }

    public bool TableExists(string tableName)
    {
        var conn = GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = @name LIMIT 1";
        cmd.Parameters.AddWithValue("@name", tableName);
        return cmd.ExecuteScalar() != null;
    }

    public bool ColumnExists(string tableName, string columnName)
    {
        if (!TableExists(tableName))
            return false;

        var conn = GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({tableName})";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader["name"].ToString(), columnName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public int GetLegacyCharacterCount()
    {
        if (!TableExists("characters"))
            return 0;

        var conn = GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM characters";
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    public int GetXaCharacterCount()
    {
        if (!TableExists("xa_characters"))
            return 0;

        var conn = GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM xa_characters";
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    public bool HasLegacyDataPendingMigration()
    {
        return GetLegacyCharacterCount() > 0 && HasLegacyTables();
    }

    public void UpsertXaCharacterSnapshot(
        ulong contentId,
        string characterName,
        string world,
        string datacenter,
        string region,
        ulong fcId,
        string fcName,
        string fcTag,
        int fcPoints,
        string fcEstate,
        string personalEstate,
        string sharedEstates,
        string apartment,
        int gil,
        int retainerGil,
        int retainerCount,
        int highestJobLevel,
        string retainerIdsJson,
        string freshnessJson,
        XaCharacterSnapshotSections sections,
        int snapshotVersion,
        string exportedUtc,
        string trigger,
        string triggerDetail,
        bool importedFromLegacy,
        string updatedUtc)
    {
        var conn = GetConnection();
        var normalizedHousing = XaCharacterSnapshotRepository.NormalizeHousingPayload(personalEstate, sharedEstates, apartment);
        using var cmd = conn.CreateCommand();
        if (ActiveTransaction != null)
            cmd.Transaction = ActiveTransaction;
        cmd.CommandText = @"
            INSERT INTO xa_characters (
                content_id,
                character_name,
                world,
                datacenter,
                region,
                fc_id,
                fc_name,
                fc_tag,
                fc_points,
                fc_estate,
                personal_estate,
                shared_estates,
                apartment,
                gil,
                retainer_gil,
                retainer_count,
                highest_job_level,
                retainer_ids_json,
                inventory_summaries_json,
                freshness_json,
                character_json,
                free_company_json,
                fc_members_json,
                currencies_json,
                jobs_json,
                inventory_json,
                saddlebag_json,
                crystals_json,
                armoury_json,
                equipped_json,
                items_json,
                retainers_json,
                listings_json,
                retainer_items_json,
                collections_json,
                active_quests_json,
                msq_milestones_json,
                squadron_json,
                voyages_json,
                validation_json,
                snapshot_version,
                exported_utc,
                trigger,
                trigger_detail,
                imported_from_legacy,
                updated_utc
            )
            VALUES (
                @cid,
                @character_name,
                @world,
                @datacenter,
                @region,
                @fc_id,
                @fc_name,
                @fc_tag,
                @fc_points,
                @fc_estate,
                @personal_estate,
                @shared_estates,
                @apartment,
                @gil,
                @retainer_gil,
                @retainer_count,
                @highest_job_level,
                @retainer_ids_json,
                @inventory_summaries_json,
                @freshness_json,
                @character_json,
                @free_company_json,
                @fc_members_json,
                @currencies_json,
                @jobs_json,
                @inventory_json,
                @saddlebag_json,
                @crystals_json,
                @armoury_json,
                @equipped_json,
                @items_json,
                @retainers_json,
                @listings_json,
                @retainer_items_json,
                @collections_json,
                @active_quests_json,
                @msq_milestones_json,
                @squadron_json,
                @voyages_json,
                @validation_json,
                @snapshot_version,
                @exported_utc,
                @trigger,
                @trigger_detail,
                @imported_from_legacy,
                @updated_utc
            )
            ON CONFLICT(content_id) DO UPDATE SET
                character_name = excluded.character_name,
                world = excluded.world,
                datacenter = excluded.datacenter,
                region = excluded.region,
                fc_id = excluded.fc_id,
                fc_name = excluded.fc_name,
                fc_tag = excluded.fc_tag,
                fc_points = excluded.fc_points,
                fc_estate = excluded.fc_estate,
                personal_estate = excluded.personal_estate,
                shared_estates = excluded.shared_estates,
                apartment = excluded.apartment,
                gil = excluded.gil,
                retainer_gil = excluded.retainer_gil,
                retainer_count = excluded.retainer_count,
                highest_job_level = excluded.highest_job_level,
                retainer_ids_json = excluded.retainer_ids_json,
                inventory_summaries_json = excluded.inventory_summaries_json,
                freshness_json = excluded.freshness_json,
                character_json = excluded.character_json,
                free_company_json = excluded.free_company_json,
                fc_members_json = excluded.fc_members_json,
                currencies_json = excluded.currencies_json,
                jobs_json = excluded.jobs_json,
                inventory_json = excluded.inventory_json,
                saddlebag_json = excluded.saddlebag_json,
                crystals_json = excluded.crystals_json,
                armoury_json = excluded.armoury_json,
                equipped_json = excluded.equipped_json,
                items_json = excluded.items_json,
                retainers_json = excluded.retainers_json,
                listings_json = excluded.listings_json,
                retainer_items_json = excluded.retainer_items_json,
                collections_json = excluded.collections_json,
                active_quests_json = excluded.active_quests_json,
                msq_milestones_json = excluded.msq_milestones_json,
                squadron_json = excluded.squadron_json,
                voyages_json = excluded.voyages_json,
                validation_json = excluded.validation_json,
                snapshot_version = excluded.snapshot_version,
                exported_utc = excluded.exported_utc,
                trigger = excluded.trigger,
                trigger_detail = excluded.trigger_detail,
                imported_from_legacy = excluded.imported_from_legacy,
                updated_utc = excluded.updated_utc";
        cmd.Parameters.AddWithValue("@cid", (long)contentId);
        cmd.Parameters.AddWithValue("@character_name", characterName ?? string.Empty);
        cmd.Parameters.AddWithValue("@world", world ?? string.Empty);
        cmd.Parameters.AddWithValue("@datacenter", datacenter ?? string.Empty);
        cmd.Parameters.AddWithValue("@region", region ?? string.Empty);
        cmd.Parameters.AddWithValue("@fc_id", ToSqliteInteger(fcId));
        cmd.Parameters.AddWithValue("@fc_name", fcName ?? string.Empty);
        cmd.Parameters.AddWithValue("@fc_tag", fcTag ?? string.Empty);
        cmd.Parameters.AddWithValue("@fc_points", fcPoints);
        cmd.Parameters.AddWithValue("@fc_estate", fcEstate ?? string.Empty);
        cmd.Parameters.AddWithValue("@personal_estate", normalizedHousing.PersonalEstate);
        cmd.Parameters.AddWithValue("@shared_estates", normalizedHousing.SharedEstates);
        cmd.Parameters.AddWithValue("@apartment", normalizedHousing.Apartment);
        cmd.Parameters.AddWithValue("@gil", gil);
        cmd.Parameters.AddWithValue("@retainer_gil", retainerGil);
        cmd.Parameters.AddWithValue("@retainer_count", retainerCount);
        cmd.Parameters.AddWithValue("@highest_job_level", highestJobLevel);
        cmd.Parameters.AddWithValue("@retainer_ids_json", string.IsNullOrWhiteSpace(retainerIdsJson) ? "[]" : retainerIdsJson);
        cmd.Parameters.AddWithValue("@inventory_summaries_json", sections.InventorySummariesJson);
        cmd.Parameters.AddWithValue("@freshness_json", string.IsNullOrWhiteSpace(freshnessJson) ? "{}" : freshnessJson);
        cmd.Parameters.AddWithValue("@character_json", sections.CharacterJson);
        cmd.Parameters.AddWithValue("@free_company_json", sections.FreeCompanyJson);
        cmd.Parameters.AddWithValue("@fc_members_json", sections.FcMembersJson);
        cmd.Parameters.AddWithValue("@currencies_json", sections.CurrenciesJson);
        cmd.Parameters.AddWithValue("@jobs_json", sections.JobsJson);
        cmd.Parameters.AddWithValue("@inventory_json", sections.InventoryJson);
        cmd.Parameters.AddWithValue("@saddlebag_json", sections.SaddlebagJson);
        cmd.Parameters.AddWithValue("@crystals_json", sections.CrystalsJson);
        cmd.Parameters.AddWithValue("@armoury_json", sections.ArmouryJson);
        cmd.Parameters.AddWithValue("@equipped_json", sections.EquippedJson);
        cmd.Parameters.AddWithValue("@items_json", sections.ItemsJson);
        cmd.Parameters.AddWithValue("@retainers_json", sections.RetainersJson);
        cmd.Parameters.AddWithValue("@listings_json", sections.ListingsJson);
        cmd.Parameters.AddWithValue("@retainer_items_json", sections.RetainerItemsJson);
        cmd.Parameters.AddWithValue("@collections_json", sections.CollectionsJson);
        cmd.Parameters.AddWithValue("@active_quests_json", sections.ActiveQuestsJson);
        cmd.Parameters.AddWithValue("@msq_milestones_json", sections.MsqMilestonesJson);
        cmd.Parameters.AddWithValue("@squadron_json", sections.SquadronJson);
        cmd.Parameters.AddWithValue("@voyages_json", sections.VoyagesJson);
        cmd.Parameters.AddWithValue("@validation_json", sections.ValidationJson);
        cmd.Parameters.AddWithValue("@snapshot_version", snapshotVersion);
        cmd.Parameters.AddWithValue("@exported_utc", exportedUtc ?? string.Empty);
        cmd.Parameters.AddWithValue("@trigger", trigger ?? string.Empty);
        cmd.Parameters.AddWithValue("@trigger_detail", triggerDetail ?? string.Empty);
        cmd.Parameters.AddWithValue("@imported_from_legacy", importedFromLegacy ? 1 : 0);
        cmd.Parameters.AddWithValue("@updated_utc", updatedUtc ?? string.Empty);
        cmd.ExecuteNonQuery();
    }

    public void DropLegacyTables()
    {
        var conn = GetConnection();
        if (ActiveTransaction != null)
        {
            DropLegacyTablesInternal(conn, ActiveTransaction);
            return;
        }

        using var transaction = conn.BeginTransaction();
        try
        {
            DropLegacyTablesInternal(conn, transaction);
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public void ClearAllCharacterData()
    {
        var conn = GetConnection();
        using var transaction = conn.BeginTransaction();
        try
        {
            if (TableExists("xa_characters"))
            {
                using var deleteCmd = conn.CreateCommand();
                deleteCmd.Transaction = transaction;
                deleteCmd.CommandText = "DELETE FROM xa_characters";
                deleteCmd.ExecuteNonQuery();
            }

            DropLegacyTablesInternal(conn, transaction);
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public string GetDbPath() => dbPath;
    public string GetDbDirectory() => Path.GetDirectoryName(dbPath) ?? ".";

    public void Dispose()
    {
        if (connection?.State == System.Data.ConnectionState.Open)
        {
            try
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[XA] WAL checkpoint error: {ex.Message}");
            }
        }

        connection?.Close();
        connection?.Dispose();
        connection = null;
    }

    private void ExecuteSchemaStatements(SqliteConnection conn, SqliteTransaction transaction)
    {
        foreach (var sql in Schema.CreateStatements)
            ExecuteNonQuery(conn, transaction, sql);
    }

    private void ExecuteNonQuery(SqliteConnection conn, SqliteTransaction transaction, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private void TryExecuteNonQuery(SqliteConnection conn, SqliteTransaction transaction, string sql)
    {
        try
        {
            ExecuteNonQuery(conn, transaction, sql);
        }
        catch (SqliteException)
        {
        }
    }

    private void UpsertSchemaVersion(SqliteConnection conn, SqliteTransaction transaction)
    {
        using var versionCmd = conn.CreateCommand();
        versionCmd.Transaction = transaction;
        versionCmd.CommandText = @"
            DELETE FROM schema_version;
            INSERT INTO schema_version (version) VALUES (@version)";
        versionCmd.Parameters.AddWithValue("@version", Schema.CurrentVersion);
        versionCmd.ExecuteNonQuery();
    }

    private bool NeedsXaCharactersUpgrade()
    {
        if (!TableExists("xa_characters"))
            return false;

        return ColumnExists("xa_characters", "snapshot_json")
            || !ColumnExists("xa_characters", "character_json")
            || !ColumnExists("xa_characters", "inventory_json")
            || !ColumnExists("xa_characters", "snapshot_version")
            || !ColumnExists("xa_characters", "inventory_summaries_json")
            || !ColumnExists("xa_characters", "highest_job_level")
            || !ColumnExists("xa_characters", "fc_id");
    }

    private void AddXaCharacterRegionColumn(SqliteConnection conn, SqliteTransaction transaction)
    {
        if (!TableExists("xa_characters") || ColumnExists("xa_characters", "region"))
            return;

        ExecuteNonQuery(conn, transaction, "ALTER TABLE xa_characters ADD COLUMN region TEXT NOT NULL DEFAULT ''");

        var rows = new List<(long ContentId, string Region)>();

        using var selectCmd = conn.CreateCommand();
        selectCmd.Transaction = transaction;
        selectCmd.CommandText = "SELECT content_id, world FROM xa_characters";
        using var reader = selectCmd.ExecuteReader();
        while (reader.Read())
        {
            var contentId = (long)reader["content_id"];
            var world = reader["world"].ToString() ?? string.Empty;
            rows.Add((contentId, WorldData.ResolveRegion(world)));
        }

        reader.Close();

        foreach (var row in rows)
        {
            using var updateCmd = conn.CreateCommand();
            updateCmd.Transaction = transaction;
            updateCmd.CommandText = "UPDATE xa_characters SET region = @region WHERE content_id = @cid";
            updateCmd.Parameters.AddWithValue("@cid", row.ContentId);
            updateCmd.Parameters.AddWithValue("@region", row.Region);
            updateCmd.ExecuteNonQuery();
        }
    }

    private void AddXaCharacterSharedEstatesColumn(SqliteConnection conn, SqliteTransaction transaction)
    {
        if (!TableExists("xa_characters") || ColumnExists("xa_characters", "shared_estates"))
            return;

        ExecuteNonQuery(conn, transaction, "ALTER TABLE xa_characters ADD COLUMN shared_estates TEXT NOT NULL DEFAULT ''");
    }

    private bool HasLegacyTables() => LegacyTables.Any(TableExists);

    private void UpgradeXaCharactersTable(SqliteConnection conn, SqliteTransaction transaction)
    {
        if (!TableExists("xa_characters") || !NeedsXaCharactersUpgrade())
            return;

        ExecuteNonQuery(conn, transaction, "ALTER TABLE xa_characters RENAME TO xa_characters_v16_backup");
        ExecuteSchemaStatements(conn, transaction);

        var legacyRows = new List<LegacyXaCharacterMigrationRow>();

        using var selectCmd = conn.CreateCommand();
        selectCmd.Transaction = transaction;
        selectCmd.CommandText = "SELECT * FROM xa_characters_v16_backup";
        using var reader = selectCmd.ExecuteReader();
        while (reader.Read())
        {
            legacyRows.Add(new LegacyXaCharacterMigrationRow
            {
                ContentId = (ulong)(long)reader["content_id"],
                CharacterName = reader["character_name"].ToString() ?? string.Empty,
                World = reader["world"].ToString() ?? string.Empty,
                Datacenter = reader["datacenter"].ToString() ?? string.Empty,
                FcName = reader["fc_name"].ToString() ?? string.Empty,
                FcTag = reader["fc_tag"].ToString() ?? string.Empty,
                FcPoints = Convert.ToInt32(reader["fc_points"]),
                FcEstate = reader["fc_estate"].ToString() ?? string.Empty,
                PersonalEstate = reader["personal_estate"].ToString() ?? string.Empty,
                Apartment = reader["apartment"].ToString() ?? string.Empty,
                Gil = Convert.ToInt32(reader["gil"]),
                RetainerGil = Convert.ToInt32(reader["retainer_gil"]),
                RetainerCount = Convert.ToInt32(reader["retainer_count"]),
                RetainerIdsJson = reader["retainer_ids_json"].ToString() ?? "[]",
                ValidationJson = reader["validation_json"].ToString() ?? "{}",
                FreshnessJson = reader["freshness_json"].ToString() ?? "{}",
                SnapshotJson = reader["snapshot_json"].ToString() ?? "{}",
                UpdatedUtc = reader["updated_utc"].ToString() ?? string.Empty,
                Trigger = reader["trigger"].ToString() ?? string.Empty,
                TriggerDetail = reader["trigger_detail"].ToString() ?? string.Empty,
                ImportedFromLegacy = Convert.ToInt32(reader["imported_from_legacy"]) == 1,
            });
        }

        reader.Close();

        foreach (var legacyRow in legacyRows)
        {
            var datacenter = XaCharacterSnapshotRepository.ResolveDatacenter(legacyRow.World, legacyRow.Datacenter);
            var region = XaCharacterSnapshotRepository.ResolveRegion(legacyRow.World);
            var trigger = string.IsNullOrWhiteSpace(legacyRow.Trigger)
                ? ReadLegacySnapshotString(legacyRow.SnapshotJson, "trigger", string.Empty)
                : legacyRow.Trigger;
            var triggerDetail = string.IsNullOrWhiteSpace(legacyRow.TriggerDetail)
                ? ReadLegacySnapshotString(legacyRow.SnapshotJson, "triggerDetail", string.Empty)
                : legacyRow.TriggerDetail;
            var importedFromLegacy = legacyRow.ImportedFromLegacy || ReadLegacySnapshotBool(legacyRow.SnapshotJson, "importedFromLegacy", false);
            var snapshotVersion = ReadLegacySnapshotInt(legacyRow.SnapshotJson, "snapshotVersion", 1);
            var exportedUtc = ReadLegacySnapshotString(legacyRow.SnapshotJson, "exportedUtc", legacyRow.UpdatedUtc);
            var sections = XaCharacterSnapshotRepository.BuildSectionsFromLegacySnapshotJson(
                legacyRow.SnapshotJson,
                legacyRow.ContentId,
                legacyRow.CharacterName,
                legacyRow.World,
                datacenter,
                region,
                legacyRow.PersonalEstate,
                string.Empty,
                legacyRow.Apartment,
                legacyRow.Gil,
                legacyRow.RetainerGil,
                legacyRow.ValidationJson);
            var jobs = DeserializeList<JobEntry>(sections.JobsJson);
            var normalizedRetainers = DeserializeList<RetainerEntry>(sections.RetainersJson);
            var fcId = ReadFreeCompanyId(sections.FreeCompanyJson);

            UpsertXaCharacterSnapshot(
                legacyRow.ContentId,
                legacyRow.CharacterName,
                legacyRow.World,
                datacenter,
                region,
                fcId,
                legacyRow.FcName,
                legacyRow.FcTag,
                legacyRow.FcPoints,
                legacyRow.FcEstate,
                legacyRow.PersonalEstate,
                string.Empty,
                legacyRow.Apartment,
                legacyRow.Gil,
                legacyRow.RetainerGil,
                normalizedRetainers.Count,
                XaCharacterSnapshotRepository.GetHighestJobLevel(jobs),
                JsonSerializer.Serialize(normalizedRetainers.Select(retainer => retainer.RetainerId).Distinct()),
                legacyRow.FreshnessJson,
                sections,
                snapshotVersion,
                exportedUtc,
                trigger,
                triggerDetail,
                importedFromLegacy,
                legacyRow.UpdatedUtc);
        }
        ExecuteNonQuery(conn, transaction, "DROP TABLE IF EXISTS xa_characters_v16_backup");
    }

    private void DropLegacyTablesInternal(SqliteConnection conn, SqliteTransaction transaction)
    {
        ExecuteNonQuery(conn, transaction, "PRAGMA defer_foreign_keys = ON");

        foreach (var table in LegacyTables)
            ExecuteNonQuery(conn, transaction, $"DROP TABLE IF EXISTS {table}");
    }

    private static ulong ReadFreeCompanyId(string freeCompanyJson)
    {
        if (string.IsNullOrWhiteSpace(freeCompanyJson))
            return 0;

        try
        {
            var freeCompany = JsonSerializer.Deserialize<FreeCompanyEntry>(freeCompanyJson, JsonOptions);
            return freeCompany?.FcId ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    private static List<T> DeserializeList<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<T>();

        try
        {
            return JsonSerializer.Deserialize<List<T>>(json, JsonOptions) ?? new List<T>();
        }
        catch
        {
            return new List<T>();
        }
    }

    private static string ReadLegacySnapshotString(string snapshotJson, string propertyName, string fallback)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
            return fallback;

        try
        {
            using var doc = JsonDocument.Parse(snapshotJson);
            if (doc.RootElement.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString() ?? fallback;
        }
        catch
        {
        }

        return fallback;
    }

    private static int ReadLegacySnapshotInt(string snapshotJson, string propertyName, int fallback)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
            return fallback;

        try
        {
            using var doc = JsonDocument.Parse(snapshotJson);
            if (doc.RootElement.TryGetProperty(propertyName, out var value))
            {
                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
                    return number;
                if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number))
                    return number;
            }
        }
        catch
        {
        }

        return fallback;
    }

    private static bool ReadLegacySnapshotBool(string snapshotJson, string propertyName, bool fallback)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
            return fallback;

        try
        {
            using var doc = JsonDocument.Parse(snapshotJson);
            if (doc.RootElement.TryGetProperty(propertyName, out var value))
            {
                if (value.ValueKind == JsonValueKind.True) return true;
                if (value.ValueKind == JsonValueKind.False) return false;
                if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var result))
                    return result;
            }
        }
        catch
        {
        }

        return fallback;
    }

    private static long ToSqliteInteger(ulong value)
    {
        return value > long.MaxValue ? long.MaxValue : (long)value;
    }
}

internal sealed class LegacyXaCharacterMigrationRow
{
    public ulong ContentId { get; init; }
    public string CharacterName { get; init; } = string.Empty;
    public string World { get; init; } = string.Empty;
    public string Datacenter { get; init; } = string.Empty;
    public string FcName { get; init; } = string.Empty;
    public string FcTag { get; init; } = string.Empty;
    public int FcPoints { get; init; }
    public string FcEstate { get; init; } = string.Empty;
    public string PersonalEstate { get; init; } = string.Empty;
    public string Apartment { get; init; } = string.Empty;
    public int Gil { get; init; }
    public int RetainerGil { get; init; }
    public int RetainerCount { get; init; }
    public string RetainerIdsJson { get; init; } = "[]";
    public string ValidationJson { get; init; } = "{}";
    public string FreshnessJson { get; init; } = "{}";
    public string SnapshotJson { get; init; } = "{}";
    public string UpdatedUtc { get; init; } = string.Empty;
    public string Trigger { get; init; } = string.Empty;
    public string TriggerDetail { get; init; } = string.Empty;
    public bool ImportedFromLegacy { get; init; }
}
