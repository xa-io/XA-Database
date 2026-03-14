namespace XADatabase.Database;

public static class Schema
{
    public const int CurrentVersion = 19;

    public static readonly string[] CreateStatements =
    {
        @"CREATE TABLE IF NOT EXISTS schema_version (
            version INTEGER NOT NULL
        )",

        @"CREATE TABLE IF NOT EXISTS xa_characters (
            content_id INTEGER PRIMARY KEY,
            character_name TEXT NOT NULL DEFAULT '',
            world TEXT NOT NULL DEFAULT '',
            datacenter TEXT NOT NULL DEFAULT '',
            region TEXT NOT NULL DEFAULT '',
            fc_id INTEGER NOT NULL DEFAULT 0,
            fc_name TEXT NOT NULL DEFAULT '',
            fc_tag TEXT NOT NULL DEFAULT '',
            fc_points INTEGER NOT NULL DEFAULT 0,
            fc_estate TEXT NOT NULL DEFAULT '',
            personal_estate TEXT NOT NULL DEFAULT '',
            shared_estates TEXT NOT NULL DEFAULT '',
            apartment TEXT NOT NULL DEFAULT '',
            gil INTEGER NOT NULL DEFAULT 0,
            retainer_gil INTEGER NOT NULL DEFAULT 0,
            retainer_count INTEGER NOT NULL DEFAULT 0,
            highest_job_level INTEGER NOT NULL DEFAULT 0,
            retainer_ids_json TEXT NOT NULL DEFAULT '[]',
            inventory_summaries_json TEXT NOT NULL DEFAULT '[]',
            freshness_json TEXT NOT NULL DEFAULT '{}',
            character_json TEXT NOT NULL DEFAULT '{}',
            free_company_json TEXT NOT NULL DEFAULT 'null',
            fc_members_json TEXT NOT NULL DEFAULT '[]',
            currencies_json TEXT NOT NULL DEFAULT '[]',
            jobs_json TEXT NOT NULL DEFAULT '[]',
            inventory_json TEXT NOT NULL DEFAULT '[]',
            saddlebag_json TEXT NOT NULL DEFAULT '[]',
            crystals_json TEXT NOT NULL DEFAULT '[]',
            armoury_json TEXT NOT NULL DEFAULT '[]',
            equipped_json TEXT NOT NULL DEFAULT '[]',
            items_json TEXT NOT NULL DEFAULT '[]',
            retainers_json TEXT NOT NULL DEFAULT '[]',
            listings_json TEXT NOT NULL DEFAULT '[]',
            retainer_items_json TEXT NOT NULL DEFAULT '[]',
            collections_json TEXT NOT NULL DEFAULT '[]',
            active_quests_json TEXT NOT NULL DEFAULT '[]',
            msq_milestones_json TEXT NOT NULL DEFAULT '[]',
            squadron_json TEXT NOT NULL DEFAULT 'null',
            voyages_json TEXT NOT NULL DEFAULT 'null',
            validation_json TEXT NOT NULL DEFAULT '{}',
            snapshot_version INTEGER NOT NULL DEFAULT 1,
            exported_utc TEXT NOT NULL DEFAULT '',
            trigger TEXT NOT NULL DEFAULT '',
            trigger_detail TEXT NOT NULL DEFAULT '',
            imported_from_legacy INTEGER NOT NULL DEFAULT 0,
            updated_utc TEXT NOT NULL DEFAULT ''
        )",

        @"CREATE INDEX IF NOT EXISTS idx_xa_characters_updated_utc
            ON xa_characters(updated_utc)",
    };
}
