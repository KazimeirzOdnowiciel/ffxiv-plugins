# FFXIV plugin repository

A Dalamud custom plugin repository for these plugins:

| Plugin | What it does |
|---|---|
| **Slot Yield** | Market scan, retainer-slot portfolio optimisation, listing advice, alerts, craft planning, and opt-in repricing / listing automation. |
| **Roulette Bingo** | A generated 5x5 bingo card for every duty, with shareable seeds. |
| **Crit Sound** | Audio feedback for critical hits, direct hits, and crit directs. |

## Install (in game)

1. `/xlplugins` → **Settings** → **Experimental** → **Custom Plugin Repositories**.
2. Add this repository's `repo.json` URL, for example:
   `https://raw.githubusercontent.com/YOURNAME/ffxiv-plugins/main/repo.json`
3. Enable it, then install any of the plugins from the normal plugin list.

## Publishing your own copy

The repository does not need the plugin sources - Dalamud only needs `repo.json` and the zips it points at.

1. Create a GitHub repository and push this folder.
2. Decide how the zips are served. Two options:
   - **raw.githubusercontent.com** (simplest): commit `dist/*.zip` and point `BaseUrl` at
     `https://raw.githubusercontent.com/<user>/<repo>/main`.
   - **GitHub Releases** (nicer for versioned downloads): upload `dist/*.zip` as release assets and
     point `BaseUrl` at `https://github.com/<user>/<repo>/releases/download/<tag>`.
3. Regenerate `repo.json` with your URL:

   ```powershell
   .\build.ps1 -BaseUrl "https://raw.githubusercontent.com/<user>/<repo>/main"
   ```

   Add `-NoBuild` to skip rebuilding and only repackage what is already built.
4. Commit `repo.json` and `dist/`.

## Updating a plugin

Bump `AssemblyVersion` and `RepoUrl` in that plugin's `.csproj` (they feed its generated manifest),
then run `build.ps1` again. Dalamud compares `AssemblyVersion` to decide whether an update exists, so
the number has to increase or nothing will be offered.

## Notes

- `LastUpdate` is refreshed on every build, which is what the in-game plugin list sorts on.
- The plugin sources live outside this folder; `build.ps1` expects them as siblings
  (`..\SlotYield`, `..\RouletteBingo`, `..\CritSound`). Adjust the `$plugins` table if your layout differs.
- Local testing: Dalamud accepts `file:///` URLs for a repository in some builds, but plain
  `https` is the reliable path.
