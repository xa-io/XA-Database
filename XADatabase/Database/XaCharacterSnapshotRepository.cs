using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using XADatabase.Core.Localization;
using XADatabase.Core.Storage;
using XADatabase.Core.Policies;
using XADatabase.Data;
using XADatabase.Models;

namespace XADatabase.Database;

public sealed class XaCharacterSnapshotRepository
{
    private readonly DatabaseService db;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
    private static readonly Regex ParentheticalTextRegex = new(@"\s*[(（][^)）]*[)）]", RegexOptions.Compiled);
    private static readonly Regex OwnerSuffixRegex = new(@"\s*\[[^\]]+\]\s*$", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    public XaCharacterSnapshotRepository(DatabaseService db)
    {
        this.db = db;
    }

    public CharacterRow? GetCharacter(ulong contentId)
    {
        var conn = db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT content_id, character_name, world, datacenter, region, updated_utc, exported_utc, personal_estate, shared_estates, apartment
            FROM xa_characters
            WHERE content_id = @cid
            LIMIT 1";
        cmd.Parameters.AddTypedValue("@cid", contentId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;

        var normalizedHousing = NormalizeHousingPayload(
            reader["personal_estate"].ToString() ?? string.Empty,
            reader["shared_estates"].ToString() ?? string.Empty,
            reader["apartment"].ToString() ?? string.Empty);

        return new CharacterRow
        {
            ContentId = SqliteIdentity.Decode((long)reader["content_id"]),
            Name = reader["character_name"].ToString() ?? string.Empty,
            World = reader["world"].ToString() ?? string.Empty,
            Datacenter = ResolveDatacenter(reader["world"].ToString() ?? string.Empty, reader["datacenter"].ToString() ?? string.Empty),
            Region = ResolveRegion(reader["world"].ToString() ?? string.Empty, reader["region"].ToString() ?? string.Empty),
            LastSeenUtc = reader["updated_utc"].ToString() ?? string.Empty,
            CreatedUtc = reader["exported_utc"].ToString() ?? string.Empty,
            PersonalEstate = normalizedHousing.PersonalEstate,
            SharedEstates = normalizedHousing.SharedEstates,
            Apartment = normalizedHousing.Apartment,
        };
    }

    public XaCharacterSnapshotData? GetSnapshot(ulong contentId)
    {
        var conn = db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT *
            FROM xa_characters
            WHERE content_id = @cid
            LIMIT 1";
        cmd.Parameters.AddTypedValue("@cid", contentId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;

        return Parse(ReadRow(reader));
    }

    public List<XaCharacterSnapshotData> GetAllSnapshots()
    {
        var results = new List<XaCharacterSnapshotData>();
        var conn = db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT *
            FROM xa_characters
            ORDER BY updated_utc DESC, character_name ASC";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            results.Add(Parse(ReadRow(reader)));
        return results;
    }

    public List<XaCharacterRosterData> GetRoster()
    {
        var results = new List<XaCharacterRosterData>();
        var conn = db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT content_id, character_name, world, datacenter, region,
                   snapshot_version, updated_utc, jobs_json
            FROM xa_characters
            ORDER BY updated_utc DESC, character_name ASC";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var parseErrors = new Dictionary<string, string>(StringComparer.Ordinal);
            results.Add(new XaCharacterRosterData
            {
                ContentId = SqliteIdentity.Decode((long)reader["content_id"]),
                CharacterName = reader["character_name"].ToString() ?? string.Empty,
                World = reader["world"].ToString() ?? string.Empty,
                Datacenter = ResolveDatacenter(reader["world"].ToString() ?? string.Empty, reader["datacenter"].ToString() ?? string.Empty),
                Region = ResolveRegion(reader["world"].ToString() ?? string.Empty, reader["region"].ToString() ?? string.Empty),
                SnapshotVersion = ReadInt32(reader, "snapshot_version"),
                UpdatedUtc = reader["updated_utc"].ToString() ?? string.Empty,
                Jobs = NormalizeJobs(DeserializeListTracked<JobEntry>(reader["jobs_json"].ToString(), "jobs_json", parseErrors)),
                ParseErrors = parseErrors,
            });
        }
        return results;
    }

    public List<XaCharacterItemsData> GetAllItemSections()
    {
        var results = new List<XaCharacterItemsData>();
        var conn = db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT content_id, character_name, world, updated_utc, snapshot_version,
                   inventory_json, saddlebag_json, crystals_json, armoury_json,
                   equipped_json, items_json, retainers_json, retainer_items_json
            FROM xa_characters
            ORDER BY updated_utc DESC, character_name ASC";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var parseErrors = new Dictionary<string, string>(StringComparer.Ordinal);
            var snapshotVersion = ReadInt32(reader, "snapshot_version");
            var inventory = DeserializeListTracked<ContainerItemEntry>(reader["inventory_json"].ToString(), "inventory_json", parseErrors);
            var saddlebag = DeserializeListTracked<ContainerItemEntry>(reader["saddlebag_json"].ToString(), "saddlebag_json", parseErrors);
            var crystals = DeserializeListTracked<ContainerItemEntry>(reader["crystals_json"].ToString(), "crystals_json", parseErrors);
            var armoury = DeserializeListTracked<ContainerItemEntry>(reader["armoury_json"].ToString(), "armoury_json", parseErrors);
            var equipped = DeserializeListTracked<ContainerItemEntry>(reader["equipped_json"].ToString(), "equipped_json", parseErrors);
            var itemsJson = DeserializeListTracked<ContainerItemEntry>(reader["items_json"].ToString(), "items_json", parseErrors);
            var allItems = snapshotVersion >= 3
                ? inventory.Concat(saddlebag).Concat(crystals).Concat(armoury).Concat(equipped).Concat(itemsJson).ToList()
                : itemsJson.Count > 0
                    ? itemsJson
                    : inventory.Concat(saddlebag).Concat(crystals).Concat(armoury).Concat(equipped).ToList();

            results.Add(new XaCharacterItemsData
            {
                ContentId = SqliteIdentity.Decode((long)reader["content_id"]),
                CharacterName = reader["character_name"].ToString() ?? string.Empty,
                World = reader["world"].ToString() ?? string.Empty,
                UpdatedUtc = reader["updated_utc"].ToString() ?? string.Empty,
                AllItems = allItems,
                Retainers = DeserializeListTracked<RetainerEntry>(reader["retainers_json"].ToString(), "retainers_json", parseErrors),
                RetainerItems = DeserializeListTracked<RetainerInventoryItem>(reader["retainer_items_json"].ToString(), "retainer_items_json", parseErrors),
                ParseErrors = parseErrors,
            });
        }
        return results;
    }

    public (int FcGil, bool Observed, ulong SourceContentId) GetLatestObservedFcGil(ulong fcId)
    {
        var conn = db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT content_id,
                   json_extract(free_company_json, '$.FcGil') AS fc_gil,
                   json_extract(free_company_json, '$.FcGilObserved') AS observed
            FROM xa_characters
            WHERE fc_id = @fcid
              AND free_company_json IS NOT NULL
              AND free_company_json != 'null'
              AND json_valid(free_company_json)
            ORDER BY updated_utc DESC
            LIMIT 8";
        cmd.Parameters.Add("@fcid", SqliteType.Integer).Value = SqliteIdentity.Encode(fcId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var gil = ReadInt32(reader, "fc_gil");
            var observed = ReadInt32(reader, "observed") == 1 || gil > 0;
            if (observed)
                return (gil, true, SqliteIdentity.Decode((long)reader["content_id"]));
        }

        return (0, false, 0);
    }

    /// <summary>Clear one FC's saved chest gil without refreshing unrelated snapshot data.</summary>
    public int ResetFreeCompanyGil(ulong contentId, ulong expectedFcId)
    {
        if (contentId == 0 || expectedFcId == 0)
            throw new InvalidOperationException("A saved character with a known FC identity is required.");
        if (db.HasActiveTransaction)
            throw new InvalidOperationException("A database save is already in progress. Try again after it finishes.");

        var conn = db.GetConnection();
        using var transaction = conn.BeginTransaction();
        var updates = new List<(long ContentId, string Json)>();
        var selectedFound = false;
        using (var select = conn.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT content_id, free_company_json FROM xa_characters WHERE fc_id = @fcid";
            select.Parameters.AddTypedValue("@fcid", expectedFcId);
            using var reader = select.ExecuteReader();
            while (reader.Read())
            {
                var storedContentId = reader.GetInt64(0);
                var isSelected = SqliteIdentity.Decode(storedContentId) == contentId;
                var json = reader.IsDBNull(1) ? "null" : reader.GetString(1);
                var fc = JsonSerializer.Deserialize<FreeCompanyEntry>(json, JsonOptions);
                if (fc == null && !isSelected)
                    continue;
                if (fc == null || fc.FcId != expectedFcId)
                    throw new InvalidOperationException("Saved FC identity changed or is incomplete. Refresh the Dashboard and try again.");

                using var document = JsonDocument.Parse(json);
                if (document.RootElement.EnumerateObject().Any(property =>
                    property.Name.Equals(nameof(FreeCompanyEntry.FcId), StringComparison.OrdinalIgnoreCase)
                    && (property.Value.ValueKind != JsonValueKind.Number
                        || !property.Value.TryGetUInt64(out var identity)
                        || identity != expectedFcId)))
                {
                    throw new InvalidOperationException("Saved FC data contains conflicting identities. The reset was cancelled.");
                }

                var payload = JsonNode.Parse(json)!.AsObject();
                // Keep unknown fields, and normalize aliases accepted by the case-insensitive reader.
                foreach (var key in payload.Select(pair => pair.Key).Where(key =>
                    key.Equals(nameof(FreeCompanyEntry.FcGil), StringComparison.OrdinalIgnoreCase)
                    || key.Equals(nameof(FreeCompanyEntry.FcGilObserved), StringComparison.OrdinalIgnoreCase)).ToArray())
                {
                    payload.Remove(key);
                }
                payload[nameof(FreeCompanyEntry.FcGil)] = 0;
                payload[nameof(FreeCompanyEntry.FcGilObserved)] = true;
                updates.Add((storedContentId, payload.ToJsonString()));
                selectedFound |= isSelected;
            }
        }

        if (!selectedFound)
            throw new InvalidOperationException("The selected saved character or FC changed. Refresh the Dashboard and try again.");

        foreach (var update in updates)
        {
            using var command = conn.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE xa_characters SET free_company_json = @json WHERE content_id = @cid";
            command.Parameters.AddTypedValue("@cid", update.ContentId);
            command.Parameters.AddTypedValue("@json", update.Json);
            command.ExecuteNonQuery();
        }
        transaction.Commit();
        return updates.Count;
    }

    public static XaCharacterSnapshotSections BuildSections(
        ulong contentId,
        string characterName,
        string world,
        string datacenter,
        string region,
        string personalEstate,
        string sharedEstates,
        string apartment,
        int gil,
        long retainerGil,
        List<CurrencyEntry> currencies,
        List<JobEntry> jobs,
        List<InventorySummary> inventorySummaries,
        List<ContainerItemEntry> items,
        List<RetainerEntry> retainers,
        List<RetainerListingEntry> listings,
        List<RetainerInventoryItem> retainerItems,
        FreeCompanyEntry? freeCompany,
        List<FcMemberEntry> fcMembers,
        SquadronInfo? squadron,
        VoyageInfo? voyages,
        List<CollectionSummary> collections,
        List<ActiveQuestEntry> activeQuests,
        List<MsqMilestoneEntry> msqMilestones,
        string validationJson)
    {
        var resolvedWorld = world ?? string.Empty;
        var resolvedDatacenter = ResolveDatacenter(resolvedWorld, datacenter);
        var resolvedRegion = ResolveRegion(resolvedWorld, region);
        var normalizedJobs = NormalizeJobs(jobs);
        var normalizedRetainers = NormalizeRetainerPayload(retainers, listings, retainerItems, contentId);
        var itemSections = BuildItemSections(items);
        var normalizedHousing = NormalizeHousingPayload(personalEstate, sharedEstates, apartment);

        return new XaCharacterSnapshotSections
        {
            InventorySummariesJson = Serialize(inventorySummaries, "[]"),
            CharacterJson = Serialize(new
            {
                contentId,
                name = characterName ?? string.Empty,
                world = resolvedWorld,
                datacenter = resolvedDatacenter,
                region = resolvedRegion,
                personalEstate = normalizedHousing.PersonalEstate,
                sharedEstates = normalizedHousing.SharedEstates,
                apartment = normalizedHousing.Apartment,
                gil,
                retainerGil,
            }, "{}"),
            FreeCompanyJson = Serialize(freeCompany, "null"),
            FcMembersJson = Serialize(fcMembers, "[]"),
            CurrenciesJson = Serialize(currencies, "[]"),
            JobsJson = Serialize(normalizedJobs, "[]"),
            InventoryJson = itemSections.InventoryJson,
            SaddlebagJson = itemSections.SaddlebagJson,
            CrystalsJson = itemSections.CrystalsJson,
            ArmouryJson = itemSections.ArmouryJson,
            EquippedJson = itemSections.EquippedJson,
            ItemsJson = itemSections.UnclassifiedJson,
            RetainersJson = Serialize(normalizedRetainers.Retainers, "[]"),
            ListingsJson = Serialize(normalizedRetainers.Listings, "[]"),
            RetainerItemsJson = Serialize(normalizedRetainers.RetainerItems, "[]"),
            CollectionsJson = Serialize(collections, "[]"),
            ActiveQuestsJson = Serialize(activeQuests, "[]"),
            MsqMilestonesJson = Serialize(msqMilestones, "[]"),
            SquadronJson = Serialize(squadron, "null"),
            VoyagesJson = Serialize(voyages, "null"),
            ValidationJson = string.IsNullOrWhiteSpace(validationJson) ? "{}" : validationJson,
        };
    }

    public static XaCharacterSnapshotSections BuildSectionsFromLegacySnapshotJson(
        string snapshotJson,
        ulong contentId,
        string characterName,
        string world,
        string datacenter,
        string region,
        string personalEstate,
        string sharedEstates,
        string apartment,
        int gil,
        long retainerGil,
        string validationJson)
    {
        var normalizedHousing = NormalizeHousingPayload(personalEstate, sharedEstates, apartment);

        if (string.IsNullOrWhiteSpace(snapshotJson))
        {
            return new XaCharacterSnapshotSections
            {
                CharacterJson = Serialize(new
                {
                    contentId,
                    name = characterName ?? string.Empty,
                    world = world ?? string.Empty,
                    datacenter = ResolveDatacenter(world ?? string.Empty, datacenter ?? string.Empty),
                    region = ResolveRegion(world ?? string.Empty, region ?? string.Empty),
                    personalEstate = normalizedHousing.PersonalEstate,
                    sharedEstates = normalizedHousing.SharedEstates,
                    apartment = normalizedHousing.Apartment,
                    gil,
                    retainerGil,
                }, "{}"),
                ValidationJson = string.IsNullOrWhiteSpace(validationJson) ? "{}" : validationJson,
            };
        }

        try
        {
            using var doc = JsonDocument.Parse(snapshotJson);
            var root = doc.RootElement;

            var characterSection = BuildCharacterSection(root, contentId, characterName, world, datacenter, region, normalizedHousing.PersonalEstate, normalizedHousing.SharedEstates, normalizedHousing.Apartment, gil, retainerGil);
            var inventorySummaries = DeserializeList<InventorySummary>(GetRawProperty(root, "inventory", "[]"));
            var items = DeserializeList<ContainerItemEntry>(GetRawProperty(root, "items", "[]"));
            var jobs = NormalizeJobs(DeserializeList<JobEntry>(GetRawProperty(root, "jobs", "[]")));
            var normalizedRetainers = NormalizeRetainerPayload(
                DeserializeList<RetainerEntry>(GetRawProperty(root, "retainers", "[]")),
                DeserializeList<RetainerListingEntry>(GetRawProperty(root, "listings", "[]")),
                DeserializeList<RetainerInventoryItem>(GetRawProperty(root, "retainerItems", "[]")),
                contentId);
            var itemSections = BuildItemSections(items);
            var resolvedValidationJson = GetRawProperty(root, "validation", string.IsNullOrWhiteSpace(validationJson) ? "{}" : validationJson);

            return new XaCharacterSnapshotSections
            {
                InventorySummariesJson = Serialize(inventorySummaries, "[]"),
                CharacterJson = Serialize(characterSection, "{}"),
                FreeCompanyJson = GetRawProperty(root, "freeCompany", "null"),
                FcMembersJson = GetRawProperty(root, "fcMembers", "[]"),
                CurrenciesJson = GetRawProperty(root, "currencies", "[]"),
                JobsJson = Serialize(jobs, "[]"),
                InventoryJson = itemSections.InventoryJson,
                SaddlebagJson = itemSections.SaddlebagJson,
                CrystalsJson = itemSections.CrystalsJson,
                ArmouryJson = itemSections.ArmouryJson,
                EquippedJson = itemSections.EquippedJson,
                ItemsJson = itemSections.UnclassifiedJson,
                RetainersJson = Serialize(normalizedRetainers.Retainers, "[]"),
                ListingsJson = Serialize(normalizedRetainers.Listings, "[]"),
                RetainerItemsJson = Serialize(normalizedRetainers.RetainerItems, "[]"),
                CollectionsJson = GetRawProperty(root, "collections", "[]"),
                ActiveQuestsJson = GetRawProperty(root, "activeQuests", "[]"),
                MsqMilestonesJson = GetRawProperty(root, "msqMilestones", "[]"),
                SquadronJson = GetRawProperty(root, "squadron", "null"),
                VoyagesJson = GetRawProperty(root, "voyages", "null"),
                ValidationJson = string.IsNullOrWhiteSpace(resolvedValidationJson) ? "{}" : resolvedValidationJson,
            };
        }
        catch
        {
            return new XaCharacterSnapshotSections
            {
                CharacterJson = Serialize(new
                {
                    contentId,
                    name = characterName ?? string.Empty,
                    world = world ?? string.Empty,
                    datacenter = ResolveDatacenter(world ?? string.Empty, datacenter ?? string.Empty),
                    region = ResolveRegion(world ?? string.Empty, region ?? string.Empty),
                    personalEstate = normalizedHousing.PersonalEstate,
                    sharedEstates = normalizedHousing.SharedEstates,
                    apartment = normalizedHousing.Apartment,
                    gil,
                    retainerGil,
                }, "{}"),
                ValidationJson = string.IsNullOrWhiteSpace(validationJson) ? "{}" : validationJson,
            };
        }
    }

    public static string ResolveDatacenter(string world, string fallbackDatacenter = "")
    {
        return WorldData.ResolveDataCenter(world, fallbackDatacenter);
    }

    public static string ResolveRegion(string world, string fallbackRegion = "")
    {
        return WorldData.ResolveRegion(world, fallbackRegion);
    }

    public static (string PersonalEstate, string SharedEstates, string Apartment) NormalizeHousingPayload(
        string personalEstate,
        string sharedEstates,
        string apartment)
    {
        var normalizedPersonalEstate = NormalizeEstateDisplayValue(personalEstate);
        var normalizedApartment = NormalizeApartmentDisplayValue(apartment);
        var cleanedSharedEntries = new List<string>();
        var seenComparisonKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenDisplayValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in SplitHousingEntries(sharedEstates))
        {
            if (AddressesMatch(normalizedPersonalEstate, entry))
                continue;

            var comparisonKey = BuildHousingComparisonKey(entry);
            if (comparisonKey.Length > 0)
            {
                if (!seenComparisonKeys.Add(comparisonKey))
                    continue;
            }
            else
            {
                var displayKey = NormalizeHousingTextForComparison(entry);
                if (!seenDisplayValues.Add(displayKey))
                    continue;
            }

            cleanedSharedEntries.Add(entry);
        }

        return (normalizedPersonalEstate, string.Join("\n", cleanedSharedEntries), normalizedApartment);
    }

