# GBX.NET updates and CLI

Item Studio runs the patched `GBX.NET.dll` in `lib/`. The current build is
upstream `v2.4.5` for `net8.0`, with the compatibility changes in
`patches/gbx-net-null-class-id.patch` and `patches/gbx-net-skin-weights.patch`.
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
`Tests/ParserSentinels`, `Tests/ParserSurfaces`, and `Tests/ItemScene` after updates.

The scheduled workflow `.github/workflows/update-gbx-net.yml` runs this process
weekly and on demand. It opens a pull request containing the new DLL and
manifest only after those three suites and the Studio build succeed. Native
compatibility still requires the relevant browser and game checks before merge.

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
