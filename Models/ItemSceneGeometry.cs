using System.Numerics;
using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Plug;

namespace TM_Item_Studio.Models;

public static partial class ItemScene
{
    private sealed partial class Builder
    {
        /// <summary>
        /// Mesh Modeler items keep their authored mesh in CPlugCrystal under
        /// CGameCommonItemEntityModelEdition. A crystal face stores references into a
        /// shared position table, so it is converted to ordinary indexed triangle buffers
        /// for the read-only viewport preview. The source GBX graph is never replaced.
        /// </summary>
        private void Edition(CGameCommonItemEntityModelEdition edition, string path, Matrix4x4? world,
            ItemSceneEntryHandle? entry, int depth)
        {
            var crystal = edition.MeshCrystal;
            var crystalPath = path + "/meshCrystal";
            if (crystal is null)
            {
                Node(crystalPath, path, null, ItemSceneKind.Visual, ItemSceneState.Absent, Matrix4x4.Identity, world);
                Issue(crystalPath, "edition-mesh-missing", ItemSceneState.Absent,
                    "Mesh Modeler edition has no embedded crystal mesh.");
                return;
            }

            Node(crystalPath, path, crystal, ItemSceneKind.Visual,
                world.HasValue ? ItemSceneState.Present : ItemSceneState.Invalid,
                Matrix4x4.Identity, world);

            var layers = crystal.Layers ?? [];
            var geometryLayerIndex = 0;
            foreach (var layer in layers)
            {
                if (layer is not CPlugCrystal.GeometryLayer geometryLayer)
                {
                    Issue($"{crystalPath}/layer:{geometryLayerIndex}", "edition-layer",
                        ItemSceneState.Unsupported, "Mesh Modeler layer is not a decoded geometry layer.");
                    geometryLayerIndex++;
                    continue;
                }

                var layerPath = $"{crystalPath}/layer:{geometryLayerIndex}";
                geometryLayerIndex++;
                if (!geometryLayer.IsEnabled || !geometryLayer.IsVisible)
                    continue;
                if (geometryLayer.Crystal is null)
                {
                    Issue(layerPath, "edition-crystal-missing", ItemSceneState.Absent,
                        "Mesh Modeler geometry layer has no crystal payload.");
                    continue;
                }

                EmitCrystalLayer(geometryLayer.Crystal, layerPath, world, entry);
            }
        }

