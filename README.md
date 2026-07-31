# CS2-HanZipLine

SwiftlyS2 plugin for player-created, two-way CS2 ziplines.

P0 controls:

- `!zipline`: open the player menu.
- `!zipline create` or `!zipline_create`: trace the aimed end surface and create a two-anchor zipline from nearby ground.
- `!zipline remove` or `!zipline_remove`: remove your zipline aimed at by the crosshair.
- `!zipline admin` or `!zipline_admin`: open the administrator menu. Requires a permission listed in `AdminPermissions`.
- `!zipline_clear`: remove all active ziplines. Requires a permission listed in `AdminPermissions`.
- Press `F` near either endpoint to ride toward the other end. Press `F` again to detach.

Bot riding is opt-in through `BotAllowUse`. Enabled bots acquire the nearest usable endpoint inside `BotUseRange`, receive a horizontal approach velocity at `BotApproachSpeed`, then attach directly once they enter `UseRadius`; the plugin does not change their view or simulate F/E keypresses. `BotTargetTimeoutSeconds` abandons unreachable endpoints and `BotUseCooldownSeconds` prevents immediate reattachment loops.

The plugin precaches the configured anchor model, build-flight model, and sound-event file, creates one permanent `CBeam` per pair, tracks player runtime state by `SessionId`, and restores captured movement values from every detach path. With `RealisticBuild` enabled, the start anchor appears first, the configured `BuildFlightModel` follows a configurable parabolic arc to the target, and the same `CBeam` follows the flight until the final anchor replaces it.

Administrators can create and remove any zipline, save the active layout, reload the saved layout, clear the current layout, and toggle anchor outlines. The outline toggle uses direct CS2 entity glow, so it is a global debugging view visible to all players.

Map layouts are stored as JSON in `<PluginDataDirectory>/maps/<map>.json`. Layouts are restored after a map load. When `ClearEachRound` is enabled, all active ziplines are removed at round end and the saved map layout is restored at the next round start.

`AdminPermissions` is a comma-separated list of SwiftlyS2 permission flags. A player needs any one listed permission; for example: `"admin.dex,admin.root"`. Leave it empty to disable administrator actions.