    public static string NormalizeApartmentDisplayValue(string value)
    {
        return StripHousingOwnerSuffix(NormalizeHousingDisplayValue(value));
    }

    public static string PreferSizedPersonalEstateValue(string currentValue, string persistedValue)
    {
        var currentWithoutOwner = StripHousingOwnerSuffix(currentValue);
        var persistedWithoutOwner = StripHousingOwnerSuffix(persistedValue);
        var normalizedCurrent = NormalizeEstateDisplayValue(currentWithoutOwner);
        var normalizedPersisted = NormalizeEstateDisplayValue(persistedWithoutOwner);
        if (normalizedCurrent.Length == 0)
            return normalizedPersisted;
        if (normalizedPersisted.Length == 0)
            return normalizedCurrent;
        if (!AddressesMatch(normalizedCurrent, normalizedPersisted))
            return normalizedCurrent;

        var currentHasSizeSuffix = ParentheticalTextRegex.IsMatch(currentWithoutOwner);
        var persistedHasSizeSuffix = ParentheticalTextRegex.IsMatch(persistedWithoutOwner);
        if (!currentHasSizeSuffix && persistedHasSizeSuffix)
            return normalizedPersisted;

        return normalizedCurrent;
    }

    internal static string StripHousingOwnerSuffix(string value)
    {
        var normalizedValue = NormalizeHousingDisplayValue(value);
        return normalizedValue.Length == 0 ? string.Empty : OwnerSuffixRegex.Replace(normalizedValue, string.Empty).Trim();
    }

