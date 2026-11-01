# Data provenance: the obtain snapshot

`src/Mountbook/Data/mount-sources.json` is the plugin's only third-party dataset. It is generated at
development time, committed, and embedded in the assembly as a resource; the plugin never calls the API
at runtime (CONTRIBUTING N2). This document records where every field comes from, what the source's terms
actually say, how the licence is honoured in the shipped plugin, and the measurements behind the size
budget and the schema version. `docs/patch-day.md` is the runbook that uses them.

## 1. Where each fact comes from

| Field in the snapshot | Source | Note |
| --- | --- | --- |
| `id` | FFXIV Collect `Mount` records | Cross-checked against the game's `Mount` sheet: every id must be a sheet row (`unknown-mount-id`), and a sheet row may legitimately have no enrichment |
| `patch` | FFXIV Collect | The plugin's patch filter and expansion strip read it; a value that maps to no known expansion fails the build (`patch-not-in-sheet`) |
| `itemId` | FFXIV Collect `item_id` | The whistle item, for the market price lookup and the chat item link |
| `owned` | FFXIV Collect `owned` | Community ownership percentage. An *aggregate statistic*, not per-player data (see §3) |
| `tradeable` | FFXIV Collect `tradeable` | Gates the price-watch UI |
| `customMusic` | FFXIV Collect `custom_music` | Whether the mount overrides the BGM |
| `sources[]` | FFXIV Collect `sources[]` (`type`, `text`, `related_type`, `related_id`) | The "How to obtain" list. `type` is the site's own 28-value taxonomy, adopted rather than invented and pinned by the plugin's filter, its counts and its grouping (§9) |
| mount name, description, tooltip, icon, artwork, seats, movement | **The game's own sheets** (`Mount`, `MountTransient`, `Item`) | Read at runtime through Lumina. None of it is taken from the site — and the two fields the site also publishes (seats, movement) are cross-checked against the sheet at build time instead (§7) |

Two fields the site also publishes are deliberately **not** shipped: the mount's `description` /
`enhanced_description` / `tooltip` copy, and its `image` / `icon` / `bgm` URLs. The text is Square Enix's
prose and the game's own sheets already carry it, so copying it would duplicate data the plugin can read
for free — and, as §4 measures, it is the single change that would triple the file.

## 2. The licence reading (checked 2026-11-01)

The API documentation page, `https://ffxivcollect.com/api/docs`, carries the only statement of terms the
project publishes. Its "Acceptable Use" section, quoted in full:

> The data provided by this API is made possible by open source software, generous crowd funding in
> support of an ad-free internet, and countless volunteer contributions. This API is restricted to
> **non-commercial** use. I encourage you to publish projects that make use of this API as open source
> software, but it is not required. Attribution linking to FFXIV Collect is greatly appreciated.

Read literally, that settles four things:

