using System.Text.Json.Serialization;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;
using KC = GBX.NET.Engines.Meta.NPlugDyna_SKinematicConstraint;

namespace TM_Item_Studio.Models;

public sealed record ItemMotionTarget(int RawSlot, int? OriginalArrayIndex, string? Path,
    ItemMotionStatus Status, bool IsWorld, string? Reason)
{
    [JsonIgnore] public CPlugPrefab.EntRef? SourceEntry { get; init; }
}
public sealed record ItemMotionSlot(int Slot, int OriginalArrayIndex, string Path)
{
    [JsonIgnore] public CPlugPrefab.EntRef SourceEntry { get; init; } = null!;
}
public sealed record ItemMotionBinding(ItemMotionTarget Parent, ItemMotionTarget Child,
    IReadOnlyList<ItemMotionSlot> Slots, ItemMotionStatus Status, string? Reason)
{
    [JsonIgnore] public KC Source { get; init; } = null!;
    [JsonIgnore] public NPlugDyna_SPrefabConstraintParams? Parameters { get; init; }
}

/// <summary>Resolves verified flat root prefabs. Native nested flattening does not rebase constraint slots;
/// nested occurrences therefore require a separate verified flattened resolver, not leaf-local targeting.</summary>
public static class ItemMotionBindings
{
    public static string RootPath(int documentOrdinal, int? variantOrdinal)
    {
        if (documentOrdinal < 0 || variantOrdinal < 0) throw new ArgumentOutOfRangeException(nameof(documentOrdinal));
        return $"doc:{documentOrdinal}/variant:{variantOrdinal?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"}/root";
    }

    public static ItemMotionBinding Resolve(KC constraint, CPlugPrefab owner,
        NPlugDyna_SPrefabConstraintParams? parameters, string prefabInstancePath, bool isNestedPrefabOccurrence = false)
    {
        ArgumentNullException.ThrowIfNull(constraint);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefabInstancePath);
        var slots = new List<ItemMotionSlot>();
        string? tableError = null;
        var tableStatus = ItemMotionStatus.Unresolved;
        // The shared path contract marks nested entity occurrences with /ent:. The explicit flag also
        // covers callers whose nested instance is reached through a different owned edge.
        if (isNestedPrefabOccurrence || prefabInstancePath.Contains("/ent:", StringComparison.Ordinal))
        {
            tableError = "Nested prefab motion bindings are unsupported: native flattening retains raw constraint slots without rebasing. No leaf-local target is assumed.";
            tableStatus = ItemMotionStatus.Unsupported;
        }
        if (owner.Ents is null) tableError = "Owning prefab has no entity array.";
        else for (int index = 0; index < owner.Ents.Length; index++)
        {
            var entry = owner.Ents[index];
            if (entry is null) { tableError ??= "Null prefab entry prevents reliable slot-table construction."; continue; }
            if (entry.ModelFile is not null) { tableError ??= "External model prevents reliable slot classification; no resolution was attempted."; continue; }
            var model = entry.Model;
            if (model is CPlugPrefab)
            {
                tableError ??= "Nested entries require the native flattened and class-sorted slot-table mapping; leaf-local targets are not safe to infer.";
                tableStatus = ItemMotionStatus.Unsupported;
                continue;
            }
            if (model is not CPlugDynaObjectModel) continue;
            // Native classification requires the typed params AND IsKinematic. Missing params do not imply defaults.
            if (entry.Params is null) continue;
            if (entry.Params is not NPlugDynaObjectModel_SInstanceParams instance)
            { tableError ??= "Dyna entry has an unsupported instance-params type."; tableStatus = ItemMotionStatus.Unsupported; continue; }
            if (instance.Version is < 0 or > 2)
            { tableError ??= "Dyna entry has an unsupported instance-params version."; tableStatus = ItemMotionStatus.Unsupported; continue; }
            if (instance.IsKinematic)
                slots.Add(new(slots.Count, index, $"{prefabInstancePath}/ent:{index}") { SourceEntry = entry });
        }
        ItemMotionTarget Target(int raw, bool parent)
        {
            if (tableError is not null) return new(raw, null, null, tableStatus, false, tableError);
            if (raw >= 0 && raw < slots.Count)
            {
                var slot = slots[raw];
                return new(raw, slot.OriginalArrayIndex, slot.Path, ItemMotionStatus.Supported, false, null) { SourceEntry = slot.SourceEntry };
            }
            // Out-of-range parent slots produce a null/world handle. Child null handles cannot animate anything.
            if (parent) return new(raw, null, prefabInstancePath, ItemMotionStatus.Supported, true,
                "Parent slot is outside the kinematic table: owning occurrence world context.");
            return new(raw, null, null, ItemMotionStatus.Unresolved, false, "Child slot is outside the kinematic table; no fallback target.");
        }
        if (parameters is null)
        {
            var absent = new ItemMotionTarget(0, null, null, ItemMotionStatus.Absent, false, "Constraint parameters are absent; no default binding is assumed.");
            return new(absent, absent, slots.AsReadOnly(), ItemMotionStatus.Absent, absent.Reason) { Source = constraint };
        }
        var parent = Target(parameters.Ent1, true);
        var child = Target(parameters.Ent2, false);
        var status = parent.Status != ItemMotionStatus.Supported ? parent.Status : child.Status;
        string? reason = parent.Status != ItemMotionStatus.Supported ? parent.Reason : child.Reason;
        if (parameters.Version != 0) { status = ItemMotionStatus.Unsupported; reason = "Unsupported constraint-params version."; }
        if (!Finite(parameters.Pos1) || !Finite(parameters.Pos2))
        { status = ItemMotionStatus.Invalid; reason = "Constraint anchor positions contain nonfinite values."; }
        if (status == ItemMotionStatus.Supported && !parent.IsWorld && parent.OriginalArrayIndex == child.OriginalArrayIndex)
        { status = ItemMotionStatus.Unsupported; reason = "Self-parented constraints are not supported in preview."; }
        // These authored anchor vectors are retained. Their influence on native rest-pose creation is not verified.
        if (status == ItemMotionStatus.Supported && (parameters.Pos1 != default || parameters.Pos2 != default))
        { status = ItemMotionStatus.Unsupported; reason = "Nonzero constraint anchor positions are preserved but their preview semantics are unverified."; }
        return new(parent, child, slots.AsReadOnly(), status, reason) { Source = constraint, Parameters = parameters };
    }

    /// <summary>Rebind using raw filtered slots. Shared parameters and both anchor vectors are preserved.</summary>
    public static ItemMotionResult<bool> ApplyTargets(KC constraint, CPlugPrefab owner,
        NPlugDyna_SPrefabConstraintParams parameters, string prefabInstancePath, int parentSlot, int childSlot,
        bool isNestedPrefabOccurrence = false)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var candidate = new NPlugDyna_SPrefabConstraintParams
        { Version = parameters.Version, Ent1 = parentSlot, Ent2 = childSlot, Pos1 = parameters.Pos1, Pos2 = parameters.Pos2 };
        var binding = Resolve(constraint, owner, candidate, prefabInstancePath, isNestedPrefabOccurrence);
        if (binding.Status != ItemMotionStatus.Supported) return ItemMotionResult<bool>.Fail(binding.Status, binding.Reason!);
        parameters.Ent1 = parentSlot;
        parameters.Ent2 = childSlot;
        return ItemMotionResult<bool>.Ok(true);
    }

    private static bool Finite(GBX.NET.Vec3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
