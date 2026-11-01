# Interface strings and the CJK golden

Mountbook's user-facing text lives in one table per language, not in the views. Translating it is a data
change: add `src/Mountbook/Data/Strings/<code>.json`, and the next build draws that language for a player
whose Dalamud UI is set to it.

- **Table**: `src/Mountbook/Data/Strings/en.json`, one file per language, embedded as
  `Mountbook.Data.Strings.<code>.json`.
- **Lookup**: `Services/InterfaceLanguage.cs`, `Services/StringTable.cs`, `Services/StringLookup.cs`,
  `Services/Loc.cs`.
- **Wiring**: `Localisation.Apply` in the plugin's constructor, from `IPluginInterface.UiLanguage`.
- **Size today**: 263 keys in English, 29 of them source categories generated from the obtain dataset.

## Why one table and not `Resources.resx`

The plugin already ships data this way: `Data/mount-sources.json` is generated, committed, embedded and
parsed with `System.Text.Json` by `Services/MountShelf.cs`. A translation is the same kind of artefact - a
file a translator edits, not code - so it uses the same shape:

```json
{
  "language": "ja",
  "strings": {
    "window.main": "マウント図鑑",
    "header.collected": "{1} 匹中 {0} 匹を収集"
  }
}
```

`language` is the code of the file it sits in (`ja`, `en`, `de`, `fr`, `zh-Hans`, `zh-Hant`, `ko`); a table
that declares another one is rejected, because the alternative is a Japanese file silently installed as
German. The loader reads every resource whose name ends in `.Strings.<code>.json`, so **adding a language is
adding a file** - no code, no csproj entry, no switch statement.

## The lookup

`Loc` is static, and that is a deliberate compromise:

- the views draw from a `MountViewState` and a Look and nothing else (CONTRIBUTING T6, T7). Threading a
  string service through every draw call would put a dependency in the render path to reach a value that
  cannot change between frames;
- the headless previews install nothing and still draw English, because `Loc` reads its own assembly's
  resources on first use;
- the pure part lives in `StringLookup`, so a test can hold a Japanese table without touching the global the
  plugin installed. That is what keeps the fallback tests independent of the rest of the suite, which runs
  in parallel.

The chain is **language table → English table → the key itself**. The last step is on purpose: a missing key
draws `detail.ride` on screen, which is a bug report someone can file, rather than a blank or a throw inside
a draw. It cannot reach a player unnoticed, because a test asserts that every key the code asks for exists in
English.

### `Loc.T`, `Loc.TOr`, `Loc.F`

| Call | Use |
| --- | --- |
| `Loc.T("detail.ride")` | a label |
| `Loc.F("header.collected", owned, total)` | a sentence with placeholders (`{0}`, `{1}`) |
| `Loc.TOr("source.trial", category)` | a value that is *data* and may have no label |

Placeholders are filled with `string.Format` under the current culture, so a player's own digits and
separators are used. Patterns must number their placeholders `{0}..{n}` with no gaps, and a test enforces it
- a translation that reorders the words is fine, a translation that renumbers them is not.

### Data that is also text

Three values are identities *and* labels, and they are the only places where the two are allowed to differ:

- **Source categories** (`"Trial"`, `"V&C Dungeon"`, ...) are what the query filters on and what the obtain
  dataset stores. `UiText.Source` turns a category into a label (`source.trial`, `source.v-c-dungeon`) and
  falls back to the category itself; the view translates at the point of drawing, the filter keeps the data.
  `UiText.SourceKey` is the slug rule: lower case, every run of non-alphanumerics folded to one hyphen.
- **Expansion names** (`"A Realm Reborn"`) are what `MountGroups` buckets by and what `MountBreakdown`
  matches a filter by, so `UiText.Expansion`/`ExpansionShort` translate at the display point.
- **Group titles** go through `UiText.GroupTitle`, which routes to the two above and keeps patch numbers as
  they are.

## The CJK golden

`src/Mountbook.Previews.Cjk` renders `goldens/cjk.png`: the plugin's own labels - 23 keys - in Japanese,
Simplified Chinese and Korean, drawn with the plugin's own type sizes and Look colours.

