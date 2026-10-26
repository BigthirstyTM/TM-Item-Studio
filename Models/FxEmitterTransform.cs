using GBX.NET.Engines.Plug;
using System.Globalization;

namespace TM_Item_Studio.Models;

/// <summary>
/// Item-space emission points for the validated static-object plus FX prefab.
/// The native expressions live in the FX owner's frame, which may be rotated.
/// A strict temporary parse proves that frame belongs to prefab entity one;
/// only FxEmissionPoint may write the original opaque item bytes.
/// </summary>
public static class FxEmitterTransform
{
    const double MaxCoordinate = 10000;
    const double QuaternionNormTolerance = .001;

    public sealed record Rotation(float X, float Y, float Z, float W);
    public sealed record OwnerTransform(FxEmissionPoint.Coordinates Position, Rotation Rotation);
    public sealed record Inspection(OwnerTransform Owner, FxEmissionPoint.Coordinates Primary,
        IReadOnlyList<FxEmissionPoint.Coordinates> Emitters, FxEmissionPoint.Inspection Native,
        int OwnerRecordOffset);
    public sealed record Result(byte[] ItemBytes, Inspection Before, Inspection After,
        FxEmissionPoint.Evidence Proof, bool NativeOffsetWritten);

    public static Inspection Read(ReadOnlyMemory<byte> itemBytes)
    {
        var native = FxEmissionPoint.Read(itemBytes);
        Require(native.Emitters.All(emitter => IsZeroWorldOffset(emitter.WorldOffsetExpression)),
            "Item-space emission points require constant zero WorldOffsetExpr values on all emitters.");
        // This copy removes precisely the validated FX node, retains its owner
        // record, and strictly parses the original surrounding prefab/mesh.
        // Its typed nodes are an inspection surface and never an export source.
        var preview = FxMeshPreview.Read(itemBytes);
        Require(preview.Proof.PreviewOnly && preview.Proof.OriginalBodyRestorableExactly
            && preview.Proof.MeshPrefixPreservedExactly && preview.Proof.PrefabEntries == 2,
            "The FX owner requires an exactly reversible, validated prefab inspection.");
        var prefab = preview.PreviewRoot;
        Require(prefab.Ents is { Length: 2 }
            && prefab.Ents[0].Model is CPlugStaticObjectModel
            && prefab.Ents[1].Model is null && prefab.Ents[1].ModelFile is null,
            "The supported FX owner must be the second entry of a direct two-entry prefab.");
        var entry = prefab.Ents[1];
        Require(entry.Params is null && string.IsNullOrEmpty(entry.U01),
            "FX owners with additional entity parameters are not supported.");
        var owner = new OwnerTransform(new(entry.Position.X, entry.Position.Y, entry.Position.Z),
            new(entry.Rotation.X, entry.Rotation.Y, entry.Rotation.Z, entry.Rotation.W));
        ValidateOwner(owner);
        var points = native.Emitters.Select(emitter => LocalToItem(owner, emitter.Position)).ToArray();
        var recordOffset = checked(native.FxGraphOffset - 4 + preview.Proof.RemovedFxBytes);
        Require(recordOffset > native.FxGraphOffset, "Invalid FX owner record boundary.");
        return new(owner, points[0], Array.AsReadOnly(points), native, recordOffset);
    }

    public static bool TryRead(ReadOnlyMemory<byte> itemBytes, out Inspection? inspection, out string? error)
    {
        try { inspection = Read(itemBytes); error = null; return true; }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { inspection = null; error = exception.Message; return false; }
    }

    public static FxEmissionPoint.Coordinates LocalToItem(OwnerTransform owner,
        FxEmissionPoint.Coordinates local)
    {
        var matrix = ValidateOwner(owner);
        ValidateCoordinates(local, "FX-local point");
        return Point(
            matrix.XX * local.X + matrix.XY * local.Y + matrix.XZ * local.Z + owner.Position.X,
            matrix.YX * local.X + matrix.YY * local.Y + matrix.YZ * local.Z + owner.Position.Y,
            matrix.ZX * local.X + matrix.ZY * local.Y + matrix.ZZ * local.Z + owner.Position.Z,
            "Item-space point");
    }

