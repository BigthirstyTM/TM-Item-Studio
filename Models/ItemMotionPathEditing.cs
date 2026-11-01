using System.Numerics;

namespace TM_Item_Studio.Models;

/// <summary>Edits the complete schedule, keeping all following channels synchronized.</summary>
public static class ItemMotionPathEditing
{
    public static ItemMotionResult<MotionPathPlan> ChangeDuration(MotionPathPlan plan, int index, int milliseconds)
    {
        if (plan.SpeedPoints is not null)
            return ItemMotionResult<MotionPathPlan>.Fail(ItemMotionStatus.Invalid, "Edit the speed curve or disable it before changing segment durations.");
        if (index < 0 || index >= plan.Legs.Count || milliseconds < 80)
            return ItemMotionResult<MotionPathPlan>.Fail(ItemMotionStatus.Invalid, "Choose a segment duration of at least 80 ms.");
        var legs = plan.Legs.ToArray();
        legs[index] = legs[index] with { MoveMs = milliseconds };
        // The native straight obstacle uses the same channel in reverse.
        if (plan.Use == MotionPathUse.Obstacle && legs.Length == 2)
            legs[1-index] = legs[1-index] with { MoveMs = milliseconds };
        long elapsed = plan.PauseMs + plan.StartDelayMs;
        for (var i = 0; i < legs.Length; i++)
        {
            if (elapsed + legs[i].MoveMs + plan.PauseMs > int.MaxValue)
                return ItemMotionResult<MotionPathPlan>.Fail(ItemMotionStatus.Invalid, "The route exceeds the native timer range.");
            legs[i] = legs[i] with { StartMs = (int)elapsed };
            elapsed += legs[i].MoveMs + (long)plan.PauseMs;
        }
        if (plan.Repeat != MotionPathRepeat.Reset) elapsed -= plan.PauseMs;
        return ItemMotionResult<MotionPathPlan>.Ok(plan with { Legs = legs, DurationMs = (int)elapsed });
    }

    public static ItemMotionResult<MotionPathPlan> ChangeRelaySettings(MotionPathPlan plan, int delayMs, float parking)
    {
        var delta = (long)delayMs - plan.StartDelayMs;
        if (plan.Use != MotionPathUse.CollisionRelay || delayMs is < 0 or > 60000 || plan.DurationMs + delta > int.MaxValue)
            return ItemMotionResult<MotionPathPlan>.Fail(ItemMotionStatus.Invalid, "Choose a collision start delay between 0 and 60 seconds.");
        var changed = plan with { StartDelayMs = delayMs, ParkingDistance = parking,
            DurationMs = (int)(plan.DurationMs + delta),
            Legs = plan.Legs.Select(l => l with { StartMs = (int)(l.StartMs + delta) }).ToArray() };
        return ItemMotionPath.Validate(changed);
    }

    public static Vector3[] Points(MotionPathPlan plan)
    {
        var result = new Vector3[plan.Legs.Count + 1]; result[0] = plan.Start;
        for (var i = 0; i < plan.Legs.Count; i++) result[i+1] = result[i] + plan.Legs[i].Delta;
        return result;
    }

    public static ItemMotionResult<MotionPathPlan> MovePoint(MotionPathPlan plan, int index, Vector3 point)
    {
        var points = Points(plan);
        if (index < 0 || index >= points.Length || !float.IsFinite(point.X) || !float.IsFinite(point.Y)
            || !float.IsFinite(point.Z) || point.Length() > 10000)
            return ItemMotionResult<MotionPathPlan>.Fail(ItemMotionStatus.Invalid, "Use finite coordinates within 10,000 metres.");
        points[index] = point;
        if (plan.Repeat == MotionPathRepeat.Closed || plan.Repeat == MotionPathRepeat.PingPong)
        {
            if (index == 0) points[^1] = point;
            if (index == points.Length - 1) points[0] = point;
            if (plan.Repeat == MotionPathRepeat.PingPong) points[points.Length-1-index] = point;
        }
        var legs = plan.Legs.Select((leg,i)=>leg with { Delta = points[i+1]-points[i] }).ToArray();
        if (legs.Any(l=>l.Delta.Length()<.01f))
            return ItemMotionResult<MotionPathPlan>.Fail(ItemMotionStatus.Invalid, "Neighbouring route points must be at least 1 cm apart.");
        return ItemMotionResult<MotionPathPlan>.Ok(plan with { Start = points[0], Legs = legs });
    }
}
