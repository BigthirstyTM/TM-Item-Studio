# Typed scene tests

Run from the repository root:

```sh
DOTNET_ROLL_FORWARD=Major dotnet run --project Tests/ItemScene -m:2
```

This standalone console suite links the production `Models/ItemScene*.cs` files and uses the repository's bundled GBX.NET DLL. Its fixtures are synthetic and need no game assets. In a concurrent local workflow, wrap builds and runs in the coordinator's shared `flock` build lock.

`ItemScene.Build(selectedRoot, documentOrdinal, variantOrdinal)` returns a preview snapshot and separate C# source handles. Only `result.Preview` should be sent to JavaScript. The result also ignores its Handles property during JSON serialization. Positions and normals are already transformed into item coordinates, before viewer framing. Transform arrays use System.Numerics row-major storage and row-vector composition (`local * parent`). Source quaternions retain X/Y/Z/W ordering.

Paths are scoped to the loaded document and selected original variant slot. The root is `doc:0/variant:none/root`; prefab entries append `/ent:originalIndex`. Property edges append explicit property names and indexed tables append segments such as `/visual:2`. Paths identify occurrences. Source IDs identify shared objects only within the returned snapshot. They must not be treated as stable IDs across builds, selection changes or reloads.

The optional fourth `Build` argument is an `ItemSceneGeometryCache` owned by the
loaded-document session. It keys buffers by actual parsed-node reference identity
and world transform, retaining fresh occurrence paths and handles on every build.
`GeometryId` is stable only within that cache's `Epoch`; it is not `SourceId`.
Call `Clear()` after any authored edit (including in-place array changes) and on
document replacement. Cached snapshot arrays are read-only to consumers, although
they remain separate from authored GBX arrays. The default uncached build still
returns independent mutable snapshots. Partial/invalid geometry is not pooled.
The cache regression covers distinct transforms, revisits, unrelated documents,
fresh paths and invalidation after editing a shared source in place.

The suite checks nested rotations, repeated prefabs, original entry slots, path-local cycles, save/reparse topology and unchanged serialized bytes, independent attribute streams, CPU indexed attributes, normals/UV0/UV1, material/LOD mappings, invalid indices/values/transforms, collision ownership and generated status, external-resolution traps, selected variants, zero-valued lights, socket ownership, light ownership classes and value persistence capability.

Supported geometry is the bundled `CPlugVisualIndexedTriangles` with one unambiguous decoded position channel, optional normal/UV channels, and an index buffer. Both inherited CPU arrays and decoded vertex streams are supported separately. Separate streams supply attributes for the same vertices; they are not concatenated. Collision triangle meshes and compound transforms are exposed separately from visual geometry. Normals use inverse transpose, and invalid geometry or attribute channels produce diagnostics. Preview arrays are copies; source handles retain authored arrays for a separate validated editor.

Ordinary `CPlugTree` graphs expose their Visual, Surface and Children, including the `CPlugSolid.Tree` wrapper. Optional tree Location is composed with the parent transform; paths append `/tree`, `/children:N` and `/visual`. Repeated shared trees retain distinct occurrence paths and the same source identity, with path-local cycle detection. External TreeFile, ShaderFile and FuncTreeFile slots never invoke resolving getters. Tree subclasses retain their supported base graph but report unsupported subclass LOD/animation semantics; shader/function/generator interpretation remains unsupported. Tree visibility/collision flags are retained as source metadata, not evaluated as a replacement for explicit geometry inventory.

Collision mesh versions 1/2/3/5 select CookedTriangles; versions 6/7 select Triangles. Missing required arrays or an incompatible/ambiguous alternate array are invalid, never silently substituted. Supported empty triangle arrays are absent geometry; invalid vertices/indices/transforms are diagnosed. All other mesh versions are unsupported even if public arrays are populated. Version 4 explicitly reads/writes no mesh payload in the bundled serializer, but that alone does not prove absent native collision, so it also remains unsupported. Tests save/reparse every supported version, version 4 and unknown versions before classifying them.

The bundled public API does not expose vertex-stream counts/declarations/shared-model slots or skeleton socket arrays. Decoded stream geometry carries a `stream-layout-opaque` diagnostic; an empty/opaque stream is unsupported rather than absent. Duplicate attributes and mixed CPU/stream layouts are rejected. No private-field reflection is used. `CPlugVisualTriangles` is not a public type in this DLL and is not inferred from its CPU base class. Legacy/unknown visuals, analytic collision tessellation, skinning/morph/subvisual animation and trigger transforms remain unsupported.

`Solid.LightInsts[i].ModelIndex` selects `Solid.LightUserModels`; `SocketIndex` is retained but does not become a guessed light position. Those light instances are unsupported for positional preview and retain typed model/instance ownership for editing. A direct prefab light model has an explicit prefab-entry transform. Existing zero color, intensity and distance values remain zero.

Every light in the preview carries an `Ownership` classification plus a `ValuesPersist` capability:

- `PrefabEntry` — the light is a direct prefab entry model; the entry transform owns its
  position and the owning entry path is reported in `EntityPath` and in a
  `light-owner-entry` (Present) diagnostic. A nonfinite or non-unit entry transform keeps
  the `PrefabEntry` classification but marks the light Invalid without the Present
  ownership diagnostic, matching the scene-transform branch.
- `SolidInstance` — the light is `LightUserModels[LightInst.ModelIndex]`. Position is
  explicitly uninferrable: the socket transform needs `CPlugSkel` socket records, which are
  serialized by chunk `090BA000` but kept private in the bundled GBX.NET 2.4.4 public API.
  `light-socket-transform` (Unsupported) states that exact blocker. `SocketIndex` is typed
  data, not an array index into positions.
- `SceneTransform` — no prefab entry owns the light; its position is the composed scene
  transform chain (`light-owner-scene`, Present).

Lights without any instance (`uninstanced-light`) and legacy `Solid.Lights` arrays
(`legacy-light`) stay diagnostic-only and never enter the editable light inventory.
`ValuesPersist` is true only when the node carries `CPlugLightUserModel` chunk `090F9000`,
the sole serializer of color/intensity/distance; a missing chunk emits
`light-values-not-persisted` (Unsupported) because in-memory value edits would not survive
a save. The save/reparse test proves both directions: values with the chunk round-trip
identically, and values without it fall back to defaults after reparse. Traversal leaves
serialized source bytes unchanged in both cases.


Motion target resolution belongs to the motion module, using `Handles.Prefabs` and `Handles.Entries`. Scene traversal never interprets constraint entity indices. Editing, collision generation, OBJ export and viewport behavior are outside this module. These tests establish bundled-parser and scene-model behavior, not game compatibility.