        private void EmitCrystalLayer(CPlugCrystal.Crystal crystal, string path, Matrix4x4? world,
            ItemSceneEntryHandle? entry)
        {
            var positions = crystal.Positions ?? [];
            var faces = crystal.Faces ?? [];
            if (positions.Length == 0 || faces.Length == 0)
            {
                Issue(path, "edition-empty-mesh", ItemSceneState.Absent,
                    "Mesh Modeler crystal contains no positions or faces.");
                return;
            }

            // Crystal materials are authored as object references. Use their position in the
            // face groups only as a stable preview slot; this avoids relying on a runtime
            // Solid2 material table that is absent from Mesh Modeler items.
            var groups = new List<(CPlugCrystal.Material? Material, List<CPlugCrystal.Face> Faces)>();
            foreach (var face in faces)
            {
                var groupIndex = groups.FindIndex(group => ReferenceEquals(group.Material, face.Material));
                if (groupIndex < 0)
                {
                    groups.Add((face.Material, new List<CPlugCrystal.Face>()));
                    groupIndex = groups.Count - 1;
                }
                groups[groupIndex].Faces.Add(face);
            }

            for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                var group = groups[groupIndex];
                var visualPath = $"{path}/visual:{groupIndex}";
                var materialPath = $"{path}/material:{groupIndex}";
                var material = group.Material;
                var instance = material?.MaterialUserInst;
                var materialName = instance?.MaterialName ?? material?.MaterialName;
                materials.Add(new(materialPath, groupIndex, material is null ? null : Id(material),
                    materialName, material is null ? ItemSceneState.Absent : ItemSceneState.Present,
                    "CrystalMaterial", materialName, instance?.Link, instance?.Color?.ToArray()));
                mappings.Add(new(path + $"/shadedGeom:{groupIndex}", path, groupIndex, groupIndex, 1,
                    material is null ? ItemSceneState.Absent : ItemSceneState.Present));

                var localPositions = new List<Vec3>(group.Faces.Count * 3);
                var uvValues = new List<Vec2>(group.Faces.Count * 3);
                var indices = new List<int>(group.Faces.Count * 3);
                var validUvs = true;
                foreach (var face in group.Faces)
                {
                    var corners = face.Vertices;
                    if (corners is null || corners.Length < 3)
                    {
                        Issue(visualPath, "edition-face", ItemSceneState.Invalid,
                            "Mesh Modeler face does not contain three corners.");
                        continue;
                    }
                    var baseIndex = localPositions.Count;
                    var valid = true;
                    // Mesh Modeler stores both triangles and quads (and can retain
                    // higher-order polygon faces). Emit the complete polygon instead
                    // of silently dropping every corner after the third one. A fan is
                    // deterministic for the convex authored faces used by crystals and
                    // preserves each corner's independent UV.
                    for (var corner = 0; corner < corners.Length; corner++)
                    {
                        var vertex = corners[corner];
                        if (vertex.Index < 0 || vertex.Index >= positions.Length)
                        {
                            valid = false;
                            break;
                        }
                        localPositions.Add(positions[vertex.Index]);
                        uvValues.Add(vertex.TexCoord);
                        validUvs &= float.IsFinite(vertex.TexCoord.X) && float.IsFinite(vertex.TexCoord.Y);
                    }
                    if (!valid)
                    {
                        while (localPositions.Count > baseIndex) localPositions.RemoveAt(localPositions.Count - 1);
                        while (uvValues.Count > baseIndex) uvValues.RemoveAt(uvValues.Count - 1);
                        Issue(visualPath, "edition-face-index", ItemSceneState.Invalid,
                            "Mesh Modeler face references a position outside its crystal table.");
                        continue;
                    }
                    for (var corner = 1; corner < corners.Length - 1; corner++)
                    {
                        indices.Add(baseIndex);
                        indices.Add(baseIndex + corner);
                        indices.Add(baseIndex + corner + 1);
                    }
                }

                if (localPositions.Count == 0)
                {
                    Issue(visualPath, "edition-empty-part", ItemSceneState.Absent,
                        "Mesh Modeler material group contains no valid faces.");
                    continue;
                }

                var uvs = validUvs ? new Dictionary<int, Vec2[]> { [0] = uvValues.ToArray() } : new();
                var source = new CrystalGeometryKey(crystal, groupIndex, groupIndex);
                EmitGeometry(visualPath, entry?.Path, source, false, localPositions.ToArray(), null,
                    uvs, indices.ToArray(), world);
            }
        }

