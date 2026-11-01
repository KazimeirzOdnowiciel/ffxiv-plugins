# API 16 readiness audit (Mountbook, A2)

Written before 8.0 lands, to answer one question precisely: **what in this plugin breaks under
Dalamud API 16, and where.** It is an inventory, not a migration: nothing in the plugin was changed
for it, and the tree it describes builds and passes exactly as it did before
(`dotnet build src/Mountbook -c Release` 0 warnings; 358 tests green).

## How this was verified

Three independent sources, not one:

| Source | What it is |
| --- | --- |
| `goatcorp/Dalamud` branch `api16`, commit `e972aadb` ("Bump version to 16.0.0.0", 2026-10-30) | Upstream source of truth for the new API surface |
| the published API 16 distribution, `goatcorp.github.io/dalamud-distrib/api16/latest.zip` | `Dalamud.dll 16.0.0.0`, `Lumina 7.7.0` (unchanged from 15), **ships `Dalamud.Excel.Sheets.dll` and no `Lumina.Excel.dll`** |
| a scratch copy of this tree compiled against that distribution | the breakage below is the compiler's output, not a reading of release notes |

The compile probe is the load-bearing one: it turns "this looks risky" into "these lines, and only
these lines, fail". It was run on a copy of the working tree at `f632558`, with `DALAMUD_HOME`
pointed at the API 16 distribution, SDK still `Dalamud.NET.Sdk/15.0.0` (16 is not on nuget yet).

Verdicts below are `safe` (compiles and means the same thing on 16), `needs-change` (fails or
silently changes meaning), or `plan` (project file / release wiring).

## Summary

| Risk class | Rows audited | safe | needs-change | plan |
| --- | --- | --- | --- | --- |
| A. Mutable SeString / payload assumptions | 7 | 5 | 2 | – |
| B. `On`-prefixed events | 4 | 4 | – | – |
| C. Row objects instead of RowId | 9 | 8 | 1 | – |
| D. `Lumina.Excel.Sheets` → `Dalamud.Excel.Sheets` | 6 | 1 | 2 | 3 |

Five C# edits (four code, one comment) and three project-file/release items in total. A2 is in good
shape: the plugin was written on the `ReadOnlySeString` side already, so most of this audit is
proving absence.

## A. Mutable SeString and payload assumptions

The tree contains no `Dalamud.Game.Text.SeStringHandling` using, no mutable `SeString`, no
`SeStringBuilder` from that namespace, and no direct payload classes (`TextPayload`, `IconPayload`,
`UIForegroundPayload`, `UILinkPayload`). What it does use:

| Site | Code | Verdict |
| --- | --- | --- |
| `src/Mountbook/Game/MountActions.cs:24-28` | doc comment: "the API-15 `IChatGui.Print(ReadOnlySpan<byte>)` overload … while the byte overload survives" | needs-change — **wrong**. API 16 removes the byte overload (see `:334`); the comment asserts the opposite of what the audit found |
| `src/Mountbook/Game/MountActions.cs:311` | `ReadOnlySeString name = ItemUtil.GetItemName(itemId, includeIcon: false)` | safe — `ItemUtil` stayed in `Dalamud.Utility`; signature is `GetItemName(uint, bool includeIcon = true, ClientLanguage? language = null)` |
| `src/Mountbook/Game/MountActions.cs:318` | `ItemUtil.GetItemRarityColorType(itemId)` | safe — `(uint itemId, bool isEdgeColor = false)` in 16, so the one-argument call still binds |
| `src/Mountbook/Game/MountActions.cs:322` | `using RentedSeStringBuilder builder = new()` | safe — `RentedSeStringBuilder` survives in `Dalamud.Utility` (it is what Dalamud 16 itself uses) |
| `src/Mountbook/Game/MountActions.cs:323-332` | `.Append(char).Append(string).PushColorType().PushEdgeColorType().PushLinkItem(id, text).Append(ReadOnlySeString).PopLink().PopEdgeColorType().PopColorType()` | safe — these are `Lumina`'s `SeStringBuilder` members, and 16 pins the **same** Lumina 7.7.0 that 15 ships, so this surface cannot move under us |
| `src/Mountbook/Game/MountActions.cs:334` | `chatGui.Print(builder.GetViewAsSpan())` | **needs-change** — see below |
| `src/Mountbook/Game/MountCatalog.cs:198,244,256,257,258`; `Game/CurrencyStock.cs:130`; `Windows/MainWindow.cs:172` | `.ExtractText()` | safe — an instance method on `Lumina.Text.ReadOnly.ReadOnlySeString`, not a Dalamud extension; Lumina does not change between 15 and 16 |

The one real break, from the compiler:

