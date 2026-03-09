# XA Database

A Dalamud plugin for FINAL FANTASY XIV that collects character data — inventories, currencies, job levels, retainers, free company info, and more — and stores it in a local SQLite database. XA Database is the passive snapshot/query layer for the XA suite, while XA Slave owns active automation.

## Key Features

- **Offline Character Browser** — Select any character from the database at any time without logging in. Search and filter by name or world.
- **Full Inventory Tracking** — Every item across 22 container types (equipped, armoury, saddlebags, crystals) with name, quantity, HQ status
- **Cross-Character Search** — Search items across all saved characters, retainers, and saddlebags with item stacking for duplicates
- **Currency Tracking** — 25+ currencies (Gil, Tomestones, Scrips, GC Seals, PvP, Tribal) with history
- **Job Levels** — All 32 DoW/DoM/DoH/DoL jobs displayed with sortable columns and horizontal scrolling
- **Retainer Management** — Overview table with inventory, market listings, venture status, and sale detection
- **Free Company** — FC name, tag, rank, member list, points, squadron data, airship/submarine voyages
- **Housing** — Personal estate and apartment tracking
- **Collections & Quests** — Mounts, minions, orchestrion rolls, triple triad cards, active quests, MSQ milestone progress
- **Dashboard** — Cross-character comparison with sticky `Character`, `World`, `Server`, and `Region` columns plus total gil, market value, venture status, MSQ progress, and collections
- **Auto-Save** — Saves on login, logout, addon close, and configurable timer with transaction batching
- **Legacy Snapshot Migration** — Detects old multi-table databases and offers in-plugin hold-to-confirm import / clear actions before switching to `xa_characters` snapshot rows
- **Export** — CSV and JSON export for current or all characters

## Commands

| Command       | Description                   |
| ------------- | ----------------------------- |
| `/xadb`       | Toggle the XA Database window |
| `/xadatabase` | Toggle the XA Database window |

## Dependencies

- **Optional:** [XA Slave](https://github.com/xa-io/XA-Slave) — Handles automation tasks and sends data via IPC

## This Plugin is in Development

This means that there are still features being implemented and enhanced. Suggestions and feature requests are welcome via github issues or by visiting the discord server for direct support.

## Installation

1. Install [FFXIVQuickLauncher](https://github.com/goatcorp/FFXIVQuickLauncher) and enable Dalamud in its settings. You must run the game through FFXIVQuickLauncher for plugins to work.
2. Open Dalamud settings by typing `/xlsettings` in game chat.
3. Go to the **Experimental** tab.
4. In the **Custom Plugin Repositories** section, paste the following URL:

   ```text
   https://raw.githubusercontent.com/xa-io/MyDalamudPlugins/master/pluginmaster.json
   ```

5. Click **Save**.
6. Open the plugin installer with `/xlplugins`, go to **All Plugins**, and search for **XA Database**.

## Support

- Discord server: <https://discord.gg/g2NmYxPQCa>
- Open an issue on the relevant GitHub repository for bugs or feature requests.
- [XA Database Issues](https://github.com/xa-io/XA-Database/issues)

## License

[AGPL-3.0-or-later](LICENSE)
