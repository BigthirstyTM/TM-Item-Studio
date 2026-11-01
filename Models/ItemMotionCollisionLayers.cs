using System.Numerics;
using KC=GBX.NET.Engines.Meta.NPlugDyna_SKinematicConstraint;
namespace TM_Item_Studio.Models;

/// <summary>Experimental sampled world-bound poses. Handoffs can eject passengers.</summary>
public static class ItemMotionCollisionLayers
{
    public static ItemMotionResult<MotionPathPlan> Compile(MotionPathPlan p)
    {
        if(p.Use!=MotionPathUse.CollisionRelay || p.CollisionPoses is not null || (!p.SampleCollisionRotation&&p.Layers?.Any(l=>l.Enabled)!=true))return ItemMotionResult<MotionPathPlan>.Ok(p);
        ItemMotionResult<MotionPathPlan> Fail(string s)=>ItemMotionResult<MotionPathPlan>.Fail(ItemMotionStatus.Invalid,s);
        var layers=(p.Layers??[]).Where(l=>l.Enabled).ToArray();
        int step=Math.Max(80,Math.Min(500,layers.Length==0?500:layers.Min(l=>l.PeriodMs)/8));
        var counts=p.Legs.Select(l=>Math.Max(1,Math.Min(ItemMotionPath.MaxLegs,Math.Min(l.MoveMs/80,(int)Math.Ceiling(l.MoveMs/(double)step))))).ToArray();
        while(counts.Sum()>ItemMotionPath.MaxLegs){int best=Enumerable.Range(0,counts.Length).Where(i=>counts[i]>1).OrderByDescending(i=>counts[i]/(double)p.Legs[i].MoveMs).First();counts[best]--;}
        var legs=new List<MotionPathLeg>();var rotations=new List<MotionPathRotation>();var poses=new List<Quaternion>();
        Vector3 cursor=p.Start,first=default,previous=default;int elapsed=p.StartDelayMs;
        float Ease(float x)=>p.Ease switch{KC.AnimEase.QuadIn=>x*x,KC.AnimEase.QuadOut=>x*(2-x),KC.AnimEase.QuadInOut=>x<.5f?2*x*x:1-2*(1-x)*(1-x),_=>x};
        for(int i=0;i<p.Legs.Count;i++){
            var leg=p.Legs[i];int count=counts[i];
            (Vector3 Position,Quaternion Pose,MotionPathRotation Rotation) Evaluate(double time){
                float x=(float)((time-leg.StartMs)/leg.MoveMs),u=leg.VelocityRatio==1?Ease(x):leg.VelocityRatio*x+(1-leg.VelocityRatio)*x*x;
                var orientation=ItemMotionPathProfile.Lerp(p.Rotations![i],p.Rotations[i+1],Ease(x));
                var q=Quaternion.CreateFromAxisAngle(Vector3.UnitY,orientation.Yaw*MathF.PI/180)*Quaternion.CreateFromAxisAngle(Vector3.UnitZ,orientation.Pitch*MathF.PI/180)*Quaternion.CreateFromAxisAngle(Vector3.UnitX,orientation.Roll*MathF.PI/180);
                var position=cursor+leg.Delta*u;
                foreach(var layer in layers){
                    double phase=(time-p.StartDelayMs)%layer.PeriodMs;int half=layer.PeriodMs/2;
                    float t=(float)(phase<half?phase/half:1-(phase-half)/(layer.PeriodMs-half));
                    float eased=t<.5f?2*t*t:1-2*(1-t)*(1-t),sign=layer.Reverse?-1:1;
                    float value=layer.Kind==MotionLayerKind.Spin?(float)(360*phase/layer.PeriodMs)*sign:layer.Amplitude*sign*(2*eased-1);
                    var axis=layer.Axis==KC.EAxis.X?Vector3.UnitX:layer.Axis==KC.EAxis.Y?Vector3.UnitY:Vector3.UnitZ;
                    if(layer.Kind==MotionLayerKind.Translate)position+=Vector3.Transform(axis*value,q);
                    else q=Quaternion.Normalize(q*Quaternion.CreateFromAxisAngle(axis,value*MathF.PI/180));
                }
                return(position,q,orientation);
            }
            for(int j=0;j<=count;j++){
                int time=leg.StartMs+(int)Math.Round(leg.MoveMs*j/(double)count);var sample=Evaluate(time);
                var position=sample.Position;
                if(i==0&&j==0){first=previous=position;rotations.Add(sample.Rotation);continue;}
                if(j==0)continue;
                // Physical layers restart with the route clock. Close the last position
                // sample even when an independent layer period does not divide the lap.
                if(i==p.Legs.Count-1&&j==count)position=first;
                var delta=position-previous;
                if(time-elapsed<80 || delta.Length()<.01f)return Fail("Physical layers create a sample below 80 ms or 1 cm. Adjust the route, amplitude or period.");
                if(Vector3.Cross(Vector3.Normalize(delta),Vector3.UnitY).Length()<.05f)return Fail("A physical layer creates a near-vertical segment, which the collision relay cannot support.");
                legs.Add(new(delta,elapsed,time-elapsed,leg.SourceIndex<0?i:leg.SourceIndex));rotations.Add(sample.Rotation);poses.Add(Evaluate((elapsed+time)/2d).Pose);elapsed=time;previous=position;
            }
            cursor+=leg.Delta;
        }
        return ItemMotionPath.Validate(p with{Start=first,Legs=legs,Rotations=rotations.ToArray(),CollisionPoses=poses.ToArray(),Layers=null,SpeedPoints=null,ContinuousVelocity=false,Ease=KC.AnimEase.Linear});
    }
}
