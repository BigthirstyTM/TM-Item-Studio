using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.MetaNotPersistent;
using GBX.NET.Engines.Plug;

namespace TM_Item_Studio.Models;

/// <summary>Validated edits of explicitly selected authored records. No graph discovery or cloning.</summary>
public static class ItemEdits
{
    public const string SharedSourceScope = "Changes affect every occurrence referencing this authored source record.";
    public const string OwnerPositionScope = "Position changes move the selected owning entry and all of its contents in every occurrence of that entry.";

    public readonly record struct Pivot(Vec3 Position, Quat Rotation);

    /// <summary>Read without inventing an origin pivot or repairing absent/mismatched rotations.</summary>
    public static Pivot[] ReadPivots(CGameItemPlacementParam placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        var positions = placement.PivotPositions ?? [];
        var rotations = placement.PivotRotations ?? [];
        if (positions.Length != rotations.Length)
            throw new NotSupportedException("Pivot positions and rotations are not paired. Repair requires an explicit orientation decision; no edit was applied.");
        var result = new Pivot[positions.Length];
        for (var i = 0; i < result.Length; i++)
        {
            ValidatePosition(positions[i]);
            ValidateRotation(rotations[i]);
            result[i] = new(positions[i], rotations[i]);
        }
        return result;
    }

    public static void AddPivot(CGameItemPlacementParam placement, Pivot pivot)
    {
        ValidatePivot(pivot);
        var previous = ReadPivots(placement);
        WritePivots(placement, [.. previous, pivot]);
    }

    public static void EditPivot(CGameItemPlacementParam placement, int index, Pivot pivot)
    {
        ValidatePivot(pivot);
        var previous = ReadPivots(placement);
        ValidateIndex(index, previous.Length);
        previous[index] = pivot;
        WritePivots(placement, previous);
    }

    /// <summary>Coordinate-only edit preserves all rotation bytes, including missing/mismatched arrays.</summary>
    public static void EditPivotPosition(CGameItemPlacementParam placement, int index, Vec3 position)
    {
        ArgumentNullException.ThrowIfNull(placement);
        ValidatePosition(position);
        var previous = placement.PivotPositions ?? [];
        ValidateIndex(index, previous.Length);
        var updated = (Vec3[])previous.Clone();
        updated[index] = position;
        EnsurePivotChunk(placement);
        placement.PivotPositions = updated;
    }

    public static void RemovePivot(CGameItemPlacementParam placement, int index)
    {
        var previous = ReadPivots(placement);
        ValidateIndex(index, previous.Length);
        WritePivots(placement, previous.Where((_, i) => i != index).ToArray());
    }

    private static void WritePivots(CGameItemPlacementParam placement, Pivot[] pivots)
    {
        // Complete validation/allocation before touching source. Existing chunks and unknown data survive.
        var positions = pivots.Select(p => p.Position).ToArray();
        var rotations = pivots.Select(p => p.Rotation).ToArray();
        EnsurePivotChunk(placement);
        placement.PivotPositions = positions;
        placement.PivotRotations = rotations;
    }

    private static void EnsurePivotChunk(CGameItemPlacementParam placement)
    {
        if (placement.Chunks.Get(0x2E020001) is { } chunk && chunk is not CGameItemPlacementParam.Chunk2E020001)
            throw new NotSupportedException("Pivot chunk is opaque; editing would overwrite unsupported data.");
        placement.CreateChunk<CGameItemPlacementParam.Chunk2E020001>();
    }

    /// <summary>RGB components are nonnegative source values (HDR is allowed), not HTML bytes.</summary>
    public static void EditLight(CPlugLightUserModel light, Vec3 color, float intensity, float distance)
    {
        ArgumentNullException.ThrowIfNull(light);
        ValidatePosition(color);
        Nonnegative(color.X, nameof(color));
        Nonnegative(color.Y, nameof(color));
        Nonnegative(color.Z, nameof(color));
        Nonnegative(intensity, nameof(intensity));
        Nonnegative(distance, nameof(distance));
        // An existing model must have its serializer chunk; silently creating one would invent version metadata.
        if (light.Chunks.Get<CPlugLightUserModel.Chunk090F9000>() is null)
            throw new NotSupportedException("Light has no supported serialization chunk; no edit was applied.");
        light.Color = color;
        light.Intensity = intensity;
        light.Distance = distance;
    }