    internal static bool HousingDisplayValuesMatch(string left, string right)
    {
        return AddressesMatch(left, right);
    }

    public static int GetHighestJobLevel(IEnumerable<JobEntry> jobs) => jobs.Any() ? jobs.Max(j => j.Level) : 0;

    private static XaCharacterSnapshotData Parse(XaCharacterSnapshotRow row)
    {
        var parseErrors = new Dictionary<string, string>(StringComparer.Ordinal);
        var snapshot = new XaCharacterSnapshotData
        {
            Row = row,
            InventorySummaries = DeserializeListTracked<InventorySummary>(row.InventorySummariesJson, "inventory_summaries_json", parseErrors),
            Currencies = DeserializeListTracked<CurrencyEntry>(row.CurrenciesJson, "currencies_json", parseErrors),
            Jobs = NormalizeJobs(DeserializeListTracked<JobEntry>(row.JobsJson, "jobs_json", parseErrors)),
            InventoryItems = DeserializeListTracked<ContainerItemEntry>(row.InventoryJson, "inventory_json", parseErrors),
            SaddlebagItems = DeserializeListTracked<ContainerItemEntry>(row.SaddlebagJson, "saddlebag_json", parseErrors),
            CrystalItems = DeserializeListTracked<ContainerItemEntry>(row.CrystalsJson, "crystals_json", parseErrors),
            ArmouryItems = DeserializeListTracked<ContainerItemEntry>(row.ArmouryJson, "armoury_json", parseErrors),
            EquippedItems = DeserializeListTracked<ContainerItemEntry>(row.EquippedJson, "equipped_json", parseErrors),
            AllItems = DeserializeListTracked<ContainerItemEntry>(row.ItemsJson, "items_json", parseErrors),
            Retainers = DeserializeListTracked<RetainerEntry>(row.RetainersJson, "retainers_json", parseErrors),
            Listings = DeserializeListTracked<RetainerListingEntry>(row.ListingsJson, "listings_json", parseErrors),
            RetainerItems = DeserializeListTracked<RetainerInventoryItem>(row.RetainerItemsJson, "retainer_items_json", parseErrors),
            FreeCompany = DeserializeObjectTracked<FreeCompanyEntry>(row.FreeCompanyJson, "free_company_json", parseErrors),
            FcMembers = DeserializeListTracked<FcMemberEntry>(row.FcMembersJson, "fc_members_json", parseErrors),
            Squadron = DeserializeObjectTracked<SquadronInfo>(row.SquadronJson, "squadron_json", parseErrors),
            Voyages = DeserializeObjectTracked<VoyageInfo>(row.VoyagesJson, "voyages_json", parseErrors),
            Collections = DeserializeListTracked<CollectionSummary>(row.CollectionsJson, "collections_json", parseErrors),
            ActiveQuests = DeserializeListTracked<ActiveQuestEntry>(row.ActiveQuestsJson, "active_quests_json", parseErrors),
            MsqMilestones = DeserializeListTracked<MsqMilestoneEntry>(row.MsqMilestonesJson, "msq_milestones_json", parseErrors),
            ParseErrors = parseErrors,
        };

        if (row.SnapshotVersion >= 3)
        {
            snapshot.AllItems = snapshot.InventoryItems
                .Concat(snapshot.SaddlebagItems)
                .Concat(snapshot.CrystalItems)
                .Concat(snapshot.ArmouryItems)
                .Concat(snapshot.EquippedItems)
                .Concat(snapshot.AllItems)
                .ToList();
        }
        else if (snapshot.AllItems.Count == 0)
        {
            snapshot.AllItems = snapshot.InventoryItems
                .Concat(snapshot.SaddlebagItems)
                .Concat(snapshot.CrystalItems)
                .Concat(snapshot.ArmouryItems)
                .Concat(snapshot.EquippedItems)
                .ToList();
        }

        var normalizedRetainers = NormalizeRetainerPayload(snapshot.Retainers, snapshot.Listings, snapshot.RetainerItems, row.ContentId);
        snapshot.Retainers = normalizedRetainers.Retainers;
        snapshot.Listings = normalizedRetainers.Listings;
        snapshot.RetainerItems = normalizedRetainers.RetainerItems;

        return snapshot;
    }

