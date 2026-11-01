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

Mountbook is the one plugin kept in the full multi-project shape it was built in: `src/Mountbook`,
`src/Mountbook.Tests`, `src/Mountbook.Previews` and `src/Mountbook.Tools` sit side by side, and its
sixteen goldens live in `src/Mountbook.Previews/goldens`. Its gate (the last line is the only one that proves the layout: it
exits 1 on any mismatch, and `--out` is **not** optional - without it the tool prints its help and exits
0, which reads exactly like a pass):

```powershell
dotnet build src/Mountbook/Mountbook.csproj -c Release
dotnet run --project src/Mountbook.Tests/Mountbook.Tests.csproj -c Release   # 604 tests committed
dotnet build src/Mountbook.Previews/Mountbook.Previews.csproj -c Release
~/.dotnet/tools/glasswork-preview.exe --assembly src/Mountbook.Previews/bin/Release/Mountbook.Previews.dll --compare src/Mountbook.Previews/goldens --out artifacts/previews/Mountbook
```

`--update` instead of `--compare` re-baselines every scene; check `git status` afterwards and expect only
the images you meant to move. A scene is any public parameterless `IPreviewScreen` in that assembly, and
`--scene <name>` renders one of them. Every scene renders at UI scale 1 - the interface has no scale
member and `--scale` is refused with `--assembly` - so scale-dependent defects are caught by audit and by
a fix's own reasoning, not by an image.

The suite and the golden compare run in CI too: `.github/workflows/ci.yml` has a **Tests** job
(`dotnet run` on the xunit v3 project, nothing else - it compiles `Services/` by path and
references no Dalamud assembly, so it needs no `DALAMUD_HOME`) and a **Goldens** job (Dalamud
assemblies, `glasswork.preview 0.1.0-beta.2` pinned, then `--compare` with `--out`).

The goldens are gating, not a local-only habit. The question was whether a runner with no GPU
can render them at all; it can. `--list-adapters` on `windows-latest` reports only "Microsoft
Basic Render Driver", and the ten committed goldens still come back with 0 differing pixels and
max delta 0 in 9 s for all ten (job: 75 s, mostly the Dalamud download and the build). The
tests job is ~45 s, 11 s of it the suite. A failure uploads the actuals and diffs as the
`previews` artifact.

Two things follow from that: the renderer version in `ci.yml` is pinned because a bump moves
pixels and must be a re-baseline decision, and a new scene needs its golden committed in the
same change - CI renders every `IPreviewScreen` in the assembly, so an uncommitted golden is a
red build, not a quiet skip.

`dotnet test` does not start in this environment (the test host cannot get a named pipe); `dotnet run` on
the xunit v3 project is the portable path.

Only SlotYield, RouletteBingo and Mountbook have a test harness. Prefer it over ad-hoc builds:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File src/SlotYield/verify.ps1
# -Live also hits Universalis for a real scan
```

The other plugins have no automated tests; verify by building and, when the change is behavioural,
say explicitly that in-game verification is still pending.

## The obtain dataset

`src/Mountbook/Data/mount-sources.json` is generated, committed and embedded - the plugin never calls
ffxivcollect at runtime. It goes stale on a patch, so refreshing it is a runbook, not a build step:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File src/Mountbook/tools/patch-day.ps1
```

One command regenerates it into a scratch file and validates it against the committed snapshot and the
game's `Mount` sheet (row band, unknown ids, vanished mounts, lost sources, patch values the expansion
map cannot read); the committed file is replaced only when nothing is an error. `docs/patch-day.md` is
the runbook - the flags, what every finding code means, and the rest of the release path.

Two of that snapshot's fields are contracts, not data: `sources[].type` must be one of the 28 types in
`src/Mountbook/Services/MountSourceKinds.cs` (the filter, its counts, the grid's source headings and the
`source:` search term all read that table, and an unlisted type fails the run), and the per-type counts are
pinned in `MountSourcesDataTests`. Adopting a type upstream added is an edit to that table, its strings key and
that test - see `docs/data-provenance.md` §9.

## Release

CI gates all of it: `release.yml` calls `.github/workflows/ci.yml` as a `needs:` job, so a red
test or a moved golden stops the run before `build.ps1` and nothing is committed. A pull request
runs the same workflow on its own.

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
