# The settings file: shape, migration and recovery

`%APPDATA%\XIVLauncher\pluginConfigs\Mountbook.json` is the only state Mountbook keeps between sessions:
the browse filters, the wanted list, the owned baseline, the grind records, the price watches and the theme.
This document records what the file actually is, what happens to one that cannot be read (measured, not
assumed), how a file written by an older build is upgraded, and what the reset-to-defaults control is allowed
to take without asking.

CONTRIBUTING **N5** is the rule this implements: a configuration has a `Version` and a `Migrate`, and a field
is never deleted or renamed without a version bump.

## 1. What the file is, and who reads it

Dalamud writes it, not the plugin. `IPluginConfiguration.SavePluginConfig` reaches
`Dalamud.Configuration.PluginConfigurations` (Dalamud 15.0.3.9, decompiled for this reading):

| Direction | Code | Shape |
| --- | --- | --- |
| save | `PluginConfigurations.Save` → `SerializeConfig(object)` | `JsonConvert.SerializeObject(config, Indented, TypeNameHandling.Objects, TypeNameAssemblyFormatHandling.Simple)` |
| load | `DalamudPluginInterface.GetPluginConfig()` → reflection into `PluginConfigurations.LoadForType<T>` | `JsonConvert.DeserializeObject<T>(File.ReadAllText(...), new JsonSerializerSettings { Converters = [DalamudAssemblyTypeNameForcingJsonConverter.Instance] })` |

Consequences that matter for a migration:

- The **load path is TypeNameHandling.None**. The `$type` in the file is written by the save path and ignored
  by the load path for a plugin's own types (the one converter only `CanConvert` Dalamud's assembly). So a
  fixture with `$type` in it is read exactly like the real file, and Newtonsoft is the deserialiser the tests
  must use - not `System.Text.Json`.
- A member **missing** from the file keeps the value its property initialiser gave, because the constructor
  runs and only the members the file names are then filled. A member present as `null` overwrites it with
  `null`. Both are exercised by the tests; the blank comments in `Configuration.Shape.cs` say which.
- `Load` (the other entry point) is only used when the calling assembly declares no `IPluginConfiguration`
  type; Mountbook's `Configuration` is found by the reflection path.

## 2. What a damaged file actually does

Measured, not assumed - the shipped loader was invoked with `T` bound to the built `Mountbook.Configuration`
and fed six files (probe: load `Dalamud.dll`, `PluginConfigurations.LoadForType<Configuration>`, one temp
folder per case):

| File | Loader result |
| --- | --- |
| the real settings file | loads; every field as written |
| truncated JSON (400 bytes of it) | **throws** `JsonSerializationException: Unexpected end when deserializing object. Path 'SavedSource', line 17` |
| 400 bytes cut inside an array | **throws** `JsonSerializationException: Unexpected end when deserializing array. Path 'KnownOwnedIds[8]'` |
| not JSON at all | **throws** `JsonReaderException: Unexpected character encountered while parsing value: g.` |
| a zero-byte file | returns **null** |
| the literal `null` | returns **null** |

Nothing between `LoadForType` and the plugin's own constructor catches those exceptions: `LocalPlugin.LoadAsync`
wraps the constructor in `catch (Exception)` and sets `PluginState.LoadError`, logging `Error while loading
{PluginName}`. So the widely repeated claim "Dalamud returns defaults for a corrupt config" is **false** for
this loader: a corrupt file makes the plugin **not load at all**, and only an *empty or null* file reaches the
`?? new Configuration()` path. Before this change Mountbook had no guard of its own, so that was its
behaviour.

## 3. The decision: recover, keep the evidence, say so

`Plugin.LoadConfiguration` now wraps `GetPluginConfig()`:

1. The exception is logged at `Error`, with its own stack - a user report that starts "Mountbook will not
   load" now has a line naming the file.
2. `Services/ConfigRecovery.TryMoveAside` **moves** the unreadable file to
   `Mountbook.corrupt-YYYYMMDD-HHmmss.json` beside it (a second failure in the same second gets `-1`, `-2`, ...
   so the older backup survives). If the file is locked - the loader may still hold it - it is **copied**
   instead, and the original is left for the next save to overwrite. A file that can be neither moved nor
   copied is logged as such; nothing is invented.
3. The plugin continues with a fresh `Configuration`, so the wanted list is gone from the running session but
   is recoverable by hand from the backup.
4. The player is told once, by a toast on the first frame (`toast.configRecovered`), because the read happens
   before login, when there is no chat window to write to.

Moving rather than deleting is the whole point: the file is the only copy of a hunting list a player may have
spent an evening on.

## 4. The ladder

`Configuration.CurrentVersion` is **4**. `Migrate()` runs the always-on guards, then walks the ladder one step
per version the file is behind, then asks its Dalamud half to save once (a partial method, so the migration
itself is Dalamud-free and the tests compile it - T1).

| Step | Shape that arrived with it | What the step does |
| --- | --- | --- |
| 1 | the first file: browse state, wanted list, owned baseline, recent-collected map, theme | nothing - it is the baseline the others are written against |
| 2 | the grind tracker: `Grinds`, a map of mount id → `MountGrindRecord` | sweeps the map: drops a null value and a record that holds nothing (`MountGrind.IsTracked`), clamps a negative count to zero, folds a whitespace note to null |
| 3 | the price watch: `Watches`, a map of mount id → `MountWatchRecord` | sweeps the map: drops a record whose threshold is not positive (`MountWatch.IsWatching` - the pane never persists one, so it came from a hand edit), clamps negative timestamps |
| 4 | the obtain-type taxonomy: `SavedSource` is one type's own spelling | rewrites the saved filter as the taxonomy spells it (`pvp` → `PvP`), clears a value that names no type |

