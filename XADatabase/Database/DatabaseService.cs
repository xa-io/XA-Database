using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using XADatabase.Core.Storage;
using XADatabase.Core.Policies;
using XADatabase.Data;
using XADatabase.Models;

namespace XADatabase.Database;

public sealed class DatabaseService : IDisposable
{
    private const int MaxDatabaseBackups = 5;
    private static readonly Regex SafeIdentifier = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

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
    private readonly string connectionString;
    private readonly object connectionGate = new();
    private SqliteConnection? connection;
    public DatabaseHealthCheckResult LastHealthCheck { get; private set; } = new();
    public string SchemaInitializationError { get; private set; } = string.Empty;

    public DatabaseService(string pluginConfigDir)
    {
        Directory.CreateDirectory(pluginConfigDir);
        dbPath = Path.Combine(pluginConfigDir, "xa.db");
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Pooling = false,
        }.ToString();
    }

    public SqliteConnection GetConnection()
    {
        lock (connectionGate)
        {
            if (connection == null)
            {
                var created = new SqliteConnection(connectionString);
                created.Open();

                using var pragmaCmd = created.CreateCommand();
                pragmaCmd.CommandText = @"
                    PRAGMA journal_mode = WAL;
                    PRAGMA synchronous = NORMAL;
                    PRAGMA busy_timeout = 3000;
                    PRAGMA foreign_keys = ON;";
                pragmaCmd.ExecuteNonQuery();

                connection = created;
            }
            else if (connection.State != System.Data.ConnectionState.Open)
            {
                connection.Open();
            }

            return connection;
        }
    }

    public DatabaseHealthCheckResult RunHealthCheck()
    {
        var result = new DatabaseHealthCheckResult
        {
            DbPath = dbPath,
            CheckedAtUtc = SnapshotTime.Format(DateTime.UtcNow),
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
                integrityCmd.CommandText = "PRAGMA quick_check";
                using var reader = integrityCmd.ExecuteReader();
                var integrityLines = new List<string>();
                while (reader.Read())
                    integrityLines.Add(reader.GetString(0));

                result.IntegrityOk = integrityLines.Count == 1
                    && string.Equals(integrityLines[0], "ok", StringComparison.OrdinalIgnoreCase);
                if (!result.IntegrityOk)
                    result.Error = integrityLines.Count == 0
                        ? "quick_check returned no rows."
                        : string.Join("; ", integrityLines);
            }

            using (var probe = conn.BeginTransaction(deferred: false))
            {
                probe.Rollback();
            }

            result.WriteOk = true;
            result.Success = result.ReadOk && result.WriteOk && result.IntegrityOk;
            result.Summary = result.Success
                ? "Database read/write health check passed."
                : $"Database integrity check returned: {result.Error}";
        }
        catch (Exception ex)
        {
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
        try
        {
            InitializeSchemaInternal();
            SchemaInitializationError = string.Empty;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[XA] Failed to initialize database schema: {ex}");
            SchemaInitializationError = ex.Message;
        }
    }

    private void InitializeSchemaInternal()
    {
        var conn = GetConnection();

        // This repair must run before the current-version early return so databases
        // affected by the v16-to-v17 index rename recover on their next load.
        EnsureIndexes(conn);

        var currentVersion = GetSchemaVersion();
        var needsXaUpgrade = NeedsXaCharactersUpgrade();
        var needsRegionUpgrade = TableExists("xa_characters") && !ColumnExists("xa_characters", "region");
        var needsSharedEstatesUpgrade = TableExists("xa_characters") && !ColumnExists("xa_characters", "shared_estates");
        var needsRetainerGilRepair = TableExists("xa_characters") && HasNegativeRetainerGilRows();
        var needsFreeCompanyIdRecovery = currentVersion < 21 && TableExists("xa_characters");

        if (currentVersion >= Schema.CurrentVersion && !needsXaUpgrade && !needsRegionUpgrade && !needsSharedEstatesUpgrade && !needsRetainerGilRepair)
        {
            Plugin.Log.Information($"[XA] Database schema is up to date (v{currentVersion}).");
            return;
        }

        if (HasBackupWorthySchema(conn) && BackupDatabaseFile("pre-schema-migration") == null)
            throw new InvalidOperationException("Schema migration was refused because a database backup could not be created.");

        using var transaction = conn.BeginTransaction();
        try
        {
            ExecuteSchemaStatements(conn, transaction);

            if (currentVersion < 3 && TableExists(conn, transaction, "retainers"))
            {
                AddColumnIfMissing(conn, transaction, "retainers", "venture_id", "INTEGER NOT NULL DEFAULT 0");
                AddColumnIfMissing(conn, transaction, "retainers", "venture_complete_unix", "INTEGER NOT NULL DEFAULT 0");
                AddColumnIfMissing(conn, transaction, "retainers", "venture_status", "TEXT NOT NULL DEFAULT ''");
                AddColumnIfMissing(conn, transaction, "retainers", "venture_eta", "TEXT NOT NULL DEFAULT ''");
                Plugin.Log.Information("[XA] Applied schema migration v2 → v3 (retainer venture columns)");
            }

            if (currentVersion < 10 && TableExists(conn, transaction, "fc_members"))
            {
                AddColumnIfMissing(conn, transaction, "fc_members", "rank_name", "TEXT NOT NULL DEFAULT ''");
                Plugin.Log.Information($"[XA] Applied schema migration v{currentVersion} → v10 (fc_members.rank_name)");
            }

            if (currentVersion < 12 && TableExists(conn, transaction, "voyages"))
            {
                AddColumnIfMissing(conn, transaction, "voyages", "build_string", "TEXT NOT NULL DEFAULT ''");
                Plugin.Log.Information("[XA] Applied schema migration v11 → v12 (voyages build_string column)");
            }

            if (currentVersion < 13 && TableExists(conn, transaction, "free_companies"))
            {
                AddColumnIfMissing(conn, transaction, "free_companies", "fc_points", "INTEGER NOT NULL DEFAULT 0");
                AddColumnIfMissing(conn, transaction, "free_companies", "estate", "TEXT NOT NULL DEFAULT ''");
                Plugin.Log.Information("[XA] Applied schema migration v12 → v13 (fc_points, estate columns)");
            }

            if (currentVersion < 15 && TableExists(conn, transaction, "characters"))
            {
                AddColumnIfMissing(conn, transaction, "characters", "personal_estate", "TEXT NOT NULL DEFAULT ''");
                AddColumnIfMissing(conn, transaction, "characters", "apartment", "TEXT NOT NULL DEFAULT ''");
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

            if (needsXaUpgrade || needsRetainerGilRepair)
                RepairNegativeRetainerGilRows(conn, transaction);

            if (needsFreeCompanyIdRecovery)
            {
                var recoveredRows = RecoverFreeCompanyIds(conn, transaction, out var clearedRows);
                if (recoveredRows > 0)
                    Plugin.Log.Information($"[XA] Recovered {recoveredRows} free-company identity value(s) from preserved snapshot data.");
                if (clearedRows > 0)
                    Plugin.Log.Warning($"[XA] Cleared {clearedRows} unrecoverable clamped fc_id sentinel value(s); the correct FC identity requires a future authoritative live snapshot.");
                Plugin.Log.Information($"[XA] Applied schema migration v{currentVersion} → v21 (recover preserved FC identity values)");
            }

            EnsureIndexes(conn, transaction);
            UpsertSchemaVersion(conn, transaction);

            transaction.Commit();
            Plugin.Log.Information($"[XA] Database schema initialized to v{Schema.CurrentVersion} at {dbPath}");
        }
        catch
        {
            try
            {
                transaction.Rollback();
            }
            catch
            {
                // The original migration exception is the actionable failure.
            }

            throw;
        }
    }

    public SqliteTransaction? ActiveTransaction { get; private set; }

    public bool HasActiveTransaction => ActiveTransaction != null;

    public SqliteTransaction BeginTransaction()
    {
        if (ActiveTransaction != null)
            throw new InvalidOperationException("[XA] A database transaction is already active; nested transactions are not supported.");

        var tx = GetConnection().BeginTransaction();
        ActiveTransaction = tx;
        return tx;
    }

    public void CommitTransaction()
    {
        var transaction = ActiveTransaction;
        ActiveTransaction = null;
        if (transaction == null)
            return;

        try
        {
            transaction.Commit();
        }
        finally
        {
            transaction.Dispose();
        }
    }

    public void RollbackTransaction()
    {
        var transaction = ActiveTransaction;
        ActiveTransaction = null;
        if (transaction == null)
            return;

        try
        {
            transaction.Rollback();
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"[XA] Transaction rollback failed: {ex.Message}");
        }
        finally
        {
            transaction.Dispose();
        }
    }

    public int GetSchemaVersion()
    {
        var conn = GetConnection();
        if (!TableExists(conn, null, "schema_version"))
            return 0;

        using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT version FROM schema_version LIMIT 1";
        var result = checkCmd.ExecuteScalar();
        return result == null || result == DBNull.Value
            ? 0
            : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    public bool TableExists(string tableName)
    {
        return TableExists(GetConnection(), ActiveTransaction, tableName);
    }

    public bool ColumnExists(string tableName, string columnName)
    {
        return ColumnExists(GetConnection(), ActiveTransaction, tableName, columnName);
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
        long retainerGil,
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
        string updatedUtc,
        SqliteTransaction? transaction = null)
    {
        var conn = GetConnection();
        var normalizedFcEstate = HousingPlotSizeData.ApplySizeSuffix(fcEstate);
        var normalizedHousing = XaCharacterSnapshotRepository.NormalizeHousingPayload(personalEstate, sharedEstates, apartment);
        using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction ?? ActiveTransaction;
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
        AddId(cmd, "@cid", contentId);
        AddText(cmd, "@character_name", characterName);
        AddText(cmd, "@world", world);
        AddText(cmd, "@datacenter", datacenter);
        AddText(cmd, "@region", region);
        AddId(cmd, "@fc_id", fcId);
        AddText(cmd, "@fc_name", fcName);
        AddText(cmd, "@fc_tag", fcTag);
        AddInt(cmd, "@fc_points", fcPoints);
        AddText(cmd, "@fc_estate", normalizedFcEstate);
        AddText(cmd, "@personal_estate", normalizedHousing.PersonalEstate);
        AddText(cmd, "@shared_estates", normalizedHousing.SharedEstates);
        AddText(cmd, "@apartment", normalizedHousing.Apartment);
        AddInt(cmd, "@gil", gil);
        AddInt(cmd, "@retainer_gil", retainerGil);
        AddInt(cmd, "@retainer_count", retainerCount);
        AddInt(cmd, "@highest_job_level", highestJobLevel);
        AddText(cmd, "@retainer_ids_json", string.IsNullOrWhiteSpace(retainerIdsJson) ? "[]" : retainerIdsJson);
        AddText(cmd, "@inventory_summaries_json", sections.InventorySummariesJson);
        AddText(cmd, "@freshness_json", string.IsNullOrWhiteSpace(freshnessJson) ? "{}" : freshnessJson);
        AddText(cmd, "@character_json", sections.CharacterJson);
        AddText(cmd, "@free_company_json", sections.FreeCompanyJson);
        AddText(cmd, "@fc_members_json", sections.FcMembersJson);
        AddText(cmd, "@currencies_json", sections.CurrenciesJson);
        AddText(cmd, "@jobs_json", sections.JobsJson);
        AddText(cmd, "@inventory_json", sections.InventoryJson);
        AddText(cmd, "@saddlebag_json", sections.SaddlebagJson);
        AddText(cmd, "@crystals_json", sections.CrystalsJson);
        AddText(cmd, "@armoury_json", sections.ArmouryJson);
        AddText(cmd, "@equipped_json", sections.EquippedJson);
        AddText(cmd, "@items_json", sections.ItemsJson);
        AddText(cmd, "@retainers_json", sections.RetainersJson);
        AddText(cmd, "@listings_json", sections.ListingsJson);
        AddText(cmd, "@retainer_items_json", sections.RetainerItemsJson);
        AddText(cmd, "@collections_json", sections.CollectionsJson);
        AddText(cmd, "@active_quests_json", sections.ActiveQuestsJson);
        AddText(cmd, "@msq_milestones_json", sections.MsqMilestonesJson);
        AddText(cmd, "@squadron_json", sections.SquadronJson);
        AddText(cmd, "@voyages_json", sections.VoyagesJson);
        AddText(cmd, "@validation_json", sections.ValidationJson);
        AddInt(cmd, "@snapshot_version", snapshotVersion);
        AddText(cmd, "@exported_utc", exportedUtc);
        AddText(cmd, "@trigger", trigger);
        AddText(cmd, "@trigger_detail", triggerDetail);
        AddInt(cmd, "@imported_from_legacy", importedFromLegacy ? 1 : 0);
        AddText(cmd, "@updated_utc", updatedUtc);
        cmd.ExecuteNonQuery();
    }

    public bool TryDropLegacyTablesAfterImport(string backupPath, out string reason)
    {
        reason = string.Empty;
        var conn = GetConnection();

        if (!HasLegacyTables())
            return true;

        if (ActiveTransaction == null)
        {
            reason = "Refusing to drop legacy tables outside the explicit import transaction.";
            Plugin.Log.Warning($"[XA] {reason}");
            return false;
        }

        if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath))
        {
            reason = "Refusing to drop legacy tables because the pre-import backup is missing.";
            Plugin.Log.Warning($"[XA] {reason}");
            return false;
        }

        if (TableExists(conn, ActiveTransaction, "characters")
            && !ColumnExists(conn, ActiveTransaction, "characters", "content_id"))
        {
            reason = "Refusing to drop legacy tables because characters.content_id is unavailable for the completeness proof.";
            Plugin.Log.Warning($"[XA] {reason}");
            return false;
        }

        if (!TableExists(conn, ActiveTransaction, "xa_characters")
            || !ColumnExists(conn, ActiveTransaction, "xa_characters", "content_id"))
        {
            reason = "Refusing to drop legacy tables because xa_characters.content_id is unavailable for the completeness proof.";
            Plugin.Log.Warning($"[XA] {reason}");
            return false;
        }

        var legacyIds = GetContentIds(conn, ActiveTransaction, "characters");
        var migratedIds = GetContentIds(conn, ActiveTransaction, "xa_characters");
        if (!LegacyDropProof.CanDrop(legacyIds, migratedIds, true, true, out var missing))
        {
            reason = $"Refusing to drop legacy tables: {missing.Count} legacy character(s) are not present in xa_characters.";
            Plugin.Log.Warning($"[XA] {reason}");
            return false;
        }

        DropLegacyTablesInternal(conn, ActiveTransaction);
        return true;
    }

    public void DeleteCharacter(ulong contentId)
    {
        var backupPath = BackupDatabaseFile("pre-delete-character");
        if (backupPath == null)
            throw new InvalidOperationException("Character deletion was refused because a database backup could not be created.");

        var conn = GetConnection();
        if (ActiveTransaction != null)
        {
            DeleteCharacterInternal(conn, ActiveTransaction, (long)contentId);
            return;
        }

        using var transaction = conn.BeginTransaction();
        try
        {
            DeleteCharacterInternal(conn, transaction, (long)contentId);
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
        var backupPath = BackupDatabaseFile("pre-clear-all");
        if (backupPath == null)
            throw new InvalidOperationException("Clear-all was refused because a database backup could not be created.");

        var conn = GetConnection();
        if (ActiveTransaction != null)
        {
            ClearAllCharacterDataInternal(conn, ActiveTransaction);
            return;
        }

        using var transaction = conn.BeginTransaction();
        try
        {
            ClearAllCharacterDataInternal(conn, transaction);
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

    public string? BackupDatabaseFile(string reason)
    {
        try
        {
            if (!File.Exists(dbPath))
            {
                Plugin.Log.Warning($"[XA] Database backup skipped because the source file does not exist: {dbPath}");
                return null;
            }

            var source = GetConnection();
            var backupDirectory = Path.GetDirectoryName(dbPath) ?? ".";
            var safeReason = SanitizeBackupReason(reason);
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
            var backupPath = Path.Combine(backupDirectory, $"xa.db.{timestamp}.{safeReason}.bak");
            var suffix = 1;
            while (File.Exists(backupPath))
            {
                backupPath = Path.Combine(backupDirectory, $"xa.db.{timestamp}.{safeReason}.{suffix}.bak");
                suffix++;
            }

            var destinationConnectionString = new SqliteConnectionStringBuilder
            {
                DataSource = backupPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
            }.ToString();

            using (var destination = new SqliteConnection(destinationConnectionString))
            {
                destination.Open();
                source.BackupDatabase(destination);

                using var verifyCmd = destination.CreateCommand();
                verifyCmd.CommandText = "PRAGMA quick_check(1)";
                var verification = verifyCmd.ExecuteScalar()?.ToString() ?? string.Empty;
                if (!string.Equals(verification, "ok", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Backup verification returned '{verification}'.");
            }

            PruneDatabaseBackups(backupDirectory);
            Plugin.Log.Information($"[XA] Database backup created ({reason}): {backupPath}");
            return backupPath;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[XA] Database backup failed ({reason}): {ex}");
            return null;
        }
    }

    public WalCheckpointOutcome CheckpointWal(string mode = "PASSIVE", string reason = "")
    {
        if (HasActiveTransaction)
        {
            Plugin.Log.Warning("[XA] WAL checkpoint skipped because a transaction is still active.");
            return WalCheckpointOutcome.SkippedTransactionActive;
        }

        try
        {
            var conn = GetConnection();
            var normalizedMode = NormalizeWalCheckpointMode(mode);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA wal_checkpoint({normalizedMode})";

            using var reader = cmd.ExecuteReader();
            var busy = 0L;
            var logFrames = 0L;
            var checkpointedFrames = 0L;
            if (reader.Read())
            {
                busy = reader.GetInt64(0);
                logFrames = reader.GetInt64(1);
                checkpointedFrames = reader.GetInt64(2);
            }

            var reasonSuffix = string.IsNullOrWhiteSpace(reason) ? string.Empty : $" ({reason})";
            if (busy == 0 && checkpointedFrames >= logFrames)
            {
                Plugin.Log.Information($"[XA] WAL checkpoint {normalizedMode} completed{reasonSuffix}. Frames checkpointed: {checkpointedFrames}/{logFrames}.");
                return WalCheckpointOutcome.Merged;
            }

            if (busy != 0)
            {
                Plugin.Log.Warning($"[XA] WAL checkpoint {normalizedMode} was blocked (busy={busy}){reasonSuffix}. Frames checkpointed: {checkpointedFrames}/{logFrames}.");
                return WalCheckpointOutcome.Blocked;
            }

            Plugin.Log.Warning($"[XA] WAL checkpoint {normalizedMode} was partial{reasonSuffix}: {checkpointedFrames}/{logFrames} frames merged; the WAL could not be fully checkpointed.");
            return WalCheckpointOutcome.Partial;
        }
        catch (Exception ex)
        {
            var reasonSuffix = string.IsNullOrWhiteSpace(reason) ? string.Empty : $" ({reason})";
            Plugin.Log.Error($"[XA] WAL checkpoint error{reasonSuffix}: {ex.Message}");
            return WalCheckpointOutcome.Failed;
        }
    }

    public void Dispose()
    {
        RollbackTransaction();

        lock (connectionGate)
        {
            if (connection == null)
                return;

            if (connection.State == System.Data.ConnectionState.Open)
                CheckpointWal("TRUNCATE", "dispose");

            connection.Close();
            connection.Dispose();
            SqliteConnection.ClearPool(connection);
            connection = null;
        }
    }

    private static string NormalizeWalCheckpointMode(string mode)
    {
        if (string.Equals(mode, "FULL", StringComparison.OrdinalIgnoreCase))
            return "FULL";
        if (string.Equals(mode, "RESTART", StringComparison.OrdinalIgnoreCase))
            return "RESTART";
        if (string.Equals(mode, "TRUNCATE", StringComparison.OrdinalIgnoreCase))
            return "TRUNCATE";
        return "PASSIVE";
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

    private static bool TableExists(SqliteConnection conn, SqliteTransaction? transaction, string tableName)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = @name LIMIT 1";
        cmd.Parameters.AddTypedValue("@name", tableName);
        return cmd.ExecuteScalar() != null;
    }

    private static bool ColumnExists(
        SqliteConnection conn,
        SqliteTransaction? transaction,
        string tableName,
        string columnName)
    {
        if (!TableExists(conn, transaction, tableName))
            return false;

        using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $"PRAGMA table_info({Ident(tableName)})";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader["name"].ToString(), columnName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private void AddColumnIfMissing(
        SqliteConnection conn,
        SqliteTransaction transaction,
        string tableName,
        string columnName,
        string definition)
    {
        if (ColumnExists(conn, transaction, tableName, columnName))
            return;

        try
        {
            ExecuteNonQuery(conn, transaction, $"ALTER TABLE {Ident(tableName)} ADD COLUMN {Ident(columnName)} {definition}");
        }
        catch (SqliteException ex) when (
            SchemaMigrationPolicy.IsDuplicateColumnError(ex.SqliteErrorCode, ex.Message))
        {
            // A duplicate is the only safe idempotency race. Every other SQLite
            // error must roll back so the schema version is not advanced.
        }
    }

    private void EnsureIndexes(SqliteConnection conn, SqliteTransaction? transaction = null)
    {
        if (!TableExists(conn, transaction, "xa_characters")
            || !ColumnExists(conn, transaction, "xa_characters", "updated_utc"))
            return;

        using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"CREATE INDEX IF NOT EXISTS idx_xa_characters_updated_utc
            ON xa_characters(updated_utc)";
        cmd.ExecuteNonQuery();
    }

    private static bool HasBackupWorthySchema(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT 1
            FROM sqlite_master
            WHERE type = 'table'
              AND name NOT LIKE 'sqlite_%'
              AND name <> 'schema_version'
            LIMIT 1";
        return cmd.ExecuteScalar() != null;
    }

    private static HashSet<long> GetContentIds(
        SqliteConnection conn,
        SqliteTransaction transaction,
        string tableName)
    {
        var ids = new HashSet<long>();
        if (!TableExists(conn, transaction, tableName)
            || !ColumnExists(conn, transaction, tableName, "content_id"))
            return ids;

        using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $"SELECT DISTINCT content_id FROM {Ident(tableName)} WHERE content_id IS NOT NULL";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            ids.Add(Convert.ToInt64(reader[0], CultureInfo.InvariantCulture));

        return ids;
    }

    private static string SanitizeBackupReason(string reason)
    {
        var source = string.IsNullOrWhiteSpace(reason) ? "manual" : reason.Trim();
        var safe = new string(source
            .Select(static character => char.IsLetterOrDigit(character) || character is '-' or '_'
                ? character
                : '-')
            .ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "manual" : safe;
    }

    private static void PruneDatabaseBackups(string backupDirectory)
    {
        var expectedDirectory = Path.GetFullPath(backupDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var backups = Directory
            .EnumerateFiles(expectedDirectory, "xa.db.*.bak", SearchOption.TopDirectoryOnly)
            .Select(static path => new FileInfo(path))
            .OrderByDescending(static file => file.LastWriteTimeUtc)
            .ThenByDescending(static file => file.Name, StringComparer.Ordinal)
            .ToList();

        foreach (var staleBackup in backups.Skip(MaxDatabaseBackups))
        {
            var actualDirectory = Path.GetFullPath(staleBackup.DirectoryName ?? string.Empty)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.Equals(actualDirectory, expectedDirectory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Refusing to prune a database backup outside {expectedDirectory}.");

            staleBackup.Delete();
        }
    }

    private void UpsertSchemaVersion(SqliteConnection conn, SqliteTransaction transaction)
    {
        using var versionCmd = conn.CreateCommand();
        versionCmd.Transaction = transaction;
        versionCmd.CommandText = @"
            DELETE FROM schema_version;
            INSERT INTO schema_version (version) VALUES (@version)";
        versionCmd.Parameters.AddTypedValue("@version", Schema.CurrentVersion);
        versionCmd.ExecuteNonQuery();
    }

    private void DeleteCharacterInternal(SqliteConnection conn, SqliteTransaction transaction, long contentId)
    {
        var fcId = GetCharacterFreeCompanyId(conn, transaction, contentId);
        var replacementContentId = GetReplacementFreeCompanyOwner(conn, transaction, contentId, fcId);
        var retainerIds = GetCharacterRetainerIds(conn, transaction, contentId);

        DeleteRowsByContentId(conn, transaction, "currency_balances", contentId);
        DeleteRowsByContentId(conn, transaction, "currency_history", contentId);
        DeleteRowsByContentId(conn, transaction, "job_levels", contentId);
        DeleteRowsByContentId(conn, transaction, "inventory_summaries", contentId);
        DeleteRowsByContentId(conn, transaction, "container_items", contentId);
        DeleteRowsByContentId(conn, transaction, "collection_summaries", contentId);
        DeleteRowsByContentId(conn, transaction, "active_quests", contentId);
        DeleteRowsByContentId(conn, transaction, "msq_milestones", contentId);
        DeleteRowsByContentId(conn, transaction, "squadron_info", contentId);
        DeleteRowsByContentId(conn, transaction, "squadron_members", contentId);

        foreach (var retainerId in retainerIds)
        {
            DeleteRowsByColumnValue(conn, transaction, "retainer_listings", "retainer_id", retainerId);
            DeleteRowsByColumnValue(conn, transaction, "retainer_items", "retainer_id", retainerId);
            DeleteRowsByColumnValue(conn, transaction, "retainer_sales", "retainer_id", retainerId);
        }

        DeleteRowsByContentId(conn, transaction, "retainers", contentId);
        DeleteRowsByContentId(conn, transaction, "characters", contentId);
        DeleteRowsByContentId(conn, transaction, "xa_characters", contentId);

        if (fcId == 0)
            return;

        if (replacementContentId != 0)
        {
            ReassignFreeCompanyOwner(conn, transaction, fcId, replacementContentId);
            return;
        }

        DeleteRowsByColumnValue(conn, transaction, "fc_members", "fc_id", fcId);
        DeleteRowsByColumnValue(conn, transaction, "voyages", "fc_id", fcId);
        DeleteRowsByColumnValue(conn, transaction, "free_companies", "fc_id", fcId);
    }

    private long GetCharacterFreeCompanyId(SqliteConnection conn, SqliteTransaction transaction, long contentId)
    {
        if (TableExists(conn, transaction, "xa_characters"))
        {
            using var xaCmd = conn.CreateCommand();
            xaCmd.Transaction = transaction;
            xaCmd.CommandText = "SELECT fc_id FROM xa_characters WHERE content_id = @cid LIMIT 1";
            xaCmd.Parameters.AddTypedValue("@cid", contentId);
            var xaResult = xaCmd.ExecuteScalar();
            if (xaResult != null && xaResult != DBNull.Value)
                return Convert.ToInt64(xaResult);
        }

        if (TableExists(conn, transaction, "free_companies"))
        {
            using var legacyCmd = conn.CreateCommand();
            legacyCmd.Transaction = transaction;
            legacyCmd.CommandText = "SELECT fc_id FROM free_companies WHERE content_id = @cid LIMIT 1";
            legacyCmd.Parameters.AddTypedValue("@cid", contentId);
            var legacyResult = legacyCmd.ExecuteScalar();
            if (legacyResult != null && legacyResult != DBNull.Value)
                return Convert.ToInt64(legacyResult);
        }

        return 0;
    }

    private long GetReplacementFreeCompanyOwner(SqliteConnection conn, SqliteTransaction transaction, long deletedContentId, long fcId)
    {
        if (fcId == 0 || !TableExists(conn, transaction, "xa_characters"))
            return 0;

        using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            SELECT content_id
            FROM xa_characters
            WHERE fc_id = @fcid AND content_id != @cid
            ORDER BY updated_utc DESC, content_id ASC
            LIMIT 1";
        cmd.Parameters.AddTypedValue("@fcid", fcId);
        cmd.Parameters.AddTypedValue("@cid", deletedContentId);
        var result = cmd.ExecuteScalar();
        return result != null && result != DBNull.Value ? Convert.ToInt64(result) : 0;
    }

    private List<long> GetCharacterRetainerIds(SqliteConnection conn, SqliteTransaction transaction, long contentId)
    {
        var retainerIds = new List<long>();
        if (!TableExists(conn, transaction, "retainers"))
            return retainerIds;

        using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "SELECT retainer_id FROM retainers WHERE content_id = @cid";
        cmd.Parameters.AddTypedValue("@cid", contentId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            retainerIds.Add((long)reader["retainer_id"]);

        return retainerIds;
    }

    private void DeleteRowsByContentId(SqliteConnection conn, SqliteTransaction transaction, string tableName, long contentId)
    {
        DeleteRowsByColumnValue(conn, transaction, tableName, "content_id", contentId);
    }

    private void DeleteRowsByColumnValue(SqliteConnection conn, SqliteTransaction transaction, string tableName, string columnName, long value)
    {
        if (!TableExists(conn, transaction, tableName))
            return;

        using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $"DELETE FROM {Ident(tableName)} WHERE {Ident(columnName)} = @value";
        cmd.Parameters.AddTypedValue("@value", value);
        cmd.ExecuteNonQuery();
    }

    private void ReassignFreeCompanyOwner(SqliteConnection conn, SqliteTransaction transaction, long fcId, long contentId)
    {
        if (!TableExists(conn, transaction, "free_companies"))
            return;

        using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "UPDATE free_companies SET content_id = @cid WHERE fc_id = @fcid";
        cmd.Parameters.AddTypedValue("@cid", contentId);
        cmd.Parameters.AddTypedValue("@fcid", fcId);
        cmd.ExecuteNonQuery();
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
        if (!TableExists(conn, transaction, "xa_characters")
            || ColumnExists(conn, transaction, "xa_characters", "region"))
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
            updateCmd.Parameters.AddTypedValue("@cid", row.ContentId);
            updateCmd.Parameters.AddTypedValue("@region", row.Region);
            updateCmd.ExecuteNonQuery();
        }
    }

    private void AddXaCharacterSharedEstatesColumn(SqliteConnection conn, SqliteTransaction transaction)
    {
        if (!TableExists(conn, transaction, "xa_characters")
            || ColumnExists(conn, transaction, "xa_characters", "shared_estates"))
            return;

        ExecuteNonQuery(conn, transaction, "ALTER TABLE xa_characters ADD COLUMN shared_estates TEXT NOT NULL DEFAULT ''");
    }

    private bool HasNegativeRetainerGilRows()
    {
        if (!TableExists("xa_characters") || !ColumnExists("xa_characters", "retainer_gil"))
            return false;

        var conn = GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM xa_characters WHERE retainer_gil < 0 LIMIT 1";
        return cmd.ExecuteScalar() != null;
    }

    private void RepairNegativeRetainerGilRows(SqliteConnection conn, SqliteTransaction transaction)
    {
        if (!TableExists(conn, transaction, "xa_characters"))
            return;

        var repairs = new List<(long ContentId, string CharacterName, string World, string Datacenter, string Region, string PersonalEstate, string SharedEstates, string Apartment, int Gil, long RetainerGil)>();

        using var selectCmd = conn.CreateCommand();
        selectCmd.Transaction = transaction;
        selectCmd.CommandText = @"
            SELECT content_id,
                   character_name,
                   world,
                   datacenter,
                   region,
                   personal_estate,
                   shared_estates,
                   apartment,
                   gil,
                   retainers_json
            FROM xa_characters
            WHERE retainer_gil < 0";
        using var reader = selectCmd.ExecuteReader();
        while (reader.Read())
        {
            var contentId = (long)reader["content_id"];
            var retainers = XaCharacterSnapshotRepository.NormalizeRetainerPayload(
                DeserializeList<RetainerEntry>(reader["retainers_json"].ToString() ?? "[]"),
                Enumerable.Empty<RetainerListingEntry>(),
                Enumerable.Empty<RetainerInventoryItem>(),
                (ulong)contentId).Retainers;
            var repairedRetainerGil = retainers.Sum(retainer => (long)retainer.Gil);
            if (repairedRetainerGil <= 0)
                continue;

            repairs.Add((
                ContentId: contentId,
                CharacterName: reader["character_name"].ToString() ?? string.Empty,
                World: reader["world"].ToString() ?? string.Empty,
                Datacenter: reader["datacenter"].ToString() ?? string.Empty,
                Region: reader["region"].ToString() ?? string.Empty,
                PersonalEstate: reader["personal_estate"].ToString() ?? string.Empty,
                SharedEstates: reader["shared_estates"].ToString() ?? string.Empty,
                Apartment: reader["apartment"].ToString() ?? string.Empty,
                Gil: ReadSqliteInt32(reader, "gil"),
                RetainerGil: repairedRetainerGil));
        }

        reader.Close();

        foreach (var repair in repairs)
        {
            var normalizedHousing = XaCharacterSnapshotRepository.NormalizeHousingPayload(repair.PersonalEstate, repair.SharedEstates, repair.Apartment);
            var characterJson = JsonSerializer.Serialize(new
            {
                contentId = (ulong)repair.ContentId,
                name = repair.CharacterName,
                world = repair.World,
                datacenter = XaCharacterSnapshotRepository.ResolveDatacenter(repair.World, repair.Datacenter),
                region = XaCharacterSnapshotRepository.ResolveRegion(repair.World, repair.Region),
                personalEstate = normalizedHousing.PersonalEstate,
                sharedEstates = normalizedHousing.SharedEstates,
                apartment = normalizedHousing.Apartment,
                gil = repair.Gil,
                retainerGil = repair.RetainerGil,
            });

            using var updateCmd = conn.CreateCommand();
            updateCmd.Transaction = transaction;
            updateCmd.CommandText = @"
                UPDATE xa_characters
                SET retainer_gil = @retainer_gil,
                    character_json = @character_json
                WHERE content_id = @cid";
            updateCmd.Parameters.AddTypedValue("@cid", repair.ContentId);
            updateCmd.Parameters.AddTypedValue("@retainer_gil", repair.RetainerGil);
            updateCmd.Parameters.AddTypedValue("@character_json", characterJson);
            updateCmd.ExecuteNonQuery();
        }

        if (repairs.Count > 0)
            Plugin.Log.Information($"[XA] Repaired negative retainer_gil for {repairs.Count} xa_characters row(s).");
    }

    private bool HasLegacyTables() => LegacyTables.Any(TableExists);

    private void UpgradeXaCharactersTable(SqliteConnection conn, SqliteTransaction transaction)
    {
        if (!TableExists(conn, transaction, "xa_characters"))
            return;

        // SQLite keeps index names when a table is renamed. Free the known name
        // first so the rebuilt table receives its own updated_utc index.
        ExecuteNonQuery(conn, transaction, "DROP INDEX IF EXISTS idx_xa_characters_updated_utc");
        ExecuteNonQuery(conn, transaction, "ALTER TABLE xa_characters RENAME TO xa_characters_v16_backup");
        ExecuteSchemaStatements(conn, transaction);

        long sourceRowCount;
        using (var countCmd = conn.CreateCommand())
        {
            countCmd.Transaction = transaction;
            countCmd.CommandText = "SELECT COUNT(*) FROM xa_characters_v16_backup";
            sourceRowCount = Convert.ToInt64(countCmd.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
        }

        Plugin.Log.Information($"[XA] Streaming {sourceRowCount} xa_characters v16 row(s) into the current snapshot schema.");
        long? lastSignedContentId = null;
        while (true)
        {
            LegacyXaCharacterMigrationRow legacyRow;
            using (var rowCmd = conn.CreateCommand())
            {
                rowCmd.Transaction = transaction;
                rowCmd.CommandText = lastSignedContentId.HasValue
                    ? "SELECT * FROM xa_characters_v16_backup WHERE content_id > @after ORDER BY content_id LIMIT 1"
                    : "SELECT * FROM xa_characters_v16_backup ORDER BY content_id LIMIT 1";
                if (lastSignedContentId.HasValue)
                    AddInt(rowCmd, "@after", lastSignedContentId.Value);
                using var rowReader = rowCmd.ExecuteReader();
                if (!rowReader.Read())
                    break;
                legacyRow = ReadLegacyMigrationRow(rowReader);
            }
            lastSignedContentId = unchecked((long)legacyRow.ContentId);

            var datacenter = XaCharacterSnapshotRepository.ResolveDatacenter(legacyRow.World, legacyRow.Datacenter);
            var region = XaCharacterSnapshotRepository.ResolveRegion(legacyRow.World);
            var trigger = string.IsNullOrWhiteSpace(legacyRow.Trigger)
                ? ReadLegacySnapshotString(legacyRow.SnapshotJson, "trigger", string.Empty)
                : legacyRow.Trigger;
            var triggerDetail = string.IsNullOrWhiteSpace(legacyRow.TriggerDetail)
                ? ReadLegacySnapshotString(legacyRow.SnapshotJson, "triggerDetail", string.Empty)
                : legacyRow.TriggerDetail;
            var importedFromLegacy = legacyRow.ImportedFromLegacy || ReadLegacySnapshotBool(legacyRow.SnapshotJson, "importedFromLegacy", false);
            var snapshotVersion = ReadLegacySnapshotInt(legacyRow.SnapshotJson, "snapshotVersion", Schema.CurrentSnapshotVersion);
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
                XaCharacterSnapshotRepository.BuildRetainerOwnerReferencesJson(normalizedRetainers, legacyRow.ContentId),
                legacyRow.FreshnessJson,
                sections,
                snapshotVersion,
                exportedUtc,
                trigger,
                triggerDetail,
                importedFromLegacy,
                legacyRow.UpdatedUtc,
                transaction);
        }
        ExecuteNonQuery(conn, transaction, "DROP TABLE IF EXISTS xa_characters_v16_backup");
        EnsureIndexes(conn, transaction);
    }

    private void DropLegacyTablesInternal(SqliteConnection conn, SqliteTransaction transaction)
    {
        ExecuteNonQuery(conn, transaction, "PRAGMA defer_foreign_keys = ON");

        foreach (var table in LegacyTables)
            ExecuteNonQuery(conn, transaction, $"DROP TABLE IF EXISTS {Ident(table)}");
    }

    private void ClearAllCharacterDataInternal(SqliteConnection conn, SqliteTransaction transaction)
    {
        if (TableExists(conn, transaction, "xa_characters"))
        {
            using var deleteCmd = conn.CreateCommand();
            deleteCmd.Transaction = transaction;
            deleteCmd.CommandText = "DELETE FROM xa_characters";
            deleteCmd.ExecuteNonQuery();
        }

        DropLegacyTablesInternal(conn, transaction);
    }

    private static int RecoverFreeCompanyIds(
        SqliteConnection conn,
        SqliteTransaction transaction,
        out int clearedRows)
    {
        const long clampedSentinel = long.MaxValue;
        var candidates = new List<(long ContentId, long StoredFcId, long RecoveredFcId, bool ClearOnly)>();

        using (var selectCmd = conn.CreateCommand())
        {
            selectCmd.Transaction = transaction;
            selectCmd.CommandText = @"
                SELECT content_id, fc_id, free_company_json
                FROM xa_characters
                WHERE fc_id = 0 OR fc_id = @clampedSentinel";
            selectCmd.Parameters.Add("@clampedSentinel", SqliteType.Integer).Value = clampedSentinel;

            using var reader = selectCmd.ExecuteReader();
            while (reader.Read())
            {
                var contentId = reader.GetInt64(0);
                var storedFcId = reader.GetInt64(1);
                var recoveredFcId = ReadFreeCompanyId(reader.GetString(2));
                if (recoveredFcId != 0)
                {
                    var encodedFcId = SqliteIdentity.Encode(recoveredFcId);
                    if (encodedFcId != storedFcId)
                        candidates.Add((contentId, storedFcId, encodedFcId, false));
                }
                else if (storedFcId == clampedSentinel)
                {
                    candidates.Add((contentId, storedFcId, 0, true));
                }
            }
        }

        var recoveredRows = 0;
        clearedRows = 0;
        foreach (var candidate in candidates)
        {
            using var updateCmd = conn.CreateCommand();
            updateCmd.Transaction = transaction;
            updateCmd.CommandText = @"
                UPDATE xa_characters
                SET fc_id = @recoveredFcId
                WHERE content_id = @contentId AND fc_id = @storedFcId";
            updateCmd.Parameters.Add("@recoveredFcId", SqliteType.Integer).Value = candidate.RecoveredFcId;
            updateCmd.Parameters.Add("@contentId", SqliteType.Integer).Value = candidate.ContentId;
            updateCmd.Parameters.Add("@storedFcId", SqliteType.Integer).Value = candidate.StoredFcId;
            var changedRows = updateCmd.ExecuteNonQuery();
            if (candidate.ClearOnly)
                clearedRows += changedRows;
            else
                recoveredRows += changedRows;
        }

        return recoveredRows;
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

    private static string ReadOptionalText(
        SqliteDataReader reader,
        HashSet<string> columns,
        string columnName,
        string fallback)
    {
        if (!columns.Contains(columnName))
            return fallback;

        var value = reader[columnName];
        return value == null || value == DBNull.Value
            ? fallback
            : value.ToString() ?? fallback;
    }

    private static void AddId(SqliteCommand command, string name, ulong value)
    {
        command.Parameters.Add(name, SqliteType.Integer).Value = SqliteIdentity.Encode(value);
    }

    private static void AddInt(SqliteCommand command, string name, long value)
    {
        command.Parameters.Add(name, SqliteType.Integer).Value = value;
    }

    private static void AddText(SqliteCommand command, string name, string? value)
    {
        command.Parameters.Add(name, SqliteType.Text).Value = value ?? string.Empty;
    }

    private static string Ident(string name)
    {
        if (!SafeIdentifier.IsMatch(name))
            throw new ArgumentException($"Unsafe SQL identifier: '{name}'.", nameof(name));
        return name;
    }

    private static LegacyXaCharacterMigrationRow ReadLegacyMigrationRow(SqliteDataReader reader)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < reader.FieldCount; index++)
            columns.Add(reader.GetName(index));

        var rawContentId = ReadSqliteInt64(reader, columns, "content_id", long.MinValue);
        if (rawContentId == long.MinValue)
            throw new InvalidDataException("The legacy xa_characters table has no readable content_id column.");

        return new LegacyXaCharacterMigrationRow
        {
            ContentId = unchecked((ulong)rawContentId),
            CharacterName = ReadOptionalText(reader, columns, "character_name", string.Empty),
            World = ReadOptionalText(reader, columns, "world", string.Empty),
            Datacenter = ReadOptionalText(reader, columns, "datacenter", string.Empty),
            FcName = ReadOptionalText(reader, columns, "fc_name", string.Empty),
            FcTag = ReadOptionalText(reader, columns, "fc_tag", string.Empty),
            FcPoints = ReadSqliteInt32(reader, columns, "fc_points"),
            FcEstate = ReadOptionalText(reader, columns, "fc_estate", string.Empty),
            PersonalEstate = ReadOptionalText(reader, columns, "personal_estate", string.Empty),
            Apartment = ReadOptionalText(reader, columns, "apartment", string.Empty),
            Gil = ReadSqliteInt32(reader, columns, "gil"),
            RetainerGil = ReadSqliteInt64(reader, columns, "retainer_gil"),
            RetainerCount = ReadSqliteInt32(reader, columns, "retainer_count"),
            RetainerIdsJson = ReadOptionalText(reader, columns, "retainer_ids_json", "[]"),
            ValidationJson = ReadOptionalText(reader, columns, "validation_json", "{}"),
            FreshnessJson = ReadOptionalText(reader, columns, "freshness_json", "{}"),
            SnapshotJson = ReadOptionalText(reader, columns, "snapshot_json", "{}"),
            UpdatedUtc = ReadOptionalText(reader, columns, "updated_utc", string.Empty),
            Trigger = ReadOptionalText(reader, columns, "trigger", string.Empty),
            TriggerDetail = ReadOptionalText(reader, columns, "trigger_detail", string.Empty),
            ImportedFromLegacy = ReadSqliteInt32(reader, columns, "imported_from_legacy") == 1,
        };
    }

    private static int ReadSqliteInt32(
        SqliteDataReader reader,
        HashSet<string> columns,
        string columnName,
        int fallback = 0)
    {
        return columns.Contains(columnName)
            ? ReadSqliteInt32(reader, columnName, fallback)
            : fallback;
    }

    private static long ReadSqliteInt64(
        SqliteDataReader reader,
        HashSet<string> columns,
        string columnName,
        long fallback = 0)
    {
        return columns.Contains(columnName)
            ? ReadSqliteInt64(reader, columnName, fallback)
            : fallback;
    }

    private static int ReadSqliteInt32(SqliteDataReader reader, string columnName, int fallback = 0)
    {
        var value = reader[columnName];
        if (value == null || value == DBNull.Value)
            return fallback;

        if (value is string textValue)
        {
            if (string.IsNullOrWhiteSpace(textValue))
                return fallback;

            if (int.TryParse(textValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedTextValue))
                return parsedTextValue;

            return fallback;
        }

        try
        {
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return fallback;
        }
    }

    private static long ReadSqliteInt64(SqliteDataReader reader, string columnName, long fallback = 0)
    {
        var value = reader[columnName];
        if (value == null || value == DBNull.Value)
            return fallback;

        if (value is string textValue)
        {
            if (string.IsNullOrWhiteSpace(textValue))
                return fallback;

            if (long.TryParse(textValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedTextValue))
                return parsedTextValue;

            return fallback;
        }

        try
        {
            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return fallback;
        }
    }

}

public enum WalCheckpointOutcome
{
    Merged,
    Partial,
    Blocked,
    SkippedTransactionActive,
    Failed,
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
    public long RetainerGil { get; init; }
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
