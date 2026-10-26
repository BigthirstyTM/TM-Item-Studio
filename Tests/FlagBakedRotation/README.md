# Offline combined cloth-rotation and waving fixture

This isolated executable authors one embedded research map from the exact proven v6 Long250 Item and the original near-native embedded editor-map template. It never installs, stages or loads files in Trackmania and never touches a runtime process.

Run from the repository:

```powershell
dotnet build Tests/FlagBakedRotation --nologo
dotnet run --project Tests/FlagBakedRotation -- --generate artifacts/flag-selfcontained-20261022/<new-directory>
```

An existing output path is refused. Source hashes are pinned, and original source bytes are checked again before output is created. Five complete 86-frame cloth visuals receive a 360-degree rigid yaw conjugated through the unchanged entity rest transform. All source fold positions remain recoverable by the inverse rigid transform. The last frame uses the first frame's normals to close an existing source lighting seam. Every LOD has conservative bounds over all its transformed frames.

The static pole remains byte-exact. Materials, index buffers, frame tables, UVs, graph structure and dependencies remain unchanged. The four ordinary non-kinematic Dyna timing fields request a common 12-second, zero-phase loop. No LocAnim, kinematic constraint or moving collision is introduced.

The manifest and English findings distinguish offline evidence from native rendering. The `.Map.Gbx.manifest.json` sidecar follows the existing `plugin_free_embedded` package schema. The genuine native `Flag16m` anchor is first and is not embedded.

This is a control for combined motion within one cloth mesh, not whole-assembly rotation or an independently steerable aircraft. A fresh-process in-game test is required for motion, timing, loop smoothness, attachment, lighting and culling.
