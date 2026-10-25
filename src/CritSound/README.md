# Crit Sound

A small Dalamud plugin for FFXIV that plays a sound when an attack **crits**, **direct hits**, or **critically direct hits**.

The three cases get their own sound, volume, and toggle, so a crit and a chaos crit can feel different.

## Features

- Separate sound slot for crit / direct hit / crit + direct hit
- Bundled sounds, generated for this plugin (a bright *tink*, a duller *thock*, a longer *ching*)
- Volume per slot, 0–100 %
- Minimum gap between sounds, so a burst window doesn't turn into a machine gun
- Optional reaction to auto-attack and DoT ticks (they arrive as their own flying-text kinds)
- Drop your own `.wav` files into the plugin's sounds folder and pick them from a dropdown
- Silent while the game UI is hidden (cutscenes, character creation)

## Build

Requires the .NET 10 SDK and a local XIVLauncher install (the SDK picks up Dalamud from `%APPDATA%\XIVLauncher\addon\Hooks\dev`).

```powershell
dotnet build -c Release
```

Output lands in `bin\Release`.

## Install (development)

1. Build.
2. In game: `/xlsettings` → **Experimental** → **Dev Plugin Locations** → add `D:\Deepseek\CritSound\bin\Release`.
3. `/xlplugins` → **Dev Tools** → **Installed Dev Plugins** → enable **Crit Sound**.

For anyone else, zip the contents of `bin\Release` and host it in a custom repository (`pluginmaster.json`), or publish it as-is.

## Use

`/critsound` opens the settings window. Each slot has an **Enabled** checkbox, a **Test** button, a sound dropdown, and a volume slider.

The first time the plugin runs it copies the bundled sounds into:

```
%APPDATA%\XIVLauncher\pluginConfigs\CritSound\sounds
```

Drop your own **16-bit PCM `.wav`** files there, press **Rescan**, and select them. Anything the plugin can't parse as 16-bit PCM still plays — it just ignores the volume slider for that file.

## How it works

The plugin subscribes to `IFlyTextGui.FlyTextCreated`, which fires for every piece of native flying text *before* it is drawn. The event carries a `FlyTextKind`, and the game already distinguishes the cases:

| Kind | Meaning |
| --- | --- |
| `Damage` | normal hit |
| `DamageDh` | direct hit |
| `DamageCrit` | critical hit |
| `DamageCritDh` | critical + direct hit |
| `AutoAttackOrDot*` | the same three variants, for auto-attacks and DoT ticks |

No hooking, no signature scanning, no memory reading — that's why the plugin survives game patches without maintenance.

Sound is played through `winmm.dll`'s `PlaySound`, from an in-memory copy of the WAV so the volume slider can scale the samples without touching the system mixer.

## Known limitation

Flying text **does not say who dealt the damage**. The event gives you the value and the hit type, not the caster, so in a party the plugin also reacts to your party members' crits. In solo content that is invisible; in an 8-man it is noticeably busier.

Fixing that properly means hooking `ActionEffectHandler.Receive` and filtering on `casterEntityId == LocalPlayer.EntityId`, which is a follow-up rather than something this version does. The throttle is the current mitigation.

## Audio routing

The sound plays on the default Windows output device, at the system volume — the in-game volume sliders don't affect it. Game capture in OBS won't pick it up; desktop/loopback capture will.

## Files

| File | Purpose |
| --- | --- |
| `Plugin.cs` | entry point, flying-text subscription, trigger mapping |
| `Configuration.cs` | persisted settings |
| `ConfigWindow.cs` | the `/critsound` window |
| `SoundBank.cs` | sounds folder, catalog, WAV parsing and gain |
| `Winmm.cs` | `PlaySound` interop |
| `tools/make_sounds.py` | regenerates the three bundled sounds |