```
MountActions.cs(334,21): error CS0121: The call is ambiguous between
  'IChatGui.Print(Lumina.Text.ReadOnly.ReadOnlySeString, string?, ushort?)' and
  'IChatGui.Print(Lumina.Text.ReadOnly.ReadOnlySeStringSpan, string?, ushort?)'
```

API 15's `IChatGui.Print` set is `Print(SeString)`, `Print(ReadOnlySpan<byte>)`, `Print(string)`,
`Print(XivChatEntry)`. API 16's is `Print(IPrintableChatMessage)`, `Print(ReadOnlySeString)`,
`Print(ReadOnlySeStringSpan)` — the mutable overloads and the raw byte overload are gone, and a
`ReadOnlySpan<byte>` now converts implicitly to *both* surviving overloads, hence the ambiguity.
The fix is to hand it the ReadOnlySeString the builder already owns:

```csharp
chatGui.Print(builder.ToReadOnlySeString());   // same bytes; RentedSeStringBuilder.ToReadOnlySeString exists in 16
```

`SeString.CreateItemLink(uint itemId, ItemKind kind = ItemKind.Normal, ReadOnlySeString
displayNameOverride = default)` also survives in 16 if the hand-rolled payload order ever looks like
a liability — that is a judgement for the port, not this audit, and it would move the golden link
text, so it is deliberately not in the change list.

## B. `On`-prefixed events

The plugin subscribes to three events in total, all of them already unprefixed, and to nothing else
(no `IFramework.Update`, no `IClientState.TerritoryChanged`, no `IAddonLifecycle`, no
`IGameInventory.InventoryChanged`):

| Site | Code | Verdict |
| --- | --- | --- |
| `src/Mountbook/Plugin.cs:154-156` | `UiBuilder.Draw += OnDraw`, `.OpenConfigUi += ToggleConfigUi`, `.OpenMainUi += ToggleMainUi` | safe — all three are `event Action?` on `IUiBuilder` in 16 (`Dalamud/Interface/UiBuilder.cs:36,46,51`) |
| `src/Mountbook/Plugin.cs:165-167` | the matching `-=` pairs | safe |
| `src/Mountbook/Windows/MainWindow.cs`, `Windows/ConfigWindow.cs` | `override void OnOpen()` / `OnClose()` | safe — those are `Window` *virtuals*, not events (`Dalamud/Interface/Windowing/Window.cs:183,188` in 16). Local names starting with `On` are not a risk; the class is about the API's event names |
| `src/Mountbook/Plugin.cs:140-142` | `CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand) {...})` | safe — `CommandInfo` unchanged |

Nothing here needs a rename. The reason the class exists at all: 16 keeps zero `On*`-prefixed events
in `Dalamud.Plugin.Services` (checked: no `event .*On[A-Z]` remains), so any subscription written
against an older spelling is a compile error rather than a silent one — which is the good case.

## C. Row objects instead of RowId

Mountbook already stores ids, not rows: `MountShelfItem` (`src/Mountbook/Services/MountRecords.cs:36-50`)
is `uint Id` plus copied strings, and every comparison downstream (`MountCatalog.IsOwnedNow`, the
wanted list, the price cache keys) is an id comparison. No `Mount`/`Item`/`MountTransient` value is
retained past the loop that reads it.

| Site | Code | Verdict |
| --- | --- | --- |
| `Game/MountCatalog.cs:229-230` | `GetExcelSheet<Mount>()`, `GetExcelSheet<MountTransient>()` | safe — `IDataManager.GetExcelSheet<T>(ClientLanguage? = null, string? = null) where T : struct, IExcelRow<T>` is unchanged in shape |
| `Game/MountCatalog.cs:236` | `var mountId = row.RowId` used as the key everywhere | safe — `RowId` is still `UInt32` on generated rows |
| `Game/MountCatalog.cs:254` | `transientSheet.TryGetRow(mountId, out MountTransient transient)` | safe — Lumina 7.7.0 does not change |
| `Game/MountCatalog.cs:256-258` | `Description`, `DescriptionEnhanced`, `Tooltip` | safe — all three still on `MountTransient` in the regenerated sheet |
| `Game/MountCatalog.cs:273,275,290` | `(uint)row.Icon`, `row.ExtraSeats + 1`, `(uint)row.Equip*` | safe — `Icon` is `UInt16`, `ExtraSeats` `Byte`, `EquipHead/Body/Leg/Foot` `Int32`; the existing casts still compile |
| **`Game/MountCatalog.cs:293`** | `row.IsFlying != 0` | **needs-change** — see below |
| `Game/MountCatalog.cs:191-198`, `Game/CurrencyStock.cs:126-139` | `Item.Name`, `Item.Plural`, `item.RowId` | safe |
| `Windows/MainWindow.cs:172` | `PlayerState.HomeWorld.ValueNullable?.DataCenter.ValueNullable?.Name.ExtractText()` | safe — `IPlayerState.HomeWorld` is still `RowRef<World>` and the `RowRef` members come from Lumina 7.7.0 |

