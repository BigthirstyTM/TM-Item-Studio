# GBX.NET updates and CLI

Item Studio runs the patched `GBX.NET.dll` in `lib/`. The current build is
upstream `v2.5.0` for `net8.0`, with the compatibility changes in
`patches/gbx-net-null-class-id.patch`, `patches/gbx-net-skin-weights.patch`,
and `patches/gbx-net-crystal-deformation-layer.patch`.
The null-reference patch is still required because the
upstream reader only treats `0xFFFFFFFF` as a null node reference when it is in
an encapsulated stream; Editor++ item files can contain the same sentinel in
an indexed node reference.

Run the updater from the repository root when a new stable GBX.NET release is
available:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File eng/Update-GbxNet.ps1
```

The script selects the newest stable package version from NuGet, checks out the
matching upstream tag, verifies and applies the compatibility patch, builds the
patched `net8.0` assembly, and refreshes `lib/GBX.NET.version.json`. Pass
`-Version 2.4.5` to reproduce a known build. It stops when the patch no longer
applies, so an upstream change cannot silently remove the Editor++ compatibility
behavior. The skin-weights patch preserves Float3 blend weights in vertex
streams. Both patch hashes are recorded in the manifest. Run
`Tests/ParserSentinels`, `Tests/ParserSurfaces`, `Tests/ItemScene`,
`Tests/ItemEdits`, and `Tests/VariantExport` after updates.

The scheduled workflow `.github/workflows/update-gbx-net.yml` runs this process
weekly and on demand. It opens a pull request containing the new DLL and
manifest only after those three suites and the Studio build succeed. Native
compatibility still requires the relevant browser and game checks before merge.

## GBX.NET 2.5 migration status

GBX.NET 2.5.0 was migrated into Item Studio on 30 October 2026. The runtime
assembly and all project references now use 2.5-compatible `NodeRef<T>`,
`NodeRefList<T>`, and `References` APIs. The migration covers scene traversal,
geometry/material inspection, item edits, flag conversion, motion templates,
and variant export. `TmEssentials` was raised to 2.8.0, matching the 2.5
runtime dependency.

The updater enables Git long paths, applies the Float3 blend-weight and Mesh
Modeler deformation compatibility contracts by semantic anchors, and patches
the indexed `0xFFFFFFFF` null class-id sentinel in the 2.5 `NodeRef` reader.
It fails closed when any expected upstream anchor is absent. The checked-in
baseline is recorded in `lib/GBX.NET.version.json` with the upstream commit and
all patch hashes.

The 2.5 regression gate is: the Studio build, parser sentinel and 23-fixture
surface round trips, 27 ItemScene checks, 91 ItemEdits checks, 31 ItemMotion
checks, 38 VariantExport checks, plus ItemIcons and ItemMaterials. These checks
exercise parse/save/reparse behavior, external reference metadata, collision
and material inventories, motion graph generation, and failed-write byte
preservation.

## What the GBX.NET CLI provides

`GBX.NET.Tool.CLI` (currently 0.5.3) is a library for building a project-owned
command-line tool; it is not a standalone item-editor executable. A tool built
on it can accept files, directories, URLs, or redirected stdin, parse GBX data,
load JSON/YAML configuration, produce or mutate objects, write results to an
output directory, and optionally check its own GitHub releases for updates.
The framework uses Spectre.Console and initializes GBX.NET with LZO support.

That host belongs in maintenance and batch workflows, not in the Blazor browser
runtime: the browser cannot launch a local process, and pulling the console
framework into the WASM payload would add code without improving item editing.
The useful application for Item Studio is a separate compatibility/reporting
CLI in the future (bulk parse, round-trip, chunk diagnostics, and export
verification). The current updater and parser tests already cover the shared
need to keep the parser current, so no CLI dependency is added to the product
bundle.
