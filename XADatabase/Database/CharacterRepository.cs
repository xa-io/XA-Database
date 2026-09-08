using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using XADatabase.Core.Storage;
using XADatabase.Data;

namespace XADatabase.Database;

public class CharacterRepository
{
    private readonly DatabaseService db;

    public CharacterRepository(DatabaseService db)
    {
        this.db = db;
    }

    public (string PersonalEstate, string SharedEstates, string Apartment) GetPersonalHousing(ulong contentId)
    {
        var conn = db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT personal_estate, shared_estates, apartment FROM xa_characters WHERE content_id = @cid LIMIT 1";
        cmd.Parameters.AddTypedValue("@cid", contentId);
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            var normalizedHousing = XaCharacterSnapshotRepository.NormalizeHousingPayload(
                reader["personal_estate"].ToString() ?? "",
                reader["shared_estates"].ToString() ?? "",
                reader["apartment"].ToString() ?? ""
            );
            return normalizedHousing;
        }
        return ("", "", "");
    }

    public CharacterRow? Get(ulong contentId)
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

        var normalizedHousing = XaCharacterSnapshotRepository.NormalizeHousingPayload(
            reader["personal_estate"].ToString() ?? "",
            reader["shared_estates"].ToString() ?? "",
            reader["apartment"].ToString() ?? "");

        return new CharacterRow
        {
            ContentId = SqliteIdentity.Decode((long)reader["content_id"]),
            Name = reader["character_name"].ToString() ?? "",
            World = reader["world"].ToString() ?? "",
            Datacenter = WorldData.ResolveDataCenter(reader["world"].ToString() ?? "", reader["datacenter"].ToString() ?? ""),
            Region = WorldData.ResolveRegion(reader["world"].ToString() ?? "", reader["region"].ToString() ?? ""),
            LastSeenUtc = reader["updated_utc"].ToString() ?? "",
            CreatedUtc = reader["exported_utc"].ToString() ?? "",
            PersonalEstate = normalizedHousing.PersonalEstate,
            SharedEstates = normalizedHousing.SharedEstates,
            Apartment = normalizedHousing.Apartment,
        };
    }

    public List<CharacterRow> GetAllLegacy()
    {
        var results = new List<CharacterRow>();
        if (!db.TableExists("characters"))
            return results;

        var conn = db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT content_id, name, world, datacenter, last_seen_utc, created_utc, personal_estate, apartment
            FROM characters
            ORDER BY last_seen_utc DESC, name ASC";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var normalizedHousing = XaCharacterSnapshotRepository.NormalizeHousingPayload(
                reader["personal_estate"].ToString() ?? "",
                string.Empty,
                reader["apartment"].ToString() ?? "");
            results.Add(new CharacterRow
            {
                ContentId = SqliteIdentity.Decode((long)reader["content_id"]),
                Name = reader["name"].ToString() ?? "",
                World = reader["world"].ToString() ?? "",
                Datacenter = WorldData.ResolveDataCenter(reader["world"].ToString() ?? "", reader["datacenter"].ToString() ?? ""),
                Region = WorldData.ResolveRegion(reader["world"].ToString() ?? ""),
                LastSeenUtc = reader["last_seen_utc"].ToString() ?? "",
                CreatedUtc = reader["created_utc"].ToString() ?? "",
                PersonalEstate = normalizedHousing.PersonalEstate,
                Apartment = normalizedHousing.Apartment,
            });
        }

        return results;
    }

    public void Delete(ulong contentId)
    {
        db.DeleteCharacter(contentId);
    }

    public List<CharacterRow> GetAll()
    {
        var results = new List<CharacterRow>();
        var conn = db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT content_id, character_name, world, datacenter, region, updated_utc, exported_utc, personal_estate, shared_estates, apartment
            FROM xa_characters
            ORDER BY updated_utc DESC, character_name ASC";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var normalizedHousing = XaCharacterSnapshotRepository.NormalizeHousingPayload(
                reader["personal_estate"].ToString() ?? "",
                reader["shared_estates"].ToString() ?? "",
                reader["apartment"].ToString() ?? "");
            results.Add(new CharacterRow
            {
                ContentId = SqliteIdentity.Decode((long)reader["content_id"]),
                Name = reader["character_name"].ToString() ?? "",
                World = reader["world"].ToString() ?? "",
                Datacenter = WorldData.ResolveDataCenter(reader["world"].ToString() ?? "", reader["datacenter"].ToString() ?? ""),
                Region = WorldData.ResolveRegion(reader["world"].ToString() ?? "", reader["region"].ToString() ?? ""),
                LastSeenUtc = reader["updated_utc"].ToString() ?? "",
                CreatedUtc = reader["exported_utc"].ToString() ?? "",
                PersonalEstate = normalizedHousing.PersonalEstate,
                SharedEstates = normalizedHousing.SharedEstates,
                Apartment = normalizedHousing.Apartment,
            });
        }
        return results;
    }
}

public class CharacterRow
{
    public ulong ContentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string World { get; set; } = string.Empty;
    public string Datacenter { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string LastSeenUtc { get; set; } = string.Empty;
    public string CreatedUtc { get; set; } = string.Empty;
    public string PersonalEstate { get; set; } = string.Empty;
    public string SharedEstates { get; set; } = string.Empty;
    public string Apartment { get; set; } = string.Empty;
}
