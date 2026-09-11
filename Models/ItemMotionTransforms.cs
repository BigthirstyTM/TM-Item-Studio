using System.Numerics;

namespace TM_Item_Studio.Models;

public static class ItemMotionTransforms
{
    /// <summary>
    /// Row-vector convention: restRelative = childRestWorld * inverse(parentRestWorld),
    /// world = signal * restRelative * parentLiveWorld. For a world parent supply the owning
    /// prefab occurrence's world transform as both parent matrices. Framing belongs outside this transform.
    /// This is a visual pose; chained collision and renderer-deferred parent lag are not simulated.
    /// </summary>
    public static ItemMotionResult<Matrix4x4> ComposeVisual(Matrix4x4 childRestWorld,
        Matrix4x4 parentRestWorld, Matrix4x4 parentLiveWorld, Matrix4x4 signal)
    {
        foreach (var (name, matrix) in new[] { ("child rest", childRestWorld), ("parent rest", parentRestWorld),
            ("parent live", parentLiveWorld), ("signal", signal) })
            if (!IsRigid(matrix)) return ItemMotionResult<Matrix4x4>.Fail(ItemMotionStatus.Invalid, $"Invalid {name} rigid transform.");
        if (!Matrix4x4.Invert(parentRestWorld, out var inverse))
            return ItemMotionResult<Matrix4x4>.Fail(ItemMotionStatus.Invalid, "Parent rest is singular.");
        var world = signal * childRestWorld * inverse * parentLiveWorld;
        return IsFinite(world) ? ItemMotionResult<Matrix4x4>.Ok(world)
            : ItemMotionResult<Matrix4x4>.Fail(ItemMotionStatus.Invalid, "Composed transform overflowed.");
    }

    public static bool IsRigid(Matrix4x4 m)
    {
        if (!IsFinite(m) || MathF.Abs(m.M14) > 1e-5 || MathF.Abs(m.M24) > 1e-5 || MathF.Abs(m.M34) > 1e-5 || MathF.Abs(m.M44 - 1) > 1e-5) return false;
        var x = new Vector3(m.M11, m.M12, m.M13);
        var y = new Vector3(m.M21, m.M22, m.M23);
        var z = new Vector3(m.M31, m.M32, m.M33);
        return MathF.Abs(x.LengthSquared() - 1) < 1e-4 && MathF.Abs(y.LengthSquared() - 1) < 1e-4
            && MathF.Abs(z.LengthSquared() - 1) < 1e-4 && MathF.Abs(Vector3.Dot(x, y)) < 1e-4
            && MathF.Abs(Vector3.Dot(y, z)) < 1e-4 && MathF.Abs(Vector3.Dot(x, z)) < 1e-4
            && Vector3.Dot(Vector3.Cross(x, y), z) > .9999f;
    }

    public static bool IsFinite(Matrix4x4 m) =>
        float.IsFinite(m.M11) && float.IsFinite(m.M12) && float.IsFinite(m.M13) && float.IsFinite(m.M14)
        && float.IsFinite(m.M21) && float.IsFinite(m.M22) && float.IsFinite(m.M23) && float.IsFinite(m.M24)
        && float.IsFinite(m.M31) && float.IsFinite(m.M32) && float.IsFinite(m.M33) && float.IsFinite(m.M34)
        && float.IsFinite(m.M41) && float.IsFinite(m.M42) && float.IsFinite(m.M43) && float.IsFinite(m.M44);
}
