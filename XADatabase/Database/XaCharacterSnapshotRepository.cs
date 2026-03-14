using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.Data.Sqlite;
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
    private static readonly Regex PlotNumberRegex = new(@"\bPlot\s+(?<plot>\d+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex WardNumberRegex = new(@"\b(?:Ward\s+(?<wardAfter>\d+)|(?<wardBefore>\d+)(?:st|nd|rd|th)\s+Ward)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RoomNumberRegex = new(@"\bRoom\s+#?(?<room>\d+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ParentheticalTextRegex = new(@"\s*\([^)]*\)", RegexOptions.Compiled);
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
        cmd.Parameters.AddWithValue("@cid", (long)contentId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;

        var normalizedHousing = NormalizeHousingPayload(
            reader["personal_estate"].ToString() ?? string.Empty,
            reader["shared_estates"].ToString() ?? string.Empty,
            reader["apartment"].ToString() ?? string.Empty);

        return new CharacterRow
        {
            ContentId = (ulong)(long)reader["content_id"],
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
        cmd.Parameters.AddWithValue("@cid", (long)contentId);
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
        int retainerGil,
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
        var normalizedRetainers = NormalizeRetainerPayload(retainers, listings, retainerItems);
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
            ItemsJson = Serialize(items, "[]"),
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
        int retainerGil,
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
                DeserializeList<RetainerInventoryItem>(GetRawProperty(root, "retainerItems", "[]")));
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
                ItemsJson = Serialize(items, "[]"),
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
        var normalizedPersonalEstate = NormalizeHousingDisplayValue(personalEstate);
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
        var normalizedValue = StripHousingOwnerSuffix(NormalizeHousingDisplayValue(value));
        if (normalizedValue.Length == 0)
            return string.Empty;

        var roomNumber = ExtractNumber(RoomNumberRegex, normalizedValue, "room");
        var wardNumber = ExtractWardNumber(normalizedValue);
        var districtName = ExtractDistrictDisplayName(normalizedValue);
        if (roomNumber > 0 && wardNumber > 0 && districtName.Length > 0)
            return $"Room {roomNumber}, Ward {wardNumber}, {districtName}";

        return normalizedValue;
    }

    public static string PreferSizedPersonalEstateValue(string currentValue, string persistedValue)
    {
        var normalizedCurrent = StripHousingOwnerSuffix(currentValue);
        var normalizedPersisted = StripHousingOwnerSuffix(persistedValue);
        if (normalizedCurrent.Length == 0)
            return normalizedPersisted;
        if (normalizedPersisted.Length == 0)
            return normalizedCurrent;
        if (!AddressesMatch(normalizedCurrent, normalizedPersisted))
            return normalizedCurrent;

        var currentHasSizeSuffix = ParentheticalTextRegex.IsMatch(normalizedCurrent);
        var persistedHasSizeSuffix = ParentheticalTextRegex.IsMatch(normalizedPersisted);
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
        var snapshot = new XaCharacterSnapshotData
        {
            Row = row,
            InventorySummaries = DeserializeList<InventorySummary>(row.InventorySummariesJson),
            Currencies = DeserializeList<CurrencyEntry>(row.CurrenciesJson),
            Jobs = NormalizeJobs(DeserializeList<JobEntry>(row.JobsJson)),
            InventoryItems = DeserializeList<ContainerItemEntry>(row.InventoryJson),
            SaddlebagItems = DeserializeList<ContainerItemEntry>(row.SaddlebagJson),
            CrystalItems = DeserializeList<ContainerItemEntry>(row.CrystalsJson),
            ArmouryItems = DeserializeList<ContainerItemEntry>(row.ArmouryJson),
            EquippedItems = DeserializeList<ContainerItemEntry>(row.EquippedJson),
            AllItems = DeserializeList<ContainerItemEntry>(row.ItemsJson),
            Retainers = DeserializeList<RetainerEntry>(row.RetainersJson),
            Listings = DeserializeList<RetainerListingEntry>(row.ListingsJson),
            RetainerItems = DeserializeList<RetainerInventoryItem>(row.RetainerItemsJson),
            FreeCompany = DeserializeObject<FreeCompanyEntry>(row.FreeCompanyJson),
            FcMembers = DeserializeList<FcMemberEntry>(row.FcMembersJson),
            Squadron = DeserializeObject<SquadronInfo>(row.SquadronJson),
            Voyages = DeserializeObject<VoyageInfo>(row.VoyagesJson),
            Collections = DeserializeList<CollectionSummary>(row.CollectionsJson),
            ActiveQuests = DeserializeList<ActiveQuestEntry>(row.ActiveQuestsJson),
            MsqMilestones = DeserializeList<MsqMilestoneEntry>(row.MsqMilestonesJson),
        };

        if (snapshot.AllItems.Count == 0)
        {
            snapshot.AllItems = snapshot.InventoryItems
                .Concat(snapshot.SaddlebagItems)
                .Concat(snapshot.CrystalItems)
                .Concat(snapshot.ArmouryItems)
                .Concat(snapshot.EquippedItems)
                .ToList();
        }

        var normalizedRetainers = NormalizeRetainerPayload(snapshot.Retainers, snapshot.Listings, snapshot.RetainerItems);
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
            ContentId = (ulong)(long)reader["content_id"],
            CharacterName = reader["character_name"].ToString() ?? string.Empty,
            World = reader["world"].ToString() ?? string.Empty,
            Datacenter = ResolveDatacenter(reader["world"].ToString() ?? string.Empty, reader["datacenter"].ToString() ?? string.Empty),
            Region = ResolveRegion(reader["world"].ToString() ?? string.Empty, reader["region"].ToString() ?? string.Empty),
            FcId = ReadUInt64(reader, "fc_id"),
            FcName = reader["fc_name"].ToString() ?? string.Empty,
            FcTag = reader["fc_tag"].ToString() ?? string.Empty,
            FcPoints = Convert.ToInt32(reader["fc_points"]),
            FcEstate = reader["fc_estate"].ToString() ?? string.Empty,
            PersonalEstate = normalizedHousing.PersonalEstate,
            SharedEstates = normalizedHousing.SharedEstates,
            Apartment = normalizedHousing.Apartment,
            Gil = Convert.ToInt32(reader["gil"]),
            RetainerGil = Convert.ToInt32(reader["retainer_gil"]),
            RetainerCount = Convert.ToInt32(reader["retainer_count"]),
            HighestJobLevel = Convert.ToInt32(reader["highest_job_level"]),
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
            SnapshotVersion = Convert.ToInt32(reader["snapshot_version"]),
            ExportedUtc = reader["exported_utc"].ToString() ?? string.Empty,
            Trigger = reader["trigger"].ToString() ?? string.Empty,
            TriggerDetail = reader["trigger_detail"].ToString() ?? string.Empty,
            ImportedFromLegacy = Convert.ToInt32(reader["imported_from_legacy"]) == 1,
            UpdatedUtc = reader["updated_utc"].ToString() ?? string.Empty,
        };
    }

    private static XaCharacterItemSections BuildItemSections(IEnumerable<ContainerItemEntry> items)
    {
        var itemList = items.ToList();
        return new XaCharacterItemSections
        {
            InventoryJson = Serialize(itemList.Where(i => IsInventoryContainer(i.ContainerName)).ToList(), "[]"),
            SaddlebagJson = Serialize(itemList.Where(i => IsSaddlebagContainer(i.ContainerName)).ToList(), "[]"),
            CrystalsJson = Serialize(itemList.Where(i => IsCrystalsContainer(i.ContainerName)).ToList(), "[]"),
            ArmouryJson = Serialize(itemList.Where(i => IsArmouryContainer(i.ContainerName)).ToList(), "[]"),
            EquippedJson = Serialize(itemList.Where(i => IsEquippedContainer(i.ContainerName)).ToList(), "[]"),
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
        return jobs.Select(job => new JobEntry
        {
            Abbreviation = NormalizeUpper(job.Abbreviation),
            Name = NormalizeUpper(job.Name),
            Category = job.Category,
            Level = job.Level,
            IsUnlocked = job.IsUnlocked,
        }).ToList();
    }

    public static (List<RetainerEntry> Retainers, List<RetainerListingEntry> Listings, List<RetainerInventoryItem> RetainerItems) NormalizeRetainerPayload(
        IEnumerable<RetainerEntry> retainers,
        IEnumerable<RetainerListingEntry> listings,
        IEnumerable<RetainerInventoryItem> retainerItems)
    {
        var normalizedRetainers = retainers
            .Where(retainer => retainer != null && retainer.RetainerId != 0)
            .GroupBy(retainer => retainer.RetainerId)
            .Select(group => group
                .OrderByDescending(GetRetainerCompletenessScore)
                .ThenBy(retainer => retainer.Name, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderBy(retainer => retainer.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalizedRetainers.Count == 0)
            return (new List<RetainerEntry>(), new List<RetainerListingEntry>(), new List<RetainerInventoryItem>());

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

        return (normalizedRetainers, normalizedListings, normalizedRetainerItems);
    }

    private static int GetRetainerCompletenessScore(RetainerEntry retainer)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(retainer.Name)) score += 8;
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

    private static string Serialize<T>(T value, string fallbackJson)
    {
        try
        {
            return JsonSerializer.Serialize(value, JsonOptions);
        }
        catch
        {
            return fallbackJson;
        }
    }

    private static List<T> DeserializeList<T>(string? json)
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

    private static T? DeserializeObject<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch
        {
            return null;
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
        int fallbackRetainerGil)
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
            retainerGil = GetInt32(character, "retainerGil", retainerGil);
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
            .Select(NormalizeHousingDisplayValue)
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

        var plotNumber = ExtractNumber(PlotNumberRegex, normalizedValue, "plot");
        var wardNumber = ExtractWardNumber(normalizedValue);
        var districtName = ExtractDistrictName(normalizedValue);
        if (plotNumber <= 0 || wardNumber <= 0 || districtName.Length == 0)
            return string.Empty;

        return $"plot:{plotNumber}|ward:{wardNumber}|district:{districtName}";
    }

    private static int ExtractWardNumber(string value)
    {
        var match = WardNumberRegex.Match(value);
        if (!match.Success)
            return 0;

        if (int.TryParse(match.Groups["wardAfter"].Value, out var wardAfter))
            return wardAfter;

        if (int.TryParse(match.Groups["wardBefore"].Value, out var wardBefore))
            return wardBefore;

        return 0;
    }

    private static int ExtractNumber(Regex regex, string value, string groupName)
    {
        var match = regex.Match(value);
        if (!match.Success)
            return 0;

        return int.TryParse(match.Groups[groupName].Value, out var parsedValue) ? parsedValue : 0;
    }

    private static string ExtractDistrictName(string value)
    {
        return NormalizeHousingTextForComparison(ExtractDistrictDisplayName(value));
    }

    private static string ExtractDistrictDisplayName(string value)
    {
        var segments = StripHousingOwnerSuffix(value)
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => segment.Trim())
            .Where(segment => segment.Length > 0)
            .ToArray();
        if (segments.Length == 0)
            return string.Empty;

        return ParentheticalTextRegex.Replace(segments[^1], string.Empty).Trim().Trim(',', ' ');
    }

    private static string NormalizeHousingDisplayValue(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
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

        return (ulong)Convert.ToInt64(value);
    }
}

public sealed class XaCharacterSnapshotSections
{
    public string InventorySummariesJson { get; init; } = "[]";
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
    public int RetainerGil { get; init; }
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

internal sealed class XaCharacterItemSections
{
    public string InventoryJson { get; init; } = "[]";
    public string SaddlebagJson { get; init; } = "[]";
    public string CrystalsJson { get; init; } = "[]";
    public string ArmouryJson { get; init; } = "[]";
    public string EquippedJson { get; init; } = "[]";
}
