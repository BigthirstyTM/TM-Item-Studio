using System.Numerics;
using KC=GBX.NET.Engines.Meta.NPlugDyna_SKinematicConstraint;
namespace TM_Item_Studio.Models;

public sealed record BezierHandle(Vector3 In,Vector3 Out);
public sealed record BezierSettings(bool Enabled=false,int Detail=8,bool Linked=true,BezierHandle[]? Handles=null);
public static class ItemMotionAuthoring
{
    public static ItemMotionResult<MotionPathPlan> Bezier(IReadOnlyList<Vector3> points,BezierSettings settings,
        MotionPathUse use,MotionPathRepeat repeat,float speed,KC.AnimEase ease,int pause,int delay,float parking)
    {
        ItemMotionResult<MotionPathPlan> Fail(string reason)=>ItemMotionResult<MotionPathPlan>.Fail(ItemMotionStatus.Invalid,reason);
        if(points.Count<2||settings.Detail is <2 or >16||settings.Handles?.Length!=points.Count)return Fail("Bezier handles must match the route points; use detail 2–16.");
        if(settings.Handles.Any(h=>h is null||!float.IsFinite(h.In.Length())||!float.IsFinite(h.Out.Length())||h.In.Length()>10000||h.Out.Length()>10000))return Fail("Invalid Bezier handle.");
        var samples=new List<Vector3>{points[0]};int count=repeat==MotionPathRepeat.Closed?points.Count:points.Count-1;
        for(int i=0;i<count;i++){
            int next=(i+1)%points.Count;var a=points[i];var b=a+settings.Handles[i].Out;var d=points[next];var c=d+settings.Handles[next].In;
            for(int j=1;j<=settings.Detail;j++){float t=j/(float)settings.Detail,u=1-t;var p=u*u*u*a+3*u*u*t*b+3*u*t*t*c+t*t*t*d;
                if(Vector3.Distance(p,samples[^1])>=.01f)samples.Add(p);}
        }
        if(repeat==MotionPathRepeat.Closed&&Vector3.Distance(samples[0],samples[^1])<.01f)samples.RemoveAt(samples.Count-1);
        // Waypoint pauses belong to authored corners, not every curve sample.
        if(pause!=0)return Fail("Bezier paths require zero waypoint pause. Use segment timing after baking the curve.");
        return ItemMotionPath.Plan(samples,use,repeat,speed,ease,0,delay,parking);
    }
    public static MotionPathPlan LookAt(MotionPathPlan plan)
    {
        if(plan.LookAtTarget is not {} target)return plan;
        var check=plan.Start;
        foreach(var leg in plan.Legs){var t=Math.Clamp(Vector3.Dot(target-check,leg.Delta)/leg.Delta.LengthSquared(),0,1);if(Vector3.Distance(target,check+leg.Delta*t)<.01f)throw new InvalidOperationException("Move the look-at target away from the route.");check+=leg.Delta;}
        var rotations=new List<MotionPathRotation>();var p=plan.Start;
        for(int i=0;i<=plan.Legs.Count;i++){
            var d=target-p;if(d.Length()<.01f)throw new InvalidOperationException("The look-at target is on a route point. Move it away from the route.");
            float yaw=MathF.Atan2(-d.Z,d.X)*180/MathF.PI;
            if(rotations.Count>0)yaw=rotations[^1].Yaw+(float)Math.IEEERemainder(yaw-rotations[^1].Yaw,360);
            rotations.Add(new(yaw,MathF.Atan2(d.Y,MathF.Sqrt(d.X*d.X+d.Z*d.Z))*180/MathF.PI,plan.Rotations?[i].Roll??0));
            if(i<plan.Legs.Count)p+=plan.Legs[i].Delta;
        }
        return plan with{Rotations=rotations.ToArray()};
    }
}
