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
tested in a fresh Trackmania map editor session. For pivot changes on an
unverified source, use the **Native Trackmania pivot export** request with the
included Openplanet bridge; it delegates the write and inventory registration
to Editor++ and Trackmania. See
`Tests/Openplanet/TMItemStudioNativeBridge/README.md`.

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
- A modern browser with WebAssembly support.

## Run locally

```bash
dotnet restore
dotnet run
```

The development server uses the URL configured in `Properties/launchSettings.json`.

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

Run `npm --prefix Tests/Browser run test:kinematic-preservation` for the real
SnowCar upload → variant switching → explicit edit → export → reopen regression.
See [the test contract](Tests/Browser/MotionArchive/README.md) and the
[additional approved fixtures](Tests/Browser/Fixtures/Approved/additional-items.md).

The project includes a compatibility-patched `GBX.NET` assembly. Editor++
multi-variant items can contain valid `0xFFFFFFFF` null node references inside
surface data. The upstream parser currently treats one of these references as
an unknown class ID, so the local assembly preserves the official API and
handles that sentinel as a null reference.
