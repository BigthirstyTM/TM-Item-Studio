using System.Numerics;

namespace TM_Item_Studio.Models;

public enum MotionBehaviorKind { Rollercoaster, BirdFlight, CarBody, Boat, Train, LiftPlatform, CableCar, Drone, Fish, Butterfly, Robot, Ride, Conveyor }
public sealed record MotionBehaviorSettings(MotionBehaviorKind Kind = MotionBehaviorKind.Rollercoaster,
    float Speed = 22, float MinimumSpeed = 3, float MaximumSpeed = 45, float Gravity = 9.81f,
    float Resistance = .06f, float LiftSpeed = 4, int LiftFrom = -1, int LiftTo = -1,
    float Braking = 3, float CornerAcceleration = 6, float BankStrength = 1, float MaximumBank = 35,
    bool FollowSlope = true, int Detail = 8, int StopMs = 1000, float SwayDegrees = 8, int Cycles = 2, int Turns = 2);

/// <summary>Deterministic authored motion; no live physics or vehicle controller is exported.</summary>
public static class ItemMotionBehavior
{
    public static MotionBehaviorSettings Defaults(MotionBehaviorKind kind) => kind switch {
        MotionBehaviorKind.BirdFlight => new(kind, Speed:14, MaximumSpeed:25, BankStrength:1, MaximumBank:40),
        MotionBehaviorKind.CarBody => new(kind, Speed:20, MaximumSpeed:35, BankStrength:-.12f, MaximumBank:5),
        MotionBehaviorKind.Boat => new(kind, Speed:8, MaximumSpeed:15, BankStrength:-.3f, MaximumBank:12, FollowSlope:false),
        MotionBehaviorKind.Train => new(kind, Speed:25, MaximumSpeed:40, BankStrength:.15f, MaximumBank:5),
        MotionBehaviorKind.LiftPlatform => new(kind, Speed:4, MinimumSpeed:1, MaximumSpeed:10, FollowSlope:false),
        MotionBehaviorKind.CableCar => new(kind, Speed:5, MinimumSpeed:1, MaximumSpeed:15, BankStrength:0, FollowSlope:false, SwayDegrees:4),
        MotionBehaviorKind.Drone => new(kind, Speed:10, MinimumSpeed:1, MaximumSpeed:25, BankStrength:1, MaximumBank:25),
        MotionBehaviorKind.Fish => new(kind, Speed:5, MinimumSpeed:1, MaximumSpeed:15, BankStrength:.4f, SwayDegrees:12, Cycles:4),
        MotionBehaviorKind.Butterfly => new(kind, Speed:4, MinimumSpeed:1, MaximumSpeed:12, MaximumBank:45, SwayDegrees:18, Cycles:3),
        MotionBehaviorKind.Robot => new(kind, Speed:2, MinimumSpeed:.5f, MaximumSpeed:6, BankStrength:0, SwayDegrees:4, Cycles:4),
        MotionBehaviorKind.Ride => new(kind, Speed:6, MinimumSpeed:1, MaximumSpeed:20, BankStrength:0, FollowSlope:false, SwayDegrees:12),
        MotionBehaviorKind.Conveyor => new(kind, Speed:3, MinimumSpeed:1, MaximumSpeed:10, FollowSlope:false),
        _ => new()
    };

