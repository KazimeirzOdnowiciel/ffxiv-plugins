# The CJK sample face

`NotoSansCJKjp-Medium-subset.otf` is a subset of **Noto Sans CJK JP Medium**, cut down to the 243 glyphs the
CJK golden draws (ASCII, kana, the Japanese, Simplified Chinese and Korean labels in `../fixtures/`).

- Source: `NotoSansCJKjp-Medium.otf`, shipped with Dalamud in
  `%APPDATA%\XIVLauncher\dalamudAssets\dev\UIRes\` (the face the game's own Japanese UI uses).
- Copyright: © 2014-2019 Adobe (http://www.adobe.com/), Noto Sans CJK.
- Licence: SIL Open Font License 1.1 — "This Font Software is licensed under the SIL Open Font License,
  Version 1.1. This Font Software is distributed on an 'AS IS' BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
  KIND, either express or implied. See the SIL Open Font License for the specific language, permissions and
  limitations governing your use of this Font Software." <http://scripts.sil.org/OFL>
- Subset command (fontTools 4.68):

      python -m fontTools.subset NotoSansCJKjp-Medium.otf \
          --text-file=cjkchars.txt --output-file=NotoSansCJKjp-Medium-subset.otf \
          --layout-features='' --no-hinting --desubroutinize

Why a subset and not the shipped font: the whole face is 17 MB and lives in a folder the repository cannot
count on. 28 KB of the exact glyphs the golden draws keeps the image reproducible on a machine that has no
CJK font installed at all, which is what the CI runner is.
