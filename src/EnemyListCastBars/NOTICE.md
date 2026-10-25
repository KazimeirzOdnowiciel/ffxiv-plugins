# Third-party notice

`EnemyListCastBars` is **not** first-party code. It is vendored here so the
repository is self-contained and the plugin survives a reinstall of Dalamud.

- **Original location:** `%LOCALAPPDATA%\XIVLauncher\devPlugins\sdsd\EnemyListCastBars`
  (a Dalamud dev-plugin checkout)
- **Imported:** 2026-10-25, unmodified, excluding `bin/` and `obj/`
- **License:** MIT, as stated for this plugin in the repository README
- **Path in this repo:** `src/EnemyListCastBars`

It is built and packaged by `build.ps1` exactly like the first-party plugins,
and shipped in `dist/` under its own manifest. Keep the upstream `README.md`
with the sources; if the upstream license text becomes available, drop it in
this directory alongside this notice.

Modifications, if any, should be recorded here so a future update from upstream
can be diffed rather than blindly overwritten.
