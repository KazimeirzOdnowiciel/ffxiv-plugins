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
| `sources[]` | FFXIV Collect `sources[]` (`type`, `text`, `related_type`, `related_id`) | The "How to obtain" list. `type` is the site's own 28-value taxonomy, adopted rather than invented |
| mount name, description, tooltip, icon, artwork, seats, movement | **The game's own sheets** (`Mount`, `MountTransient`, `Item`) | Read at runtime through Lumina. None of it is taken from the site |

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
   (`ffxivcollect.com, 2026-11-01, schema v2`, and "built offline" when that is what it is), and under it the
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
  "schemaVersion": 2,
  "generated": "2026-11-01",
  "source": "ffxivcollect.com",
  "enrichment": "full",
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
- **v1 → v2** (this change) added the header itself: `schemaVersion`, `generated`, `source`,
  `enrichment`, `attribution`, `attributionUrl`. The row shape did not move, which is why a v1 file still
  parses and still draws.

## 7. When the site is gone

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

## 8. What this document does not settle

- It is an honest reading of a published paragraph, **not legal advice**.
- The terms are a paragraph on a documentation page, not a versioned licence: the site can change them,
  and nothing here re-checks them. Re-read `/api/docs` when the generator's terms of use are next in
  question, and update §2 rather than assuming it still holds.
- The site's own data quality is outside this document's reach: a wrong patch value or a missing source
  line ships as the site has it. The validator checks shape, coverage and id drift — never correctness.