        private void Visual(CPlugVisual visual, string path, Matrix4x4? world, ItemSceneEntryHandle? entry,
            CPlugSolid2Model? solid, int? visualIndex)
        {
            handles.Visuals.Add(new(path, solid, visualIndex, visual, entry?.Path, world));
            var bounds = visual.BoundingBox;
            if (!new[] { bounds.X, bounds.Y, bounds.Z, bounds.X2, bounds.Y2, bounds.Z2 }.All(float.IsFinite))
                Issue(path, "nonfinite-source-bounds", ItemSceneState.Invalid, "Authored visual bounds contain nonfinite values; preview bounds are calculated independently.");
            if (visual is not CPlugVisualIndexedTriangles)
            { Issue(path, "visual-representation", ItemSceneState.Unsupported, $"Unsupported visual representation: {visual.GetType().Name}."); return; }
            var visual3D = (CPlugVisual3D)visual;
            var streams = visual.VertexStreams?.ToArray() ?? Array.Empty<CPlugVertexStream>();
            var vertices = visual3D.Vertices ?? Array.Empty<CPlugVisual3D.Vertex>();
            Vec3[]? positions = null;
            Vec3[]? normals = null;
            Vec3[]? animationPositions = null;
            Vec3[]? animationNormals = null;
            var animationFrameCount = 0;
            var uvs = new Dictionary<int, Vec2[]>();
            var ambiguous = false;
            for (var i = 0; i < streams.Length; i++)
            {
                var stream = streams[i];
                var streamPath = path + $"/stream:{i}";
                if (stream is null)
                { Issue(streamPath, "null-stream", ItemSceneState.Invalid, "Null vertex stream."); ambiguous = true; continue; }
                handles.Streams.Add(new(path, i, Id(stream), stream));
                // These four public getters read decoded arrays only; none resolve the private stream model.
                if (stream.Positions is { Length: > 0 })
                {
                    if (positions is not null) ambiguous = true;
                    else positions = stream.Positions;
                }
                if (stream.Normals is { Length: > 0 })
                {
                    if (normals is not null) ambiguous = true;
                    else normals = stream.Normals;
                }
                foreach (var (channel, values) in stream.UVs)
                    if (!uvs.TryAdd(channel, values)) ambiguous = true;
                if (stream.Positions is not { Length: > 0 } && stream.Normals is not { Length: > 0 } && stream.UVs.Count == 0)
                {
                    Issue(streamPath, "opaque-stream", ItemSceneState.Unsupported, "No decoded position/normal/UV attributes. Shared-model and declaration slots are private in the bundled API; absence cannot be distinguished from an unresolved or unsupported stream.");
                    ambiguous = true;
                }
            }
            if (streams.Length > 0)
            {
                Issue(path, "stream-layout-opaque", ItemSceneState.Unsupported, "Decoded attributes are available; serialized count, declarations and shared-model references cannot be inventoried through this bundled public API.");
                if (vertices.Length > 0 || visual.TexCoords.Length > 0) ambiguous = true;
            }
            else
            {
                // Native waving flags use one indexed vertex buffer containing
                // complete baked frames and a SubVisuals table that points at
                // each frame. Expose those frames to the browser preview while
                // retaining only frame zero as the ordinary geometry payload.
                var indexedVisual = (CPlugVisualIndexedTriangles)visual;
                if (TryReadBakedAnimation(indexedVisual, vertices, out animationFrameCount, out var bakedFrameVertexCount))
                {
                    animationPositions = vertices.Select(x => x.Position).ToArray();
                    positions = animationPositions.Take(bakedFrameVertexCount).ToArray();
                    if (vertices.All(x => x.Normal.HasValue))
                    {
                        animationNormals = vertices.Select(x => x.Normal!.Value).ToArray();
                        normals = animationNormals.Take(bakedFrameVertexCount).ToArray();
                    }
                }
                else
                {
                    positions = vertices.Select(x => x.Position).ToArray();
                }
                if (vertices.Any(x => x.Normal.HasValue) && normals is null)
                {
                    if (vertices.Any(x => !x.Normal.HasValue))
                    { Issue(path, "partial-normals", ItemSceneState.Invalid, "CPU normals are present for only some vertices."); }
                    else normals = vertices.Select(x => x.Normal!.Value).ToArray();
                }
                for (var channel = 0; channel < visual.TexCoords.Length; channel++)
                    if (visual.TexCoords[channel] is not null)
                    {
                        var values = visual.TexCoords[channel].TexCoords.Select(x => x.UV).ToArray();
                        var uvFrameVertexCount = positions.Length;
                        uvs[channel] = values.Length >= uvFrameVertexCount
                            ? values.Take(uvFrameVertexCount).ToArray() : values;
                    }
            }
            if (ambiguous)
            { Issue(path, "ambiguous-attributes", ItemSceneState.Unsupported, "Duplicate attributes, mixed CPU/stream data or opaque stream; no buffer concatenation inferred."); return; }
            if (positions is not { Length: > 0 })
            { Issue(path, "missing-positions", streams.Length == 0 ? ItemSceneState.Absent : ItemSceneState.Unsupported, "No decoded positions available."); return; }
            int[]? indices;
            if (visual is CPlugVisualIndexedTriangles indexed)
            {
                indices = indexed.IndexBuffer?.Indices;
                if (indices is null)
                { Issue(path, "missing-index-buffer", ItemSceneState.Absent, "Indexed triangles have no index buffer."); return; }
            }
            else indices = Enumerable.Range(0, positions.Length).ToArray();
            if (visual.SkinData is not null || visual.MorphCount != 0
                || (visual.SubVisuals is { Length: > 0 } && animationFrameCount == 0)
                || visual.Splits is { Length: > 0 })
                Issue(path, "visual-animation-layout", ItemSceneState.Unsupported, "Skin/morph/subvisual/split metadata retained; only decoded base geometry is exposed.");
            EmitGeometry(path, entry?.Path, visual, false, positions, normals, uvs, indices, world,
                animationPositions, animationNormals, animationFrameCount);
        }

