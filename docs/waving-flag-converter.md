# Waving flag converter and embedded-map export

Updated: 22 October 2026. Technical handoff for TM Item Studio and XertroV.

## Delivered behavior and evidence boundary

The **Flag item conversion** tab now contains a waving-cloth exporter as well
as the older classification/placement converter. These are different operations:
the new exporter writes actual vertex animation and its material dependencies;
the older converter changes metadata only.

Three presets reproduce the exact previously tested **Swallowtail**, **Long250**
and **Round** Item bytes. The corresponding dependency-complete map was observed
with visible custom cloth deformation in a cold ordinary Local race, without
Openplanet or matching loose custom assets. A genuine native Stadium `Flag16m`
anchor remained in that map. See the
[English runtime evidence report](custom-flag-embedded-no-openplanet-2026-10-22.md).

**SourceMesh** creates new animation from supported static item geometry. Its
serialization and geometry are checked offline. The Blender baked-sequence
route has now also been exercised natively: after a fresh Trackmania process,
the generated `BlenderGridImported_WavingFlag.Item.Gbx` was discovered in the
user Items folder, placed, rendered and observed to change silhouette across
successive native screenshots. This is an item-folder/session proof for the
new sequence, not a recipient or map-embedding proof. The browser packaging
workflow remains an offline implementation of the measured repair route.

The native editor save path still reports `Error while saving items into the map
file` for this new package, and the raw embedded-map control did not expose the
custom anchors through the game editor. Those results keep map embedding and
recipient independence explicitly unverified; do not call the new item a
portable single-file map asset yet.

The portable unit for players is a **complete embedded map with a native flag**.
The authoring export is a ZIP with **one Item plus three sibling dependencies**.
It is not a single independently initialized `.Item.Gbx`.

## Authoring workflow

1. Open an item in Item Studio and expand **Flag item conversion**.
2. Under **Waving flag converter**, choose a tested shape or **Convert this
   item's mesh (experimental)**. For SourceMesh, select the fixed X/Z edge,
   amplitude in metres, wavelength in metres and loop period in seconds.
   A conventional vertical cloth should lie in the XY or ZY plane; the wave
   displaces the other horizontal axis. A horizontal source is not reoriented.
3. Download **Export waving flag package (.zip)**. Extract its entire folder
   under `Documents\Trackmania\Items`. Keep the four GBX files together.
4. Place a genuine native Stadium 16 m flag in the map, then the custom flags.
   Keeping the native flag first reproduces the conservative proven setup.
5. Save the map in Trackmania. Native saving may omit the dependencies or
   rewrite their references; that intermediate map is not the sharing artifact.
6. In **Package a saved map**, choose the saved `.Map.Gbx` and the exported ZIP.
   The ZIP from the current conversion is already available to this step;
   uploading it again is optional. Download the new embedded map.
7. Test that exact downloaded file after a full game restart. Test without the
   loose authoring files and without a flag helper. The previously proven maps
   are supplied separately as controls.

Packaging produces a new download and does not overwrite the source map. A
later native editor save can undo the repaired dependency closure; repeat the
packaging step after saving an edited map. For multiple different custom flag
packages, apply each package to the preceding downloaded map, keeping the final
result as the artifact to test and share.

## Four-file material graph

`Models/ItemWavingFlagExport.cs` embeds six audited resources from
`Resources/Flags`: the three proven Item shapes and three shared dependencies.
The complete resource bytes are hash-checked before use.

```text
<name>_WavingFlag.Item.Gbx
  MaterialModifierFile -> TMIS_MatRemap.TerrainModifier.Gbx
  cloth mesh material -> TMIS_MatRemap_Placeholder.Material.Gbx

TMIS_MatRemap.TerrainModifier.Gbx
  -> TMIS_MatRemap.GameSkin.Gbx

TMIS_MatRemap.GameSkin.Gbx
  source FID -> TMIS_MatRemap_Placeholder.Material.Gbx
  replacement -> native Stadium\Media\Material\ItemFlag.Material.Gbx
```

The placeholder is an ordinary material containing a non-null inline
`CPlugShaderApply` with empty passes. It does not bundle HLSL or an external
shader. The GameSkin source is an actual file/FID reference (`UseFile=true`),
not a loaded inline material node. The Item's placeholder and modifier use node
references (`UseFile=false`); their files are siblings, with ancestor level zero.
Changing these reference modes is not equivalent to retaining the graph.

The material remap lets the native item construction path resolve `ItemFlag`
early and prepare the effective VertexTween mesh. GPU research distinguished
the shader's structured vertex input from the ordinary IA buffer provided by
failing custom paths. The successful embedded cold runs were not GPU-instrumented;
they establish visible behavior and isolation, not the precise renderbuffer write.

The native flag anchor is a measured requirement of the current cold-start
route. The complete custom-only map loaded poles without cloth. Externalizing
only the mesh or prefab did not remove that requirement. No renderer patch,
plugin companion, custom shader installation or game pack modification is
part of this export.

## Static mesh to animated cloth

## Viewport cloth playback

Item Studio now recognises the audited native baked-cloth layout directly in
the 3D preview. A visual with a valid `SubVisuals` table (currently the
measured 86-frame flag layout) is sent to the browser as frame-aware geometry.
The Three.js viewer interpolates the local positions and normals between
successive frames using the authored cloth period. The existing toolbar
play/pause button controls the shared preview clock, and **Enable/Disable
cloth preview** in the Flag item panel controls whether baked cloth frames are
applied. The source GBX is never rewritten by this preview path.

