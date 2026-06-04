# XA Database

A Dalamud plugin for FINAL FANTASY XIV that collects character data — inventories, currencies, job levels, retainers, free company info, and more — and stores it in a local SQLite database.

- View all our utilities & plugins here: https://aethertek.io/

## Key Features

- **Plugin Operations** - Utility settings include `Open Plugin on Load` and a default-on `Show Version in Window Title` toggle that keeps the current XA Database version visible unless you turn it off.

- **Offline Character Browser** — Browse saved characters anytime with name and world filters.
- **Full Inventory Tracking** — Track equipped gear, armoury, saddlebags, crystals, and more.
- **Cross-Character Search** — Search items across characters, retainers, and saddlebags, show owned location totals directly in live item tooltips, reuse the same recent-first ownership summary on Search tab hover, show a final matching-quantity total in Search results, and jump straight into an exact search from the XA `Search For Item` inventory right-click action.
- **Scoped IPC Item Search** — Automation consumers can request current-character retainer item rows by item ID through structured JSON without receiving other-character search results.
- **Currency Tracking** — Track gil, retainer gil, master-only FC chest gil, and expanded common, battle, other, and society currencies from live wallet and container data.
- **Job Levels** — View all combat, limited, crafting, and gathering job levels in sortable tables, including Beastmaster.
- **Retainer Management** — Review retainer inventory, ventures, market listings, and sale status.
- **Free Company** — Save FC name, rank, members, points, master-only chest gil, squadron, and workshop voyage data.
- **Housing** — Track personal, shared, and apartment housing with cleaner normalization; non-apartment estate sizes are normalized from a verified hardcoded table for every residential district plot.
- **Collections & Quests** — View mounts, minions, rolls, cards, active quests, and MSQ progress.
- **Dashboard** — Compare characters, gil, retainers, FC chest gil, ventures, market value, MSQ, and collections in one view.
- **Safe Character Cleanup** — Delete stored character snapshots from the Dashboard or Settings only while holding `Ctrl+Shift`.
- **Auto-Save** — Save on login, logout, timers, and supported addon or window flows; successful logout saves now checkpoint WAL changes back into the base `xa.db` file for external file-copy consumers.
- **Database Health Checks** — Run built-in health, read/write, and integrity checks from Settings.
- **Export** — Export current or saved character data to CSV or JSON, and open the actual `xa.db` folder directly from Settings.

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
   https://aethertek.io/x.json
   ```

5. Click **Save**.
6. Open the plugin installer with `/xlplugins`, go to **All Plugins**, and search for **XA Database**.

## Support

- Discord server: <https://discord.gg/g2NmYxPQCa>
- Open an issue on the relevant GitHub repository for bugs or feature requests.
- [XA Database Issues](https://github.com/xa-io/XA-Database/issues)

## License

[AGPL-3.0-or-later](LICENSE)
