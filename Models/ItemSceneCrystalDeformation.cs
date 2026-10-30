using System.Numerics;
using GBX.NET;
using GBX.NET.Engines.Plug;

namespace TM_Item_Studio.Models;

/// <summary>
/// Native-measured planar, single-segment crystal bend. Frames use their Z axis
/// along the curve. Unsupported control layouts remain explicit diagnostics.
/// This evaluator never modifies the authored crystal or its control frames.
/// </summary>
internal sealed class CrystalDeformation
{
    private readonly Vector3 start, end, control1, control2, right, up, forward;
    private readonly float sourceLength;

    private CrystalDeformation(Vector3 start, Vector3 end, Vector3 right, Vector3 up,
        Vector3 forward, Vector3 endForward, float sourceLength, float handleLength)
    {
        this.start = start; this.end = end; this.right = right; this.up = up;
        this.forward = forward; this.sourceLength = sourceLength;
        control1 = start + forward * handleLength;
        control2 = end - endForward * handleLength;
    }

    internal static bool TryCreate(CPlugCrystal.DeformationLayer layer, out CrystalDeformation? result)
    {
        result = null;
        if (layer.DeformationVersion != 0 || layer.ModifierVersion != 0 || layer.Transforms is not { Length: 1 })
            return false;
        var a = layer.Transform; var b = layer.Transforms[0];
        var start = new Vector3(a.TX, a.TY, a.TZ); var end = new Vector3(b.TX, b.TY, b.TZ);
        var right = new Vector3(a.XX, a.YX, a.ZX); var up = new Vector3(a.XY, a.YY, a.ZY);
        var forward = new Vector3(a.XZ, a.YZ, a.ZZ);
        var endRight = new Vector3(b.XX, b.YX, b.ZX); var endUp = new Vector3(b.XY, b.YY, b.ZY);
        var endForward = new Vector3(b.XZ, b.YZ, b.ZZ);
        var box = layer.Box;
        var center = new Vector3(box.X, box.Y, box.Z);
        var half = new Vector3(box.X2, box.Y2, box.Z2);
        if (!Finite(start) || !Finite(end) || !Finite(center) || !Finite(half)
            || half.X <= 0 || half.Y <= 0 || half.Z <= 0
            || !Frame(right, up, forward) || !Frame(endRight, endUp, endForward)) return false;
        right = Vector3.Normalize(right); up = Vector3.Normalize(up); forward = Vector3.Normalize(forward);
        endForward = Vector3.Normalize(endForward);
        if (Vector3.Distance(up, endUp) > .0001f) return false; // Twist/3D paths need separate native proof.
        var halfLength = Vector3.Dot(Vector3.Abs(forward), half);
        if (Vector3.Distance(center, start + forward * halfLength) > .01f) return false;
        var delta = end - start; var distance = delta.Length();
        if (!float.IsFinite(distance) || distance < .0001f || MathF.Abs(Vector3.Dot(delta, up)) > .01f) return false;
        var cosine = Math.Clamp(Vector3.Dot(forward, endForward), -1f, 1f);
        if (cosine < -.99f) return false;
        var bisector = Vector3.Normalize(forward + endForward);
        if (Vector3.Distance(Vector3.Normalize(delta), bisector) > .0001f) return false;
        // Circular-arc cubic: h = (4/3) r tan(angle/4), expressed using the chord.
        var handleLength = distance * (2f / 3f) / (1f + MathF.Sqrt((1f + cosine) * .5f));
        result = new(start, end, right, up, forward, endForward, 2 * halfLength, handleLength);
        return true;
    }

    internal Vec3 Apply(Vec3 point)
    {
        var offset = new Vector3(point.X, point.Y, point.Z) - start;
        var t = Math.Clamp(Vector3.Dot(offset, forward) / sourceLength, 0f, 1f);
        var u = 1 - t;
        var center = u * u * u * start + 3 * u * u * t * control1 + 3 * u * t * t * control2 + t * t * t * end;
        var tangent = Vector3.Normalize(3 * u * u * (control1 - start) + 6 * u * t * (control2 - control1) + 3 * t * t * (end - control2));
        var x = Vector3.Normalize(Vector3.Cross(up, tangent));
        var y = Vector3.Cross(tangent, x);
        var position = center + x * Vector3.Dot(offset, right) + y * Vector3.Dot(offset, up);
        return new(position.X, position.Y, position.Z);
    }

    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    private static bool Frame(Vector3 x, Vector3 y, Vector3 z)
        => Finite(x) && Finite(y) && Finite(z)
        && MathF.Abs(x.LengthSquared() - 1) < .0001f && MathF.Abs(y.LengthSquared() - 1) < .0001f
        && MathF.Abs(z.LengthSquared() - 1) < .0001f && Vector3.Distance(Vector3.Cross(x, y), z) < .0001f;
}