This is a visual playback feature: it does not claim to emulate the game's
GPU shader, physics or collision. Unsupported animation layouts continue to
show their decoded base geometry and remain explicitly diagnosed.

The converter accepts a single concrete variant with supported inline static
triangle geometry: `CPlugSolid2Model`, a static-object wrapper, supported common
item wrappers and rigid prefab transforms. Transforms are flattened into item
coordinates. All accepted render triangles become one cloth mesh. Source UV
values and triangle indices are retained; the native flag material replaces
the source materials. Source collision, physics, placement metadata and rigid
submeshes are not transferred.

The bounded input budget is 2,048 vertices and 8,192 triangles. External references,
moving/constraint nodes, existing animated frames, skin/morph data, vertex streams,
tangent/split layouts, ambiguous LODs and invalid or degenerate triangles are
refused. No implicit filesystem resolver is invoked. These limits describe the
implemented subset; the converter does not accept every Trackmania item.

For a minimum-X attachment edge, let `d = x - minX`, `L = maxX - minX`,
`u = d/L`, and `theta = 2*pi*d/wavelength - 2*pi*frame/85`. The displacement is:

```text
x' = x
y' = y
z' = z + amplitude * u^2 * sin(theta)
```

Maximum-X reverses the distance direction. A Z attachment exchanges the X and
Z axes, so the wave moves along X. The selected edge stays fixed. This is a
deterministic geometric wave, not physical wind simulation. Normals are updated
with the inverse-transpose deformation Jacobian and normalized; missing normals
are reconstructed from triangle areas.

Each of the five output LOD visuals contains 86 frames and a corresponding
`SubVisuals` entry `(frame * verticesPerFrame, 0, indexCount)`. Frames 0 through
84 are generated; frame 85 is copied from frame 0 for bitwise loop closure.
Every LOD uses the same source topology: distant geometry is not simplified.
UV channels are repeated for each frame, and bounds include every frame.
The pinned GBX.NET version stores its serialized inline vertex `Count`
independently from `Vertices`; both must be updated. This reflection setter
changes the file model, not the running game's memory.

The output retains the accepted ordinary non-kinematic Dyna cloth host. Its
rest transform becomes identity because source transforms were already flattened.
`PeriodSc` and `PeriodScMax` receive the requested period; both phase values
are zero. No location animation or moving collision shape is added. The static
pole from the preset is removed for SourceMesh; a pole present in the source
would become cloth together with every other accepted triangle.

After writing, the exporter reparses its output and checks frame tables,
geometry/UV equality, fixed-edge coordinates, loop endpoints, bounds, normals,
nondegenerate triangles, timing and the dependency-reference graph. The source
document is read by the backend and is not used as the output document.

## Package and map implementation

The authoring ZIP contains one root folder with the four GBX payloads,
`manifest.json` and English `README.md`. The manifest includes payload SHA-256
hashes, settings, geometry counts, reference modes and explicit evidence flags.
Presets keep their original Item bytes even when the downloaded filename changes;
their ordinary user-FID identity is supplied by the file path.

`Models/FlagPackaging` implements the saved-map step. It validates the package,
the genuine native flag anchor, the supported custom graph and matching embedded
Items. It restores the required dependency closure and normalizes matching Item
references to sibling paths. It refuses unknown/ambiguous layouts instead of
attempting a general GBX rewrite.

The repair edits the embedded ZIP through a bounded raw archive splice. It
preserves the map header, reference table, non-ZIP raw body, blocks, anchors,
validation data and unrelated archive payloads. The complete map node is not
normally deserialized and reserialized as a replacement. No native flag anchor
is fabricated. A successful offline repair is not itself proof of native rendering.
The optional **Download map packaging report (.json)** records input/output
hashes, matching graph information, restored dependencies and preservation checks.

## Rotating and waving cloth

The demonstrated rotating flag uses **combined baked vertex animation**. For
each original waving frame, positions and normals are rotated around the fixed
pole axis, then written back into the frame/LOD data. The static pole stays
separate. The resulting non-kinematic cloth graph retains the successful
material route and runs on its existing vertex-tween clock.

This combined movement was visibly repeated in a cold no-Openplanet editor
recording: 1,792 actual frames over 30 seconds, approximately 59.73 captured
frames per second. The combined cycle recurs every 12 seconds. Offline endpoint
derivatives differ, so a small loop kink is not excluded by that recording.

An earlier true kinematic conversion rotated the object but froze the cloth.
Consequently, independently controlled physical kinematics plus waving cloth
is **not solved**. The converter does not present baked rotation as a kinematic
export option. A moving aircraft with waving banners remains a separate,
paused research task. The handoff package contains the baked fixture source,
map, video and construction explanation for further work.

## Verification commands

Run from the repository root:

```powershell
dotnet run --project Tests/WavingFlagExport
dotnet run --project Tests/WavingFlagMapPackaging
dotnet run --project Tests/FlagConversion
dotnet build TM-Item-Studio.csproj
npm --prefix Tests/Browser run test:waving-flag-converter
```

The new focused browser runner publishes an isolated app and uses headless
Chromium. It does not drive the game or use the user's browser session.
The runtime report and handoff package supply separate native evidence for
the existing presets and baked rotating fixture.
