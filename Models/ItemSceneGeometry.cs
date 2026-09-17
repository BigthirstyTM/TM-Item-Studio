using System.Numerics;
using GBX.NET;
using GBX.NET.Engines.Plug;

namespace TM_Item_Studio.Models;

public static partial class ItemScene
{
    private sealed partial class Builder
    {
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
                positions = vertices.Select(x => x.Position).ToArray();
                if (vertices.Any(x => x.Normal.HasValue))
                {
                    if (vertices.Any(x => !x.Normal.HasValue))
                    { Issue(path, "partial-normals", ItemSceneState.Invalid, "CPU normals are present for only some vertices."); }
                    else normals = vertices.Select(x => x.Normal!.Value).ToArray();
                }
                for (var channel = 0; channel < visual.TexCoords.Length; channel++)
                    if (visual.TexCoords[channel] is not null)
                        uvs[channel] = visual.TexCoords[channel].TexCoords.Select(x => x.UV).ToArray();
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
            if (visual.SkinData is not null || visual.MorphCount != 0 || visual.SubVisuals is { Length: > 0 } || visual.Splits is { Length: > 0 })
                Issue(path, "visual-animation-layout", ItemSceneState.Unsupported, "Skin/morph/subvisual/split metadata retained; only decoded base geometry is exposed.");
            EmitGeometry(path, entry?.Path, visual, false, positions, normals, uvs, indices, world);
        }

        private void EmitGeometry(string path, string? entityPath, object source, bool collision, Vec3[] positions,
            Vec3[]? normals, Dictionary<int, Vec2[]> uvs, int[] indices, Matrix4x4? world)
        {
            if (!world.HasValue || !ValidTransform(world.Value))
            { Issue(path, "invalid-geometry-transform", ItemSceneState.Invalid, "Geometry has no finite invertible world transform."); return; }
            if (geometryCache?.Find(source, world.Value) is { } cached)
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
            var result = new ItemSceneGeometry(path, entityPath, Id(source), collision, Pack(local), Pack(transformed), indices.ToArray(),
                localNormals, worldNormals, channels, Pack(min), Pack(max), WorldTransform: Pack(world.Value));
            if (indices.Length == 0) Issue(path, "empty-indices", ItemSceneState.Absent, "No triangles in the index buffer.");
            else if (Enumerable.Range(0, indices.Length / 3).Any(i => indices[3 * i] == indices[3 * i + 1]
                || indices[3 * i] == indices[3 * i + 2] || indices[3 * i + 1] == indices[3 * i + 2]))
                Issue(path, "degenerate-triangle", ItemSceneState.Invalid, "At least one triangle repeats a vertex index.");
            // Invalid channels can depend on the occurrence transform. Do not pool partial geometry.
            if (geometryCache is not null && diagnostics.Count == diagnosticStart)
                result = geometryCache.Add(source, world.Value, result);
            geometry.Add(result);
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