    public static FxEmissionPoint.Coordinates ItemToLocal(OwnerTransform owner,
        FxEmissionPoint.Coordinates item)
    {
        var matrix = ValidateOwner(owner);
        ValidateCoordinates(item, "Item-space point");
        // A normalized quaternion is orthogonal: its inverse is its transpose.
        var x = (double)item.X - owner.Position.X;
        var y = (double)item.Y - owner.Position.Y;
        var z = (double)item.Z - owner.Position.Z;
        return Point(matrix.XX * x + matrix.YX * y + matrix.ZX * z,
            matrix.XY * x + matrix.YY * y + matrix.ZY * z,
            matrix.XZ * x + matrix.YZ * y + matrix.ZZ * z, "FX-local point");
    }

    public static Result Export(ReadOnlyMemory<byte> itemBytes, FxEmissionPoint.Coordinates itemPrimary)
    {
        ValidateCoordinates(itemPrimary, "Requested item-space emission point");
        var before = Read(itemBytes);
        // Preserve the exact original native expressions on a UI no-op; inverse
        // rotation of rounded display coordinates would otherwise add noise.
        var local = itemPrimary == before.Primary ? before.Native.Primary
            : ItemToLocal(before.Owner, itemPrimary);
        var native = FxEmissionPoint.Export(itemBytes, local);
        var after = Read(native.ItemBytes);
        Require(after.Owner == before.Owner, "Editing the emission point changed its prefab owner transform.");
        Require(Close(after.Primary, itemPrimary), "The native expression did not resolve to the requested item-space point.");
        Require(after.Emitters.Count == before.Emitters.Count, "Editing the emission point changed the emitter count.");
        var dx = (double)itemPrimary.X - before.Primary.X;
        var dy = (double)itemPrimary.Y - before.Primary.Y;
        var dz = (double)itemPrimary.Z - before.Primary.Z;
        for (var index = 0; index < before.Emitters.Count; index++)
        {
            var expected = Point(before.Emitters[index].X + dx, before.Emitters[index].Y + dy,
                before.Emitters[index].Z + dz, "Translated item-space emitter");
            Require(Close(after.Emitters[index], expected),
                "Moving the primary point changed a secondary emitter's relative item-space position.");
        }
        return new(native.ItemBytes, before, after, native.Proof, native.NativeOffsetWritten);
    }

    static Matrix ValidateOwner(OwnerTransform owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(owner.Rotation);
        ValidateCoordinates(owner.Position, "FX owner position");
        var raw = owner.Rotation;
        double x = raw.X, y = raw.Y, z = raw.Z, w = raw.W;
        var normSquared = x * x + y * y + z * z + w * w;
        Require(double.IsFinite(normSquared) && Math.Abs(normSquared - 1) <= QuaternionNormTolerance,
            "The FX owner must have a finite unit quaternion (within native float tolerance).");
        var scale = 1 / Math.Sqrt(normSquared);
        x *= scale; y *= scale; z *= scale; w *= scale;
        return new(1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w),
            2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w),
            2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y));
    }

    static void ValidateCoordinates(FxEmissionPoint.Coordinates point, string label)
    {
        ArgumentNullException.ThrowIfNull(point);
        Require(float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z)
            && Math.Abs(point.X) <= MaxCoordinate && Math.Abs(point.Y) <= MaxCoordinate
            && Math.Abs(point.Z) <= MaxCoordinate,
            $"{label} must contain finite coordinates between -10000 and 10000 metres.");
    }
    static FxEmissionPoint.Coordinates Point(double x, double y, double z, string label)
    {
        var point = new FxEmissionPoint.Coordinates((float)x, (float)y, (float)z);
        ValidateCoordinates(point, label); return point;
    }
    static bool Close(FxEmissionPoint.Coordinates actual, FxEmissionPoint.Coordinates expected)
        => Near(actual.X, expected.X) && Near(actual.Y, expected.Y) && Near(actual.Z, expected.Z);
    static bool IsZeroWorldOffset(string? expression)
    {
        var text = expression?.Trim();
        if (text is null || !text.StartsWith("float3(", StringComparison.Ordinal) || !text.EndsWith(')')) return false;
        var components = text[7..^1].Split(',');
        return components.Length is 1 or 3 && components.All(component =>
            double.TryParse(component.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && value == 0);
    }
    static bool Near(float actual, float expected)
        => Math.Abs((double)actual - expected) <= .00002 + .0000004 * Math.Max(Math.Abs(actual), Math.Abs(expected));
    static void Require(bool condition, string message)
    { if (!condition) throw new InvalidDataException(message); }

    readonly record struct Matrix(double XX, double XY, double XZ, double YX, double YY, double YZ,
        double ZX, double ZY, double ZZ);
}
