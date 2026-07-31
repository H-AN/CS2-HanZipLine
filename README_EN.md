<p align="center">
  <a href="README.md"><kbd>简体中文</kbd></a>
  &nbsp;
  <a href="README_EN.md"><kbd>English</kbd></a>
</p>

# CS2-HanZipLine

A two-way zipline plugin for CS2 servers. Players can place and ride ziplines with `F`. Administrators can save every player-built zipline on a map as a persistent map layout that restores whenever the map loads.

Current version: `v0.4.0`

## Features

- Players can create and remove their own ziplines.
- Press `F` near either anchor to ride to the other end; press `F` again to detach.
- Administrators can create CT, T, and Global ziplines, remove any zipline, save layouts, reload layouts, and show anchor outlines.
- A player-created zipline is assigned to the player's CT or T team at the moment it is created. Players cannot create Global ziplines.
- Choose between unrestricted use of every zipline or same-team plus Global-only access.
- CT, T, and Global ziplines each have a configurable color for both the cable beam and administrator outline glow.
- Bots can optionally approach and ride eligible ziplines automatically.
- Limits are available for active ziplines, player ownership, cooldowns, distance, uses, lifetime, and round cleanup.
- Administrators can collect a layout made by multiple players and save it per map.

## Requirements

- A CS2 dedicated server with SwiftlyS2 installed and running.
- .NET 10 SDK only when building from source.
- Configured anchor models and sound events must be available on the server. The defaults use CS2 assets.

## Installation

1. Download a release, or build from source:

   ```powershell
   dotnet build CS2-HanZipLine.csproj
   ```

2. Copy everything from `build/CS2-HanZipLine/` into the server's SwiftlyS2 plugin directory, preserving the directory layout.

   ```text
   CS2-HanZipLine.dll
   CS2-HanZipLine.jsonc
   resources/
   ```

3. Restart the server or reload the plugin through your normal SwiftlyS2 workflow.
4. Configure `CS2-HanZipLine.jsonc`, especially administrator permissions, before opening the plugin to players.

> Do not copy only the DLL. `resources/translations/` contains menu and chat translations.

## Quick Start

### Create and ride a zipline

1. Stand near usable ground and aim at the solid surface for the far anchor.
2. Run `!zipline` and select **Create zipline**, or run `!zipline create`.
3. The first anchor is placed near your current ground position and the second anchor is placed on the aimed surface.
4. Walk near either anchor and press `F` to ride. Press `F` again while riding to detach.

The chat message explains common failures such as no valid aimed surface, no nearby ground, invalid distance, anchors too close together, cooldown, or a reached limit.

### Save a player-built map layout

1. Temporarily set `MaxPerPlayer` to `1` and let players place useful routes.
2. An administrator runs `!zipline admin`.
3. Select **Save current map ziplines**.
4. Every fully created active zipline is saved, including normal player CT/T ziplines and administrator ziplines.
5. The saved layout restores automatically the next time that map loads.

Map files are stored at:

```text
<PluginDataDirectory>/maps/<map name>.json
```

## Commands

| Command | Who can use it | Purpose |
| --- | --- | --- |
| `!zipline` | Alive player | Opens the player menu. |
| `!zipline create` / `!zipline_create` | Alive player | Creates a zipline for the player's current team. |
| `!zipline remove` / `!zipline_remove` | Alive player | Removes the player's zipline aimed at by the crosshair. |
| `!zipline admin` / `!zipline_admin` | Administrator | Opens the administrator menu. |
| `!zipline_clear` | Administrator | Immediately removes every active zipline. |
| `F` | Alive player near an anchor | Rides a zipline; detaches when pressed again while riding. |

Action buttons do not close the menu. Use the menu's own Tab/back controls to leave it.

## Administrator Menu

`AdminPermissions` controls administrator access. A player with any listed permission flag can use the administrator menu and clear command.

| Menu item | Purpose |
| --- | --- |
| Create CT zipline | Creates a zipline permanently assigned to CT, regardless of the administrator's current team. |
| Create T zipline | Creates a zipline permanently assigned to T, regardless of the administrator's current team. |
| Create Global zipline | Creates a zipline available to both teams. Only administrators can create it. |
| Remove aimed zipline | Removes a zipline from any team or creator. |
| Save current map | Saves every fully created active zipline on the current map. |
| Reload saved map ziplines | Clears current ziplines and restores the saved map layout. |
| Remove all active ziplines | Removes active ziplines without deleting the saved map file. |
| Show/hide anchor outlines | Toggles anchor glow. CT, T, and Global ziplines use their own configured colors. |

