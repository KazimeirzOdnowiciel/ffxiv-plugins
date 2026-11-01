# Quality review pass, 0.4.0 -> 1.0.0

A whole-tree read of `src/Mountbook`, `src/Mountbook.Previews` and `src/Mountbook.Tests` after the 0.2.0-0.4.0
layout and feature work, with the fixes applied on branch `quality/review-pass`. This is a quality pass, not a
second design pass: nothing was restructured for taste, and the shapes the earlier tasks argued for (the band
test in `DrawGrid`, the two-form detail header, the four-row filter block, the settings-shape split) were left
exactly as they are.

## The gate, after

| Step | Result |
| --- | --- |
| `dotnet build src/Mountbook -c Release` | 0 warnings, 0 errors |
| `dotnet build src/Mountbook.Previews -c Release` | 0 warnings, 0 errors |
| `dotnet build src/Mountbook.Tests -c Release` | 0 warnings, 0 errors (see F13: it used to emit 12) |
| `dotnet run --project src/Mountbook.Tests` | 704 / 704 passed |
| `dotnet format --verify-no-changes` (plugin, tests, previews) | clean |
| `glasswork-preview --compare` | 25 / 25 at 0 differing pixels, max delta 0 |

**No golden moved.** Every fix below is behaviour-preserving at UI scale 1, which is why the images are
evidence rather than a re-baseline: the ones that are not (the pixel/logical title width, F1) only bite at a
scale the harness cannot render, and that is stated in the fix.

## Fixed

Severity is the reader's risk: **high** = wrong output or a dead window, **medium** = an invariant the repo
states and the code broke, **low** = a future reader misled by the code or its comment.

### Correctness