    private static XaCharacterSnapshotRow ReadRow(SqliteDataReader reader)
    {
        var normalizedHousing = NormalizeHousingPayload(
            reader["personal_estate"].ToString() ?? string.Empty,
            reader["shared_estates"].ToString() ?? string.Empty,
            reader["apartment"].ToString() ?? string.Empty);

        return new XaCharacterSnapshotRow
        {
            ContentId = SqliteIdentity.Decode((long)reader["content_id"]),
            CharacterName = reader["character_name"].ToString() ?? string.Empty,
            World = reader["world"].ToString() ?? string.Empty,
            Datacenter = ResolveDatacenter(reader["world"].ToString() ?? string.Empty, reader["datacenter"].ToString() ?? string.Empty),
            Region = ResolveRegion(reader["world"].ToString() ?? string.Empty, reader["region"].ToString() ?? string.Empty),
            FcId = SqliteIdentity.Decode(ReadInt64(reader, "fc_id")),
            FcName = reader["fc_name"].ToString() ?? string.Empty,
            FcTag = reader["fc_tag"].ToString() ?? string.Empty,
            FcPoints = ReadInt32(reader, "fc_points"),
            FcEstate = HousingPlotSizeData.ApplySizeSuffix(reader["fc_estate"].ToString() ?? string.Empty),
            PersonalEstate = normalizedHousing.PersonalEstate,
            SharedEstates = normalizedHousing.SharedEstates,
            Apartment = normalizedHousing.Apartment,
            Gil = ReadInt32(reader, "gil"),
            RetainerGil = ResolveRetainerGil(reader),
            RetainerCount = ReadInt32(reader, "retainer_count"),
            HighestJobLevel = ReadInt32(reader, "highest_job_level"),
            RetainerIdsJson = reader["retainer_ids_json"].ToString() ?? "[]",
            InventorySummariesJson = reader["inventory_summaries_json"].ToString() ?? "[]",
            FreshnessJson = reader["freshness_json"].ToString() ?? "{}",
            CharacterJson = reader["character_json"].ToString() ?? "{}",
            FreeCompanyJson = reader["free_company_json"].ToString() ?? "null",
            FcMembersJson = reader["fc_members_json"].ToString() ?? "[]",
            CurrenciesJson = reader["currencies_json"].ToString() ?? "[]",
            JobsJson = reader["jobs_json"].ToString() ?? "[]",
            InventoryJson = reader["inventory_json"].ToString() ?? "[]",
            SaddlebagJson = reader["saddlebag_json"].ToString() ?? "[]",
            CrystalsJson = reader["crystals_json"].ToString() ?? "[]",
            ArmouryJson = reader["armoury_json"].ToString() ?? "[]",
            EquippedJson = reader["equipped_json"].ToString() ?? "[]",
            ItemsJson = reader["items_json"].ToString() ?? "[]",
            RetainersJson = reader["retainers_json"].ToString() ?? "[]",
            ListingsJson = reader["listings_json"].ToString() ?? "[]",
            RetainerItemsJson = reader["retainer_items_json"].ToString() ?? "[]",
            CollectionsJson = reader["collections_json"].ToString() ?? "[]",
            ActiveQuestsJson = reader["active_quests_json"].ToString() ?? "[]",
            MsqMilestonesJson = reader["msq_milestones_json"].ToString() ?? "[]",
            SquadronJson = reader["squadron_json"].ToString() ?? "null",
            VoyagesJson = reader["voyages_json"].ToString() ?? "null",
            ValidationJson = reader["validation_json"].ToString() ?? "{}",
            SnapshotVersion = ReadInt32(reader, "snapshot_version", Schema.CurrentSnapshotVersion),
            ExportedUtc = reader["exported_utc"].ToString() ?? string.Empty,
            Trigger = reader["trigger"].ToString() ?? string.Empty,
            TriggerDetail = reader["trigger_detail"].ToString() ?? string.Empty,
            ImportedFromLegacy = ReadInt32(reader, "imported_from_legacy") == 1,
            UpdatedUtc = reader["updated_utc"].ToString() ?? string.Empty,
        };
    }