## Team Access and Colors

Every zipline has a permanent `CT`, `T`, or `Global` assignment.

| `AllowAllTeamsUseZiplines` | CT player can use | T player can use | Bot behavior |
| --- | --- | --- | --- |
| `true` | Every zipline | Every zipline | May use every zipline. |
| `false` | CT and Global ziplines | T and Global ziplines | Follows the same rules as players. |

- A normal player's zipline keeps the team it had when created, even if that player later changes teams.
- Administrators explicitly choose the assignment in the administrator menu.
- The team assignment is saved with each map zipline. Older map files without this field load as `Global`.
- `CTZiplineColor`, `TZiplineColor`, and `GlobalZiplineColor` accept `R G B` or `R G B A`, such as `"80 180 255 255"`.
- Use the same value for all three colors when no visual distinction is wanted.

## Bot Use

Bot usage is disabled by default. Enable it only after checking that bots can physically reach the anchor routes.

```jsonc
"BotAllowUse": true,
"BotUseRange": 300.0,
"BotUseCooldownSeconds": 20.0,
"BotTargetTimeoutSeconds": 5.0,
"BotApproachSpeed": 450.0
```

| Setting | Default | Purpose |
| --- | ---: | --- |
| `BotAllowUse` | `false` | Enables automatic bot zipline use. |
| `BotUseRange` | `300` | Distance at which a bot starts selecting a nearby anchor. |
| `BotUseCooldownSeconds` | `20` | Delay after ending or abandoning an attempt. |
| `BotTargetTimeoutSeconds` | `5` | Time before abandoning an unreachable target. `0` disables the timeout. |
| `BotApproachSpeed` | `450` | Bot movement speed while approaching an anchor. |

Bots do not simulate key presses or change their view. They move to eligible anchors and attach automatically.

## Configuration Reference

The main configuration file is `CS2-HanZipLine.jsonc` under the `CS2HanZipLine` section. A value of `0` means unlimited for `MaxUses` and `LifetimeSeconds`.

### Core Limits

| Setting | Default | Description |
| --- | ---: | --- |
| `Enable` | `true` | Master switch. Disabling it removes active ziplines. |
| `AdminOnlyCreate` | `false` | Restricts zipline creation to administrators. |
| `AdminPermissions` | `hanzipline.admin.manage` | Comma-separated permission flags. Any one flag grants access. An empty value disables administrator actions. |
| `MaxActivePairs` | `64` | Maximum active ziplines on the server. |
| `MaxPerPlayer` | `10` | Maximum ziplines owned by one normal player. |
| `CreateCooldownSeconds` | `8` | Delay between player creation attempts. |
| `MinDistance` | `128` | Minimum anchor-to-anchor distance. |
| `MaxDistance` | `5000` | Maximum anchor-to-anchor distance. |
| `AnchorSeparation` | `96` | Minimum distance from a new anchor to existing anchors. |
| `UseRadius` | `96` | Maximum distance for `F` use or automatic bot attachment. |

### Teams and Visuals

| Setting | Default | Description |
| --- | --- | --- |
| `AllowAllTeamsUseZiplines` | `true` | Allows every player and bot to use every zipline. When `false`, only same-team and Global ziplines are allowed. |
| `CTZiplineColor` | `80 180 255 255` | CT cable and CT outline color. |
| `TZiplineColor` | `255 80 80 255` | T cable and T outline color. |
| `GlobalZiplineColor` | `255 255 255 255` | Global cable and Global outline color. |
| `AdminVisionGlowRange` | `5000` | Outline glow range while administrator vision is enabled. `0` disables the range. |
| `BeamWidth` | `0.5` | Cable width at the start. |
| `BeamEndWidth` | `0.5` | Cable width at the end. |
| `BeamHaloScale` | `3` | Cable halo size. |

### Riding and Lifetime