Two rules that decide whether a step is allowed to do this:

- **Every step sweeps rather than trusts the version it claims.** The three shapes shipped before the ladder
  existed, so every file on disk says `Version: 1` whatever it grew - a file claiming 1 can already carry
  `Grinds`. A step that assumes "this member cannot exist yet" would do nothing on the files that need it.
- **The null and enum guards run on every load, not only on a step.** A hand edit does not bump `Version`, so
  the file most likely to be broken claims to be current. This also makes `Migrate` safe to call on a shape
  this build does not know: a `Version` above `CurrentVersion` is left alone (forwards compatible), while the
  guards still repair what they know.

A step must also never throw: `Migrate` runs from the plugin constructor, so an exception here is a plugin
that does not load. The sweep checks `IsTracked`/`IsWatching` *before* touching a record's members - a null
value in the map is exactly the case that made an earlier draft of step 2 throw a `NullReferenceException`.

**Saved views is not a step yet, because it is not in the shape.** The 0.3.0 roadmap promises a list of saved
filters; nothing in `Configuration.Shape.cs` persists one today, so there is no fourth collection to migrate
and no fixture for it. When it lands it takes step 5 (`CurrentVersion` 5) with a fixture of its own, and the
member it adds is the first *list* member since the wanted set - the ladder and
`EveryMemberAFixtureCarriesIsStillAMemberOfTheShape` are what it plugs into, not something it has to invent.

## 5. Reset to defaults, and the confirmation rule

The settings window has a **Reset everything** row under *Browse* (`ConfigView`). It clears every persisted
member, including the three collections the player fills in themselves.

`Configuration.ResetToDefaults` copies the values of a throwaway `Configuration` rather than listing defaults
by hand (so a member added later cannot be forgotten), keeps `Version` at `CurrentVersion` (a reset file is
the current shape, not one that still has to be upgraded), and **clears the collections in place**: the main
window may still hold the set it copied this frame, and replacing the instance would leave it drawing the list
the player just cleared.

`Services/ConfigReset.NeedsConfirmation(ConfigResetImpact)` is the rule, and it is a test rather than a read
of `ConfigWindow`: a reset that would discard a wanted mount, a grind record or a price watch **opens a
confirmation row** first (counts, "There is no undo.", *Clear everything* / *Keep it*); a reset that would
only undo settings applies on the first click. The pending flag lives in the window, because the view state is
rebuilt every frame and a confirmation has to outlive the frame its click landed in.

The confirmation is a second golden - `config-reset.png` - because it is a branch that can destroy data and
the default scene shows only the button that opens it.

## 6. The tests, and how to reproduce the measurement

`src/Mountbook.Tests/ConfigurationMigrationTests.cs` reads four files out of `Fixtures/`, which are the
member lists of the three commits that each added a persisted collection (`26e6ba2`, `6fa6035`, `75afaba`),
written the way Dalamud writes them:

| Fixture | What it pins |
| --- | --- |
| `config-v1-pre-grind.json` | a file from before the grind tracker: every field kept, `Grinds`/`Watches` arrive empty, `BoardSort`/`BoardWindowOpen` keep their defaults |
| `config-v1-grind.json` | the grind era: both records survive the sweep member-for-member (including the one whose `Want` is 0), `Watches` arrives empty |
| `config-v1-watch.json` | the watch era: all four members of both watch records survive, `Grinds` untouched |
| `config-v1-handedited.json` | a damaged file of the same era: null collections, a null map value, a dead grind record, a zero-threshold watch, negative counts and timestamps, out-of-range enums, `"pvp "` |

`EveryMemberAFixtureCarriesIsStillAMemberOfTheShape` walks the fixtures and fails if a member they carry no
longer exists - a rename cannot pass the migration tests by reading as a default.

Reproducing the measurement outside the tests: build the plugin, then load the shipped
`Dalamud.dll` (`%APPDATA%\XIVLauncher\addon\Hooks\<version>\Dalamud.dll`) into a scratch console app, get
`Dalamud.Configuration.PluginConfigurations` by name, `MakeGenericMethod` `LoadForType` against
`Mountbook.Configuration` from `src/Mountbook/bin/Release/Mountbook.dll`, and feed it each fixture on disk.
Against the build in this change every fixture came back as the tests assert (Version 1 → 4, `pvp ` → `PvP`,
3 grinds → 1, 3 watches → 1) and the truncated real file threw `JsonSerializationException` out of the loader.
The only failure in that harness is at the very end of `Migrate`, where the save reaches
`Plugin.PluginInterface` - which exists only inside the game.

## 7. What this does not cover

- **A live client pass.** The DalamudMCP listener on `127.0.0.1:18777` serves 100 tools and none of them can
  reload a plugin (`load_dev_plugin`, `get_dalamud_log` and plugin management are absent), so the running
  client keeps whatever DLL it loaded. The evidence above is the built assembly driven through Dalamud's own
  loader, plus the unit tests - not a session in the game.
- **A backup that can be restored by the plugin itself.** The corrupt file is kept and named in the log; putting
  the wanted list back is a hand edit (or a *Copy wanted* import from a previous export). A "restore from
  backup" button is a separate feature.
- **Translation of the new strings.** `config.resetAll*` and `toast.configRecovered`/`toast.resetAll` exist in
  English only, like the rest of the table.
