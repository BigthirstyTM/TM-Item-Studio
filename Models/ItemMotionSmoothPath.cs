using System.Numerics;
using KC = GBX.NET.Engines.Meta.NPlugDyna_SKinematicConstraint;

namespace TM_Item_Studio.Models;

/// <summary>Rounds control-polygon corners with quadratic Beziers, then compiles
/// a bounded native polyline. The control polygon remains unchanged.</summary>
public static class ItemMotionSmoothPath
{
    public static ItemMotionResult<MotionPathPlan> Plan(IReadOnlyList<Vector3> points,
        MotionPathRepeat repeat, float speed, float smoothness, int subdivisions)
    {
        ItemMotionResult<MotionPathPlan> Fail(string reason) => ItemMotionResult<MotionPathPlan>.Fail(ItemMotionStatus.Invalid, reason);
        if (!float.IsFinite(smoothness) || smoothness < 0 || smoothness > 1 || subdivisions is < 2 or > 16)
            return Fail("Smoothness must be 0–100% and curve detail 2–16.");
        // Validate input without consuming the return budget: smoothing performs the final budget check.
        var valid = ItemMotionPath.Plan(points, MotionPathUse.Scenery, MotionPathRepeat.Reset, speed, KC.AnimEase.Linear);
        if (!valid.Success) return valid;
        if (smoothness == 0 || points.Count == 2)
            return ItemMotionPath.Plan(points, MotionPathUse.Scenery, repeat, speed, KC.AnimEase.Linear);
        var controls = points.ToList();
        var closed = repeat == MotionPathRepeat.Closed;
        if (closed && Vector3.Distance(controls[0], controls[^1]) < .01f) controls.RemoveAt(controls.Count - 1);
        if (controls.Count < 3) return Fail("A rounded loop requires three distinct control points.");
        var samples = new List<Vector3>();
        void Add(Vector3 point) { if (samples.Count == 0 || Vector3.Distance(samples[^1], point) > .001f) samples.Add(point); }
        for (var i = 0; i < controls.Count; i++)
        {
            var p = controls[i];
            if (!closed && (i == 0 || i == controls.Count - 1)) { Add(p); continue; }
            var before = controls[(i + controls.Count - 1) % controls.Count] - p;
            var after = controls[(i + 1) % controls.Count] - p;
            if (before.Length() < .01f || after.Length() < .01f) return Fail("Consecutive control points must be distinct.");
            if (MathF.Abs(Vector3.Dot(Vector3.Normalize(before), Vector3.Normalize(after))) > .9999f) { Add(p); continue; }
            var cut = MathF.Min(before.Length(), after.Length()) * .45f * smoothness;
            var entry = p + Vector3.Normalize(before) * cut;
            var exit = p + Vector3.Normalize(after) * cut;
            Add(entry);
            // Avoid emitting unplayably short keys simply because detail was increased.
            var count = subdivisions;
            Vector3 At(float t) => (1-t)*(1-t)*entry + 2*(1-t)*t*p + t*t*exit;
            while (count > 1 && Enumerable.Range(1, count).Any(k => Vector3.Distance(At((k-1f)/count), At((float)k/count)) / speed < .08f)) count--;
            for (var j = 1; j <= count; j++) Add(At((float)j/count));
        }
        var legs = closed ? samples.Count : (samples.Count - 1) * (repeat == MotionPathRepeat.PingPong ? 2 : 1);
        if (legs > ItemMotionPath.MaxLegs)
            return Fail($"Rounded route needs {legs} legs; maximum {ItemMotionPath.MaxLegs}. Reduce curve detail or use fewer control points.");
        return ItemMotionPath.Plan(samples, MotionPathUse.Scenery, repeat, speed, KC.AnimEase.Linear);
    }
}