        private void EmitGeometry(string path, string? entityPath, object source, bool collision, Vec3[] positions,
            Vec3[]? normals, Dictionary<int, Vec2[]> uvs, int[] indices, Matrix4x4? world,
            Vec3[]? animationLocalPositions = null, Vec3[]? animationLocalNormals = null, int animationFrameCount = 0)
        {
            if (!world.HasValue || !ValidTransform(world.Value))
            { Issue(path, "invalid-geometry-transform", ItemSceneState.Invalid, "Geometry has no finite invertible world transform."); return; }
            if (animationLocalPositions is null && geometryCache?.Find(source, world.Value) is { } cached)
            {
                geometry.Add(cached with { Path = path, EntityPath = entityPath, SourceId = Id(source) });
                return;
            }
            var diagnosticStart = diagnostics.Count;
            if (positions.Length == 0)
            { Issue(path, "empty-geometry", ItemSceneState.Absent, "No geometry vertices."); return; }
            if (positions.Any(x => !Finite(V(x))))
            { Issue(path, "nonfinite-position", ItemSceneState.Invalid, "Nonfinite source position; visual omitted."); return; }
            if (indices.Length % 3 != 0 || indices.Any(x => x < 0 || x >= positions.Length))
            { Issue(path, "invalid-indices", ItemSceneState.Invalid, "Triangle indices are incomplete or outside the vertex buffer; visual omitted."); return; }
            var local = positions.Select(V).ToArray();
            var transformed = local.Select(x => Vector3.Transform(x, world.Value)).ToArray();
            if (transformed.Any(x => !Finite(x)))
            { Issue(path, "nonfinite-world-position", ItemSceneState.Invalid, "Transform overflows a position; visual omitted."); return; }
            float[]? localNormals = null;
            float[]? worldNormals = null;
            if (normals is not null)
            {
                if (normals.Length != positions.Length || normals.Any(x => !Finite(V(x)) || !float.IsFinite(V(x).LengthSquared()) || V(x).LengthSquared() <= 0))
                    Issue(path, "invalid-normals", ItemSceneState.Invalid, "Normals must be finite, nonzero and match the vertex count; normal channel omitted.");
                else
                {
                    var transformedNormals = new Vector3[normals.Length];
                    var validNormals = true;
                    Matrix4x4.Invert(world.Value, out var inverse);
                    var normalMatrix = Matrix4x4.Transpose(inverse);
                    for (var i = 0; i < normals.Length; i++)
                        validNormals &= TryNormal(V(normals[i]), normalMatrix, out transformedNormals[i]);
                    if (!validNormals)
                        Issue(path, "invalid-world-normals", ItemSceneState.Invalid, "Normal transform overflows or collapses; channel omitted.");
                    else
                    {
                        localNormals = Pack(normals.Select(V));
                        worldNormals = Pack(transformedNormals);
                    }
                }
            }
            var channels = new Dictionary<int, float[]>();
            foreach (var (channel, values) in uvs)
            {
                if (channel < 0 || values is null || values.Length != positions.Length || values.Any(x => !float.IsFinite(x.X) || !float.IsFinite(x.Y)))
                    Issue(path, "invalid-uv", ItemSceneState.Invalid, $"UV{channel} must be finite and match the vertex count; channel omitted.");
                else
                {
                    var packed = new float[values.Length * 2];
                    for (var i = 0; i < values.Length; i++) { packed[i * 2] = values[i].X; packed[i * 2 + 1] = values[i].Y; }
                    channels.Add(channel, packed);
                }
            }
            var min = transformed.Aggregate(Vector3.Min);
            var max = transformed.Aggregate(Vector3.Max);
            float[]? animationWorldPositions = null;
            float[]? animationWorldNormals = null;
            if (animationLocalPositions is not null && animationFrameCount > 1)
            {
                if (animationLocalPositions.Length % animationFrameCount != 0)
                { Issue(path, "animation-frame-layout", ItemSceneState.Invalid, "Baked animation frames do not divide the vertex buffer evenly."); return; }
                var frameWorld = new Vector3[animationLocalPositions.Length];
                for (var i = 0; i < animationLocalPositions.Length; i++)
                    frameWorld[i] = Vector3.Transform(V(animationLocalPositions[i]), world.Value);
                if (frameWorld.Any(p => !Finite(p)))
                { Issue(path, "animation-frame-position", ItemSceneState.Invalid, "Baked animation contains a nonfinite position."); return; }
                animationWorldPositions = Pack(frameWorld);
                if (animationLocalNormals is not null)
                {
                    if (animationLocalNormals.Length != animationLocalPositions.Length)
                    { Issue(path, "animation-frame-normal-layout", ItemSceneState.Invalid, "Baked animation normals do not match positions."); return; }
                    var normalMatrix = Matrix4x4.Transpose(Matrix4x4.Invert(world.Value, out var inverse) ? inverse : Matrix4x4.Identity);
                    var frameNormals = new Vector3[animationLocalNormals.Length];
                    for (var i = 0; i < animationLocalNormals.Length; i++)
                        if (!TryNormal(V(animationLocalNormals[i]), normalMatrix, out frameNormals[i]))
                        { Issue(path, "animation-frame-normal", ItemSceneState.Invalid, "Baked animation contains an invalid normal."); return; }
                    animationWorldNormals = Pack(frameNormals);
                }
            }
            var result = new ItemSceneGeometry(path, entityPath, Id(source), collision, Pack(local), Pack(transformed), indices.ToArray(),
                localNormals, worldNormals, channels, Pack(min), Pack(max), WorldTransform: Pack(world.Value),
                AnimationLocalPositions: animationLocalPositions is null ? null : Pack(animationLocalPositions.Select(V)),
                AnimationPositions: animationWorldPositions,
                AnimationLocalNormals: animationLocalNormals is null ? null : Pack(animationLocalNormals.Select(V)),
                AnimationNormals: animationWorldNormals,
                AnimationFrameCount: animationFrameCount);
            if (indices.Length == 0) Issue(path, "empty-indices", ItemSceneState.Absent, "No triangles in the index buffer.");
            else if (Enumerable.Range(0, indices.Length / 3).Any(i => indices[3 * i] == indices[3 * i + 1]
                || indices[3 * i] == indices[3 * i + 2] || indices[3 * i + 1] == indices[3 * i + 2]))
                Issue(path, "degenerate-triangle", ItemSceneState.Invalid, "At least one triangle repeats a vertex index.");
            // Invalid channels can depend on the occurrence transform. Do not pool partial geometry.
            if (animationLocalPositions is null && geometryCache is not null && diagnostics.Count == diagnosticStart)
                result = geometryCache.Add(source, world.Value, result);
            geometry.Add(result);
        }

