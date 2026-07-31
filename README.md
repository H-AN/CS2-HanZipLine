# CS2-HanZipLine

SwiftlyS2 plugin for player-created, two-way CS2 ziplines.

P0 controls:

- `!zipline`: open the player menu.
- `!zipline create` or `!zipline_create`: trace the aimed end surface and create a two-anchor zipline from nearby ground.
- `!zipline remove` or `!zipline_remove`: remove your zipline aimed at by the crosshair.
- Press `F` near either endpoint to ride toward the other end. Press `F` again to detach.

The plugin precaches the configured anchor model, build-flight model, and sound-event file, creates one permanent `CBeam` per pair, tracks player runtime state by `SessionId`, and restores captured movement values from every detach path. With `RealisticBuild` enabled, the start anchor appears first, the configured `BuildFlightModel` follows a configurable parabolic arc to the target, and the same `CBeam` follows the flight until the final anchor replaces it.

P1 map persistence, administrator management UI, and private markers are deliberately not included in this initial P0 implementation.
