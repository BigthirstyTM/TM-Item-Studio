using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;

namespace TM_Item_Studio.Models;

/// <summary>Scale U/V, rotate counterclockwise about the UV origin in degrees, then translate U/V.</summary>
public sealed record ItemUvTransform(float ScaleU = 1, float ScaleV = 1, float RotationDegrees = 0,
    float OffsetU = 0, float OffsetV = 0);
public sealed record ItemUvEditScope(int VertexCount, IReadOnlyList<string> OccurrencePaths,
    string Message = "Every listed occurrence shares this authored visual and will change. Save/reparse capability is checked on Apply.");

/// <summary>Selected authored-visual UV0 editing. No geometry conversion, external resolution or private-field access.</summary>
public static class ItemMaterialEdits
{
    /// <summary>Inspect all original variant slots, including hidden variants, before displaying edit scope.</summary>
    public static ItemUvEditScope InspectUv0(Gbx<CGameItemModel> document, int documentOrdinal, string visualPath)
    {
        var target = Select(Scenes(document, documentOrdinal), visualPath);
        return Scope(target);
    }

    /// <summary>
    /// Prove serialization on an isolated document first. The only final original mutation is replacing UV0
    /// storage on the existing source visual/stream; UV1 and other authored buffers keep their identities.
    /// </summary>
    public static ItemUvEditScope ApplyUv0(Gbx<CGameItemModel> document, int documentOrdinal, string visualPath, ItemUvTransform transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        if (!new[] { transform.ScaleU, transform.ScaleV, transform.RotationDegrees, transform.OffsetU, transform.OffsetV }.All(float.IsFinite))
            throw new ArgumentException("UV transform values must be finite.", nameof(transform));
        var originalScenes = Scenes(document, documentOrdinal);
        var target = Select(originalScenes, visualPath);
        var radians = (double)transform.RotationDegrees * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var expected = target.UV.Select(uv =>
        {
            var u = (double)uv.X * transform.ScaleU;
            var v = (double)uv.Y * transform.ScaleV;
            return new Vec2((float)(u * cos - v * sin + transform.OffsetU), (float)(u * sin + v * cos + transform.OffsetV));
        }).ToArray();
        if (expected.Any(uv => !float.IsFinite(uv.X) || !float.IsFinite(uv.Y)))
            throw new ArgumentException("UV transform overflows the source coordinate format.", nameof(transform));
        // Allocate the final replacement before any work that could fail, without changing the original.
        var commit = Prepare(target, expected);
        try
        {
            var originalBytes = Save(document);
            var clone = Parse(originalBytes);
            var cloneScenes = Scenes(clone, documentOrdinal);
            var cloneTarget = Select(cloneScenes, visualPath);
            Compare(originalScenes, cloneScenes, null, null);
            RequireSameScope(target, cloneTarget);
            if (!Save(clone).SequenceEqual(originalBytes))
                throw new NotSupportedException("Document is not byte-stable on save/reparse; UV editing cannot prove preservation.");

            Prepare(cloneTarget, expected)();
            var verified = Parse(Save(clone));
            var verifiedScenes = Scenes(verified, documentOrdinal);
            var verifiedTarget = Select(verifiedScenes, visualPath);
            RequireSameScope(target, verifiedTarget);
            Compare(originalScenes, verifiedScenes, target.Paths, expected);

            // Reverse only the verified clone's UV change. Byte equality proves untouched chunks, metadata,
            // dependencies and serialized topology survived, beyond the public geometry comparison above.
            Prepare(verifiedTarget, target.UV)();
            if (!Save(verified).SequenceEqual(originalBytes))
                throw new NotSupportedException("UV proof changed unrelated serialized data or topology; no source edit was applied.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
        {
            throw new NotSupportedException("UV0 save/reparse capability proof failed; no source UV buffer was changed. " + ex.Message, ex);
        }
        commit();
        return Scope(target);
    }

    private sealed record Selection(ItemSceneVisualHandle Visual, CPlugVertexStream? Stream, Vec2[] UV, string[] Paths);
    private static ItemUvEditScope Scope(Selection target) => new(target.UV.Length, Array.AsReadOnly(target.Paths));

    private static ItemSceneResult[] Scenes(Gbx<CGameItemModel> document, int ordinal)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (ordinal < 0) throw new ArgumentOutOfRangeException(nameof(ordinal));
        if (document.Node.EntityModel is NPlugItem_SVariantList list)
        {
            if (list.Variants is null || list.Variants.Length == 0)
                throw new NotSupportedException("No original variant slots are available for UV scope discovery.");
            return Enumerable.Range(0, list.Variants.Length).Select(i => ItemScene.Build(document.Node, ordinal, i)).ToArray();
        }
        return new[] { ItemScene.Build(document.Node, ordinal) };
    }

    private static Selection Select(ItemSceneResult[] scenes, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        // Unvisited/ambiguous geometry could hide another borrower. Material-only external references do
        // not contain visual/vertex-stream ownership and can remain unresolved without being loaded.
        foreach (var scene in scenes)
        {
            if (scene.Preview.Nodes.Any(n => n.State is ItemSceneState.Unresolved or ItemSceneState.Cycle or ItemSceneState.Invalid))
                throw new NotSupportedException("Unresolved, cyclic or invalid scene topology prevents complete shared-visual scope discovery.");
            if (scene.Preview.Diagnostics.Any(d => d.State == ItemSceneState.Invalid || d.Code is
                "unsupported-node" or "edition-model" or "traversal-limit" or "visual-representation" or
                "ambiguous-attributes" or "opaque-stream" or "visual-animation-layout" or "tree-generator" or "tree-subtype"))
                throw new NotSupportedException("Unsupported or invalid geometry prevents proving UV ownership across every variant.");
        }
        var visuals = scenes.SelectMany(x => x.Handles.Visuals).ToArray();
        var selected = visuals.SingleOrDefault(x => x.Path == path)
            ?? throw new ArgumentException("The selected visual path is not present in this document's original variants.", nameof(path));
        var occurrences = visuals.Where(x => ReferenceEquals(x.Source, selected.Source)).ToArray();
        var paths = occurrences.Select(x => x.Path).Order(StringComparer.Ordinal).ToArray();
        var materialOwners = new List<(CPlugSolid2Model Solid, int Index)>();
        foreach (var occurrence in occurrences)
        {
            if (occurrence.Solid is null || occurrence.VisualIndex is null)
                throw new NotSupportedException("Only Solid2 visuals with an explicit shaded-material mapping support UV editing.");
            var maps = occurrence.Solid.ShadedGeoms?.Where(x => x.VisualIndex == occurrence.VisualIndex).ToArray();
            if (maps is null || maps.Length == 0)
                throw new NotSupportedException("Visual has no authored material mapping; edit scope cannot be established.");
            foreach (var map in maps)
                if (!materialOwners.Any(x => ReferenceEquals(x.Solid, occurrence.Solid) && x.Index == map.MaterialIndex))
                    materialOwners.Add((occurrence.Solid, map.MaterialIndex));
        }
        if (materialOwners.Count != 1)
            throw new NotSupportedException("This authored visual is used by different material slots; selected-material UV edits would widen their scope.");
        var meshes = scenes.SelectMany(x => x.Preview.Geometry).Where(x => paths.Contains(x.Path, StringComparer.Ordinal)).ToArray();
        if (meshes.Length != paths.Length || meshes.Any(x => !x.UVs.ContainsKey(0) || x.Indices.Length == 0))
            throw new NotSupportedException("Every selected occurrence must have decoded triangles and a complete UV0 channel.");
        var streams = selected.Source.VertexStreams;
        CPlugVertexStream? uvStream = null;
        Vec2[] uv;
        if (streams.Count > 0)
        {
            var providers = streams.Where(x => x.UVs.ContainsKey(0)).ToArray();
            if (providers.Length != 1) throw new NotSupportedException("UV0 requires exactly one decoded stream provider.");
            uvStream = providers[0];
            uv = uvStream.UVs[0];
            foreach (var other in visuals.Where(x => !ReferenceEquals(x.Source, selected.Source)))
                if (other.Source.VertexStreams.Any(s => ReferenceEquals(s, uvStream) || ReferenceEquals(s.UVs, uvStream.UVs)))
                    throw new NotSupportedException("The UV0 stream or channel dictionary is shared by a different authored visual.");
            // Different streams of this visual must not share the same channel dictionary either.
            if (streams.Any(s => !ReferenceEquals(s, uvStream) && ReferenceEquals(s.UVs, uvStream.UVs)))
                throw new NotSupportedException("Multiple streams share the UV channel dictionary; scope is ambiguous.");
        }
        else
        {
            if (selected.Source.TexCoords is not { Length: > 0 } || selected.Source.TexCoords[0] is null)
                throw new NotSupportedException("CPU visual has no UV0 TexCoordSet.");
            uv = selected.Source.TexCoords[0].TexCoords.Select(x => x.UV).ToArray();
        }
        if (uv.Length == 0 || uv.Any(x => !float.IsFinite(x.X) || !float.IsFinite(x.Y))
            || meshes.Any(x => x.LocalPositions.Length / 3 != uv.Length || !x.UVs[0].SequenceEqual(Flatten(uv))))
            throw new NotSupportedException("UV0 is nonfinite, ambiguous or does not match the vertex count.");
        return new(selected, uvStream, uv.ToArray(), paths);
    }

    private static Action Prepare(Selection target, Vec2[] values)
    {
        var copy = values.ToArray();
        if (target.Stream is not null) return () => target.Stream.UVs[0] = copy;
        var sets = target.Visual.Source.TexCoords.ToArray();
        var set = sets[0];
        // Copy-on-write keeps other channels and any other visual borrowing this TexCoordSet unchanged.
        sets[0] = new CPlugVisual.TexCoordSet { Version = set.Version, Flags = set.Flags, U01 = set.U01,
            TexCoords = set.TexCoords.Select((coord, i) => coord with { UV = copy[i] }).ToArray() };
        return () => target.Visual.Source.TexCoords = sets;
    }

    private static void RequireSameScope(Selection original, Selection clone)
    {
        if (!original.Paths.SequenceEqual(clone.Paths))
            throw new NotSupportedException("Shared visual occurrence topology did not survive save/reparse.");
    }

    private static void Compare(ItemSceneResult[] before, ItemSceneResult[] after, string[]? changedPaths, Vec2[]? expected)
    {
        var oldMeshes = before.SelectMany(x => x.Preview.Geometry).ToDictionary(x => x.Path);
        var newMeshes = after.SelectMany(x => x.Preview.Geometry).ToDictionary(x => x.Path);
        if (!oldMeshes.Keys.Order().SequenceEqual(newMeshes.Keys.Order()))
            throw new NotSupportedException("Geometry inventory changed during save/reparse.");
        foreach (var (path, old) in oldMeshes)
        {
            var current = newMeshes[path];
            if (!old.LocalPositions.SequenceEqual(current.LocalPositions) || !old.Positions.SequenceEqual(current.Positions)
                || !old.Indices.SequenceEqual(current.Indices) || !Same(old.LocalNormals, current.LocalNormals)
                || !Same(old.Normals, current.Normals) || !old.UVs.Keys.Order().SequenceEqual(current.UVs.Keys.Order()))
                throw new NotSupportedException("Positions, indices, normals or UV channel inventory changed during save/reparse.");
            foreach (var (channel, values) in old.UVs)
            {
                var wanted = channel == 0 && changedPaths?.Contains(path, StringComparer.Ordinal) == true ? Flatten(expected!) : values;
                if (!wanted.SequenceEqual(current.UVs[channel]))
                    throw new NotSupportedException($"UV{channel} at {path} did not match the isolated edit's expected result.");
            }
        }
    }

    private static bool Same(float[]? a, float[]? b) => a is null ? b is null : b is not null && a.SequenceEqual(b);
    private static float[] Flatten(Vec2[] values) => values.SelectMany(x => new[] { x.X, x.Y }).ToArray();
    private static byte[] Save(Gbx<CGameItemModel> document)
    {
        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }
    private static Gbx<CGameItemModel> Parse(byte[] bytes) => Gbx.Parse<CGameItemModel>(new MemoryStream(bytes),
        new GbxReadSettings { SafeSkippableChunks = true });
}
