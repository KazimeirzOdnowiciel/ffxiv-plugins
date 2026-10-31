# PluginRepo — ffxiv-plugins distributor

The public repo (`KazimeirzOdnowiciel/ffxiv-plugins`) and the plugin sources it ships. One folder
per plugin: `src/<Name>/<Name>.csproj`, sibling `verify.ps1`/`tests/` where they exist.
Workspace-level context (API level, Glasswork, user preferences) is in `D:\Deepseek\AGENTS.md`.

## Shipped plugins

CrashTrace, CritSound, DesynthLens, DutyRecap, GilLedger, MechanicWatch, Mountbook, RouletteBingo,
SlotYield, plus vendored EnemyListCastBars (third party, MIT — keep its `NOTICE.md`).

## Build

```powershell
.\build.ps1                  # build every listed plugin, package into dist/, rewrite repo.json
.\build.ps1 -NoBuild         # repackage existing binaries only
.\build.ps1 -BaseUrl "https://github.com/KazimeirzOdnowiciel/ffxiv-plugins/releases/download/latest"
dotnet build src/SlotYield/SlotYield.csproj -c Release   # one plugin
```

If PowerShell refuses the script ("AuthorizationManager check failed"), do not change machine policy:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

## Verify

Mountbook is the one plugin kept in the full three-project shape it was built in: `src/Mountbook`,
`src/Mountbook.Tests` and `src/Mountbook.Previews` sit side by side, and its ten goldens live in
`src/Mountbook.Previews/goldens`. Its gate:

```powershell
dotnet build src/Mountbook/Mountbook.csproj -c Release
dotnet run --project src/Mountbook.Tests/Mountbook.Tests.csproj -c Release   # 308 tests
dotnet build src/Mountbook.Previews/Mountbook.Previews.csproj -c Release
~/.dotnet/tools/glasswork-preview.exe --assembly src/Mountbook.Previews/bin/Release/Mountbook.Previews.dll --compare src/Mountbook.Previews/goldens
```

`dotnet test` does not start in this environment (the test host cannot get a named pipe); `dotnet run` on
the xunit v3 project is the portable path.

Only SlotYield, RouletteBingo and Mountbook have a test harness. Prefer it over ad-hoc builds:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File src/SlotYield/verify.ps1
# -Live also hits Universalis for a real scan
```

The other plugins have no automated tests; verify by building and, when the change is behavioural,
say explicitly that in-game verification is still pending.

## Release

1. Bump `AssemblyVersion` in `src/<Name>/<Name>.csproj`. Dalamud only offers an update when this
   number changes, so a rebuild without a bump is invisible to users.
2. `.\build.ps1`.
3. Commit `dist/` **and** `repo.json` together with the version bump.

CI (`.github/workflows/release.yml`) runs on pushes to `main` touching `src/**`, `build.ps1`, or the
workflow; it downloads the Dalamud distribution into `DALAMUD_HOME`, runs `build.ps1`, and commits
artifacts only when a version actually changed. Otherwise it discards them.

## Repo-specific rules

- **Publishing is an explicit list.** `build.ps1` builds from its `$plugins` array, not a `src/`
  scan. A new plugin in `src/` is unpublished until it is added there.
- **Never hand-edit `dist/` or `repo.json`.** They are generated together; a partial edit desyncs the
  download URLs from the zips.
- **Icons:** `icons/<InternalName>.png` is referenced by `repo.json`; add it with the plugin.
- **`AssemblyName` is frozen after first release** — it is the config folder and log tag.
- **EnemyListCastBars is vendored.** Do not restyle or refactor it; it declares .NET 10 explicitly.
  Everything else takes its target framework from `Dalamud.NET.Sdk` (API 15).
- **SlotYield is mid-Glasswork** (`ui/glasswork` branch, 0.8.0.0). Check the branch before changing
  its UI; the comparators are ascending, so a descending sort must `Reverse()`.

## Do not

- Reformat or "modernise" a shipped plugin as a drive-by; the nine are exempt from the strict gate.
- Rename projects or move folders — both `build.ps1` and `repo.json` hard-code the layout.
- Commit binaries for an unchanged version.
