# Mountbook changelog

One section per version, newest first: what a player notices, one line each, no file names and no
totals that go stale. The section for the next version is written before its bump, so a release is a
date typed into a heading instead of a blank page written under deadline.

- **A change is one line, under 200 characters.** A change that will not fit is two changes, or a
  release of its own.
- **The section is the release text.** `src/Mountbook/tools/release-notes.ps1` prints the section for
  the version in the project file, and that output is the GitHub release body - copied, not retyped.
- **The source is the board.** A line starts as a kanban card finished since the previous version.
  The card keeps the evidence; this file keeps only what changed for the person using it.
- **The suite checks the shape, never the wording**: every heading parses, every section has a line,
  the versions descend, no line names a file, and the version being built has a section that is no
  longer marked unreleased - so a bump cannot land without its entry.

## 0.2.0 (2026-11-02)

Everything since 0.1.0, starting with the room to read it: the list keeps a whole card at the
smallest window, the table fits 17 rows where it fitted 11, and a heading stays put while the rows
scroll under it.

### The window

- The filter block is five rows instead of seven and a saved view rides the ownership chips: the list keeps a whole card at the smallest window, where it drew none, and 2.5 rows at the default.
- At a short pane the title and the collected counters share one row, so the header stops taking the card row the list needs.
- The "shown collected" line sits beside the title instead of above the divider, so a filter no longer costs the list a row.
- At the smallest window the cards view gives up the saved-views line rather than the last card row, so a card still reads there with a full book.
- Each dropdown names its own dimension ("All expansions", "Sort: Name") instead of standing in a labelled form, and they stack into one column on a narrow pane rather than clipping.
- The detail pane's four actions fit on one line, Check price shares a row with the wiki link, and on a wide pane the artwork moves beside the name: about 90 px back, first obtain row intact.
- At a small pane the artwork steps aside entirely and the obtain list gets the room, and the pane is sized from what it is drawn into, so no stray 2 px scrollbar.
- A mount with an unknown patch or seat count shows no badge for it, and the ownership chip says Unowned where it said Not collected.
- In the grid the group headings sit flush with the tiles under them, and a row is only as tall as its own captions instead of reserving a second line.
- The table is 32 px rows instead of 48, so 17 fit where 11 did; ownership is one accent dot under "Owned", and the rarity share sits under "Owners" rather than "Collected".
- A rare mount's share is a mark behind the number now, not a capsule beside it, so the column is as wide as its heading and every share in it ends on the same edge.
- The table opens in the order you left it in, with the arrow on that column, and Patch and Owners no longer sort backwards in one direction.
- Clicking a table heading moves the arrow to it: the arrow used to stay on the column you clicked to leave, while the rows were already in the order you asked for.
- A heading's arrow now points the way its rows run, so "Seats" and "Owned" show a downward arrow over rows that put the most seats, and an owned mount, first.
- Sorting reorders the rows and nothing else: the columns keep the widths their cells were fitted to, so clicking a heading no longer redraws the table as a different one.
- A heading stays at the top of the list while the rows scroll under it, so sorting no longer costs you the place you were reading.
- The expansion strip moves to its own line when the header is too narrow to keep it inline, instead of disappearing.
- Refresh reports what it did - how many mounts it counted as collected - or why it could not read them.
- The header's Themes button is gone: Settings, then Appearance, then Open themes opens the same folder.
- First run says one line, once per install, and an empty list names the single action its button takes.
- Reset in the settings window clears the source filter with the rest, so it does what its label says.

## 0.1.0 (2026-10-31)

First release.

- Every mount in the game in one window, as a card grid or a dense sortable table, with search operators, grouping by expansion, patch or method, and ordering that includes a seeded "surprise me".
- A wanted list with JSON export and import, so a collection can be carried between machines.
- A detail pane with the game's artwork, community rarity, token prices with your own progress, market prices on demand, and a "How to obtain" list whose rows open the wiki page they name.