        private static bool TryReadBakedAnimation(CPlugVisualIndexedTriangles visual,
            CPlugVisual3D.Vertex[] vertices, out int frameCount, out int frameVertexCount)
        {
            frameCount = 0; frameVertexCount = 0;
            var frames = visual.SubVisuals;
            var indices = visual.IndexBuffer?.Indices;
            if (frames is not { Length: > 1 and <= 256 } || indices is not { Length: > 0 }
                || vertices.Length == 0 || vertices.Length % frames.Length != 0)
                return false;
            frameVertexCount = vertices.Length / frames.Length;
            for (var frame = 0; frame < frames.Length; frame++)
            {
                var sub = frames[frame];
                if (sub.X != frame * frameVertexCount || sub.Y != 0 || sub.Z != indices.Length)
                { frameVertexCount = 0; return false; }
            }
            frameCount = frames.Length;
            return true;
        }

        private void Surface(CPlugSurface surface, string path, Matrix4x4? world, ItemSceneEntryHandle? entry,
            ItemSceneCollisionSource source)
        {
            if (surface.Geom is not null)
            {
                Issue(path + "/geom", "legacy-collision-geometry", ItemSceneState.Unsupported, "Legacy surface geometry is retained without interpreting its layout.");
                if (surface.Surf is null)
                    collisions.Add(new(path, entry?.Path, "Legacy surface geometry", ItemSceneState.Unsupported, source));
            }
            for (var i = 0; i < surface.Materials.Length; i++)
                if (surface.Materials[i]?.MaterialFile is { } file)
                    Issue(path + $"/material:{i}", "external-reference", ItemSceneState.Unresolved, $"External collision material: {file.FilePath}");
            if (surface.Surf is not null || surface.Geom is null) Surf(surface.Surf, path, world, entry, source, 0);
        }