The one real break:

```
MountCatalog.cs(293,25): error CS1061: 'Mount' does not contain a definition for 'IsFlying'
```

API 15's Lumina `Mount` row carried **both** `Byte IsFlying` and `RowRef FlyingCondition`; the API 16
row (generated from EXDSchema into `Dalamud.Excel.Sheets`) keeps only `FlyingCondition`. Everything
else the plugin reads from that row survives unchanged (`Singular`, `Icon`, `ExtraSeats`, the four
`Equip*`, and the `RowId`).

Do not guess the replacement. `IsAirborne` and `OwnerGroundFlySwimEnable` are different columns, and
`FlyingCondition` is a `RowRef` into the `MountFlyingCondition` sheet, so "can this mount fly" is now
either "`FlyingCondition.RowId != 0`" or something the row no longer answers directly. The probe used
`row.FlyingCondition.RowId != 0` only to prove the rest of the file compiles. It feeds exactly two
things — the "Flies" badge (`Views/MountbookView.Detail.cs:331`) and the `flyable:` search fact
(`Services/MountSearchQuery.cs:292`) — so the way to settle it is one pass over all rows comparing
both columns on the live sheet, then a test in `MountSearchTests`/`MountTableTests` that pins the
count. That is a port-day decision, deliberately left open here.

## D. `Lumina.Excel.Sheets` → `Dalamud.Excel.Sheets`

This is not a namespace alias: the API 16 distribution **deletes `Lumina.Excel.dll`** and ships
`Dalamud.Excel.Sheets.dll` (generated from EXDSchema by the Dalamud build, `GeneratedNamespace =
Dalamud.Excel.Sheets`, `UseSchemaAttribute = false`). This is what the compiler says on the tree
as-is:

```
warning MSB3245: Could not resolve this reference … "Lumina.Excel"
CurrencyStock.cs(5,20): error CS0234: The type or namespace name 'Sheets' does not exist in the namespace 'Lumina.Excel'
MountCatalog.cs(7,20):   error CS0234: The type or namespace name 'Sheets' does not exist in the namespace 'Lumina.Excel'
```

| Site | Code | Verdict |
| --- | --- | --- |
| `src/Mountbook/Game/MountCatalog.cs:7` | `using Lumina.Excel.Sheets;` | needs-change → `using Dalamud.Excel.Sheets;` |
| `src/Mountbook/Game/CurrencyStock.cs:5` | `using Lumina.Excel.Sheets;` | needs-change → `using Dalamud.Excel.Sheets;` |
| `src/Mountbook/Mountbook.csproj:2` | `<Project Sdk="Dalamud.NET.Sdk/15.0.0">` | plan — bump to `16.0.0` once it is published (`api.nuget.org` listed 15.0.0 as the newest on 2026-11-01) |
| `src/Mountbook.Previews/Mountbook.Previews.csproj:1` | same SDK line | plan — same bump; no code change needed in Previews (it builds against 16 with only the project-file edits) |
| `src/Mountbook/Mountbook.Tests`, `src/Mountbook.Tools` | plain `Microsoft.NET.Sdk`, `Services/` compiled by path | safe — Dalamud-free by design, nothing to move; 358 tests still pass |
| `repo.json` `DalamudApiLevel: 15` | written by `build.ps1:126` from the SDK-generated manifest | plan — **do not hand-edit**. It follows the SDK bump (`src/Mountbook/bin/Release/Mountbook/Mountbook.json` currently says 15 because the SDK does) |

Two references have to be added to the plugin csproj (and the Previews csproj) when building against
a 16 drop today. SDK 16 will probably wire them itself; until then these are proven to work:

```xml
<PropertyGroup>
  <!-- Lumina.Excel.dll no longer exists in a 16 distribution; silence the missing reference. -->
  <Use_Dalamud_Lumina_Excel>false</Use_Dalamud_Lumina_Excel>
</PropertyGroup>
<ItemGroup>
  <Reference Include="Dalamud.Excel.Sheets" Private="false" />
  <!-- ClientLanguage moved here, and it appears in GetExcelSheet/GetItemName/GetFromGameIcon signatures. -->
  <Reference Include="Dalamud.Common" Private="false" />
</ItemGroup>
```

Without `Dalamud.Common` the build fails even after the usings are fixed, because the compiler needs
the type that appears in those signatures:

