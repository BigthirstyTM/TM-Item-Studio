using System.Numerics;
using GBX.NET;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;
using TmEssentials;
using KC = GBX.NET.Engines.Meta.NPlugDyna_SKinematicConstraint;

namespace TM_Item_Studio.Models;

public enum MotionPathUse { Obstacle, Scenery }
public enum MotionPathRepeat { PingPong, Closed, Reset }
public sealed record MotionPathLeg(Vector3 Delta, int StartMs, int MoveMs);
public sealed record MotionPathPlan(Vector3 Start, MotionPathUse Use, MotionPathRepeat Repeat,
    KC.AnimEase Ease, IReadOnlyList<MotionPathLeg> Legs, int DurationMs, int PauseMs);

/// <summary>
/// Compiles paths into native scalar channels. Parent motion is visual-only in the
/// measured TM2020 build: scenery uses micro shapes, never the source collider.
/// Obstacles use one world-bound body; its local X axis is aligned with the line.
/// </summary>
public static class ItemMotionPath
{
    // A tested product bound, distinct from the engine's FOUR KEYS PER CHANNEL limit.
    public const int MaxLegs = 8;

    public static ItemMotionResult<MotionPathPlan> Plan(IReadOnlyList<Vector3> points,
        MotionPathUse use, MotionPathRepeat repeat, float speed, KC.AnimEase ease, int pauseMs = 0)
    {
        ItemMotionResult<MotionPathPlan> Fail(string reason) => ItemMotionResult<MotionPathPlan>.Fail(ItemMotionStatus.Invalid, reason);
        if (!Enum.IsDefined(use) || !Enum.IsDefined(repeat) || ease is not (KC.AnimEase.Linear or KC.AnimEase.QuadIn or KC.AnimEase.QuadOut or KC.AnimEase.QuadInOut))
            return Fail("Choose a supported path mode and easing.");
        if (!float.IsFinite(speed) || speed < .1f || speed > 1000 || pauseMs < 0 || pauseMs > 60000)
            return Fail("Speed must be 0.1–1000 m/s and each pause 0–60 seconds.");
        if (points.Count < 2 || points.Count > MaxLegs + 1 || points.Any(p => !Finite(p) || p.Length() > 10000))
            return Fail($"Use 2–{MaxLegs + 1} finite points within 10,000 metres of the item origin.");
        if (use == MotionPathUse.Obstacle && points.Count != 2)
            return Fail("Obstacle paths need exactly two points. Multi-direction parent motion does not carry vehicle collision; use Scenery for a polyline.");
        var route = points.ToList();
        if (repeat == MotionPathRepeat.Closed && Vector3.Distance(route[0], route[^1]) > .001f) route.Add(route[0]);
        else if (repeat == MotionPathRepeat.PingPong) route.AddRange(points.Reverse().Skip(1));
        if (use == MotionPathUse.Scenery && route.Count - 1 > MaxLegs)
            return Fail($"This path needs {route.Count - 1} legs including the return. The tested scenery limit is {MaxLegs}; reduce points or choose Reset.");
        var legs = new List<MotionPathLeg>();
        long elapsed = pauseMs;
        for (var i = 1; i < route.Count; i++)
        {
            var delta = route[i] - route[i - 1];
            if (delta.Length() < .01f) return Fail("Consecutive points must be at least 1 cm apart.");
            var duration = Math.Round(delta.Length() / speed * 1000d);
            if (duration < 80 || duration > int.MaxValue) return Fail("Each leg must take at least 80 ms; reduce speed or increase its length.");
            if (elapsed + duration + pauseMs > int.MaxValue) return Fail("The path duration exceeds the native timer range.");
            legs.Add(new(delta, (int)elapsed, (int)duration));
            elapsed += (long)duration + pauseMs;
        }
        // A repeated cycle pauses once at the start, and once at each OTHER waypoint.
        if (repeat != MotionPathRepeat.Reset) elapsed -= pauseMs;
        return ItemMotionResult<MotionPathPlan>.Ok(new(points[0], use, repeat, ease, legs.AsReadOnly(), (int)elapsed, pauseMs));
    }