1. **Non-commercial only.** Mountbook is free, open source, has no ads, no donations and no paid tier, so
   shipping the snapshot complies. The obligation this creates is a standing one: if the plugin is ever
   sold, bundled in a paid package or gated behind a subscription, the snapshot has to come out (or the
   site's permission has to be sought) before that build ships. Nothing in the code can enforce it — it is
   a release decision, recorded here so it is not rediscovered late.
2. **Attribution is "greatly appreciated", not demanded.** The plugin does it anyway, in four places
   (§5): leaving a volunteer project's work uncredited is not a defensible reading of "greatly
   appreciated".
3. **Open-sourcing Mountbook is encouraged, not required.** It is open source regardless.
4. **No rate limits, no key, no registration.** `GET /api/mounts?limit=1000` answers 200 with all 356
   rows in one response, and the endpoint is outside everything `robots.txt` disallows (`/characters/`,
   `/fc/`, `/groups/`, `/leaderboards`, the auth callback paths). The generator fetches once per
   regeneration and nothing at runtime.

There is no other published licence: `/credits` names the individual "Sources" contributors (Anahia Fhey,
Nyoko Miyajima, Kimai Yukihyou, Assassindied Lafourbe, Sera Fae and others) and the site's author, and
`/faq` carries the privacy note and the definitions below. **No part of the site's own code is copied or
vendored** — the plugin reads the JSON API, nothing else.

The `Mount`/`MountTransient`/`Item` sheet content that the plugin draws beside this data is Square Enix's
(FINAL FANTASY is a registered trademark of Square Enix Holdings Co., Ltd.), as the site itself notes. The
plugin does not redistribute sheet text: it reads the player's own installed game.

## 3. What the data can and cannot say

- **Ownership percentages are a sample, not the playerbase.** The site computes them from public
  characters that own at least one collectable in the category; on 2026-11-01 `/faq` reported 1,190,537
  tracked characters, 289,091 of them with public achievements. The value is presented as a community
  rarity signal, which is what it is.
- **No personal data ships.** The snapshot carries one aggregate number per mount. No character id,
  name, world or date is fetched, stored or embedded, so the privacy question is limited to the aggregate.
- **Premium and time-limited are derived, not fetched.** The site defines both in `/faq` (real-money
  including referral rewards; no longer obtainable, including seasonal and ranked rewards) and exposes
  neither as an API field, so the plugin derives its own class from the source types plus a curated flag.
  It says "class", not "fact", in the UI.
- **The site's prose is not ours to restate.** Source lines are displayed as the volunteer project wrote
  them, credited where they appear, not paraphrased into plugin copy.

## 4. The size budget (measured 2026-11-01, 356 mounts)

The file is embedded in the assembly and downloaded by every player, so its size is a budget, not an
observation. Measured against the same 356 records, in the same field order the generator writes:

| Variant | Bytes | Size |
| --- | --- | --- |
| Metadata only, minified JSON | 77,418 | 75.6 KiB |
| **The shipped snapshot as generated** (one line per source object) | **110,953** | **108.4 KiB** |
| Metadata only, `indent=2` | 131,356 | 128.3 KiB |
| Metadata + the API's `description` / `enhanced_description` / `tooltip` | 252,700 | 246.8 KiB |

The earlier working figures of "83 KB metadata only, 251 KB with the prose" are the minified rows of this
same measurement; the shipped file is larger than 83 KB only because the generator writes it readably.

**Budget: 120 KiB = 122,880 bytes.** The shipped file has ~11,900 bytes of headroom. The prose copy alone
measures 159,974 bytes, so taking it is the one change that cannot fit — which is why both the generator
and the validator refuse it (`description-copy`) rather than leaving it to review. Raising the budget is a
decision about every player's download and belongs in a commit message, not a quiet edit of a constant.

## 5. Where the attribution is shown

1. **In the file itself** — `attribution` + `attributionUrl` in the snapshot header, so the credit travels
   with the data and any future consumer inherits it.
2. **In the settings window** — the "Obtain data" row prints the snapshot's own description
   (`ffxivcollect.com, 2026-11-01, schema v3`, and "built offline" when that is what it is), and under it the
   credit line: `Obtain data from ffxivcollect.com, a volunteer community project.`
3. **In the detail pane** — `Source data: ffxivcollect.com`, drawn under the "How to obtain" list. It is
   gated on the mount having source lines *and* the snapshot naming a source, so an unenriched mount does
   not carry a footnote about data it does not have.
4. **In the plugin description** (`src/Mountbook/Mountbook.csproj`, the manifest the installer shows) — the
   full sentence, including the non-commercial restriction, so the terms are readable before installing.

The site named in the UI is the snapshot's own `source` field, not a constant in a view: a view that named
a site directly would go on crediting it after the source changed. `MountViewState.DatasetSource` and
`ConfigViewState.DatasetSource` are the two carriers, both filled from `MountCatalog.DatasetSource`; the
sentence in the file header is the record, and the manifest carries it to the installer. The validator
fails a snapshot with no attribution (`attribution-missing`), so an uncredited file cannot ship even by
accident.

## 6. The schema version

The header is what makes a stale file distinguishable from a fresh one:

```json
{
  "schemaVersion": 3,
  "generated": "2026-11-01",
  "source": "ffxivcollect.com",
  "enrichment": "full",
  "sheetCrossCheck": {
    "sheet": "Mount",
    "fields": ["ExtraSeats", "IsAirborne"],
    "mounts": 356,
    "seats": 356,
    "movement": 356
  },
  "attribution": "Patch, obtain source and community ownership data from FFXIV Collect, a volunteer community project.",
  "attributionUrl": "https://ffxivcollect.com",
  "mounts": [ ... ]
}
```

- **The number is duplicated on purpose**: `MountShelf.CurrentSchemaVersion` in the reader, `$schemaVersion`
  in the generator. The validator fails when the file's value is not the reader's (`schema-missing` for a
  file with no version at all, `schema-version` for a different one), so a plugin build and a snapshot
  cannot drift apart silently. The generator writes the number; the reader never derives compatibility
  from the file.
- **Reading is tolerant, reporting is not.** A file with no version (the first shipped shape) is read as
  v0 — *stale*, not refused, because losing every "How to obtain" line is worse than reading an old one.
  A newer file is read too, but logged as an error at load (`MountCatalog.ReportDatasetHealth`), because
  the fields it carries may be ignored. The settings window prints the same line, so a bug report says
  which snapshot the player runs without anyone reading a log.
- **Bumping it**: change both constants in the same commit as the generator change, and say in the commit
  message which field moved. A bump without a generator rerun makes every snapshot in the wild stale; a
  rerun without a bump ships a file whose shape the reader mis-describes.
- **v1 → v2** added the header itself: `schemaVersion`, `generated`, `source`,
  `enrichment`, `attribution`, `attributionUrl`. The row shape did not move, which is why a v1 file still
  parses and still draws.
- **v2 → v3** (this change) added `sheetCrossCheck` to the header: the record of the build-time
  comparison of the site's seats and movement against the game's `Mount` sheet (§7). The row shape did
  not move again — a v2 file still parses, it just cannot say whether that check ran, which is what
  `crosscheck-missing` reports.

## 7. Seats and movement: the two facts the snapshot does not carry

FFXIV Collect publishes two more fields that this snapshot deliberately does **not** ship: `seats` and
`movement` (`Airborne` / `Terrestrial`). The reason is not size — they would cost about 7.9 KB — but
authority: the game's own `Mount` sheet already answers both for every row, and the plugin reads that
sheet at runtime.

| Site field | Game sheet column | How the plugin gets it |
| --- | --- | --- |
| `seats` | `Mount.ExtraSeats + 1` | `MountCatalog.TryBuild` → `MountShelfItem.Seats`, drawn as a badge and in the sheet-fact line |
| `movement` | `Mount.IsAirborne` | not drawn; the hovering/grounded axis the site calls movement |

The sheet is the only source that cannot be stale: it ships with the player's client, while the snapshot
ships inside the DLL. A copy in the file could contradict a patched game — a mount that gains a seat
keeps the old count until somebody regenerates the data.

`Mount.IsFlying` is the easy confusion, and it is a different fact: it is set on 404 of 451 rows and
means "can fly once flight is unlocked in this zone", which is what the plugin's `Flies` badge and
`flying:` search operator answer. `Mount.IsAirborne` is true on 115 rows and is the column the site's
movement field matches, mount for mount.

### What replaces the copy: a build-time cross-check

`mdlrip --facts` writes `out/mount-sheet-facts.csv` (one row per `Mount` row:
`mount_id, extra_seats, is_airborne`), and `tools/generate-mount-sources.ps1` reads it at `-SheetFacts`
to compare every record the site returns:

- an id the sheet does not have → the run stops, **nothing written** (the validator's `unknown-mount-id`
  catches the same drift later for a file written some other way);
- `seats` that is not `Mount.ExtraSeats + 1` → the run stops, naming the mounts;
- a `movement` that is neither `Airborne` nor `Terrestrial`, or that disagrees with `Mount.IsAirborne` →
  the run stops.

Measured 2026-11-01 from `D:\ff14\...\sqpack` through Lumina 7 (the reader the plugin itself uses),
against the 356 records the site returned:

| Fact | Rows compared | Agreed |
| --- | --- | --- |
| `seats` vs `Mount.ExtraSeats + 1` | 356 | 356 |
| `movement` vs `Mount.IsAirborne` | 356 | 356 (103 Airborne, 253 Terrestrial) |

The outcome is recorded in the header, so the file states what was checked instead of leaving a reader to
assume a check happened:

```json
"sheetCrossCheck": {
  "sheet": "Mount",
  "fields": ["ExtraSeats", "IsAirborne"],
  "mounts": 356,
  "seats": 356,
  "movement": 356
}
```

`null` there means the check did **not** run — an offline build, or a machine without the sheet facts.
The validator reports that as `crosscheck-missing` (a warning: the data is still usable, but a human has
to read it), and a block that covers fewer rows than the file carries, disagrees, or names other columns
is an error (`crosscheck-drift`, `crosscheck-fields`) — that file was either edited by hand or built
against another game build.

## 8. When the site is gone

The site is one volunteer's project, so "it is down" is a normal condition, not an emergency:

- **Online run (default):** the generator fetches, and refuses to write anything when the result is
  partial or empty — fewer than `-Minimum` (300) rows throws, with nothing written, so a bad fetch can
  never replace a good snapshot. `patch-day.ps1` stops there and leaves the committed file untouched.
- **Offline run (`-Offline`):** the snapshot is built from the game's `Mount` sheet alone. The committed
  file's rows are carried over and pruned to ids the sheet still has, so nothing is invented and nothing
  is lost; with no previous snapshot to carry over it writes one bare row per sheet id rather than an
  empty file. The result declares `"enrichment": "sheet-only"`, the run prints a red banner, the validator
  reports `enrichment-sheet-only` as a **warning** (a human has to accept it), and the plugin says "built
  offline: no obtain data refreshed" in the settings window.
- **What is never acceptable** is an empty or silently partial file: the row floor throws, the row band
  fails, and both paths still write a file that parses and that the plugin can read.

## 9. The source taxonomy (28 types, measured 2026-11-01)

`src/Mountbook/Services/MountSourceKinds.cs` is the plugin's copy of the site's own classification of the
same 356 mounts into 28 types. It is adopted, not invented: the names and their order (largest group first)
are the site's, and the plugin's filter, its counts, the card grid's "group by source" headings and the
`source:` search operator all read that one table.

**The counts are two different numbers, and the difference matters.** The site publishes a count of source
*lines*; a filter selects *mounts*. Twenty-five mounts carry two types, so the per-type line counts add up to
423 while the mounts add up to 381 over 356 rows. The combo shows the mount count, because that is what
choosing the entry lists.

| Type | Mounts | Source lines | | Type | Mounts | Source lines |
| --- | --- | --- | --- | --- | --- | --- |
| Premium | 67 | 73 | | V&C Dungeon | 5 | 9 |
| Achievement | 50 | 50 | | FATE | 6 | 6 |
| Trial | 44 | 48 | | Treasure Hunt | 5 | 5 |
| Quest | 23 | 25 | | Hunts | 5 | 5 |
| PvP | 21 | 24 | | Purchase | 5 | 5 |
| Cosmic Exploration | 19 | 23 | | Chaotic Raid | 2 | 4 |
| Event | 22 | 22 | | Bozja | 4 | 4 |
| Wondrous Tails | 12 | 21 | | Eureka | 3 | 3 |
| Tribal | 21 | 21 | | Gathering | 2 | 2 |
| Raid | 14 | 14 | | Crafting | 2 | 2 |
| Occult Crescent | 9 | 13 | | Other | 2 | 2 |
| Island Sanctuary | 12 | 13 | | Dungeon | 1 | 1 |
| Gold Saucer | 9 | 9 | | Voyages | 1 | 1 |
| Deep Dungeon | 7 | 9 | | | | |
| Skybuilders | 8 | 9 | | | | |

`src/Mountbook.Tests/MountSourcesDataTests.cs` pins both columns against the committed snapshot, and asserts
that the number the combo shows for a type is exactly how many mounts choosing it lists - so a regenerated
snapshot whose types moved fails the suite instead of quietly changing a count a player reads.

**The identity is the type, never the label.** Each type's key is its name folded to lower case with every
run of punctuation collapsed to one hyphen (`V&C Dungeon` -> `v-c-dungeon`), and that key is also the
localisation table's key suffix. The filter compares types, so translating a label cannot change what a
filter selects; the `source:` operator accepts the key, any spelling of the name and any unambiguous
beginning of it (`source:cosmic`, `source:v-c-dungeon`).

**A type this build does not know still works, and still fails the build.** A snapshot written for a newer
patch may carry a 29th type: the reader keeps its own spelling, the filter and the grouping use it, and it
sorts after every ranked type - but the patch-day validator reports `source-type-unknown` as an **error**, so
adopting it (its rank, its label) is a deliberate edit to `MountSourceKinds.cs` and the strings table, not
something a regeneration does on its own. A mount whose obtain lines carry no type at all is
`source-type-missing`, also an error: it would be invisible to the filter and to the counts while the detail
pane still drew the text.

## 10. What this document does not settle

- It is an honest reading of a published paragraph, **not legal advice**.
- The terms are a paragraph on a documentation page, not a versioned licence: the site can change them,
  and nothing here re-checks them. Re-read `/api/docs` when the generator's terms of use are next in
  question, and update §2 rather than assuming it still holds.
- The site's own data quality is outside this document's reach: a wrong patch value or a missing source
  line ships as the site has it. The validator checks shape, coverage and id drift — never correctness.
