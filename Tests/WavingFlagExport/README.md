# Waving flag export checks

Run from the repository root:

```powershell
dotnet run --project Tests/WavingFlagExport
```

The project links the production exporter and embeds the same six pinned flag
resources as the app. It uses the repository's patched GBX.NET assembly and
restores the other dependencies through NuGet. No game files, running game,
Openplanet or ignored research artifacts are required.

The 32 checks cover exact preset payloads and distinct default export names,
four attachment directions, 86-frame/five-LOD serialization, bitwise fixed
attachment coordinates, loop closure, UV/topology preservation, normals,
all-frame bounds, rigid transforms, source isolation, deterministic packages,
input budgets and malformed/unsupported graph refusal.

The test creates an inline static 12-vertex, 12-triangle, two-UV-channel source
and sample ZIPs under `bin/Debug/net8.0/fixtures` (or the corresponding build
configuration). `InlineStaticClothSource.Item.Gbx` is an input for the headless
browser workflow. Generated SourceMesh exports are offline controls and have
not been tested in Trackmania.

See [the converter guide](../../docs/waving-flag-converter.md) for the file graph,
deformation equation, limits and the separate native evidence boundary.