| # | Where | Sev | What was wrong | Fix |
| --- | --- | --- | --- | --- |
| F1 | `Views/MountbookView.Detail.cs` `DrawDetailBody` | high | The selected mount's measured name was passed to `LayoutMeasure.DetailHeader` in **pixels** while every other argument is **logical**. At 200% UI scale the artwork gave way to a title twice its width and the two-column header collapsed back to stacked. Invisible to the goldens: they all render at scale 1. | Divided by `Frame.Scale` at the one call site; parameter renamed `titleWidthLogical` and the trap written into its doc (`Services/LayoutMeasure.cs`). |
| F2 | `Configuration.Shape.cs` guard block | high | A `null` entry in `SavedViews` survived a file claiming `Version: 5` (only step 5 swept the list), and the row reads `views[index].Name` with no guard - one null entry threw on every frame and the main window's catch drew nothing at all. | `SavedViewRules.Normalise` now runs in the always-on guard block. Test: `ANullSavedViewIsDroppedEvenWhenTheFileClaimsTheCurrentVersion`. |
| F3 | `Plugin.cs` `Dispose` | medium | The window and market disposals ran before `CommandManager.RemoveHandler` and `Host.Dispose` with no protection, and the comment claimed the windows own texture handle maps. A throw in any of them left `/mountbook` registered against a dead plugin. | `try { ... } finally { RemoveHandler; Host.Dispose; }`; comment corrected (the board window holds nothing disposable, so it has none to call). |
| F4 | `Plugin.cs` `DataCenterName` | medium | `catch (Exception) { return string.Empty; }` - a broken home-world chain made every market read vanish with no log line (N4). | Logged once on the transition; the reason it is once and not per frame is in the doc. |
| F5 | `Services/PathLabel.cs` `Shorten` | medium | The "even the last segment is too wide" branch fitted the **whole path**, so it kept `C:\` and cut the folder name - the exact part the class exists to keep - while the doc promised the segment was trimmed. | Fits the prefixed tail (`...\ave...`); test pins the exact string instead of `EndsWith`. |
| F6 | `Services/MountWantedList.cs` `TryParse` | medium | `format` was written into every export and never checked on import, so any object carrying a `mounts` array - a settings block, a saved view - parsed as a wanted list. | Checked when present (absent still means a hand-written file), with a test that carries the array so only `format` can refuse it. |
| F7 | `Services/WantedBoard.cs` `CompareByName` | medium | Ignore-case name compare with no tie-break is not a total order, and `List.Sort` is unstable: two rows whose names differ only by case could swap between frames. | Id tie-break, matching the rule `MountQuery` already states. |
| F8 | `Views/MountbookView.Detail.cs` price row | medium | Two `Gw.TooltipFor` calls on one item in one frame: the price box replaced the explanation box, so the explanation was dead text whenever a price was known. | One tooltip: the price, or the explanation. |
| F9 | `Views/MountbookView.cs` `InvalidateMeasurements` | medium | `detailActionWidths` was cached per **scale** and never cleared by a Look change, so after a theme switch at the same scale `ActionRowCount` decided the action row from the previous type face's widths. | Cleared with every other measurement. |
| F10 | `Services/Loc.cs` `ScanOnce` | medium | `catch (FormatException) { }` - a translation that failed to parse was dropped with no log, and the load line then reported a smaller string count with no clue why (N4). | `Scan` takes an `onTableFailure` callback; `Localisation.Apply` logs the skipped resource name at Warning. |
| F11 | `Views/MountbookView.Detail.cs` disabled-reason lines | low | Fitted in `TextRole.Callout` and drawn in `TextRole.Footnote`, so a reason ellipsised earlier than it needed to. | `FitRole(..., TextRole.Footnote)` at both sites. |
| F12 | `src/Mountbook.Tests/Mountbook.Tests.csproj` | medium | `<StrictConventions>` is inert under the plain SDK (only `Dalamud.NET.Sdk` reads it), so the suite compiled with nullable off and emitted 12 CS8632 warnings - the repo's "warnings are errors" gate covered the plugin and not its tests. | `Nullable` + `TreatWarningsAsErrors` set explicitly; the six nullable signatures the analyzer then refused were annotated. |

### The draw path must not allocate

The invariant is stated in `Views/MountbookView.cs` (the class remarks and the cache fields): no string
building and no text measurement per frame. These were the remaining breaches.

| # | Where | Sev | What it was doing | Fix |
| --- | --- | --- | --- | --- |
| F13 | `Views/MountbookView.Table.cs` row loop | high | Four formatted strings per row per frame - ~1,500 allocations on the full 369-mount shelf - for a table that has no clipper and draws every row. The source cell went through `MountSourceKinds.Parse`, a `StringBuilder` and 28 string compares, per row, per frame. | `SeatsLabel` / `SourceLabel` / `PriceLabel` / `RarityLabel`, each keyed by the value it is built from (so a catalog refresh cannot serve a stale string) and bounded by the existing `Remember` cap. Rare-row tooltip text now built only while hovered. |
| F14 | `Views/MountbookView.Grid.cs` `DrawGrid` | high | `MountGroups.Build` ran every frame: a dictionary, a list per heading, a titles list and a sort over the whole filtered shelf. `Windows/MainWindow.cs` names this exact cost as what the per-frame contract forbids, and caches the breakdown and the rarity insight for it. | Cached on `(list reference, grouping)`. The list reference only changes when the window re-runs the query, which is the signal the window's own caches already key on. |
| F15 | `Views/MountbookView.Detail.cs` `DetailBadges` | medium | A fresh `(string?, Color?)[9]` per frame, plus `SeatsText` formatting a sentence per frame. | One reusable buffer refilled in place, and the seat sentence memoised per distinct count. |
| F16 | `Views/MountbookView.Detail.cs` sources block | medium | `detail.source.tooltip` was formatted once per source row per frame regardless of hover; `detail.obtain.credit` every frame. | Tooltip gated on `IsItemHovered`; credit memoised per dataset source. |
| F17 | `Views/WantedBoardView.cs` row | medium | `MountGrind.Label` built for every board row on every frame, hovered or not. | Gated on hover. |

### Duplication that should share one rule

| # | Where | Sev | What | Fix |
| --- | --- | --- | --- | --- |
| F18 | `Views/ConfigView.cs`, `Views/WantedBoardView.cs` | medium | Two byte-identical copies of `Services/TextFit.Fit` (including the ASCII-dots rationale), in the same assembly as the rule it duplicated. | Both delegate to `TextFit.Fit`, passing the ASCII ellipsis explicitly. ~60 lines gone. |
| F19 | detail footer arithmetic | medium | The footer/body sum lived in the view, and its test subtracted a hard-coded `44` from the same constants - it could not fail, so the 2 px shortfall it exists to prevent could come back silently. | `LayoutMeasure.DetailFooter` + `LayoutMeasure.BodyHeight`, called by the view; three tests over 1/2 rows, the reason line, and the clamp. |

### Dead code, orphaned docs, stale comments

| # | Where | Sev | What |
| --- | --- | --- | --- |
| F20 | `Services/MountShelfHeader.cs` | low | `MountShelfHeader.Empty` had no reference in any of the five projects. Deleted. |
| F21 | `Services/Loc.cs` | low | `Loc.Generation` was bumped in two places and read nowhere; its doc promised a caller that did not exist. Deleted. |
| F22 | `Views/MountbookView.Grid.cs`, `Views/MountbookView.cs`, `Services/MountTable.cs` | low | Five stacked or orphaned doc blocks: `hoveredCardId` carried two summaries (one describing the pre-fix per-mount lift), the "cached strip labels" summary sat on `roomPushes` while `expansionLabels` had none, `Draw`'s doc sat on `PushControlRoom` while `Draw` had none, the 360/470 budget remarks sat on `detailActionWidths` while the live code measures real labels, and `ClickedOrder` had two `<remarks>` (only the first survives doc generation). All moved or merged. |
| F23 | six files | low | Stale comments: "the one-per-row fallback below 360 logical px" (no such threshold any more), "(see the inset below)" pointing at nothing, a "a scale change re-measures it" on a value re-taken every frame, "every order is its own id" while Surprise shares the name order's id, "Universalist" for Universalis, "the other sixteen scenes" when there are 25, and two `CONTROL TEST:` prefixes in XML summaries. |
| F24 | `Services/LayoutMeasure.cs`, `Views/MountbookView.Grid.cs` | low | `WatchRowMinWidth` restated `DetailTwoColumnMinWidth`'s 330 with prose instead of referencing it (two constants that must agree); the caption cache's own 1024 cap now says why it is not the shared 512; a double blank line removed. |

### Tests for logic that moved into `Services/`

| # | Where | Sev | What |
| --- | --- | --- | --- |
| F25 | `LayoutMeasure.WatchRowFits` | high | The gate that decides one-row vs stacked for the watch toggle and its stepper had **no test**, while every sibling gate is pinned. Theory at 329.9 / 330. |
| F26 | `MountRarity.ShareText` | medium | No test at all, though it is the table's Owners column and the header's insight number, and it owns the `<0.1%` branch. Eight cases plus unrecorded. |
| F27 | `UiText.GroupTitle` / `Expansion` / `ExpansionShort` | medium | Only the source *key* mapping was tested; the heading text - including the `NoPatch` / `NoSource` sentinels and the "draw the name as it is" fallback - was not. Theory plus fallback. |
| F28 | `WantedBoard.ProgressText` | medium | The board's per-row line had no test; `WantedBoardTests` stopped at `Build`. Two facts. |
| F29 | `MountViewRequestTests` | medium | `GrindChanged` and `WatchChanged` are reset by `ClearRequests` and asserted by nothing - the class's own doc says this is the "repeat every frame" bug class it exists for. A second reflection list. |
| F30 | three tests that could not fail | medium | `TextThatFitsIsReturnedUnchanged` was byte-identical to the exactly-fits case (both 60f); `ARankIsStablePerIdAndSeed` compared `RankOf` to itself; `BodyPlusFooterNeverExceedsThePane` computed its own constant. All three now assert something that can fail. |

Every new guard test was proved the way the review skill asks: the fix was reverted, the suite run, the three
expected tests failed, and the fix restored (`ANullSavedViewIsDropped...`, `ParseRefusesADocumentThatNamesAnotherFormat`,
`ASingleOverlongSegmentIsTrimmedFromTheEndInstead`).

## Deliberately left alone

The rest of the list is real but out of scope for a quality pass, and each has a reason rather than a shrug.
The duplicated taxonomy is the clearest case: the expansion table is written out four times (`MountBreakdown`,
`MountQuery`, `MountGrouping`, `UiText`), money formatting five, the persisted-enum clamp six, and the rarity
rounding five - but folding them into one table each is a design change with three call sites per item and a
golden to re-baseline, and it belongs in a task that is allowed to move pixels rather than a pass that is not.
The same goes for the two views that still measure a line per frame (`ConfigView.Fit`, `WantedBoardView.Fit`):
the dedup landed, but a memo would need a measurement generation threaded into `ConfigViewState`, and the
`Loc.F` that builds the sentence they fit allocates before the fitter sees it anyway - the honest fix is to
precompute those sentences into the state, which is a state-shape change. `MountCatalog`'s fallback rebuild can
still parse embedded JSON inside a draw pass, but only when the load-time build already failed (the normal path
builds in the constructor); `MarketPrices.Dispose` disposes an `HttpClient` with requests possibly in flight,
which logs a benign Error at unload; `Mountbook.Tools` has no tests; the 14 preview scenes each hand-build
their `MountViewState` where `MountbookPreview.CreateState` exists; and a dozen test helpers are re-declared
per file. Each is written down here so the 1.0.0 tracks can pick them up deliberately instead of rediscovering
them. `MountQuery.Apply(wanted:)` keeping its optional parameter and `Configuration.Migrate`'s silent
`Version > CurrentVersion` path were left as documented behaviour, not accidents.

## Blocked: AGENTS.md doc drift

`AGENTS.md` still says `631 tests committed`, `twenty-one goldens` and `the ten committed goldens`. The suite is
**704** and there are **25** goldens. The write was refused by the protected-file guard (no approval available
in an unattended run), so this needs a human edit or an approved run. `analysis/mountbook-process.md`'s
"verified build + 308 tests green" was left as it is: it is a dated record of one recovery, not a claim about
the current gate.
