# Native placement probe

`TMItemStudioPlacementProbe` is an Openplanet Developer-mode plugin that asks Trackmania's own
map editor to **place** an exported item, then reports what the engine stored. It closes the gap
the load-only smoke test leaves open: preloading a `CGameItemModel` proves the file parses, not
that the item can be placed or that its placement data survives placement.

## Arming

The probe does nothing until the request file says `"armed": true`. `Tests/ProbeAssets` writes
requests **unarmed**, and the plugin files itself under `Plugins/` only when a run is actually
wanted. A probe that fires into whatever editor session happens to be open is not acceptable;
one crashed editor session was enough to establish that.

When a request is present but unarmed, the plugin logs `SKIP request-not-armed` and stops.

## How it works

1. `Tests/ProbeAssets` writes three inputs into the user game folder:
   - the item under `Items/TMItemStudio_Probe/`,
   - a macroblock (`CGameCtnMacroBlockInfo`) that references that item at three known
     placements, differing in position, yaw and scale,
   - `TMItemStudioPlacementProbe/placement-request.json`, unarmed, naming those paths plus the
     target coordinate, direction and the map the run is allowed to touch.
2. The plugin preloads the item and the macroblock through the engine's FID loader, places with
   `PlaceMacroblock_NoDestruction`, computes the items of the resulting macroblock instance, and
   reads back both `Challenge.AnchoredObjects` and the computed instance items.
3. It writes `TMItemStudioPlacementProbe/placement-result.json` with the placement verdict, the
   map name, and per copy the item model's engine identifier (`IdName`), position, yaw/pitch/roll,
   scale, variant and the placement parameters (pivot positions, magnet locations, placement
   flags).

Every engine call is one a shipping editor plugin uses (`PlaceMacroblock_NoDestruction` and the
macroblock-instance recipe from Item Placement Toolbox and Editor++). No offsets, no memory
writes, no `Dev::` field patching.

### Which map may be touched

A run is allowed only on a map whose name matches the request's prefix, or on a map the game has
not saved yet that holds no items (`"acceptUnnamedMap": true`). The block count is deliberately
*not* used: Trackmania builds its own base platform on a new map, which already counts as
thousands of blocks, so "no blocks" is not a test for "empty".

## Diagnosing a crash

Every engine call is announced with a `CALL <name>` line before it executes and a `DONE` line
after, under the `[TMIS-PLACE]` prefix in `Openplanet.log`. If the game dies mid-run, the last
line names the call that was executing. That logging exists because a crash can otherwise leave
no evidence at all.

## Run it

From the repository root:

```sh
DOTNET_ROLL_FORWARD=Major dotnet run --project Tests/ProbeAssets -- --item "path/to/An.Item.Gbx"
```

Then enable **Developer** signature mode in Openplanet, copy this folder to
`%USERPROFILE%\OpenplanetNext\Plugins\`, start Trackmania, open the map editor, set
`"armed": true` in the request, and let the probe run. Then verify:

```sh
DOTNET_ROLL_FORWARD=Major dotnet run --project Tests/ProbeAssets -- --verify
```

`--verify` checks the plugin's report and then finds the newest `.Map.Gbx` under `Maps\Autosaves`
or `Maps\My Maps` that references the probe item, parses it with the same GBX.NET used for import,
and checks the stored copies, their relative offsets, yaw and scale. Use `--map <path>` to point
at a specific map and `--inspect-map` to dump what a map contains.

## What a pass means, and what it does not

A pass means the engine accepted a macroblock that references this item, resolved it to the item
the studio exported (`IdName` match, plus equal pivot/magnet counts — not a display-name match),
and that the saved map stores the expected copies with their laid-out offsets, yaw and scale.

It does **not** mean the item is visible in the item browser, that it collides, or that a player
can drive through it — those need the map to be played. The placement *origin* is chosen by the
engine in block units, so only the offsets between copies are compared.

## History

- 2026-10-15: a run returned `placed: true` while the saved map contained no anchored objects,
  no macroblock instances and no expected item models. The engine's "placed" verdict is therefore
  not evidence of a placed item; the map file is.
- 2026-10-15: the game crashed inside `Openplanet.dll` (access violation) while the editor was
  loading a map, with this plugin loaded but with no `[TMIS-PLACE]` line written. Cause not
  established; the probe is disarmed until a loading test without it discriminates.
