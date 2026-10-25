# Enemy List Cast Bars

A Dalamud plugin for FINAL FANTASY XIV that draws a cast bar for every enemy on the enemy list,
so you can react to every cast without having to cycle targets.

## Features

- A cast progress bar on each enemy list row, only visible while that enemy is casting.
- The cast name and the remaining cast time are drawn on top of the bar.
- Interruptible casts are highlighted in a different colour than casts that cannot be interrupted.
- Every position, size and colour is adjustable in-game and applied immediately.

## Requirements

- XIVLauncher with Dalamud (API level 15).
- .NET SDK 10 to build.

## Building

```powershell
dotnet build -c Release
```

The plugin is packaged into `bin/Release/EnemyListCastBars/latest.zip`, which is what you load
into the game.

## Installing

Copy the built plugin into your Dalamud dev plugin folder and enable it from `/xlplugins`:

```powershell
Copy-Item -Recurse -Force .\bin\Release\EnemyListCastBars\* "$env:APPDATA\XIVLauncher\devPlugins\EnemyListCastBars\"
```

Alternatively use `/xlplugins` → *Dev Tools* → *Load plugin from directory* and point it at
`bin/Release/EnemyListCastBars`.

## Configuration

Open the settings with `/enemycastbars`, or from the plugin installer.

Positions and sizes are expressed in the coordinate space of a single enemy list row, where
`(0, 0)` is the top-left corner of the row. The defaults place the bar over the lower part of the
row; tune *Position* and *Size* if your HUD scale or layout differs.

## How it works

The plugin attaches a bar frame, a progress fill and a text node to the component node of every row of
the `_EnemyList` addon, and updates them from the game's enemy list number array and each enemy's
`CastInfo` every frame before the addon is drawn. The fill is a plain stretching image rather than
part of a nine grid, so increasing the cast progress grows a smooth bar instead of squeezing a
texture. Nothing is hooked or patched in the game itself, so the UI it draws disappears cleanly when
the plugin is unloaded.