    private static XaCharacterItemSections BuildItemSections(IEnumerable<ContainerItemEntry> items)
    {
        var itemList = items.ToList();
        var classifiedItems = new HashSet<ContainerItemEntry>();
        var inventory = itemList.Where(i => IsInventoryContainer(i.ContainerName)).ToList();
        var saddlebag = itemList.Where(i => IsSaddlebagContainer(i.ContainerName)).ToList();
        var crystals = itemList.Where(i => IsCrystalsContainer(i.ContainerName)).ToList();
        var armoury = itemList.Where(i => IsArmouryContainer(i.ContainerName)).ToList();
        var equipped = itemList.Where(i => IsEquippedContainer(i.ContainerName)).ToList();
        classifiedItems.UnionWith(inventory);
        classifiedItems.UnionWith(saddlebag);
        classifiedItems.UnionWith(crystals);
        classifiedItems.UnionWith(armoury);
        classifiedItems.UnionWith(equipped);
        return new XaCharacterItemSections
        {
            InventoryJson = Serialize(inventory, "[]"),
            SaddlebagJson = Serialize(saddlebag, "[]"),
            CrystalsJson = Serialize(crystals, "[]"),
            ArmouryJson = Serialize(armoury, "[]"),
            EquippedJson = Serialize(equipped, "[]"),
            UnclassifiedJson = Serialize(itemList.Where(item => !classifiedItems.Contains(item)).ToList(), "[]"),
        };
    }

    private static bool IsInventoryContainer(string containerName) =>
        !string.IsNullOrWhiteSpace(containerName) &&
        containerName.StartsWith("Inventory ", StringComparison.OrdinalIgnoreCase);

    private static bool IsSaddlebagContainer(string containerName) =>
        !string.IsNullOrWhiteSpace(containerName) &&
        (containerName.StartsWith("Saddlebag ", StringComparison.OrdinalIgnoreCase)
         || containerName.StartsWith("Premium Saddlebag ", StringComparison.OrdinalIgnoreCase));

    private static bool IsCrystalsContainer(string containerName) =>
        string.Equals(containerName, "Crystals", StringComparison.OrdinalIgnoreCase);

    private static bool IsArmouryContainer(string containerName) =>
        !string.IsNullOrWhiteSpace(containerName) &&
        containerName.StartsWith("Armoury", StringComparison.OrdinalIgnoreCase);

    private static bool IsEquippedContainer(string containerName) =>
        !string.IsNullOrWhiteSpace(containerName) &&
        containerName.StartsWith("Equipped", StringComparison.OrdinalIgnoreCase);

    private static List<JobEntry> NormalizeJobs(IEnumerable<JobEntry> jobs)
    {
        return jobs.Select(job =>
        {
            var abbreviation = NormalizeUpper(job.Abbreviation);
            var levelCap = JobLevelCaps.ForAbbreviation(
                abbreviation,
                job.LevelCap > 0 ? job.LevelCap : JobLevelCaps.Default);
            var normalized = JobAvailability.Normalize(job);
            normalized.Abbreviation = abbreviation;
            normalized.Name = NormalizeUpper(job.Name);
            normalized.ParentAbbreviation = NormalizeUpper(job.ParentAbbreviation);
            normalized.ParentName = NormalizeUpper(job.ParentName);
            normalized.LevelCap = levelCap;
            return normalized;
        }).ToList();
    }

