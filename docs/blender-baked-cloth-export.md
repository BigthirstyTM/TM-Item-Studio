# Blender baked-cloth exporter

Updated: 26 October 2026.

Item Studio now bundles `trackmania_baked_cloth.zip` in the **Waving flag
converter** panel. The button downloads the same archive that is embedded in
the published WebAssembly application; it does not depend on a local checkout
or a web server path.

The add-on is an authoring bridge, not a GBX writer. Install the ZIP in Blender
through **Edit > Preferences > Add-ons > Install**, enable **Trackmania Baked
Cloth Exporter**, select a mesh, and open the Object properties panel. The
exporter samples the evaluated object over the configured range (86 samples by
default), expands mesh loops to preserve UV seams and split normals, and
refuses a sequence when loop count, triangle order, source-vertex mapping or
UV0 changes between samples. An optional vertex group pins the attachment edge
to its rest-frame centroid.

The output is a deterministic `.tmcloth.json` interchange file and an
optional ZIP containing that JSON and the English README. The JSON stores one
shared index/UV stream plus positions and normals for each sampled frame. It is
then consumed by Item Studio's **Import Blender baked cloth sequence** control.
Item Studio matches the rest-frame positions, UV0 and triangle order against the
selected visual, collapses loop-expanded rows back to the selected source
vertices, copies the period and anchor, and writes the native 86-frame
`VertexTween` graph to all five supported LOD visuals. The downloaded result is
a flag package containing the `.Item.Gbx` and the audited material dependencies.

The first and last samples must describe the same mesh for a seamless
Trackmania loop. The importer rejects a non-closed sequence rather than silently
changing the authored animation. Blender's mesh must also use the same triangle
order and winding as the Item Studio visual; a different triangulation is a
topology mismatch and must be corrected in Blender before export.

The add-on does not run a Trackmania simulation, write game files, or require a
network connection. A Blender-side cloth simulation must be baked before the
export. Topology-changing effects such as tearing or remeshing are rejected by
the sequence validator. The importer and package writer perform deterministic
offline serialization checks. A fresh native Trackmania editor session has
already loaded the generated `.Item.Gbx` from the user Items folder, placed it,
and captured a changing cloth silhouette. That proves the item-folder/session
route. Map embedding and recipient independence remain separate checks because
the native editor save path can reject custom dependency embedding.

## Validation

The bundled archive was inspected for exactly two entries:

```text
trackmania_baked_cloth/__init__.py
trackmania_baked_cloth/README.md
```

The add-on was exercised in Blender 5.1.1 in background mode using a deforming
triangle-grid mesh with a UV layer and an anchor vertex group. The test confirms
that 86 samples export, the source frame is restored, the topology metadata is
marked checked, and the optional ZIP contains the JSON and README. The
`Tests/BakedClothImport` test then imports that ZIP against the matching Item
Studio fixture and verifies the serialized five-LOD native item, frame table,
closed endpoint, period, normals, UVs and package hashes. The browser workflow
also covers the real published-app file upload and download path.


## Fresh-restart native result (26 October 2026)

The generated `BlenderGridImported_WavingFlag.Item.Gbx` was tested after a
clean Trackmania process restart in a disposable Stadium editor map. The game
found and placed the item, `GetItems` reported `isFlying=true`, and five native
1280x720 captures showed different hashes and changing cloth silhouettes. The
complete four-file sibling set is required for placement in this environment:

```text
BlenderGridImported_WavingFlag.Item.Gbx
TMIS_MatRemap_Placeholder.Material.Gbx
TMIS_MatRemap.GameSkin.Gbx
TMIS_MatRemap.TerrainModifier.Gbx
```

The Item alone, and the Item with only the material and terrain dependencies,
were indexable after restart but were rejected by the native placement path as
`item not found`. Treat the four files as one user-item package.

A native save of a map containing the generated item retained anchors but did
not produce a usable embedded item ZIP. The package-aware repair step therefore
refuses that save, and map portability to another player remains unproven.
The native validation receipt and contact sheet are stored in
`artifacts/blender-addon-validation-20261026`.
