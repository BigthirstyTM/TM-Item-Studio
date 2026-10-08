# Macroblock instancing tests

Run from the repository root:

```sh
DOTNET_ROLL_FORWARD=Major dotnet run --project Tests/MacroblockExport
```

This standalone console suite links the production `Models/ItemMacroblock*.cs`,
`Models/ItemMultiInstanceExport.cs` (plus `Models/ItemExportValidator*.cs` for ident
normalization) and uses the repository's bundled GBX.NET DLL. Its fixtures are
`Test Exported items/CustomItem_Static.Item.Gbx` and `CustomItem_Kinematic.Item.Gbx`.

What the suite proves:

- One item plus N placements exports as a `CGameCtnMacroBlockInfo` (class id `0x0310D000`)
  and reparses with the same parser used for import.
- Every spawn keeps the shared item ident, its absolute position, yaw and scale through a
  save/reparse round trip, at object-spawn version 14.
- Instancing stays cheap: the file grows by roughly 15 bytes per copy and never approaches
  the size of the item itself, because no geometry, material, icon or physics node is copied.
- A reparsed macroblock can be saved again without losing spawns, name or ident.
- Export refuses an item without an `Ident.Id` and refuses an empty instance list.
- Export re-reads its own bytes and refuses to return data that cannot be reopened.

Multi-instance item export (`ItemMultiInstance`), the embedded alternative to a macroblock:

- A macroblock cannot embed an item: its `ObjectSpawn` stores `ident ItemModel` only. A prefab
  entry stores a node reference plus rotation and position, and GBX.NET writes a repeated node
  once, so the export builds a root `CPlugPrefab` with one entry per copy that all point at the
  same model node — one mesh, N transforms.
- Measured on the static fixture: the 35 076 byte item becomes 34 911 bytes with one copy and
  35 052 bytes with twenty, about 7 bytes per extra copy.
- The kinematic fixture (a prefab with two source entries) keeps both entries per copy and all
  twelve exported entries share the model nodes of their source entry (`ReferenceEquals`), while
  every copy carries its own placed yaw.
- Copies are placed relative to the root: entry offsets between copies equal the placed offsets.
- The export works on its own snapshot of the document and the test asserts byte equality of the
  caller's document before and after, so editing state is never touched.
- The exported id is suffixed (`<id>_x20`) so the file does not shadow the original item, copies
  of another item are skipped with a reported warning, and a dropped scale is reported too.

## Written format assumptions

The macroblock writer is exercised only against GBX.NET itself; it has not been validated
inside Trackmania. These choices are constants on `ItemMacroblock` so they can be adjusted
after an in-game check:

- Object spawns are written with `Version = 14` into chunk `0x0310D00E` at chunk version 2
  (the TM2020 annotation in the format description).
- Positions are meters. `AbsolutePositionInMap` carries the exact value; `BlockCoord` is the
  same position divided by a 32 m block unit as a coarse fallback.
- Rotation is entered in degrees and stored as radians in `PitchYawRoll`.
- `PivotPosition` mirrors the spawn origin, and `Scale` is per instance.

Instancing semantics: a macroblock stores one spawn per copy, each referencing the item by
its ident. The referenced item must exist under that ident in Trackmania's inventory for the
copies to resolve; the macroblock itself carries no mesh.
