using System.Numerics;

namespace TM_Item_Studio.Models;

/// <summary>
/// Document-lifetime preview geometry, keyed by parsed GBX reference identity (never names
/// or snapshot-local source IDs). Cached snapshot buffers are read-only to callers.
/// Clear after any authored edit, before rebuilding; loading new documents must also clear.
/// </summary>
public sealed class ItemSceneGeometryCache
{
    private sealed class Source
    {
        public required int Id { get; init; }
        public ItemSceneGeometry? Local { get; set; }
        public Dictionary<Matrix4x4, ItemSceneGeometry> Instances { get; } = new();
    }
    private readonly Dictionary<object, Source> sources = new(ReferenceEqualityComparer.Instance);
    public int Epoch { get; private set; }

    public void Clear()
    {
        sources.Clear();
        Epoch++;
    }

    internal ItemSceneGeometry? Find(object source, Matrix4x4 world)
    {
        if (!sources.TryGetValue(source, out var cached)) return null;
        if (cached.Instances.TryGetValue(world, out var entry)) return entry;
        // A new occurrence of the same node needs only a transform. Materialize world
        // arrays for snapshot consumers, without decoding or repacking local attributes.
        var local = cached.Local!;
        var positions = new float[local.LocalPositions.Length];
        var normals = local.LocalNormals is null ? null : new float[local.LocalNormals.Length];
        Matrix4x4.Invert(world, out var inverse);
        var normalMatrix = Matrix4x4.Transpose(inverse);
        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);
        for (var i = 0; i < positions.Length; i += 3)
        {
            var p = Vector3.Transform(new(local.LocalPositions[i], local.LocalPositions[i + 1], local.LocalPositions[i + 2]), world);
            if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z)) return null;
            positions[i] = p.X; positions[i + 1] = p.Y; positions[i + 2] = p.Z;
            min = Vector3.Min(min, p); max = Vector3.Max(max, p);
            if (normals is not null)
            {
                var n = Vector3.TransformNormal(new(local.LocalNormals![i], local.LocalNormals[i + 1], local.LocalNormals[i + 2]), normalMatrix);
                var length = n.LengthSquared();
                if (!float.IsFinite(length) || length <= 0) return null;
                n = Vector3.Normalize(n);
                normals[i] = n.X; normals[i + 1] = n.Y; normals[i + 2] = n.Z;
            }
        }
        var geometry = local with { Positions = positions, Normals = normals,
            BoundsMin = new[] { min.X, min.Y, min.Z }, BoundsMax = new[] { max.X, max.Y, max.Z },
            WorldTransform = new[] { world.M11, world.M12, world.M13, world.M14, world.M21, world.M22, world.M23, world.M24,
                world.M31, world.M32, world.M33, world.M34, world.M41, world.M42, world.M43, world.M44 } };
        cached.Instances[world] = geometry;
        return geometry;
    }

    internal ItemSceneGeometry Add(object source, Matrix4x4 world, ItemSceneGeometry geometry)
    {
        if (!sources.TryGetValue(source, out var cached))
            sources.Add(source, cached = new Source { Id = sources.Count });
        var local = cached.Local ?? geometry;
        geometry = geometry with { GeometryId = cached.Id, LocalPositions = local.LocalPositions,
            LocalNormals = local.LocalNormals, Indices = local.Indices, UVs = local.UVs };
        cached.Local ??= geometry;
        cached.Instances[world] = geometry;
        return geometry;
    }
}
