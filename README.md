# FFXIV plugins

A [Dalamud](https://dalamud.dev) custom plugin repository for my Final Fantasy XIV plugins.

## Install (in game)

1. Open `/xlplugins` → **Settings** → **Experimental** → **Custom Plugin Repositories**.
2. Add this URL:

   ```
   https://raw.githubusercontent.com/KazimeirzOdnowiciel/ffxiv-plugins/main/repo.json
   ```

3. Save, enable the repository, then install any plugin from the normal plugin list.

Dalamud refreshes `repo.json` on its own schedule; use **Settings → Check for updates** if a new
version does not show up right away.

## Plugins

| Plugin | Version | What it does |
|---|---|---|
| **Slot Yield** | 0.2.0.2 | Treats your retainer slots as a portfolio: scans the market board via Universalis, models how much of each item's daily sales you can actually capture, and picks the items that earn the most gil per slot per day. Also tracks your own listings, alerts on undercuts, plans crafts from your bags, and can reprice or list for you on request. |
| **Roulette Bingo** | 0.1.0.1 | A generated 5×5 bingo card for every duty you enter, filled with the small disasters and rituals of party content. Auto-detects deaths, wipes, vuln stacks and the chat classics; every card is seeded so a code reproduces the same board. |
| **Crit Sound** | 0.1.0.1 | Audio feedback for combat: separate sounds for critical hits, direct hits and crit directs, so you can hear your damage without watching floating text. |
| **Desynth Lens** | 0.4.0.3 | Records what each desynthesis actually returns and what those materials are worth, so you can tell a profitable salvage from a waste of gil. |
| **Mechanic Watch** | 0.2.0.2 | A party-wide grid of the statuses you choose to care about, so you can see at a glance who has what during a fight. |
| **Enemy List Cast Bars** | 1.0.0.1 | Draws a cast bar and the cast name onto each enemy-list row, highlighting interruptible casts. — *third party, MIT, see [credits](#credits)* |

## Repository layout

```
.
├── src/<Plugin>/      plugin sources, one folder per plugin
├── dist/              release zips + dist/icons/ (this is what Dalamud downloads)
├── icons/             source icons referenced by repo.json
├── repo.json          the repository manifest
└── build.ps1          builds every plugin, packages it, rewrites repo.json
```

A fresh clone is self-contained: every plugin builds from `src/`, with no folders outside the repo.

## Building

Requires the .NET SDK and a Dalamud installation (API level 15). `Enemy List Cast Bars` declares
.NET 10; the first-party plugins take their target framework from `Dalamud.NET.Sdk`.

```powershell
# build, package and rewrite repo.json
.\build.ps1

# re-package already-built binaries without compiling
.\build.ps1 -NoBuild

# build against a different download root (e.g. the release-asset layout)
.\build.ps1 -BaseUrl "https://github.com/KazimeirzOdnowiciel/ffxiv-plugins/releases/download/latest"
```

A single plugin can be compiled directly:

```powershell
dotnet build src/SlotYield/SlotYield.csproj
```

If Windows refuses the script with *"AuthorizationManager check failed"*, run it in a child process
with an explicit policy instead of changing your machine policy:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

## Releasing

1. Bump `AssemblyVersion` in the plugin's `.csproj` — Dalamud compares this number to decide whether
   an update exists, so an unchanged version is never offered.
2. Run `.\build.ps1`.
3. Commit `dist/` and `repo.json` together with the version bump.

`repo.json`'s `LastUpdate` field and CRLF line endings in descriptions change on every build. That
is expected; commit the artifacts when the version changes, not on every local build.

## Credits

`Enemy List Cast Bars` is third-party code (MIT), vendored unmodified so the repository stays
self-contained. Provenance and the import path are recorded in
[`src/EnemyListCastBars/NOTICE.md`](src/EnemyListCastBars/NOTICE.md).

The first-party plugins currently carry no license file; treat them as all rights reserved.
