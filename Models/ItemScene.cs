using System.Numerics;
using System.Text.Json.Serialization;
using GBX.NET;
using GBX.NET.Engines.MwFoundations;
using GBX.NET.Engines.Plug;

namespace TM_Item_Studio.Models;

public enum ItemSceneState { Present, Absent, Unsupported, Unresolved, Invalid, Cycle }
public enum ItemSceneKind { Item, Variant, Prefab, Entity, StaticObject, DynamicObject, Solid, Visual, Collision, Light, Constraint, Other, Tree }

public sealed record ItemSceneDiagnostic(string Path, string Code, ItemSceneState State, string Message);
public sealed record ItemSceneNode(string Path, string? ParentPath, int? SourceId, ItemSceneKind Kind,
    ItemSceneState State, string Type, float[]? LocalTransform, float[]? WorldTransform);
public sealed record ItemSceneMaterial(string Path, int Index, int? SourceId, string? Name, ItemSceneState State, string Representation,
    string? GameMaterialName = null, string? GameMaterialLink = null);
public sealed record ItemSceneMapping(string Path, string SolidPath, int VisualIndex, int MaterialIndex,
    int LodMask, ItemSceneState State);
public sealed record ItemSceneSolid(string Path, int SourceId, float[]? LodDistances, int VisualCount,
    int MaterialCount, bool HasSkeleton, bool HasPreLightGenerator);
public sealed record ItemSceneLight(string Path, string? EntityPath, int SourceId, int? ModelIndex,
    int? SocketIndex, float[]? Position, float[] Color, float Intensity, float Distance, ItemSceneState State);
public sealed record ItemSceneCollision(string Path, string? EntityPath, string Representation, ItemSceneState State);

/// <summary>Snapshot buffers, separate from authored arrays. Positions are before viewer framing.</summary>
public sealed record ItemSceneGeometry(string Path, string? EntityPath, int SourceId, bool IsCollision,
    float[] LocalPositions, float[] Positions, int[] Indices, float[]? LocalNormals, float[]? Normals,
    IReadOnlyDictionary<int, float[]> UVs, float[] BoundsMin, float[] BoundsMax,
    int? GeometryId = null, float[]? WorldTransform = null);

/// <summary>Only this DTO crosses the JS/JSON boundary. It contains no GBX object handles.</summary>
public sealed record ItemScenePreview(IReadOnlyList<ItemSceneNode> Nodes, IReadOnlyList<ItemSceneGeometry> Geometry,
    IReadOnlyList<ItemSceneMapping> Mappings, IReadOnlyList<ItemSceneMaterial> Materials, IReadOnlyList<ItemSceneSolid> Solids,
    IReadOnlyList<ItemSceneLight> Lights, IReadOnlyList<ItemSceneCollision> Collisions,
    IReadOnlyList<ItemSceneDiagnostic> Diagnostics);

public sealed record ItemScenePrefabHandle(string Path, CPlugPrefab Source, Matrix4x4? WorldTransform);
public sealed record ItemSceneEntryHandle(string Path, string PrefabPath, CPlugPrefab Prefab,
    int OriginalIndex, CPlugPrefab.EntRef Entry, Matrix4x4? LocalTransform, Matrix4x4? WorldTransform);
public sealed record ItemSceneVisualHandle(string Path, CPlugSolid2Model? Solid, int? VisualIndex,
    CPlugVisual Source, string? EntityPath, Matrix4x4? WorldTransform);
public sealed record ItemSceneStreamHandle(string VisualPath, int StreamIndex, int SourceId, CPlugVertexStream Source);
public sealed record ItemSceneLightHandle(string Path, CPlugLightUserModel Source, CPlugSolid2Model? Solid,
    CPlugSolid2Model.LightInst? Instance, ItemSceneEntryHandle? Entry);

