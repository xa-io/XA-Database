# XA Database

A Dalamud plugin for FINAL FANTASY XIV that collects character data — inventories, currencies, job levels, retainers, free company info, and more — and stores it in a local SQLite database.

## Key Features

- **Offline Character Browser** — Browse saved characters anytime with name and world filters.
- **Full Inventory Tracking** — Track equipped gear, armoury, saddlebags, crystals, and more.
- **Cross-Character Search** — Search items across characters, retainers, and saddlebags.
- **Currency Tracking** — Track gil and major currencies from live wallet and container data.
- **Job Levels** — View all combat, crafting, and gathering job levels in sortable tables.
- **Retainer Management** — Review retainer inventory, ventures, market listings, and sale status.
- **Free Company** — Save FC name, rank, members, points, squadron, and workshop voyage data.
- **Housing** — Track personal, shared, and apartment housing with cleaner normalization.
- **Collections & Quests** — View mounts, minions, rolls, cards, active quests, and MSQ progress.
- **Dashboard** — Compare characters, gil, retainers, ventures, market value, MSQ, and collections in one view.
- **Auto-Save** — Save on login, logout, timers, and supported addon or window flows.
- **Database Health Checks** — Run built-in health, read/write, and integrity checks from Settings.
- **Export** — Export current or saved character data to CSV or JSON.

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