    public static ItemMotionResult<CPlugPrefab> Build(CPlugPrefab original, MotionPathPlan plan, Vector3 anchor = default)
    {
        ItemMotionResult<CPlugPrefab> Fail(string reason) => ItemMotionResult<CPlugPrefab>.Fail(ItemMotionStatus.Unsupported, reason);
        if (!Finite(anchor) || anchor.Length() > 10000 || !Enum.IsDefined(plan.Use) || !Enum.IsDefined(plan.Repeat) || !Finite(plan.Start)
            || plan.Start.Length() > 10000 || plan.PauseMs < 0 || plan.PauseMs > 60000
            || plan.Ease is not (KC.AnimEase.Linear or KC.AnimEase.QuadIn or KC.AnimEase.QuadOut or KC.AnimEase.QuadInOut)
            || plan.Legs is null || plan.Legs.Count is < 1 or > MaxLegs || plan.DurationMs <= 0)
            return Fail("Invalid motion path plan.");
        long expectedStart = plan.PauseMs;
        foreach (var leg in plan.Legs)
        {
            if (!Finite(leg.Delta) || leg.Delta.Length() < .01f || leg.Delta.Length() > 20000
                || leg.StartMs != expectedStart || leg.MoveMs < 80)
                return Fail("Invalid motion path leg or timing.");
            expectedStart += (long)leg.MoveMs + plan.PauseMs;
        }
        if (expectedStart - (plan.Repeat == MotionPathRepeat.Reset ? 0 : plan.PauseMs) != plan.DurationMs)
            return Fail("Invalid motion path cycle duration.");
        if (plan.Use == MotionPathUse.Obstacle && (plan.Legs.Count != (plan.Repeat == MotionPathRepeat.Reset ? 1 : 2)
            || (plan.Legs.Count == 2 && (plan.Legs[1].Delta != -plan.Legs[0].Delta || plan.Legs[1].MoveMs != plan.Legs[0].MoveMs))))
            return Fail("Obstacle paths require a single straight line and its optional return.");
        var entries = original.Ents;
        if (entries is null || entries.Any(e => e.Model is CPlugPrefab nested && !StaticOnly(nested, new()))
            || entries.Count(e => e.Model is CPlugDynaObjectModel) != 1 || entries.Count(e => e.Model is KC) != 1)
            return Fail("Path authoring requires one moving body and one constraint in the root prefab. Nested or multi-body animations are preserved; they are not replaced.");
        var bodyEntry = entries.Single(e => e.Model is CPlugDynaObjectModel);
        var constraintEntry = entries.Single(e => e.Model is KC);
        var source = (KC)constraintEntry.Model!;
        if (plan.Use == MotionPathUse.Obstacle && bodyEntry.Model is CPlugDynaObjectModel obstacle
            && (obstacle.StaticShape is null || obstacle.DynaShape is null))
            return Fail("Obstacle paths require the source body's collision shapes.");
        if (bodyEntry.Params is not NPlugDynaObjectModel_SInstanceParams { Version: 2, IsKinematic: true }
            || constraintEntry.Params is not NPlugDyna_SPrefabConstraintParams { Version: 0, Ent1: -1, Ent2: 0 } binding
            || binding.Pos1 != default || binding.Pos2 != default || source.Version != 0 || source.SubVersion != 3
            || bodyEntry.Model is not CPlugDynaObjectModel { Mesh: not null, LocAnim: null, DynamizeOnSpawn: false })
            return Fail("This body's binding, driver, anchors or shader animation cannot safely be replaced with a path.");
        // Clone before changing anything. Rejected builds never alter the live document.
        CPlugPrefab prefab;
        try
        {
            using var stream = new MemoryStream();
            new Gbx<CPlugPrefab>(original) { BodyCompression = GbxCompression.Uncompressed }.Save(stream);
            stream.Position = 0;
            prefab = Gbx.Parse<CPlugPrefab>(stream).Node;
        }
        catch (Exception e) { return Fail("Could not isolate the prefab for path editing: " + e.Message); }
        var body = prefab.Ents.Single(e => e.Model is CPlugDynaObjectModel);
        var oldConstraint = prefab.Ents.Single(e => e.Model is KC);
        var model = (CPlugDynaObjectModel)body.Model!;
        model.IsStatic = false;
        var newEntries = prefab.Ents.Where(e => !ReferenceEquals(e, oldConstraint)).ToList();
        body.Position = new(plan.Start.X, plan.Start.Y, plan.Start.Z);
        body.Params = Instance((NPlugDynaObjectModel_SInstanceParams)body.Params!);
        if (plan.Use == MotionPathUse.Obstacle)
        {
            var leg = plan.Legs[0];
            body.Rotation = AlignX(leg.Delta);
            var keys = new List<KC.SubAnimFunc>();
            if (plan.PauseMs > 0) keys.Add(Key(KC.AnimEase.Constant, plan.PauseMs));
            keys.Add(Key(plan.Ease, leg.MoveMs));
            if (plan.PauseMs > 0) keys.Add(Key(KC.AnimEase.Constant, plan.PauseMs, true));
            if (plan.Repeat != MotionPathRepeat.Reset) keys.Add(Key(plan.Ease, leg.MoveMs, true));
            newEntries.Add(Constraint(-1, 0, leg.Delta.Length(), keys.ToArray(), plan.DurationMs, (KC)oldConstraint.Model!));
        }
        else
        {
            var carrierModel = ItemKinematicEntityTemplate.CreateCarrierBody();
            // Native loader expects complete shapes. Keep only a 0.1 mm compatibility
            // triangle, not an invisible full-size collider left at the source position.
            model.StaticShape = carrierModel.StaticShape;
            model.DynaShape = carrierModel.DynaShape;
            for (var i = 0; i < plan.Legs.Count; i++)
            {
                var leg = plan.Legs[i];
                newEntries.Add(new() { Model = ItemKinematicEntityTemplate.CreateCarrierBody(), Rotation = AlignX(leg.Delta), Params = Instance() });
                var keys = new List<KC.SubAnimFunc>();
                if (leg.StartMs > 0) keys.Add(Key(KC.AnimEase.Constant, leg.StartMs));
                keys.Add(Key(plan.Ease, leg.MoveMs));
                var tail = plan.DurationMs - leg.StartMs - leg.MoveMs;
                if (tail > 0) keys.Add(Key(KC.AnimEase.Constant, tail, true));
                newEntries.Add(Constraint(i == 0 ? -1 : i, i + 1, leg.Delta.Length(), keys.ToArray(), plan.DurationMs));
            }
            newEntries.Add(Constraint(plan.Legs.Count, 0, 0, [Key(KC.AnimEase.Constant, plan.DurationMs)], plan.DurationMs, (KC)oldConstraint.Model!));
        }
        var rotation = body.Rotation;
        var anchorOffset = Vector3.Transform(anchor, new Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W));
        body.Position = new(plan.Start.X - anchorOffset.X, plan.Start.Y - anchorOffset.Y, plan.Start.Z - anchorOffset.Z);
        prefab.Ents = newEntries.ToArray();
        return ItemMotionResult<CPlugPrefab>.Ok(prefab);
    }

    private static CPlugPrefab.EntRef Constraint(int parent, int child, float distance, KC.SubAnimFunc[] keys, int total, KC? model = null)
    {
        // Keep the visible body's original shader animation; only replace spatial motion.
        model ??= new KC { Version = 0, SubVersion = 3 };
        model.TransAxis = KC.EAxis.X; model.TransMin = 0; model.TransMax = distance;
        model.RotAxis = KC.EAxis.Y; model.AngleMinDeg = model.AngleMaxDeg = 0;
        model.TransAnimFunc = new() { IsDuration = true, SubFuncs = keys };
        model.RotAnimFunc = new() { IsDuration = true, SubFuncs = [Key(KC.AnimEase.Constant, total)] };
        return new() { Rotation = Quat.Identity, Model = model,
            Params = new NPlugDyna_SPrefabConstraintParams { Version = 0, Ent1 = parent, Ent2 = child } };
    }
    private static NPlugDynaObjectModel_SInstanceParams Instance(NPlugDynaObjectModel_SInstanceParams? source = null) => new()
    { Version = 2, IsKinematic = true, PeriodSc = 1, PeriodScMax = -1, Phase01 = 0, Phase01Max = -1,
        TextureId = source?.TextureId ?? 0, CastStaticShadow = source?.CastStaticShadow ?? false };
    private static KC.SubAnimFunc Key(KC.AnimEase ease, int ms, bool reverse = false) => new()
    { Ease = ease, Reverse = reverse, Duration = new TimeInt32(ms) };
    public static Quat AlignX(Vector3 delta)
    {
        var d = Vector3.Normalize(delta);
        var axis = Vector3.Cross(Vector3.UnitX, d);
        var sine = axis.Length();
        var q = d.X >= -.999999f ? Quaternion.Normalize(new Quaternion(axis, 1 + d.X))
            : sine < 1e-8f ? Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI)
            : Quaternion.CreateFromAxisAngle(axis / sine, MathF.Atan2(sine, d.X));
        return new(q.X, q.Y, q.Z, q.W);
    }
    private static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
    private static bool StaticOnly(CPlugPrefab prefab, HashSet<CPlugPrefab> seen)
    {
        if (!seen.Add(prefab) || seen.Count > 100) return false;
        return prefab.Ents is not null && prefab.Ents.All(e => e.Model is not (KC or CPlugDynaObjectModel)
            && (e.Model is not CPlugPrefab nested || StaticOnly(nested, seen)));
    }
}
