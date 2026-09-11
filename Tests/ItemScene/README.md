# Typed scene tests

Run from the repository root:

```sh
DOTNET_ROLL_FORWARD=Major dotnet run --project Tests/ItemScene -m:2
```

This standalone console suite links the production `Models/ItemScene*.cs` files and uses the repository's bundled GBX.NET DLL. Its fixtures are synthetic and need no game assets. In a concurrent local workflow, wrap builds and runs in the coordinator's shared `flock` build lock.

`ItemScene.Build(selectedRoot, documentOrdinal, variantOrdinal)` returns a preview snapshot and separate C# source handles. Only `result.Preview` should be sent to JavaScript. The result also ignores its Handles property during JSON serialization. Positions and normals are already transformed into item coordinates, before viewer framing. Transform arrays use System.Numerics row-major storage and row-vector composition (`local * parent`). Source quaternions retain X/Y/Z/W ordering.

Paths are scoped to the loaded document and selected original variant slot. The root is `doc:0/variant:none/root`; prefab entries append `/ent:originalIndex`. Property edges append explicit property names and indexed tables append segments such as `/visual:2`. Paths identify occurrences. Source IDs identify shared objects only within the returned snapshot. They must not be treated as stable IDs across builds, selection changes or reloads.

The suite checks nested rotations, repeated prefabs, original entry slots, path-local cycles, save/reparse topology and unchanged serialized bytes, independent attribute streams, CPU indexed attributes, normals/UV0/UV1, material/LOD mappings, invalid indices/values/transforms, collision ownership and generated status, external-resolution traps, selected variants, zero-valued lights and socket ownership.

Supported geometry is the bundled `CPlugVisualIndexedTriangles` with one unambiguous decoded position channel, optional normal/UV channels, and an index buffer. Both inherited CPU arrays and decoded vertex streams are supported separately. Separate streams supply attributes for the same vertices; they are not concatenated. Collision triangle meshes and compound transforms are exposed separately from visual geometry. Normals use inverse transpose, and invalid geometry or attribute channels produce diagnostics. Preview arrays are copies; source handles retain authored arrays for a separate validated editor.

The bundled public API does not expose vertex-stream counts/declarations/shared-model slots or skeleton socket arrays. Decoded stream geometry carries a `stream-layout-opaque` diagnostic; an empty/opaque stream is unsupported rather than absent. Duplicate attributes and mixed CPU/stream layouts are rejected. No private-field reflection is used. `CPlugVisualTriangles` is not a public type in this DLL and is not inferred from its CPU base class. Legacy/unknown visuals, analytic collision tessellation, skinning/morph/subvisual animation and trigger transforms remain unsupported.

`Solid.LightInsts[i].ModelIndex` selects `Solid.LightUserModels`; `SocketIndex` is retained but does not become a guessed light position. Those light instances are unsupported for positional preview and retain typed model/instance ownership for editing. A direct prefab light model has an explicit prefab-entry transform. Existing zero color, intensity and distance values remain zero.

Motion target resolution belongs to the motion module, using `Handles.Prefabs` and `Handles.Entries`. Scene traversal never interprets constraint entity indices. Editing, collision generation, OBJ export and viewport behavior are outside this module. These tests establish bundled-parser and scene-model behavior, not game compatibility.