| Setting | Default | Description |
| --- | ---: | --- |
| `RideSpeed` | `700` | Travel speed along the cable. |
| `ArrivalDistance` | `48` | Distance from the end at which the ride completes. |
| `AlignmentSpeed` | `240` | Position correction speed toward the cable while riding. |
| `RideFlyDurationSeconds` | `1` | Brief fly-state duration after attaching. `0` disables it. |
| `StallTimeoutSeconds` | `1.5` | Time without progress before a safe automatic detach. |
| `MaxUses` | `0` | Maximum rides per zipline. `0` is unlimited. |
| `LifetimeSeconds` | `0` | Lifetime of temporary player/admin ziplines. `0` is unlimited; saved map ziplines are unaffected. |
| `ClearEachRound` | `true` | Removes current ziplines at round end and restores the saved layout at the next round start. |

### Models and Build Effect

| Setting | Default | Description |
| --- | --- | --- |
| `AnchorModel` | `models/props/cs_italy/it_streetlampleg.vmdl` | Anchor model at both ends. |
| `AnchorModelScale` | `0.65` | Anchor model scale. |
| `CableAttachmentHeightFallback` | `144` | Fallback cable attachment height when model bounds are unavailable. |
| `RealisticBuild` | `false` | Shows a model flying to the destination while building. |
| `BuildFlightModel` | `weapons/models/grenade/decoy/weapon_decoy.vmdl` | Model used by the build-flight effect. |
| `BuildFlightModelScale` | `1` | Build-flight model scale. |
| `BuildFlightSpeed` | `1800` | Build-flight speed. |
| `BuildFlightGravity` | `800` | Build-flight gravity. |
| `SurfaceOffset` | `2` | Visual offset away from the hit surface. |
| `GroundTraceDistance` | `256` | Maximum downward distance used to find the start ground. |

### Sounds

| Setting | Default | Description |
| --- | --- | --- |
| `PrecacheSoundEvent` | `soundevents/game_sounds_ui.vsndevts` | Sound-event file to precache. Leave empty to skip precaching only. |
| `CreateSound` | `Music.Match.LastRoundHalf` | Sound played after successful creation. Leave empty to disable. |
| `BuildSound` | `UI.ContractSeal` | Sound for the realistic build effect. Leave empty to disable. |
| `RideStartSound` | `UIPanorama.container_weapon_ticker` | Sound played when riding starts. Leave empty to disable. |
| `RideLoopSound` | `UI.StickerScratch` | Repeating riding sound. Leave empty to disable. |
| `RideLoopInterval` | `0.5` | Interval between repeating ride sounds. |
| `RideEndSound` | `UI.CrateOpen` | Sound played on detach or arrival. Leave empty to disable. |
| `SoundVolume` | `1` | Volume for all zipline sounds. |
| `SoundPitch` | `1` | Pitch for all zipline sounds. |

## Common Setups

### Team routes to mid

Create a CT zipline at CT spawn, a T zipline at T spawn, and a Global zipline at mid. Set `AllowAllTeamsUseZiplines` to `false`: CT can use CT plus Global, and T can use T plus Global.

### Open use for all players

Set `AllowAllTeamsUseZiplines` to `true`. Every team can use every zipline, while the original CT/T/Global colors remain visible for route identification.

### Community-built map routes

Set `MaxPerPlayer` to `1`, allow players to place routes, then use **Save current map ziplines** in the administrator menu. Restore `MaxPerPlayer` afterward.

## Troubleshooting

| Problem | What to check |
| --- | --- |
| Cannot create a zipline | Be alive, aim at a solid surface, stand near ground, and check distance, cooldown, and limits. |
| Cannot use a zipline | Check `AllowAllTeamsUseZiplines`. When disabled, only same-team and Global ziplines are available. |
| Bots do not use ziplines | Enable `BotAllowUse`, keep anchors within `BotUseRange`, make sure bots can reach them, and verify team permission. |
| Saved routes do not restore | Confirm the layout was saved for the current map and the server can write to `<PluginDataDirectory>/maps/`. Use **Reload saved map ziplines** to verify it. |
| No administrator menu | Ensure `Enable` is `true` and the player has a permission listed in `AdminPermissions`. |
| Missing Chinese chat text | Copy `resources/translations/` together with the DLL. |

## License

This project is licensed under [GPL-3.0](LICENSE).
