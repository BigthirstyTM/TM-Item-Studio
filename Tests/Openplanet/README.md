# Trackmania native export smoke test

`TMItemStudioSmokeTest` is an Openplanet Developer-mode plugin that uses
Trackmania's native FID loader, rather than GBX.NET, to load the studio's test
exports from:

```text
Documents\Trackmania\Items\02. Freebuff RE Items
```

It verifies each file natively: the game parses it, the item is a
`CGameItemModel` with an entity model, and the edits the studio made are the
values the engine reports back:

- `FBRE_Control.Item.Gbx` — the untouched source through the same export path,
  used as the control.
- `FBRE_Placement10.Item.Gbx` — ten placement pivots, four placement flags and
  two magnet locations (`m_MagnetLocs_Degrees`, chunk `2E020004`).
- `FBRE_Kinematic04.Item.Gbx` — the same kinematic source with every constraint
  timeline at the engine's four-segment maximum.

Generate the items with [Tests/GameTestItems](../GameTestItems/Program.cs):

```powershell
dotnet run -c Release --project Tests/GameTestItems -- `
  "$env:USERPROFILE\Documents\Trackmania\Items\02. Freebuff RE Items" `
  "$env:USERPROFILE\Documents\Trackmania\Items\BF2_ASSETS\Test_Items\Locked_In.Item.Gbx" `
  "$env:USERPROFILE\Documents\Trackmania\Items\BF2_ASSETS\Test_Items\Chained_Square_Loop.Item.Gbx"
```

## The four-segment limit

Kinematic timelines hold at most four segments in the engine; `docs/item-gbx-chunks.md`
§16 has the measurements. Nothing above four segments is written into `Items/`: five is
refused by the native loader, and eight or more end the process when the file is loaded
through the item pipeline. Passing an optional fourth argument to `GameTestItems` writes
those counts to a probe folder outside `Items/` instead, and `RunSegmentLimitProbe` in
this plugin reloads them to re-measure the boundary. That switch is off by default and is
expected to end the process when it is on.

The tool writes each file through `ItemExportValidator` and then reloads it to
check the edit survived, so a bad export fails before the game ever sees it.

## Run it

1. Enable **Developer** signature mode in Openplanet.
2. Copy the complete `TMItemStudioSmokeTest` folder to
   `%USERPROFILE%\OpenplanetNext\Plugins\`.
3. In **Openplanet → Plugin Manager**, enable the local
   `TM Item Studio export smoke test` plugin, then restart Trackmania.
4. Inspect the results:

   ```powershell
   Select-String "$env:USERPROFILE\OpenplanetNext\Openplanet.log" -Pattern '\[TMIS-SMOKE\]'
   ```

`INFO` lines are diagnostics (for example which of the three placement slots the
engine populated); `PASS`/`FAIL` lines are the checks, ending with a
`pass=<n> fail=<n>` summary.

This is a native load and property test. Trackmania exposes no supported public
Openplanet API for selecting an inventory entry and committing an item
placement; asserting that final UI action would require version-fragile
private-memory patches or screen-coordinate automation, so this test does not
falsely claim placement coverage.

## Native pivot save

`TMItemStudioNativeBridge` is the separate native-write integration for
pivot/placement changes. It requires the Editor++ DEV plug-in and saves via
Trackmania rather than GBX.NET. See its README for the installation and
safety constraints.