/// <summary>
/// Authored source handles stay in C#; edits through them affect all occurrences of that source.
/// Source IDs are snapshot-local reference identities, not occurrence IDs or IDs stable across reloads.
/// </summary>
public sealed class ItemSceneHandles
{
    public Dictionary<int, object> Sources { get; } = new();
    public List<ItemScenePrefabHandle> Prefabs { get; } = new();
    public List<ItemSceneEntryHandle> Entries { get; } = new();
    public List<ItemSceneVisualHandle> Visuals { get; } = new();
    public List<ItemSceneStreamHandle> Streams { get; } = new();
    public List<ItemSceneLightHandle> Lights { get; } = new();
}

public sealed record ItemSceneResult(ItemScenePreview Preview, [property: JsonIgnore] ItemSceneHandles Handles);

public static partial class ItemScene
{
    /// <summary>
    /// Traverse an explicitly selected root. If passed an item containing a variant list, variantOrdinal
    /// must select its original slot. External slots are inventoried without invoking resolving getters.
    /// </summary>
    public static ItemSceneResult Build(CMwNod? selectedRoot, int documentOrdinal, int? variantOrdinal = null,
        ItemSceneGeometryCache? geometryCache = null)
    {
        if (documentOrdinal < 0) throw new ArgumentOutOfRangeException(nameof(documentOrdinal));
        if (variantOrdinal < 0) throw new ArgumentOutOfRangeException(nameof(variantOrdinal));
        var builder = new Builder(variantOrdinal, geometryCache);
        builder.Visit(selectedRoot, $"doc:{documentOrdinal}/variant:{variantOrdinal?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"}/root",
            null, Matrix4x4.Identity, Matrix4x4.Identity, null, 0);
        return builder.Result();
    }

    public static Matrix4x4 FromIso4(Iso4 m) => new(m.XX, m.YX, m.ZX, 0, m.XY, m.YY, m.ZY, 0,
        m.XZ, m.YZ, m.ZZ, 0, m.TX, m.TY, m.TZ, 1);

    /// <summary>Transforms a direction with the inverse transpose, including nonuniform affine scale.</summary>
    public static bool TryTransformNormal(Vector3 normal, Matrix4x4 world, out Vector3 transformed)
    {
        transformed = default;
        if (!Finite(normal) || !ValidTransform(world)) return false;
        Matrix4x4.Invert(world, out var inverse);
        return TryNormal(normal, Matrix4x4.Transpose(inverse), out transformed);
    }

    private static bool TryNormal(Vector3 normal, Matrix4x4 normalMatrix, out Vector3 transformed)
    {
        transformed = default;
        var value = Vector3.TransformNormal(normal, normalMatrix);
        if (!Finite(value) || !float.IsFinite(value.LengthSquared()) || value.LengthSquared() <= 0) return false;
        transformed = Vector3.Normalize(value);
        return true;
    }

    public static bool TryLocalTransform(Vec3 position, Quat rotation, out Matrix4x4 matrix)
    {
        var p = V(position);
        var q = new Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W);
        // Reject invalid rotations rather than silently interpreting an uninitialized quaternion as identity.
        if (!Finite(p) || !float.IsFinite(q.LengthSquared()) || MathF.Abs(q.LengthSquared() - 1) > .001f)
        {
            matrix = default;
            return false;
        }
        matrix = Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(p);
        return ValidTransform(matrix);
    }

    private static Vector3 V(Vec3 p) => new(p.X, p.Y, p.Z);
    private static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
    private static float[] Pack(Matrix4x4 m) => new[] { m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44 };
    private static float[] Pack(Vector3 v) => new[] { v.X, v.Y, v.Z };
    private static float[] Pack(IEnumerable<Vector3> values)
    {
        var vertices = values as Vector3[] ?? values.ToArray();
        var packed = new float[vertices.Length * 3];
        for (var i = 0; i < vertices.Length; i++)
        { packed[i * 3] = vertices[i].X; packed[i * 3 + 1] = vertices[i].Y; packed[i * 3 + 2] = vertices[i].Z; }
        return packed;
    }
    private static bool ValidTransform(Matrix4x4 m) => Pack(m).All(float.IsFinite) && Matrix4x4.Invert(m, out var inverse)
        && Pack(inverse).All(float.IsFinite);
}