        private void Surf(CPlugSurface.ISurf? surf, string path, Matrix4x4? world, ItemSceneEntryHandle? entry, ItemSceneCollisionSource source, int depth)
        {
            if (surf is null)
            { collisions.Add(new(path, entry?.Path, "No decoded surface", ItemSceneState.Absent, source)); return; }
            if (depth > 128 || !ancestors.Add(surf))
            {
                collisions.Add(new(path, entry?.Path, surf.GetType().Name, depth > 128 ? ItemSceneState.Unsupported : ItemSceneState.Cycle, source));
                Issue(path, "collision-cycle-or-limit", ItemSceneState.Unsupported, "Collision traversal stopped at a cycle or depth limit.");
                return;
            }
            try
            {
                switch (surf)
                {
                    case CPlugSurface.Mesh mesh:
                        CollisionMesh(mesh, path, world, entry, source);
                        break;
                    case CPlugSurface.Compound compound:
                        collisions.Add(new(path, entry?.Path, "Compound", ItemSceneState.Present, source));
                        if (compound.Surfs.Length != compound.SurfLocs.Length)
                            Issue(path, "collision-transform-count", ItemSceneState.Invalid, "Compound child and transform counts differ.");
                        for (var i = 0; i < compound.Surfs.Length; i++)
                        {
                            Matrix4x4? composed = i < compound.SurfLocs.Length && world.HasValue
                                ? FromIso4(compound.SurfLocs[i]) * world.Value : null;
                            if (composed.HasValue && !ValidTransform(composed.Value)) composed = null;
                            Surf(compound.Surfs[i], path + $"/surf:{i}", composed, entry, source, depth + 1);
                        }
                        break;
                    default:
                        collisions.Add(new(path, entry?.Path, surf.GetType().Name, ItemSceneState.Unsupported, source));
                        Issue(path, "collision-primitive", ItemSceneState.Unsupported, "Analytic collision primitive retained; no triangle tessellation implemented.");
                        break;
                }
            }
            finally { ancestors.Remove(surf); }
        }

        private void CollisionMesh(CPlugSurface.Mesh mesh, string path, Matrix4x4? world, ItemSceneEntryHandle? entry,
            ItemSceneCollisionSource source)
        {
            if (mesh.Version is not (1 or 2 or 3 or 5 or 6 or 7))
            {
                collisions.Add(new(path, entry?.Path, "Mesh", ItemSceneState.Unsupported, source));
                Issue(path, "collision-mesh-version", ItemSceneState.Unsupported, mesh.Version == 4
                    ? "Mesh version 4 has no decoded geometry payload in the bundled serializer; native collision meaning is not established."
                    : $"Unsupported collision mesh version {mesh.Version}; array absence does not establish absent collision.");
                return;
            }
            var cooked = mesh.Version is 1 or 2 or 3 or 5;
            if (mesh.Vertices is null || (cooked
                ? mesh.CookedTriangles is null || mesh.Triangles is not null
                : mesh.Triangles is null || mesh.CookedTriangles is not null))
            {
                collisions.Add(new(path, entry?.Path, "Mesh", ItemSceneState.Invalid, source));
                Issue(path, "collision-mesh-layout", ItemSceneState.Invalid, "Collision mesh has missing arrays or arrays incompatible with its serialized version; no alternate layout is inferred.");
                return;
            }
            var indices = cooked
                ? mesh.CookedTriangles!.SelectMany(x => new[] { x.Indices.X, x.Indices.Y, x.Indices.Z }).ToArray()
                : mesh.Triangles!.SelectMany(x => new[] { x.Indices.X, x.Indices.Y, x.Indices.Z }).ToArray();
            if (mesh.Vertices.Any(p => !Finite(V(p))))
            {
                collisions.Add(new(path, entry?.Path, "Mesh", ItemSceneState.Invalid, source));
                Issue(path, "nonfinite-position", ItemSceneState.Invalid, "Collision mesh contains nonfinite vertices.");
                return;
            }
            if (indices.Length == 0)
            {
                collisions.Add(new(path, entry?.Path, "Mesh", ItemSceneState.Absent, source));
                Issue(path, "empty-collision-mesh", ItemSceneState.Absent, "Supported collision layout contains zero triangles.");
                return;
            }
            if (mesh.Vertices.Length == 0)
            {
                collisions.Add(new(path, entry?.Path, "Mesh", ItemSceneState.Invalid, source));
                Issue(path, "collision-mesh-layout", ItemSceneState.Invalid, "Collision triangles have no vertices.");
                return;
            }
            var diagnosticOffset = diagnostics.Count;
            EmitGeometry(path, entry?.Path, mesh, true, mesh.Vertices, null, new(), indices, world);
            var state = diagnostics.Skip(diagnosticOffset).Any(d => d.State == ItemSceneState.Invalid)
                ? ItemSceneState.Invalid : ItemSceneState.Present;
            collisions.Add(new(path, entry?.Path, "Mesh", state, source));
        }
    }
}
