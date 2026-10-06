using System.Numerics;
using GBX.NET.Engines.Meta;

namespace TM_Item_Studio.Models;

public sealed record ItemMotionPreviewTrack(string Path, string ChildPath, string? ParentPath,
    float[] ChildRest, float[] ParentRest, ItemMotionEdit Fields, float[] Pos1, float[] Pos2);
public sealed record ItemMotionPreviewResult(IReadOnlyList<ItemMotionPreviewTrack> Tracks,
    IReadOnlyList<string> Diagnostics);

/// <summary>Connects authored scene occurrences to the typed viewer. Never mutates source nodes
/// or substitutes a generic animation for an unsupported binding/timeline.</summary>
public static class AuthoredMotionPreview
{
    public static ItemMotionPreviewResult Build(ItemSceneResult scene)
    {
        var tracks = new List<ItemMotionPreviewTrack>();
        var diagnostics = new List<string>();
        var childClaims = new Dictionary<string, int>();
        var entries = scene.Handles.Entries.ToDictionary(e => e.Path);
        var prefabs = scene.Handles.Prefabs.ToDictionary(p => p.Path);

        var rootPrefab = scene.Handles.Prefabs.FirstOrDefault(p => !p.Path.Contains("/ent:", StringComparison.Ordinal))
            ?? scene.Handles.Prefabs.FirstOrDefault();
        var (slots, tableError, tableStatus) = rootPrefab is not null
            ? ItemMotionBindings.CollectSlots(rootPrefab.Source, rootPrefab.Path)
            : (new List<ItemMotionSlot>(), (string?)"Scene contains no prefabs.", ItemMotionStatus.Absent);
        var worldPath = rootPrefab?.Path ?? "root";

        foreach (var entry in scene.Handles.Entries)
        {
            if (entry.Entry.ModelFile is not null || entry.Entry.Model is not NPlugDyna_SKinematicConstraint source) continue;
            var binding = ItemMotionBindings.Resolve(source, slots,
                entry.Entry.Params as NPlugDyna_SPrefabConstraintParams, worldPath, tableError, tableStatus);
            if (binding.Child.Path is { } childPath)
                childClaims[childPath] = childClaims.GetValueOrDefault(childPath) + 1;
            var sample = ItemMotion.Evaluate(source, 0);
            if (binding.Status != ItemMotionStatus.Supported || !sample.Success)
            {
                diagnostics.Add($"{entry.Path}: {binding.Reason ?? sample.Reason} Authored data preserved.");
                continue;
            }
            var child = entries.GetValueOrDefault(binding.Child.Path!);
            var parentRest = binding.Parent.IsWorld ? Matrix4x4.Identity
                : entries.GetValueOrDefault(binding.Parent.Path!)?.WorldTransform;
            if (child?.WorldTransform is not Matrix4x4 childRest || parentRest is not Matrix4x4 parent
                || !ItemMotionTransforms.IsRigid(childRest) || !ItemMotionTransforms.IsRigid(parent)
                || (binding.Parent.IsWorld && parent != Matrix4x4.Identity))
            {
                diagnostics.Add($"{entry.Path}: Unsupported rest transform; authored data preserved.");
                continue;
            }
            tracks.Add(new(entry.Path, binding.Child.Path!, binding.Parent.IsWorld ? null : binding.Parent.Path,
                Pack(childRest), Pack(parent), ItemMotion.Read(source).Fields, Pack(binding.Parameters?.Pos1 ?? default), Pack(binding.Parameters?.Pos2 ?? default)));
        }

        // The viewer requires one writer per child and an acyclic, complete parent chain.
        // Reject ambiguous branches, never pick a constraint based on traversal order.
        var duplicateChildren = childClaims.Where(pair => pair.Value > 1).Select(pair => pair.Key).ToHashSet();
        var byChild = tracks.Where(t => !duplicateChildren.Contains(t.ChildPath)).ToDictionary(t => t.ChildPath);
        bool CompleteChain(ItemMotionPreviewTrack track)
        {
            var seen = new HashSet<string>();
            while (true)
            {
                if (!seen.Add(track.ChildPath) || duplicateChildren.Contains(track.ChildPath)) return false;
                if (track.ParentPath is null) return true;
                if (!byChild.TryGetValue(track.ParentPath, out var parent)) return false;
                track = parent;
            }
        }
        tracks.RemoveAll(track =>
        {
            if (CompleteChain(track)) return false;
            diagnostics.Add($"{track.Path}: Ambiguous, cyclic or unsupported parent chain; authored data preserved.");
            return true;
        });
        return new(tracks, diagnostics);
    }

    public static (List<ItemMotionSlot> Slots, string? Error, ItemMotionStatus Status) CollectSlots(ItemSceneResult scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var rootPrefab = scene.Handles.Prefabs.FirstOrDefault(p => !p.Path.Contains("/ent:", StringComparison.Ordinal))
            ?? scene.Handles.Prefabs.FirstOrDefault();
        if (rootPrefab is null)
            return (new List<ItemMotionSlot>(), "Scene contains no prefabs.", ItemMotionStatus.Absent);
        return ItemMotionBindings.CollectSlots(rootPrefab.Source, rootPrefab.Path);
    }

    public static ItemMotionBinding Resolve(NPlugDyna_SKinematicConstraint constraint, ItemSceneResult scene,
        NPlugDyna_SPrefabConstraintParams? parameters)
    {
        ArgumentNullException.ThrowIfNull(constraint);
        ArgumentNullException.ThrowIfNull(scene);
        var rootPrefab = scene.Handles.Prefabs.FirstOrDefault(p => !p.Path.Contains("/ent:", StringComparison.Ordinal))
            ?? scene.Handles.Prefabs.FirstOrDefault();
        if (rootPrefab is null)
        {
            var absent = new ItemMotionTarget(0, null, null, ItemMotionStatus.Absent, false, "Scene contains no prefabs.");
            return new(absent, absent, Array.Empty<ItemMotionSlot>(), ItemMotionStatus.Absent, absent.Reason) { Source = constraint };
        }
        var (slots, tableError, tableStatus) = CollectSlots(scene);
        return ItemMotionBindings.Resolve(constraint, slots, parameters, rootPrefab.Path, tableError, tableStatus);
    }

    private static float[] Pack(Matrix4x4 m) => new[] { m.M11, m.M12, m.M13, m.M14,
        m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44 };

    private static float[] Pack(GBX.NET.Vec3 v) => new[] { v.X, v.Y, v.Z };
}