    public static ItemMotionResult<MotionPathPlan> Apply(MotionPathPlan p, MotionBehaviorSettings s)
    {
        ItemMotionResult<MotionPathPlan> Fail(string reason) => ItemMotionResult<MotionPathPlan>.Fail(ItemMotionStatus.Invalid,reason);
        var valid=ItemMotionPath.Validate(p); if(!valid.Success)return Fail(valid.Reason!);
        if(p.Use==MotionPathUse.Obstacle)return Fail("Behavior templates require scenery or experimental collision relay.");
        if(!Enum.IsDefined(s.Kind) || new[]{s.Speed,s.MinimumSpeed,s.MaximumSpeed,s.Gravity,s.Resistance,s.LiftSpeed,s.Braking,s.CornerAcceleration,s.BankStrength,s.MaximumBank,s.SwayDegrees}.Any(v=>!float.IsFinite(v))
            || s.MinimumSpeed<.1f || s.MaximumSpeed<s.MinimumSpeed || s.MaximumSpeed>200 || s.Speed<s.MinimumSpeed || s.Speed>s.MaximumSpeed
            || s.LiftSpeed<s.MinimumSpeed || s.LiftSpeed>s.MaximumSpeed || s.Gravity is <0 or >30 || s.Resistance is <0 or >10
            || s.Braking is <=0 or >30 || s.CornerAcceleration is <=0 or >30 || Math.Abs(s.BankStrength)>3 || s.MaximumBank is <0 or >85 || s.Detail is <1 or >16 || s.StopMs is <0 or >60000 || s.SwayDegrees is <0 or >60 || s.Cycles is <1 or >16 || s.Turns is <-20 or >20)
            return Fail("Check template settings: speeds 0.1–200 m/s, min ≤ speed/lift ≤ max, gravity/resistance/braking in range, detail 1–16.");
        if(s.Kind is MotionBehaviorKind.CableCar or MotionBehaviorKind.Fish or MotionBehaviorKind.Butterfly or MotionBehaviorKind.Robot or MotionBehaviorKind.Ride) {
            int factor=Math.Min(ItemMotionPath.MaxLegs/p.Legs.Count,(int)Math.Ceiling(8d*s.Cycles/p.Legs.Count));
            if(factor>1) {
                var points=new List<Vector3>{p.Start};var cursor=p.Start;
                foreach(var leg in p.Legs){for(int j=1;j<=factor;j++)points.Add(cursor+leg.Delta*(j/(float)factor));cursor+=leg.Delta;}
                var sampled=ItemMotionPath.Plan(points,p.Use,MotionPathRepeat.Reset,.1f,GBX.NET.Engines.Meta.NPlugDyna_SKinematicConstraint.AnimEase.Linear,0,p.StartDelayMs,p.ParkingDistance);
                if(!sampled.Success)return Fail("This route is too small to sample the requested sway. Reduce cycles or enlarge the route.");
                var q=sampled.Value! with{Repeat=p.Repeat};p=q with{Parts=p.Parts,Rotations=ItemMotionPathProfile.FollowDirection(q)};
            }
        }
        int n=p.Legs.Count;
        if(s.Kind is MotionBehaviorKind.LiftPlatform or MotionBehaviorKind.Conveyor or MotionBehaviorKind.Drone) {
            var points=ItemMotionPathEditing.Points(p);
            var rebuilt=ItemMotionPath.Plan(points,p.Use,MotionPathRepeat.Reset,s.Speed,GBX.NET.Engines.Meta.NPlugDyna_SKinematicConstraint.AnimEase.QuadInOut,s.StopMs,p.StartDelayMs,p.ParkingDistance);
            if(!rebuilt.Success)return Fail(rebuilt.Reason!);
            var q=rebuilt.Value!;
            // Points already include the return; preserve its repeat semantics and one pause per station.
            q=q with{Repeat=p.Repeat,DurationMs=q.DurationMs-(p.Repeat==MotionPathRepeat.Reset?0:s.StopMs)};
            var facing=s.Kind==MotionBehaviorKind.Drone?ItemMotionPathProfile.FollowDirection(q):Enumerable.Repeat(new MotionPathRotation(),n+1).ToArray();
            return ItemMotionPath.Validate(q with{Behavior=s,Rotations=facing,Parts=p.Parts});
        }
        // Templates replace the existing timing profile, including old station pauses.
        if(p.PauseMs!=0) {
            var legs=p.Legs.Select((l,i)=>l with{StartMs=l.StartMs-p.PauseMs*(i+1)}).ToArray();
            p=p with{Legs=legs,PauseMs=0,DurationMs=p.DurationMs-p.PauseMs*(n+(p.Repeat==MotionPathRepeat.Reset?1:0)),SpeedPoints=null,ContinuousVelocity=false};
        }
        if((s.LiftFrom!=-1 || s.LiftTo!=-1) && (s.LiftFrom<0 || s.LiftTo<s.LiftFrom || s.LiftTo>=n))return Fail("Choose a contiguous lift segment range, or disable the lift.");
        bool loop=p.Repeat!=MotionPathRepeat.Reset;
        var speed=new float[n+1];
        var limits=Enumerable.Repeat(s.MaximumSpeed,n+1).ToArray();
        var curvature=new float[n+1];
        for(int i=0;i<=n;i++) {
            if(!loop && (i==0||i==n))continue;
            var prev=p.Legs[(i+n-1)%n];var next=p.Legs[i%n];
            var a=Vector3.Normalize(prev.Delta);var b=Vector3.Normalize(next.Delta);
            var turn=MathF.Atan2(Vector3.Cross(a,b).Y,Vector3.Dot(a,b));
            curvature[i]=turn/Math.Max(.01f,(prev.Delta.Length()+next.Delta.Length())/2);
            if(s.Kind is MotionBehaviorKind.CarBody or MotionBehaviorKind.Boat or MotionBehaviorKind.Train)
                limits[i]=Math.Min(s.Speed,MathF.Sqrt(s.CornerAcceleration/Math.Max(.00001f,Math.Abs(curvature[i]))));
        }
        if(s.Kind!=MotionBehaviorKind.Rollercoaster)for(int i=0;i<=n;i++)limits[i]=Math.Clamp(limits[i],s.MinimumSpeed,s.Speed);
        if(s.Kind==MotionBehaviorKind.Rollercoaster && s.LiftFrom>=0)
            for(int i=s.LiftFrom;i<=s.LiftTo+1;i++)limits[i]=Math.Min(limits[i],s.LiftSpeed);
        // Backward braking envelope avoids an instantaneous speed cut on entering a lift/slow corner.
        for(int pass=0;pass<(loop?n+1:1);pass++) {
            if(loop)limits[n]=limits[0]=Math.Min(limits[0],limits[n]);
            for(int i=n-1;i>=0;i--)limits[i]=Math.Min(limits[i],MathF.Sqrt(limits[i+1]*limits[i+1]+2*s.Braking*p.Legs[i].Delta.Length()));
        }
        float start=Math.Min(s.Speed,limits[0]);
        // Iterate the periodic boundary to a fixed point; never forcibly replace the last speed.
        bool converged=!loop;
        for(int lap=0;lap<(loop?512:1);lap++) {
            speed[0]=start;
            for(int i=0;i<n;i++) {
                var d=p.Legs[i].Delta;float length=d.Length();float v;
                if(s.Kind==MotionBehaviorKind.Rollercoaster) {
                    bool lift=s.LiftFrom>=0 && i>=s.LiftFrom && i<=s.LiftTo;
                    v=lift?s.LiftSpeed:MathF.Sqrt(Math.Max(s.MinimumSpeed*s.MinimumSpeed,speed[i]*speed[i]-2*s.Gravity*d.Y-2*s.Resistance*length));
                } else v=MathF.Sqrt(speed[i]*speed[i]+2*s.CornerAcceleration*length);
                speed[i+1]=Math.Clamp(Math.Min(v,limits[i+1]),s.MinimumSpeed,s.MaximumSpeed);
            }
            if(!loop || Math.Abs(speed[n]-start)<.0001f){converged=true;break;}
            start=speed[n];
        }
        if(!converged)return Fail("The looping speed profile did not settle. Add a lift/braking zone or increase resistance.");
        if(loop)speed[n]=speed[0];
        var rotations=ItemMotionPathProfile.FollowDirection(p);
        var distances=new float[n+1];for(int i=0;i<n;i++)distances[i+1]=distances[i]+p.Legs[i].Delta.Length();
        for(int i=0;i<=n;i++) {
            float bank=Math.Clamp(MathF.Atan(speed[i]*speed[i]*curvature[i]/9.81f)*180/MathF.PI*s.BankStrength,-s.MaximumBank,s.MaximumBank);
            float phase=2*MathF.PI*s.Cycles*distances[i]/distances[n],wave=MathF.Sin(phase)*s.SwayDegrees;
            rotations[i]=rotations[i] with {Pitch=s.FollowSlope?rotations[i].Pitch:0,Roll=bank};
            if(s.Kind is MotionBehaviorKind.CableCar or MotionBehaviorKind.Robot) rotations[i]=rotations[i] with{Roll=wave};
            if(s.Kind==MotionBehaviorKind.Fish) rotations[i]=rotations[i] with{Yaw=rotations[i].Yaw+wave};
            if(s.Kind==MotionBehaviorKind.Butterfly) { rotations[i]=rotations[i] with{Roll=bank+wave}; speed[i]*=1-.35f*(1-MathF.Cos(phase))/2; }
            if(s.Kind==MotionBehaviorKind.Ride) rotations[i]=new(360*s.Turns*distances[i]/distances[n],wave,0);
        }
        if(loop){speed[n]=speed[0];rotations[n]=rotations[n] with{Pitch=rotations[0].Pitch,Roll=rotations[0].Roll};}
        var result=p with{SpeedPoints=speed,Rotations=rotations,RampDetail=s.Detail,Behavior=s,ContinuousVelocity=p.Use==MotionPathUse.Scenery};
        var compiled=ItemMotionPathProfile.Compile(result);
        return compiled.Success?ItemMotionResult<MotionPathPlan>.Ok(result):Fail(compiled.Reason!);
    }

    public static (int From,int To) FirstLift(MotionPathPlan p)
    {
        int from=-1,to=-1;
        for(int i=0;i<p.Legs.Count;i++) {
            var d=p.Legs[i].Delta;
            if(d.Y/d.Length()>.04f){if(from<0)from=i;to=i;}
            else if(from>=0)break;
        }
        return(from,to);
    }
}