    /// <summary>Move an explicit prefab entry in its parent's local coordinates, never a light model.</summary>
    public static void EditOwnerPosition(CPlugPrefab owner, CPlugPrefab.EntRef entry, Vec3 position)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(entry);
        ValidatePosition(position);
        if (owner.Ents is null || !owner.Ents.Any(e => ReferenceEquals(e, entry)))
            throw new ArgumentException("The entry is not owned by the supplied prefab.", nameof(entry));
        ValidateRotation(entry.Rotation);
        entry.Position = position;
    }

    /// <summary>
    /// Edit an explicitly selected Solid2 Lights record's stored U05 translation.
    /// This does not resolve LightInst.SocketIndex or assert a relationship to LightUserModels.
    /// </summary>
    public static void EditLightTransform(CPlugSolid2Model owner, CPlugSolid2Model.Light record, Vec3 position)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(record);
        ValidatePosition(position);
        if (owner.Lights is null || !owner.Lights.Any(l => ReferenceEquals(l, record)))
            throw new ArgumentException("The transform record is not owned by the supplied solid.", nameof(record));
        if (owner.Chunks.Get<CPlugSolid2Model.Chunk090BB000>() is not { Version: >= 8 })
            throw new NotSupportedException("The solid version does not serialize light transform records.");
        var m = record.U05;
        ValidateMatrix(m);
        record.U05 = m with { TX = position.X, TY = position.Y, TZ = position.Z };
    }

    /// <summary>Apply a tag patch: null values remove keys; all unmentioned keys are preserved.</summary>
    public static void EditVariant(NPlugItem_SVariantList owner, NPlugItem_SVariant variant,
        IReadOnlyDictionary<string, string?> tagChanges, bool? hiddenInManualCycle = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(variant);
        ArgumentNullException.ThrowIfNull(tagChanges);
        if (owner.Variants is null || !owner.Variants.Any(v => ReferenceEquals(v, variant)))
            throw new ArgumentException("The variant is not owned by the supplied list.", nameof(variant));
        if (owner.Version is < 0 or > 1)
            throw new NotSupportedException("Unsupported variant list version.");
        if (hiddenInManualCycle.HasValue && owner.Version < 1)
            throw new NotSupportedException("Version-zero lists do not serialize HiddenInManualCycle. No metadata or version was changed.");
        if (variant.Tags is null)
            throw new NotSupportedException("Variant has no tag dictionary.");
        var updated = new Dictionary<string, string>(variant.Tags, variant.Tags.Comparer);
        foreach (var (key, value) in tagChanges)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("Tag keys must be nonempty.", nameof(tagChanges));
            if (value is null) updated.Remove(key);
            else updated[key] = value;
        }
        variant.Tags = updated;
        if (hiddenInManualCycle.HasValue) variant.HiddenInManualCycle = hiddenInManualCycle.Value;
    }

    private static void ValidatePivot(Pivot pivot)
    {
        ValidatePosition(pivot.Position);
        ValidateRotation(pivot.Rotation);
    }

    private static void ValidatePosition(Vec3 value)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            throw new ArgumentException("Coordinates must be finite.");
    }

    private static void ValidateRotation(Quat value)
    {
        double norm = (double)value.X * value.X + (double)value.Y * value.Y + (double)value.Z * value.Z + (double)value.W * value.W;
        if (!double.IsFinite(norm) || Math.Abs(norm - 1) > 0.0001)
            throw new ArgumentException("Rotation must be a finite unit quaternion in X,Y,Z,W order; no normalization was applied.");
    }

    private static void ValidateMatrix(Iso4 m)
    {
        float[] values = [m.XX, m.XY, m.XZ, m.YX, m.YY, m.YZ, m.ZX, m.ZY, m.ZZ, m.TX, m.TY, m.TZ];
        double determinant = (double)m.XX * ((double)m.YY * m.ZZ - (double)m.YZ * m.ZY)
            - (double)m.XY * ((double)m.YX * m.ZZ - (double)m.YZ * m.ZX)
            + (double)m.XZ * ((double)m.YX * m.ZY - (double)m.YY * m.ZX);
        if (values.Any(v => !float.IsFinite(v)) || !double.IsFinite(determinant) || determinant == 0)
            throw new ArgumentException("Owner transform must be finite and nonsingular.");
    }

    private static void Nonnegative(float value, string name)
    {
        if (!float.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(name, "Value must be finite and nonnegative; zero is supported.");
    }

    private static void ValidateIndex(int index, int length)
    {
        if (index < 0 || index >= length) throw new ArgumentOutOfRangeException(nameof(index));
    }
}
