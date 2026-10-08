# Trackmania 2020 Item Studio

Trackmania 2020 Item Studio is a browser-based editor for `.Item.Gbx` files.
It provides an interactive 3D viewport and exposes item metadata, placement,
kinematics, lights, sockets, physics and visual information.

## Features

- Load one or more Trackmania item files.
- Detect and browse embedded multi-variant items.
- Switch variants in the 3D viewport.
- Inspect and edit placement pivots, sockets, lights and kinematic settings.
- Export edited items, including multi-variant items.
- Export visible geometry as OBJ.
- Duplicate the loaded item into macroblock instances and export them as `.Macroblock.Gbx`, or
  embed the copies into a single multi-instance `.Item.Gbx` whose mesh is stored once.

## Macroblock export (item instancing)

The **Macroblock & instances** tab duplicates the loaded item instead of copying its data.
Each copy stores a position (meters), a rotation (degrees) and a scale, and the export
writes one `ObjectSpawn` per copy that references the item by its `Ident.Id`. No mesh,
material, icon or physics node is duplicated: a macroblock with 24 copies stays under 1 KB
while the item itself is tens of kilobytes, about 15 bytes per copy.

Controls:

- **Add copy** places a copy next to the previous one, **Duplicate last copy** repeats it
  with the current spacing, and **Fill grid** lays out `Copies` over `Columns` on the ground
  plane, continuing below the copies already placed.
- The table edits each copy's X/Y/Z position, pitch/yaw/roll and scale inline. Every change is
  drawn immediately in the 3D viewport: a copy is one more scene part that reuses the item's
  already-uploaded geometry and only carries a placement transform, so copies are visible
  without duplicating a single vertex buffer.
- **Export .Macroblock.Gbx** saves through `ItemMacroblock.Export`, which reparses its own
  bytes with the import parser and refuses to download anything that cannot be reopened.

A `.Macroblock.Gbx` can also be **loaded**: it is detected by its class id `0x0310D000` and
fills the instance table instead of being parsed as an item. When a non-item file still
reaches the item parser (which answers with `Specified cast is not valid.`), the loader
retries it as a macroblock before reporting the class id it actually found. The copies show
up in the viewport as soon as the referenced item is open.

### Multi-instance item export

**Export multi-instance .Item.Gbx** writes the same copies into one self-contained item instead
of into a macroblock. A macroblock cannot embed an item — its spawn stores an ident reference —
but a `CPlugPrefab` entry stores a node reference plus a rotation and a position, and GBX.NET
writes a repeated node only once. The export therefore builds a root prefab with one entry per
copy, every entry pointing at the same embedded mesh: one mesh, N transforms.

- Measured on the bundled fixture: the 35 076 byte item becomes 34 911 bytes with one copy and
  35 052 bytes with twenty — about 7 bytes per extra copy instead of a second copy of the mesh.
- Every copy keeps its own position and rotation relative to the root, so the copies are placed
  individually inside the item.
- The export gets its own id (`<id>_x20`) because inventory files are named after the item id;
  the original item stays untouched and usable.
- Prefab entries carry no scale: copies placed with a scale are exported at scale 1 and reported
  in the status line. Copies that reference a different item are skipped and reported, because
  one file can only embed one mesh.
- The export runs on a snapshot of the document, re-reads its own bytes with the import parser,
  and refuses to download anything that cannot be reopened.

Format notes and their current assumptions (constants on `ItemMacroblock`, validated against
GBX.NET rather than the game so far): object spawns are written at version 14 in chunk
`0x0310D00E` at chunk version 2, positions are meters with `AbsolutePositionInMap` as the
exact value plus a 32 m block-unit fallback in `BlockCoord`, degrees are stored as radians in
`PitchYawRoll`, and `PivotPosition` mirrors the spawn origin. Trackmania resolves each copy
through the item ident, so the item must exist under that ident in the inventory; the
macroblock never embeds its mesh. `Tests/MacroblockExport` runs the round-trip suite.

## Local material preview

The **Materials & LOD** panel lists each authored material slot with its native
game-material name and link. To preview vanilla textures, download Zai's
current `DefaultTextures.zip`, extract it locally, then choose the extracted
folder in that panel. The folder is selected through the browser and remains
local: Studio indexes filenames only and reads a texture file only when it is
needed for the currently loaded item.

The current package is about 918 MB, so Studio intentionally does not import
the zip into browser memory. A Chromium-based browser is required for the
local folder picker. Texture matching is deterministic and based on the
native material link (for example `Stadium\Media\Material\RoadTech`), or on
the material name when a slot has no link: Studio lower-cases the final path
segment and strips a trailing `_asset`/`_asset.N` suffix. It loads a complete
Trackmania PBR set when present: `_D` as sRGB base color, `_N` as normal, `_R`
with red=roughness and green=metallic, and `_I` as self-illumination. A plain
filename remains a base-color fallback for nonstandard material folders. A
missing base-color token, a filename set that several distinct materials
share, or an unreadable file (including malformed DDS) leaves the neutral
preview in place. DDS textures are decoded through the bundled three.js
revision. This preview never changes GBX material data, UVs, collision, or
the exported item.

## Exporting files and variants

Selecting a variant changes the preview. **Export selected file** saves the complete
original item, including its other variants, placement tags and manual-cycle flags.
It also preserves variants whose external entity is not available for preview.

When multiple files are loaded, **Combine loaded files as variants** creates a new
variant list. The first loaded file supplies item-level metadata, icon and placement;
the preview selection does not change that choice. Non-prefab entities and existing
variant tags/flags are retained. Files with external references cannot currently be
combined, because their dependency paths would need to be resolved against a shared
output location; they can still be exported separately. Empty variant lists and
legacy items without an entity model can also be exported separately.