    public static (List<RetainerEntry> Retainers, List<RetainerListingEntry> Listings, List<RetainerInventoryItem> RetainerItems, List<string> Warnings) NormalizeRetainerPayload(
        IEnumerable<RetainerEntry> retainers,
        IEnumerable<RetainerListingEntry> listings,
        IEnumerable<RetainerInventoryItem> retainerItems,
        ulong expectedOwnerContentId = 0)
    {
        var warnings = new List<string>();
        var candidates = retainers
            .Where(retainer => retainer != null && retainer.RetainerId != 0)
            .Select(retainer => StampRetainerOwnerContentId(retainer, expectedOwnerContentId))
            .ToList();
        var mismatchedRetainers = expectedOwnerContentId == 0
            ? new List<RetainerEntry>()
            : candidates.Where(retainer => retainer.OwnerContentId != expectedOwnerContentId).ToList();

        foreach (var retainer in mismatchedRetainers)
        {
            var warning = OwnerValidation.DescribeMismatch(
                expectedOwnerContentId,
                retainer.OwnerContentId,
                $"Retainer '{retainer.Name}' ({retainer.RetainerId})")!;
            Plugin.Log.Warning($"[XA] {warning}");
            warnings.Add(warning);
        }

        var normalizedRetainers = candidates
            .Where(retainer => !mismatchedRetainers.Contains(retainer))
            .GroupBy(retainer => retainer.RetainerId)
            .Select(group => group
                .OrderByDescending(GetRetainerCompletenessScore)
                .ThenBy(retainer => retainer.Name, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderBy(retainer => retainer.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalizedRetainers.Count == 0)
            return (new List<RetainerEntry>(), new List<RetainerListingEntry>(), new List<RetainerInventoryItem>(), warnings);

        var retainerIds = normalizedRetainers.Select(retainer => retainer.RetainerId).ToHashSet();
        var retainerNames = normalizedRetainers.ToDictionary(retainer => retainer.RetainerId, retainer => retainer.Name ?? string.Empty);

        var normalizedListings = listings
            .Where(listing => listing != null && listing.RetainerId != 0 && retainerIds.Contains(listing.RetainerId))
            .GroupBy(listing => (listing.RetainerId, listing.SlotIndex))
            .Select(group =>
            {
                var listing = group
                    .OrderByDescending(GetListingCompletenessScore)
                    .ThenByDescending(entry => entry.Quantity)
                    .First();
                listing.RetainerName = ResolveRetainerName(listing.RetainerId, listing.RetainerName, retainerNames);
                return listing;
            })
            .OrderBy(listing => ResolveRetainerName(listing.RetainerId, listing.RetainerName, retainerNames), StringComparer.OrdinalIgnoreCase)
            .ThenBy(listing => listing.SlotIndex)
            .ToList();

        var normalizedRetainerItems = retainerItems
            .Where(item => item != null && item.RetainerId != 0 && retainerIds.Contains(item.RetainerId))
            .Select(item =>
            {
                item.RetainerName = ResolveRetainerName(item.RetainerId, item.RetainerName, retainerNames);
                return item;
            })
            .ToList();

        return (normalizedRetainers, normalizedListings, normalizedRetainerItems, warnings);
    }

    public static string BuildRetainerOwnerReferencesJson(IEnumerable<RetainerEntry> retainers, ulong expectedOwnerContentId = 0)
    {
        var normalizedRetainers = NormalizeRetainerPayload(
            retainers,
            Enumerable.Empty<RetainerListingEntry>(),
            Enumerable.Empty<RetainerInventoryItem>(),
            expectedOwnerContentId).Retainers;

        var ownerReferences = normalizedRetainers
            .Select(retainer => new RetainerOwnerReference
            {
                RetainerId = retainer.RetainerId,
                OwnerContentId = retainer.OwnerContentId,
            })
            .ToList();

        return Serialize(ownerReferences, "[]");
    }

    private static RetainerEntry StampRetainerOwnerContentId(RetainerEntry retainer, ulong expectedOwnerContentId)
    {
        if (retainer.OwnerContentId == 0 && expectedOwnerContentId != 0)
            retainer.OwnerContentId = expectedOwnerContentId;

        return retainer;
    }

    private static int GetRetainerCompletenessScore(RetainerEntry retainer)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(retainer.Name)) score += 8;
        if (retainer.OwnerContentId > 0) score += 4;
        if (retainer.Level > 0) score += 4;
        if (retainer.ClassJob > 0) score += 4;
        if (retainer.ItemCount > 0) score += 2;
        if (retainer.MarketItemCount > 0) score += 2;
        if (retainer.Gil > 0) score += 2;
        if (retainer.VentureId > 0) score += 1;
        if (!string.IsNullOrWhiteSpace(retainer.VentureStatus)) score += 1;
        return score;
    }

    private static int GetListingCompletenessScore(RetainerListingEntry listing)
    {
        var score = 0;
        if (listing.ItemId > 0) score += 4;
        if (!string.IsNullOrWhiteSpace(listing.ItemName)) score += 2;
        if (listing.Quantity > 0) score += 1;
        if (listing.UnitPrice > 0) score += 1;
        return score;
    }

    private static string ResolveRetainerName(ulong retainerId, string fallbackName, IReadOnlyDictionary<ulong, string> retainerNames)
    {
        if (retainerNames.TryGetValue(retainerId, out var name) && !string.IsNullOrWhiteSpace(name))
            return name;

        return fallbackName ?? string.Empty;
    }

    private static string NormalizeUpper(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Trim().ToUpperInvariant();
    }

    private static string Serialize<T>(
        T value,
        string fallbackJson,
        [CallerArgumentExpression(nameof(value))] string sectionName = "snapshot section")
    {
        _ = fallbackJson;
        try
        {
            return JsonSerializer.Serialize(value, JsonOptions);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[XA] Failed to serialize snapshot section '{sectionName}': {ex.Message}");
            throw new SnapshotSerializationException(sectionName, ex);
        }
    }

    private static List<T> DeserializeList<T>(string? json)
        => DeserializeListTracked<T>(json, typeof(T).Name, new Dictionary<string, string>(StringComparer.Ordinal));

    private static List<T> DeserializeListTracked<T>(
        string? json,
        string sectionName,
        IDictionary<string, string> parseErrors)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<T>();