```powershell
dotnet run --project src/Mountbook.Previews.Cjk/Mountbook.Previews.Cjk.csproj -c Release -- --compare src/Mountbook.Previews.Cjk/goldens --out artifacts/previews/Cjk
```

It exits 1 on a byte difference, and it exits 1 (before comparing anything) on a character the committed face
has no glyph for - that is the tofu this image exists to rule out. `--update` re-baselines.

### Why it is not `glasswork-preview`

The other thirteen goldens come from `glasswork.preview 0.1.0-beta.2`, pinned. That renderer cannot draw CJK,
and the limit is in the tool, not in the font:

1. `UiFontLoader.FaceRanges` is `Pinned(32, 255, 8230, 8230)`, so the UI font is rasterised for U+0020..U+00FF
   plus the ellipsis. Everything else is ImGui's fallback glyph.
2. `GoldenCompare` passes `--font none` unless told otherwise, so the goldens are drawn with ImGui's default
   bitmap font.
3. Probed, not assumed: a throwaway `IPreviewScreen` drew `JP: マウント図鑑` and rendered `JP: ????????` with
   no font and with `--font C:/Windows/Fonts/YuGothM.ttc`. Rebuilding the atlas from inside the screen (the
   only hook a screen has) drops the texture id to 0 and the frame draws no text at all.

Re-baselining all thirteen onto a system font was the alternative, and it is worse: the goldens would then
depend on the machine's font files, which do not exist on a CI image.

So this renderer owns the atlas: it adds the committed face at every size the Look asks for, draws with
ImGui's own draw lists, and rasterises the resulting triangles on the CPU (`Software.cs`; about 200 lines,
including a PNG writer). No GPU, no system font, no network.

### The face

`fonts/NotoSansCJKjp-Medium-subset.otf` is a 28 KB subset of the Noto Sans CJK JP Medium that Dalamud ships -
the face the game's own Japanese interface uses - cut to the 243 glyphs the sheet draws. Licensing and the
exact subset command are in `fonts/README.md` (SIL OFL 1.1).

To change the sample, edit `fixtures/strings.<code>.json`, re-cut the subset over the new text, and
`--update`. The fixtures are copied to the output directory, so an edit needs a build before `--no-build`
sees it.

## The guards

`src/Mountbook.Tests/LocalisationTests.cs` embeds the view, window, service and game sources as text and
asserts:

- the shipped table parses, is English, and has no blank value;
- every key the code asks for is in the table, and every key in the table is asked for (or is a `source.*`
  category, which is looked up from the dataset);
- every source category in `Data/mount-sources.json` has a label;
- **no user-facing literal is left in `Views/`** - the only literals allowed there are ImGui ids (`##`), join
  separators, the measured `"..."` run, and table keys. `Windows/` allows the same plus developer log text
  and format templates;
- placeholders in every pattern are contiguous and formattable;
- a partial translation wins where it has a key and falls back to English where it does not.

That is why the wording of 263 strings is pinned: moving a word in `en.json` is a test failure, which is what
makes a re-word a decision rather than a drift.

## Known gaps

- **Changing the language needs a plugin reload.** `Loc.Use` runs once in the constructor. The views measure
  their labels once too (they cache widths against the Look), and wiring a live switch would mean
  invalidating every measurement cache - a follow-up, not a data change.
- **CJK captions do not wrap.** `MountbookView.Grid.Wrap` breaks on spaces, and Japanese, Chinese and Korean
  mount names do not have them: a long name is ellipsised on one line rather than wrapped to two. Short
  names (most of them) are unaffected.
- **Developer text stays English**: log lines, the diagnostics dump, exception messages, and the
  `mountbook.wanted` file format name (a promise to other copies of the file).
- **The obtain dataset's own prose stays English**: source details like `"The Minstrel's Ballad: Byakko
  Unchained"` come from ffxivcollect and are quest, duty and achievement names. Translating them means
  joining the game's sheets, not this table.