### Trackmania-ready exports

The studio rejects an export with no entity model. `Ident.Id` and
`Ident.Author` are preserved and editable, but are not sufficient evidence of
inventory compatibility: native Trackmania items can legitimately leave them
empty. `ArchetypeRef` is not the local inventory folder: local discovery is
based on the file location under `Documents\Trackmania\Items`.

Collection IDs retain their loaded encoding. In particular, Trackmania 2020's
numeric ID `26` is displayed as `Stadium2020`, but must remain numeric when it
is not edited; writing that display name as a string prevents native collection
resolution. Enter `26` to deliberately set the TM2020 numeric collection.

This collection repair is necessary but not sufficient for every item. The
studio reports when an imported archive is not byte-exact after a no-edit
GBX.NET save. Re-encoding is not automatically invalid: the writer has been
verified in a fresh Trackmania session with a Blendermania pivot item, where
the output was inventory-visible and placeable. Other source families can
still preload yet be omitted from the normal map editor inventory.

For a re-encoded source, Studio requires an explicit confirmation before it
downloads a standalone export. Confirm only when the source's output will be
tested in a fresh Trackmania map editor session. The Openplanet bridge remains
under `Tests` as a regression utility; it is not part of the Studio workflow.

These checks protect the metadata that the studio can verify. They do not turn
an arbitrary legacy or synthetic GBX archive into a native-placeable item.
Use a Trackmania-native-valid source item when creating a placeable export.

## Writer compatibility probe

`Tests/WriterCompatibility` runs the same parse/save path with a chosen
GBX.NET assembly against a user-supplied item, optionally changing only an
existing pivot X coordinate. It is intended for controlled Trackmania A/B
tests and does not include user item assets. See
`Tests/WriterCompatibility/README.md`.

## Regression checks

```bash
dotnet run --project Tests/VariantExport
```

The checks construct synthetic items and parse, save and reparse them with the
bundled GBX.NET assembly. They cover variant metadata, non-prefab and unresolved
entities, empty/single lists, combination, and source restoration after failed output.
No game assets are needed. These checks do not establish in-game compatibility.

## Native Trackmania smoke test

`Tests/Openplanet/ItemExportSmokeTest.as` runs the native Trackmania FID loader
against the static and kinematic files in `Documents\Trackmania\Items` and
records machine-readable PASS/FAIL results in Openplanet's log. See
`Tests/Openplanet/README.md` for installation and its deliberately bounded
coverage.

## Requirements

- .NET 8 SDK with the Blazor WebAssembly workload.
- A Chromium-based browser with WebAssembly support for local texture-folder
  selection (other modern browsers can still use the neutral material preview).

## Run locally

```bash
dotnet restore
dotnet run
```

The development server uses the URL configured in `Properties/launchSettings.json`.

## GitHub Pages deployment

This repository includes `.github/workflows/deploy-pages.yml` to publish the
Blazor build to GitHub Pages.

- It deploys automatically on pushes to `main`.
- It can also be run manually from **Actions** (`workflow_dispatch`).
- The workflow rewrites `<base href="/">` to
  `<base href="/TM-Item-Studio/">` during deployment so the app loads under:
  `https://bigthirstytm.github.io/TM-Item-Studio/`.

## Viewer regression checks

With the app running locally:

```bash
npm ci --prefix Tests/Browser
cd Tests/Browser
npx playwright install chromium
npm test
```

Set `STUDIO_BASE_URL` when using a URL other than `http://127.0.0.1:5183/`.
The checks load two distinct synthetic items, exercise a failed load and recovery,
check recursive gizmo cleanup, and navigate away to verify renderer/listener teardown.
No Trackmania installation or third-party item files are required.

For the upload → export → reopen collection regression, run
`npm --prefix Tests/Browser run test:collection-export`. This publishes and serves
the current checkout automatically; no development server is needed. See
[collection regression details](Tests/Browser/CollectionArchive/README.md).

## Notes

Imported kinematic constraints retain their authored axes, scalar ranges, easing,
reversed keys and separate translation/rotation timelines. The kinematics panel
edits those stored fields directly; it does not replace them with a preset.
Unsupported preview modes (including `IsDuration=true`) are reported and preserved,
with no generic fallback animation. This is not a native-playback parity claim.

For a flat, locally stored prefab with a supported existing kinematic body and
constraint, the kinematics panel can experimentally append one additional
body/constraint template. The new body deliberately shares the selected template's
mesh and collision shapes and retains its world/parent binding; it does not convert
static items, create collision geometry, or author a waypoint chain. Exported files
must still be verified after a fresh Trackmania start: inventory discovery, placement,
and in-map playback are all required checks.

The first `World → driver → visible body` probe established that Trackmania crashes
when loading a model-less, collision-less dyna driver. Studio therefore does not offer
that authoring action. If such a probe is loaded, its constraint is highlighted in red
as **INVISIBLE DRIVER — native-unsafe**; do not export it for the game. The separate
blue **VISIBLE PATH PROXY** action uses a complete existing dyna model instead. It
creates a visible duplicate to test `parent → proxy → original body` safely before an
unproven hiding strategy is attempted.

Run `npm --prefix Tests/Browser run test:kinematic-preservation` for the real
SnowCar upload → variant switching → explicit edit → export → reopen regression.
See [the test contract](Tests/Browser/MotionArchive/README.md) and the
[additional approved fixtures](Tests/Browser/Fixtures/Approved/additional-items.md).

The project includes a compatibility-patched `GBX.NET` assembly. Editor++
multi-variant items can contain valid `0xFFFFFFFF` null node references inside
surface data. The upstream parser currently treats one of these references as
an unknown class ID, so the local assembly preserves the official API and
handles that sentinel as a null reference.
