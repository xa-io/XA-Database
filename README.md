# XA Database

A Dalamud plugin for FINAL FANTASY XIV that collects character data — inventories, currencies, job levels, retainers, free company info, and more — and stores it in a local SQLite database.

- View all our utilities & plugins here: https://aethertek.io/

## Key Features

- **Plugin Operations** - Utility settings include `Open Plugin on Load` and a default-on `Show Version in Window Title` toggle that keeps the current XA Database version visible unless you turn it off.

- **Offline Character Browser** — Browse saved characters anytime with name and world filters.
- **Full Inventory Tracking** — Track equipped gear, armoury, saddlebags, character crystals, and each retainer's shards, crystals, and clusters.
- **Authoritative Refreshes** — Refresh readable inventory containers, including emptied bags, while retaining unavailable storage. Main inventory pages and saddlebag pairs are collected as coherent groups; other unavailable character sections keep their last-good data.
- **Live Inventory Capture** — Inventory-change notifications keep the current character's in-memory cache up to date during automation, including while browsing a saved character. Normal and premium saddlebags are captured when readable; closed or unavailable storage remains last-observed data.
- **Snapshot Integrity** — Browse saved characters without losing cached items, preserve the live character for logout saves, and distinguish save time from live-refresh time. Older, malformed, different-character, or lower-quality replacements are rejected; newer partial refreshes can save while preserving unavailable sections.
- **Stable Local Identity** — Preserve character, retainer, and free-company IDs in the database, identify currencies independently of their display language, and protect other FC-member content IDs with stable hashes unique to the local installation.
- **Cross-Character Search** — Search items across characters, retainers, and saddlebags, show owned location totals directly in live item tooltips, reuse the same recent-first ownership summary on Search tab hover, show a final matching-quantity total in Search results, and jump straight into an exact search from the XA `Search For Item` inventory right-click action.
- **Scoped IPC Item Search** — Automation consumers can request current-character retainer item rows by item ID through structured JSON without receiving other-character search results.
- **Currency Tracking** — Track gil, retainer gil, master-only FC chest gil, and expanded common, battle, other, and society currencies from live wallet and container data.
- **Job Levels** — View all combat, limited, crafting, and gathering job levels in sortable tables, including Beastmaster.
- **Retainer Management** — Review retainer inventory, ventures, market listings, and sale status.
- **Free Company** — Save FC name, rank, members, points, master-only chest gil, squadron, and workshop voyage data.
- **Housing** — Track personal, shared, and apartment housing with cleaner normalization; non-apartment estate sizes are normalized from a verified hardcoded table for every residential district plot.
- **Collections & Quests** — View mounts, minions, rolls, cards, active quests, and MSQ progress.
- **Dashboard** — Compare characters, gil, retainers, FC chest gil, ventures, market value, MSQ, and collections in one view.
- **Optional AutoRetainer Exclusions** — Hide characters omitted or excluded by current AutoRetainer releases from cross-character views, totals, searches, tooltips, IPC rosters/results, and all-character exports without deleting saved XA data; incompatible or unavailable IPC still fails open and shows all characters.
- **Safe Character Cleanup** — Delete stored character snapshots from the Dashboard or Settings only while holding `Ctrl+Shift`.
- **Auto-Save** — Save on login, logout, manual refresh, and IPC requests. Optional timer and addon-close settings control additional disk saves; inventory-change capture does not force a save. Queued work stays with its originating character, and active Search results refresh after saving.
- **Database Health Checks** — Run built-in health, read/write, and integrity checks from Settings.
- **Migration Safety** — Back up existing data before schema or legacy-table migrations, apply upgrades transactionally, recover preserved high-bit FC identities during corrective migrations, and retain legacy tables unless the migrated character set passes completeness checks.
- **Plugin Integration** — Share character data through 21 IPC channels, with safe responses when data is unavailable, separate save and refresh times in UTC and local time, and reliable IPC cleanup on reload or unload.
- **Export** — Export current or saved character data to CSV or JSON, and open the actual `xa.db` folder directly from Settings.

## Commands

| Command       | Description                   |
| ------------- | ----------------------------- |
| `/xadb`       | Toggle the XA Database window |
| `/xadatabase` | Toggle the XA Database window |

## Dependencies

- **Optional:** [XA Slave](https://github.com/xa-io/XA-Slave) — Handles automation tasks and sends data via IPC

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