        try
        {
            return JsonSerializer.Deserialize<List<T>>(json, JsonOptions) ?? new List<T>();
        }
        catch (Exception ex)
        {
            parseErrors[sectionName] = ex.Message;
            Plugin.Log.Error($"[XA] Snapshot section '{sectionName}' is unreadable: {ex.Message}");
            return new List<T>();
        }
    }

    private static T? DeserializeObject<T>(string? json) where T : class
        => DeserializeObjectTracked<T>(json, typeof(T).Name, new Dictionary<string, string>(StringComparer.Ordinal));

    private static T? DeserializeObjectTracked<T>(
        string? json,
        string sectionName,
        IDictionary<string, string> parseErrors) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            parseErrors[sectionName] = ex.Message;
            Plugin.Log.Error($"[XA] Snapshot section '{sectionName}' is unreadable: {ex.Message}");
            return null;
        }
    }

    public static void PreserveUnreadableSections(
        XaCharacterSnapshotSections candidate,
        XaCharacterSnapshotData? persistedSnapshot)
    {
        if (persistedSnapshot == null || persistedSnapshot.ParseErrors.Count == 0)
            return;

        var row = persistedSnapshot.Row;
        foreach (var sectionName in persistedSnapshot.ParseErrors.Keys)
        {
            switch (sectionName)
            {
                case "inventory_summaries_json": candidate.InventorySummariesJson = row.InventorySummariesJson; break;
                case "free_company_json": candidate.FreeCompanyJson = row.FreeCompanyJson; break;
                case "fc_members_json": candidate.FcMembersJson = row.FcMembersJson; break;
                case "currencies_json": candidate.CurrenciesJson = row.CurrenciesJson; break;
                case "jobs_json": candidate.JobsJson = row.JobsJson; break;
                case "inventory_json": candidate.InventoryJson = row.InventoryJson; break;
                case "saddlebag_json": candidate.SaddlebagJson = row.SaddlebagJson; break;
                case "crystals_json": candidate.CrystalsJson = row.CrystalsJson; break;
                case "armoury_json": candidate.ArmouryJson = row.ArmouryJson; break;
                case "equipped_json": candidate.EquippedJson = row.EquippedJson; break;
                case "items_json": candidate.ItemsJson = row.ItemsJson; break;
                case "retainers_json": candidate.RetainersJson = row.RetainersJson; break;
                case "listings_json": candidate.ListingsJson = row.ListingsJson; break;
                case "retainer_items_json": candidate.RetainerItemsJson = row.RetainerItemsJson; break;
                case "collections_json": candidate.CollectionsJson = row.CollectionsJson; break;
                case "active_quests_json": candidate.ActiveQuestsJson = row.ActiveQuestsJson; break;
                case "msq_milestones_json": candidate.MsqMilestonesJson = row.MsqMilestonesJson; break;
                case "squadron_json": candidate.SquadronJson = row.SquadronJson; break;
                case "voyages_json": candidate.VoyagesJson = row.VoyagesJson; break;
            }
        }
    }

    private static string GetRawProperty(JsonElement root, string propertyName, string fallback)
    {
        if (TryGetPropertyIgnoreCase(root, propertyName, out var value))
            return value.GetRawText();

        return fallback;
    }

    private static object BuildCharacterSection(
        JsonElement root,
        ulong fallbackContentId,
        string fallbackCharacterName,
        string fallbackWorld,
        string fallbackDatacenter,
        string fallbackRegion,
        string fallbackPersonalEstate,
        string fallbackSharedEstates,
        string fallbackApartment,
        int fallbackGil,
        long fallbackRetainerGil)
    {
        ulong contentId = fallbackContentId;
        var name = fallbackCharacterName ?? string.Empty;
        var world = fallbackWorld ?? string.Empty;
        var datacenter = fallbackDatacenter ?? string.Empty;
        var region = fallbackRegion ?? string.Empty;
        var personalEstate = fallbackPersonalEstate ?? string.Empty;
        var sharedEstates = fallbackSharedEstates ?? string.Empty;
        var apartment = fallbackApartment ?? string.Empty;
        var gil = fallbackGil;
        var retainerGil = fallbackRetainerGil;

        if (TryGetPropertyIgnoreCase(root, "character", out var character) && character.ValueKind == JsonValueKind.Object)
        {
            contentId = GetUInt64(character, "contentId", fallbackContentId);
            name = GetString(character, "name", name);
            world = GetString(character, "world", world);
            datacenter = GetString(character, "datacenter", datacenter);
            region = GetString(character, "region", region);
            personalEstate = GetString(character, "personalEstate", personalEstate);
            sharedEstates = GetString(character, "sharedEstates", sharedEstates);
            apartment = GetString(character, "apartment", apartment);
            gil = GetInt32(character, "gil", gil);
            retainerGil = GetInt64(character, "retainerGil", retainerGil);
        }

        datacenter = ResolveDatacenter(world, datacenter);
        region = ResolveRegion(world, region);
        var normalizedHousing = NormalizeHousingPayload(personalEstate, sharedEstates, apartment);

        return new
        {
            contentId,
            name,
            world,
            datacenter,
            region,
            personalEstate = normalizedHousing.PersonalEstate,
            sharedEstates = normalizedHousing.SharedEstates,
            apartment = normalizedHousing.Apartment,
            gil,
            retainerGil,
        };
    }

    private static IEnumerable<string> SplitHousingEntries(string value)
    {
        return (value ?? string.Empty)
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeEstateDisplayValue)
            .Where(entry => entry.Length > 0);
    }

    private static bool AddressesMatch(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        var leftKey = BuildHousingComparisonKey(left);
        var rightKey = BuildHousingComparisonKey(right);
        if (leftKey.Length > 0 && rightKey.Length > 0)
            return leftKey.Equals(rightKey, StringComparison.OrdinalIgnoreCase);

        return NormalizeHousingTextForComparison(left).Equals(NormalizeHousingTextForComparison(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildHousingComparisonKey(string value)
    {
        var normalizedValue = StripHousingOwnerSuffix(value);
        if (normalizedValue.Length == 0)
            return string.Empty;

        return HousingAddressIdentity.TryBuildComparisonKey(normalizedValue, out var key)
            ? key
            : string.Empty;
    }

    private static string NormalizeHousingDisplayValue(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static string NormalizeEstateDisplayValue(string value)
    {
        return HousingPlotSizeData.ApplySizeSuffix(NormalizeHousingDisplayValue(value));
    }

    private static string NormalizeHousingTextForComparison(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var withoutParenthetical = ParentheticalTextRegex.Replace(StripHousingOwnerSuffix(value), string.Empty);
        var collapsedWhitespace = WhitespaceRegex.Replace(withoutParenthetical, " ").Trim();
        return collapsedWhitespace.Trim(',', ' ').ToLowerInvariant();
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string GetString(JsonElement element, string propertyName, string fallback)
    {
        if (!TryGetPropertyIgnoreCase(element, propertyName, out var value))
            return fallback;

        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;
    }

    private static int GetInt32(JsonElement element, string propertyName, int fallback)
    {
        if (!TryGetPropertyIgnoreCase(element, propertyName, out var value))
            return fallback;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            return number;

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number))
            return number;

        return fallback;
    }

    private static long GetInt64(JsonElement element, string propertyName, long fallback)
    {
        if (!TryGetPropertyIgnoreCase(element, propertyName, out var value))
            return fallback;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
            return number;

        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out number))
            return number;

        return fallback;
    }

    private static ulong GetUInt64(JsonElement element, string propertyName, ulong fallback)
    {
        if (!TryGetPropertyIgnoreCase(element, propertyName, out var value))
            return fallback;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetUInt64(out var number))
            return number;

        if (value.ValueKind == JsonValueKind.String && ulong.TryParse(value.GetString(), out number))
            return number;

        return fallback;
    }

    private static ulong ReadUInt64(SqliteDataReader reader, string columnName)
    {
        var value = reader[columnName];
        if (value == null || value == DBNull.Value)
            return 0;

        if (value is string textValue)
        {
            if (string.IsNullOrWhiteSpace(textValue))
                return 0;

            if (ulong.TryParse(textValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedTextValue))
                return parsedTextValue;

            return 0;
        }

        try
        {
            return Convert.ToUInt64(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return 0;
        }
    }

    private static long ResolveRetainerGil(SqliteDataReader reader)
    {
        var storedRetainerGil = ReadInt64(reader, "retainer_gil");
        if (storedRetainerGil >= 0)
            return storedRetainerGil;

        var contentId = ReadUInt64(reader, "content_id");
        var normalizedRetainers = NormalizeRetainerPayload(
            DeserializeList<RetainerEntry>(reader["retainers_json"].ToString() ?? "[]"),
            Enumerable.Empty<RetainerListingEntry>(),
            Enumerable.Empty<RetainerInventoryItem>(),
            contentId).Retainers;
        var repairedRetainerGil = normalizedRetainers.Sum(retainer => (long)retainer.Gil);
        return repairedRetainerGil > 0 ? repairedRetainerGil : storedRetainerGil;
    }

    private static int ReadInt32(SqliteDataReader reader, string columnName, int fallback = 0)
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

    private static long ReadInt64(SqliteDataReader reader, string columnName, long fallback = 0)
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

public sealed class XaCharacterSnapshotSections
{
    public string InventorySummariesJson { get; set; } = "[]";
    public string CharacterJson { get; set; } = "{}";
    public string FreeCompanyJson { get; set; } = "null";
    public string FcMembersJson { get; set; } = "[]";
    public string CurrenciesJson { get; set; } = "[]";
    public string JobsJson { get; set; } = "[]";
    public string InventoryJson { get; set; } = "[]";
    public string SaddlebagJson { get; set; } = "[]";
    public string CrystalsJson { get; set; } = "[]";
    public string ArmouryJson { get; set; } = "[]";
    public string EquippedJson { get; set; } = "[]";
    public string ItemsJson { get; set; } = "[]";
    public string RetainersJson { get; set; } = "[]";
    public string ListingsJson { get; set; } = "[]";
    public string RetainerItemsJson { get; set; } = "[]";
    public string CollectionsJson { get; set; } = "[]";
    public string ActiveQuestsJson { get; set; } = "[]";
    public string MsqMilestonesJson { get; set; } = "[]";
    public string SquadronJson { get; set; } = "null";
    public string VoyagesJson { get; set; } = "null";
    public string ValidationJson { get; set; } = "{}";
}

public sealed class XaCharacterSnapshotRow
{
    public ulong ContentId { get; init; }
    public string CharacterName { get; init; } = string.Empty;
    public string World { get; init; } = string.Empty;
    public string Datacenter { get; init; } = string.Empty;
    public string Region { get; init; } = string.Empty;
    public ulong FcId { get; init; }
    public string FcName { get; init; } = string.Empty;
    public string FcTag { get; init; } = string.Empty;
    public int FcPoints { get; init; }
    public string FcEstate { get; init; } = string.Empty;
    public string PersonalEstate { get; init; } = string.Empty;
    public string SharedEstates { get; init; } = string.Empty;
    public string Apartment { get; init; } = string.Empty;
    public int Gil { get; init; }
    public long RetainerGil { get; init; }
    public int RetainerCount { get; init; }
    public int HighestJobLevel { get; init; }
    public string RetainerIdsJson { get; init; } = "[]";
    public string InventorySummariesJson { get; init; } = "[]";
    public string FreshnessJson { get; init; } = "{}";
    public string CharacterJson { get; init; } = "{}";
    public string FreeCompanyJson { get; init; } = "null";
    public string FcMembersJson { get; init; } = "[]";
    public string CurrenciesJson { get; init; } = "[]";
    public string JobsJson { get; init; } = "[]";
    public string InventoryJson { get; init; } = "[]";
    public string SaddlebagJson { get; init; } = "[]";
    public string CrystalsJson { get; init; } = "[]";
    public string ArmouryJson { get; init; } = "[]";
    public string EquippedJson { get; init; } = "[]";
    public string ItemsJson { get; init; } = "[]";
    public string RetainersJson { get; init; } = "[]";
    public string ListingsJson { get; init; } = "[]";
    public string RetainerItemsJson { get; init; } = "[]";
    public string CollectionsJson { get; init; } = "[]";
    public string ActiveQuestsJson { get; init; } = "[]";
    public string MsqMilestonesJson { get; init; } = "[]";
    public string SquadronJson { get; init; } = "null";
    public string VoyagesJson { get; init; } = "null";
    public string ValidationJson { get; init; } = "{}";
    public int SnapshotVersion { get; init; }
    public string ExportedUtc { get; init; } = string.Empty;
    public string Trigger { get; init; } = string.Empty;
    public string TriggerDetail { get; init; } = string.Empty;
    public bool ImportedFromLegacy { get; init; }
    public string UpdatedUtc { get; init; } = string.Empty;
}

public sealed class XaCharacterSnapshotData
{
    public XaCharacterSnapshotRow Row { get; init; } = new();
    public IReadOnlyDictionary<string, string> ParseErrors { get; init; } = new Dictionary<string, string>();
    public List<InventorySummary> InventorySummaries { get; set; } = new();
    public List<CurrencyEntry> Currencies { get; set; } = new();
    public List<JobEntry> Jobs { get; set; } = new();
    public List<ContainerItemEntry> InventoryItems { get; set; } = new();
    public List<ContainerItemEntry> SaddlebagItems { get; set; } = new();
    public List<ContainerItemEntry> CrystalItems { get; set; } = new();
    public List<ContainerItemEntry> ArmouryItems { get; set; } = new();
    public List<ContainerItemEntry> EquippedItems { get; set; } = new();
    public List<ContainerItemEntry> AllItems { get; set; } = new();
    public List<RetainerEntry> Retainers { get; set; } = new();
    public List<RetainerListingEntry> Listings { get; set; } = new();
    public List<RetainerInventoryItem> RetainerItems { get; set; } = new();
    public FreeCompanyEntry? FreeCompany { get; set; }
    public List<FcMemberEntry> FcMembers { get; set; } = new();
    public SquadronInfo? Squadron { get; set; }
    public VoyageInfo? Voyages { get; set; }
    public List<CollectionSummary> Collections { get; set; } = new();
    public List<ActiveQuestEntry> ActiveQuests { get; set; } = new();
    public List<MsqMilestoneEntry> MsqMilestones { get; set; } = new();
}

public sealed class XaCharacterRosterData
{
    public ulong ContentId { get; init; }
    public string CharacterName { get; init; } = string.Empty;
    public string World { get; init; } = string.Empty;
    public string Datacenter { get; init; } = string.Empty;
    public string Region { get; init; } = string.Empty;
    public int SnapshotVersion { get; init; }
    public string UpdatedUtc { get; init; } = string.Empty;
    public List<JobEntry> Jobs { get; init; } = new();
    public IReadOnlyDictionary<string, string> ParseErrors { get; init; } = new Dictionary<string, string>();
}

public sealed class XaCharacterItemsData
{
    public ulong ContentId { get; init; }
    public string CharacterName { get; init; } = string.Empty;
    public string World { get; init; } = string.Empty;
    public string UpdatedUtc { get; init; } = string.Empty;
    public List<ContainerItemEntry> AllItems { get; init; } = new();
    public List<RetainerEntry> Retainers { get; init; } = new();
    public List<RetainerInventoryItem> RetainerItems { get; init; } = new();
    public IReadOnlyDictionary<string, string> ParseErrors { get; init; } = new Dictionary<string, string>();
}

public sealed class SnapshotSerializationException : Exception
{
    public SnapshotSerializationException(string sectionName, Exception innerException)
        : base($"Snapshot serialization failed for section '{sectionName}'.", innerException)
    {
    }
}

internal sealed class XaCharacterItemSections
{
    public string InventoryJson { get; init; } = "[]";
    public string SaddlebagJson { get; init; } = "[]";
    public string CrystalsJson { get; init; } = "[]";
    public string ArmouryJson { get; init; } = "[]";
    public string EquippedJson { get; init; } = "[]";
    public string UnclassifiedJson { get; init; } = "[]";
}
