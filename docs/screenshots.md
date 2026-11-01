# Mountbook listing screenshots

The five images in [`screenshots/`](screenshots) are what the plugin page and the README show. They
are not screen captures: each one is a Mountbook preview scene drawn headless by
`glasswork-preview`, so the same command produces the same pixels on any machine, and nothing about
them depends on a game client being open, on a resolution, or on who was playing at the time.

## One command

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File src/Mountbook/tools/screenshots.ps1
```

It builds the previews assembly, renders the five scenes below, writes them into
`docs/screenshots/`, and fails if any file comes out at a size other than the one pinned in the
script. `-Check` renders into a scratch folder and compares instead of writing:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File src/Mountbook/tools/screenshots.ps1 -Check
```

`-Check` exits 1 and names the stale files, so a release run can call it as a gate. Look at the
images before committing: the command proves their size and their provenance, not that the layout in
them is good.

## The set

| File | Scene | Size | What it shows |
|---|---|---|---|
| `card-grid.png` | `main` | 1088×708 | the flagship window: search, ownership chips, filters, the card grid, a selected mount in the detail pane |
| `table.png` | `table` | 1088×708 | the same book as a sortable table — patch, seats, source, owned, owners, price |
| `detail-pane.png` | `price` | 1088×708 | the detail pane with a market price on demand, the badges, and the "How to obtain" rows |
| `config.png` | `config` | 608×1108 | the settings window, whole |
| `grouped.png` | `grouped` | 1088×948 | the grid grouped by expansion, with a group heading per expansion |

The sizes differ because the scenes do: the settings window is a tall single column, the browse
window is two, and the grouped grid is as tall as its eight headings. Each is the scene's own size at
1:1, pinned in `tools/screenshots.ps1`, so the numbers above are enforced rather than remembered.

## Why these are not the goldens

`src/Mountbook.Previews/goldens/` holds the same scenes as pixel baselines for the gate. Four
deliberate differences:

- **The face.** A golden renders with ImGui's default font, whose range stops at U+00FF, so a
  truncated mount name draws as `Auspicio?` instead of `Auspicio…`. That is fine as a baseline and
  wrong on a page a player is deciding from, so the listing images are rasterised with Segoe UI.
- **WARP.** The goldens compare on the software rasteriser, and so do these: a hardware adapter
  draws the same pixels on the machine that made them and a slightly different set on the next one,
  and these files are committed. WARP is also what a machine without a GPU has.
- A listing image is **not** a regression baseline. It is allowed to lag a golden by exactly one run:
  when a scene's layout changes on purpose, re-run the command and commit the images with it.
- The cards carry **no mount artwork**, and that is not a bug in the renderer. Icons and models are
  read from the player's own game files at run time; a preview has no game, and committing extracted
  Square Enix art to a public repository is not an option, so every card shows the empty-artwork
  frame the window draws while a texture is not loaded yet.

## What is missing

There is no small-window image yet. The 620 px wide `narrow` scene, and every other scene rendered
below the flagship size (820×520 `minimum`, `empty-minimum`, `filters`), draws a strip the preview
composites as a **grey checkerboard**: the glass surface does not cover the window's own padding at
those sizes, so the frame is left with alpha 0 where nothing was drawn, and the previewer shows
transparency as a checker. In game that area is the game world showing through the window's edge,
which is the intended glass effect and the reason the strip is invisible there; in a still image it
reads as a rendering fault.

The checkerboard is in the committed goldens too (`narrow.png` at x 628–641, y 643–815; the bottom
and right edges of `minimum.png`), which is worth knowing: it means that region has never been
covered by a diff. It is tracked as its own task — the fix belongs in the window's layout or in how
the previewer composites, not in a screenshot script — and the narrow listing image goes in when it
lands.

## Adding or changing an image

1. Pick the scene (`glasswork-preview --assembly <previews dll>` lists them with their sizes).
2. Add or edit its line in the `$shots` table in `tools/screenshots.ps1` — scene name, file name, and
   the size you expect.
3. Run the command, look at the PNG, and commit it with the script change.

A scene that changes size fails the command instead of quietly replacing a listing image with a
different-shaped one; move the pinned size in the same commit as the layout change.
