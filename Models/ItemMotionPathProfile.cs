using System.Numerics;
using KC = GBX.NET.Engines.Meta.NPlugDyna_SKinematicConstraint;

namespace TM_Item_Studio.Models;

public sealed record MotionPathRotation(float Yaw = 0, float Pitch = 0, float Roll = 0);

/// <summary>One shared compiler for draft playback and native export.</summary>
public static class ItemMotionPathProfile
{
    public static string? Validate(MotionPathPlan p)
    {
        if (p.Legs is null) return "Missing route segments.";
        if (p.RampDetail is < 1 or > 16) return "Ramp detail must be 1–16.";
        if (p.SpeedPoints is {} speeds && (speeds.Length != p.Legs.Count + 1 || speeds.Any(v => !float.IsFinite(v) || v < .1f || v > 1000)))
            return "Set a speed of 0.1–1000 m/s at every route point.";
        if (p.Rotations is {} angles && (angles.Length != p.Legs.Count + 1 || angles.Any(r => r is null || !float.IsFinite(r.Yaw) || !float.IsFinite(r.Pitch) || !float.IsFinite(r.Roll) || Math.Max(Math.Abs(r.Yaw), Math.Max(Math.Abs(r.Pitch), Math.Abs(r.Roll))) > 36000)))
            return "Set finite yaw, pitch and roll at every route point (maximum 100 turns).";
        if (p.Use == MotionPathUse.Obstacle && (p.SpeedPoints is not null || p.Rotations is not null))
            return "Straight obstacle mode uses one native driver. Choose scenery or experimental collision relay for speed curves and route rotation.";
        if (p.SpeedPoints is not null && p.PauseMs != 0) return "Speed ramps require zero waypoint pause. Use segment timing for stop-and-go movement.";
        if (p.Repeat != MotionPathRepeat.Reset && p.SpeedPoints is {} s && Math.Abs(s[0] - s[^1]) > .001f)
            return "A looping speed curve must end at its starting speed.";
        if (p.Repeat != MotionPathRepeat.Reset && p.Rotations is {} r &&
            new[]{r[^1].Yaw-r[0].Yaw,r[^1].Pitch-r[0].Pitch,r[^1].Roll-r[0].Roll}.Any(d=>Math.Abs(Math.IEEERemainder(d,360))>.01))
            return "Looping rotations must end at the starting orientation (whole turns are allowed).";
        return null;
    }

    public static ItemMotionResult<MotionPathPlan> Compile(MotionPathPlan p)
    {
        ItemMotionResult<MotionPathPlan> Fail(string s) => ItemMotionResult<MotionPathPlan>.Fail(ItemMotionStatus.Invalid, s);
        var valid = ItemMotionPath.Validate(p);
        if (!valid.Success) return Fail(valid.Reason!);
        if (p.SpeedPoints is null) return ItemMotionResult<MotionPathPlan>.Ok(p);
        var legs = new List<MotionPathLeg>();
        var rotations = p.Rotations is null ? null : new List<MotionPathRotation> { p.Rotations[0] };
        long elapsed = p.StartDelayMs;
        int detail = Math.Min(p.RampDetail, ItemMotionPath.MaxLegs / p.Legs.Count);
        for (int i = 0; i < p.Legs.Count; i++)
        {
            double a = p.SpeedPoints[i], b = p.SpeedPoints[i + 1], length = p.Legs[i].Delta.Length();
            double Time(double from, double to) => Math.Abs(b - a) < .00001 ? length * (to - from) / a : length / (b - a) * Math.Log((a + (b - a) * to) / (a + (b - a) * from));
            int count = Math.Abs(a - b) < .00001 ? 1 : detail;
            // Respect the proven minimum native duration, reducing sampling when necessary.
            while (count > 1 && Enumerable.Range(0, count).Any(j => Time(j / (double)count, (j + 1d) / count) < .08 || length / count < .01)) count--;
            for (int j = 0; j < count; j++)
            {
                double ms = Math.Round(Time(j / (double)count, (j + 1d) / count) * 1000);
                if (ms < 80 || ms > int.MaxValue || elapsed + ms > int.MaxValue) return Fail("Speed curve needs segments of at least 80 ms within the native timer range. Reduce speed.");
                legs.Add(new(p.Legs[i].Delta / count, (int)elapsed, (int)ms, i));
                elapsed += (int)ms;
                if (rotations is not null) rotations.Add(Lerp(p.Rotations![i], p.Rotations[i + 1], (j + 1f) / count));
            }
        }
        return ItemMotionPath.Validate(p with { Legs = legs, DurationMs = (int)elapsed, SpeedPoints = null, Rotations = rotations?.ToArray(), Ease = KC.AnimEase.Linear });
    }

    public static MotionPathRotation Lerp(MotionPathRotation a, MotionPathRotation b, float t) => new(a.Yaw + (b.Yaw-a.Yaw)*t, a.Pitch + (b.Pitch-a.Pitch)*t, a.Roll + (b.Roll-a.Roll)*t);

    /// <summary>Show effective timings on the original editable route, without replacing its points.</summary>
    public static MotionPathPlan DisplaySchedule(MotionPathPlan p)
    {
        if(p.SpeedPoints is null) return p;
        var c=Compile(p);
        if(!c.Success) return p;
        var groups=c.Value!.Legs.GroupBy(l=>l.SourceIndex).ToDictionary(g=>g.Key,g=>g.ToArray());
        return p with {DurationMs=c.Value.DurationMs,Legs=p.Legs.Select((l,i)=>l with {StartMs=groups[i][0].StartMs,MoveMs=groups[i].Sum(s=>s.MoveMs)}).ToArray()};
    }

    public static MotionPathRotation[] FollowDirection(MotionPathPlan p)
    {
        var result = new MotionPathRotation[p.Legs.Count + 1];
        for (int i = 0; i < result.Length; i++)
        {
            var before = Vector3.Normalize(p.Legs[Math.Max(0,i-1)].Delta);
            var after = Vector3.Normalize(p.Legs[Math.Min(i,p.Legs.Count-1)].Delta);
            if (p.Repeat != MotionPathRepeat.Reset && (i == 0 || i == p.Legs.Count)) { before = Vector3.Normalize(p.Legs[^1].Delta); after = Vector3.Normalize(p.Legs[0].Delta); }
            var direction = before + after;
            direction = direction.LengthSquared() < .0001f ? after : Vector3.Normalize(direction);
            float yaw = MathF.Atan2(-direction.Z,direction.X)*180/MathF.PI;
            if(i>0) { while(yaw-result[i-1].Yaw>180) yaw-=360; while(yaw-result[i-1].Yaw< -180) yaw+=360; }
            result[i] = new(yaw,MathF.Asin(Math.Clamp(direction.Y,-1,1))*180/MathF.PI,0);
        }
        return result;
    }
}