```
CurrencyStock.cs(126,25), MountActions.cs(311,37), MountCatalog.cs(191,25), (229,30), (230,34),
MountTextureCache.cs(166,59): error CS0012: The type 'ClientLanguage' is defined in an assembly
  that is not referenced. You must add a reference to assembly 'Dalamud.Common, Version=1.0.0.0'
```

The SDKs also opt in per assembly by property (`Use_Dalamud_Lumina_Excel`, `Use_Dalamud_Lumina`,
`Use_Dalamud_FFXIVClientStructs`, …), so if 16.0.0 arrives with a new property for the sheets
assembly, use it instead of the raw `<Reference>` and keep `Private=false`.

## The change list

Everything the compiler asked for, in one place (five code edits, two project files):

1. `Game/MountCatalog.cs:7` — `using Dalamud.Excel.Sheets;`
2. `Game/CurrencyStock.cs:5` — `using Dalamud.Excel.Sheets;`
3. `Game/MountActions.cs:334` — `chatGui.Print(builder.ToReadOnlySeString());`
4. `Game/MountActions.cs:24-28` — fix the doc comment (it claims the byte overload survives)
5. `Game/MountCatalog.cs:293` — replace `row.IsFlying != 0` with the settled `FlyingCondition` test
6. `Mountbook.csproj` + `Mountbook.Previews.csproj` — SDK bump, `Use_Dalamud_Lumina_Excel=false`,
   `Dalamud.Excel.Sheets` + `Dalamud.Common` references
7. `repo.json` — regenerate by running `build.ps1`; never hand-edit

With 1-4 and 6 in place (and 5 as the probe placeholder) the whole plugin compiles against the 16
distribution with **0 errors, 0 warnings**, and so does `Mountbook.Previews`. That is the measured
claim behind "most of the code already follows A2".

## Removals that do not touch Mountbook

Worth recording so the next audit does not re-derive them:

- `Dalamud.Game.Text.SeStringHandling` and `.Payloads` are gone entirely; the plugin never imports them.
- `Dalamud.Interface.DragDrop` is gone; unused.
- `Dalamud.Game.Network.Structures` becomes `Dalamud.Game.Marketboard.Network.Structures`, and a real
  `IMarketBoard` service appears. Mountbook prices come from Universalis over HTTP
  (`Game/MarketPrices.cs`), so the marketboard rework does not reach it.
- `IChatGui.Print(SeString)`, `Print(string)`, `Print(XivChatEntry)` are gone; `IPrintableChatMessage`
  replaces them. Only the span call above mattered.
- `ConditionFlag` is Dalamud's own enum, not generated from ClientStructs: every member used here
  (`BetweenAreas`, `BetweenAreas51`, `InCombat`, `Casting`, the four `Occupied*`/`Watching*`, `Unconscious`)
  is still present in 16.

## Outside the four classes, still watch on bump day

- **Glasswork 0.1.0-beta.2** was built against API 15. Its assemblies contain no API-16-removed names
  (checked the string tables: no `SeStringHandling`, `DragDrop`, `XivChatEntry`, `Marketboard`, `On*`),
  it references only `IUiBuilder` and `ITextureProvider.GetFromGameIcon`, and the whole tree — plugin
  and previews — compiled against the 16 assemblies with it in place. Compiles ≠ runs: draw it once in
  the client before believing it.
- **FFXIVClientStructs** ships pinned inside Dalamud, so `AgentMountNoteBook`, `ActionManager`,
  `ActionType.Mount` and `PlayerState.Instance()->IsMountUnlocked` move with the API bump even though
  this audit cannot test them (the CS submodule is not part of the 16 branch tarball). A signature or
  offset change there fails soft by design (`MountActions` logs once and disables the button), so it
  shows up as a dead button, not a crash.
- **Goldens** are unaffected by the namespace move: same game data, same rows. They only move if the
  `Flies` decision in C.5 changes what a scene draws — regenerate and look at the image if it does.

## Re-verify on the day

1. `dotnet nuget`-check that `Dalamud.NET.Sdk/16.0.0` exists; bump the four Mountbook project files.
2. Point `DALAMUD_HOME` at the 16 distribution and build the plugin: expect 0 warnings.
3. Run `dotnet run --project src/Mountbook.Tests` — the suite is Dalamud-free, so it must stay green
   (358 today) and should not need a single change.
4. Settle `Mount.IsFlying` against the live sheet; add the test; then re-baseline goldens only if a
   badge moved.
5. In the client: `/mountbook`, ride a mount, link a whistle, open the Mount Guide, search `flyable:y`.
6. `build.ps1` regenerates `repo.json` with `DalamudApiLevel: 16`; commit `dist/` + `repo.json` with
   the version bump as usual.
