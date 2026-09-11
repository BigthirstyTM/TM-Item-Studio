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

## Regression checks

```bash
dotnet run --project Tests/VariantExport
```

The checks construct synthetic items and parse, save and reparse them with the
bundled GBX.NET assembly. They cover variant metadata, non-prefab and unresolved
entities, empty/single lists, combination, and source restoration after failed output.
No game assets are needed. These checks do not establish in-game compatibility.

## Requirements

- .NET 8 SDK with the Blazor WebAssembly workload.
- A modern browser with WebAssembly support.

## Run locally

```bash
dotnet restore
dotnet run
```

The development server uses the URL configured in `Properties/launchSettings.json`.

## Notes

The project includes a compatibility-patched `GBX.NET` assembly. Editor++
multi-variant items can contain valid `0xFFFFFFFF` null node references inside
surface data. The upstream parser currently treats one of these references as
an unknown class ID, so the local assembly preserves the official API and
handles that sentinel as a null reference.
