using System.Collections.Generic;
using System.Linq;

namespace XADatabase.Data;

public static class WorldData
{
    public record WorldInfo(uint Id, string Name, string DataCenter, string Region);

    public static readonly string[] RegionOrder = { "NA", "EU", "JP", "OCE" };

    public static readonly Dictionary<string, string[]> DataCenterOrder = new()
    {
        ["NA"] = new[] { "Aether", "Crystal", "Dynamis", "Primal" },
        ["EU"] = new[] { "Chaos", "Light" },
        ["JP"] = new[] { "Elemental", "Gaia", "Mana", "Meteor" },
        ["OCE"] = new[] { "Materia" },
    };

    public static readonly WorldInfo[] Worlds =
    {
        new(73,  "Adamantoise", "Aether", "NA"),
        new(79,  "Cactuar",     "Aether", "NA"),
        new(54,  "Faerie",      "Aether", "NA"),
        new(63,  "Gilgamesh",   "Aether", "NA"),
        new(40,  "Jenova",      "Aether", "NA"),
        new(65,  "Midgardsormr", "Aether", "NA"),
        new(99,  "Sargatanas",  "Aether", "NA"),
        new(57,  "Siren",       "Aether", "NA"),
        new(91,  "Balmung",     "Crystal", "NA"),
        new(34,  "Brynhildr",   "Crystal", "NA"),
        new(74,  "Coeurl",      "Crystal", "NA"),
        new(62,  "Diabolos",    "Crystal", "NA"),
        new(81,  "Goblin",      "Crystal", "NA"),
        new(75,  "Malboro",     "Crystal", "NA"),
        new(37,  "Mateus",      "Crystal", "NA"),
        new(41,  "Zalera",      "Crystal", "NA"),
        new(408, "Cuchulainn",  "Dynamis", "NA"),
        new(411, "Golem",       "Dynamis", "NA"),
        new(406, "Halicarnassus", "Dynamis", "NA"),
        new(409, "Kraken",      "Dynamis", "NA"),
        new(407, "Maduin",      "Dynamis", "NA"),
        new(404, "Marilith",    "Dynamis", "NA"),
        new(410, "Rafflesia",   "Dynamis", "NA"),
        new(405, "Seraph",      "Dynamis", "NA"),
        new(78,  "Behemoth",    "Primal", "NA"),
        new(93,  "Excalibur",   "Primal", "NA"),
        new(53,  "Exodus",      "Primal", "NA"),
        new(35,  "Famfrit",     "Primal", "NA"),
        new(95,  "Hyperion",    "Primal", "NA"),
        new(55,  "Lamia",       "Primal", "NA"),
        new(64,  "Leviathan",   "Primal", "NA"),
        new(77,  "Ultros",      "Primal", "NA"),
        new(80,  "Cerberus",    "Chaos", "EU"),
        new(83,  "Louisoix",    "Chaos", "EU"),
        new(71,  "Moogle",      "Chaos", "EU"),
        new(39,  "Omega",       "Chaos", "EU"),
        new(401, "Phantom",     "Chaos", "EU"),
        new(97,  "Ragnarok",    "Chaos", "EU"),
        new(400, "Sagittarius", "Chaos", "EU"),
        new(85,  "Spriggan",    "Chaos", "EU"),
        new(402, "Alpha",       "Light", "EU"),
        new(36,  "Lich",        "Light", "EU"),
        new(66,  "Odin",        "Light", "EU"),
        new(56,  "Phoenix",     "Light", "EU"),
        new(403, "Raiden",      "Light", "EU"),
        new(67,  "Shiva",       "Light", "EU"),
        new(33,  "Twintania",   "Light", "EU"),
        new(42,  "Zodiark",     "Light", "EU"),
        new(90,  "Aegis",       "Elemental", "JP"),
        new(68,  "Atomos",      "Elemental", "JP"),
        new(45,  "Carbuncle",   "Elemental", "JP"),
        new(58,  "Garuda",      "Elemental", "JP"),
        new(94,  "Gungnir",     "Elemental", "JP"),
        new(49,  "Kujata",      "Elemental", "JP"),
        new(72,  "Tonberry",    "Elemental", "JP"),
        new(50,  "Typhon",      "Elemental", "JP"),
        new(43,  "Alexander",   "Gaia", "JP"),
        new(69,  "Bahamut",     "Gaia", "JP"),
        new(92,  "Durandal",    "Gaia", "JP"),
        new(46,  "Fenrir",      "Gaia", "JP"),
        new(59,  "Ifrit",       "Gaia", "JP"),
        new(98,  "Ridill",      "Gaia", "JP"),
        new(76,  "Tiamat",      "Gaia", "JP"),
        new(51,  "Ultima",      "Gaia", "JP"),
        new(44,  "Anima",       "Mana", "JP"),
        new(23,  "Asura",       "Mana", "JP"),
        new(70,  "Chocobo",     "Mana", "JP"),
        new(47,  "Hades",       "Mana", "JP"),
        new(48,  "Ixion",       "Mana", "JP"),
        new(96,  "Masamune",    "Mana", "JP"),
        new(28,  "Pandaemonium", "Mana", "JP"),
        new(61,  "Titan",       "Mana", "JP"),
        new(24,  "Belias",      "Meteor", "JP"),
        new(82,  "Mandragora",  "Meteor", "JP"),
        new(60,  "Ramuh",       "Meteor", "JP"),
        new(29,  "Shinryu",     "Meteor", "JP"),
        new(30,  "Unicorn",     "Meteor", "JP"),
        new(52,  "Valefor",     "Meteor", "JP"),
        new(31,  "Yojimbo",     "Meteor", "JP"),
        new(32,  "Zeromus",     "Meteor", "JP"),
        new(22,  "Bismarck",    "Materia", "OCE"),
        new(21,  "Ravana",      "Materia", "OCE"),
        new(86,  "Sephirot",    "Materia", "OCE"),
        new(87,  "Sophia",      "Materia", "OCE"),
        new(88,  "Zurvan",      "Materia", "OCE"),
    };

    private static readonly Dictionary<uint, WorldInfo> ById = Worlds.ToDictionary(w => w.Id);
    private static readonly Dictionary<string, WorldInfo> ByName = Worlds.ToDictionary(w => w.Name.ToLowerInvariant());

    public static WorldInfo? GetById(uint id) => ById.TryGetValue(id, out var world) ? world : null;

    public static WorldInfo? GetByName(string name) =>
        string.IsNullOrWhiteSpace(name) ? null : ByName.TryGetValue(name.ToLowerInvariant(), out var world) ? world : null;

    public static string ResolveDataCenter(string worldName, string fallbackDataCenter = "") =>
        GetByName(worldName)?.DataCenter ?? fallbackDataCenter ?? string.Empty;

    public static string ResolveRegion(string worldName, string fallbackRegion = "") =>
        GetByName(worldName)?.Region ?? fallbackRegion ?? string.Empty;
}
