using System.Text.Json;

namespace TM_Item_Studio.Models;

public sealed record MotionCustomTemplate(string Name, string Description, string Category, MotionPathPlan Profile,
    bool IncludeRoute=false, bool IncludeSpeed=true, bool IncludeRotation=true, bool IncludeTiming=true, bool IncludeParts=false, int Version=1);

public static class ItemMotionCustomTemplate
{
    public static readonly JsonSerializerOptions Json = new(){IncludeFields=true,WriteIndented=true};
    public static string? Validate(MotionCustomTemplate t) {
        if(t.Version!=1 || string.IsNullOrWhiteSpace(t.Name) || t.Name.Length>80 || t.Description is null || t.Description.Length>1000 || t.Category is null || t.Category.Length>80 || t.Profile is null)
            return "Invalid template metadata or unsupported version.";
        return ItemMotionPathProfile.Compile(t.Profile).Reason;
    }
    public static ItemMotionResult<MotionPathPlan> Apply(MotionCustomTemplate t, MotionPathPlan target, bool mapParts=false) {
        ItemMotionResult<MotionPathPlan> Fail(string message)=>ItemMotionResult<MotionPathPlan>.Fail(ItemMotionStatus.Invalid,message);
        var error=Validate(t);if(error!=null)return Fail(error);
        var check=ItemMotionPath.Validate(target);if(!check.Success)return Fail(check.Reason!);
        var source=t.Profile;
        if(t.IncludeRoute) target=source with{Parts=mapParts&&t.IncludeParts?source.Parts:null};
        else {
            if(source.Use!=target.Use)return Fail("Choose the same scenery/collision mode as the saved template.");
            var a=Fractions(source);var b=Fractions(target);
            float Sample(float[] values,float x) {int i=0;while(i<a.Length-2&&a[i+1]<x)i++;float u=(x-a[i])/(a[i+1]-a[i]);return values[i]+(values[i+1]-values[i])*u;}
            float[]? speeds=target.SpeedPoints;MotionPathRotation[]? rotations=target.Rotations;
            if(t.IncludeSpeed) {
                var values=source.SpeedPoints??source.Legs.Select(l=>l.Delta.Length()*1000/l.MoveMs).Append(source.Legs[^1].Delta.Length()*1000/source.Legs[^1].MoveMs).ToArray();
                speeds=b.Select(x=>Sample(values,x)).ToArray();
                if(target.Repeat!=MotionPathRepeat.Reset)speeds[^1]=speeds[0];
            }
            if(t.IncludeRotation && source.Rotations is {} r) {
                var original=ItemMotionPathProfile.FollowDirection(source);var next=ItemMotionPathProfile.FollowDirection(target);
                var yaw=r.Select((v,i)=>v.Yaw-original[i].Yaw).ToArray();var pitch=r.Select((v,i)=>v.Pitch-original[i].Pitch).ToArray();var roll=r.Select(v=>v.Roll).ToArray();
                rotations=b.Select((x,i)=>new MotionPathRotation(next[i].Yaw+Sample(yaw,x),next[i].Pitch+Sample(pitch,x),Sample(roll,x))).ToArray();
                if(target.Repeat!=MotionPathRepeat.Reset)rotations[^1]=rotations[0] with{Yaw=rotations[0].Yaw+360*MathF.Round((rotations[^1].Yaw-rotations[0].Yaw)/360)};
            } else if(t.IncludeRotation)rotations=null;
            int pause=t.IncludeTiming?source.PauseMs:target.PauseMs;
            int delay=t.IncludeTiming?source.StartDelayMs:target.StartDelayMs;
            // Station timing cannot coexist with a point speed curve.
            if(pause>0)speeds=null;
            long elapsed=delay+pause;
            var legs=target.Legs.Select(l=>{var v=l with{StartMs=(int)elapsed,VelocityRatio=1};elapsed+=l.MoveMs+(long)pause;return v;}).ToArray();
            if(target.Repeat!=MotionPathRepeat.Reset)elapsed-=pause;
            if(elapsed>int.MaxValue)return Fail("Template timing exceeds native limits.");
            target=target with{Legs=legs,DurationMs=(int)elapsed,PauseMs=pause,StartDelayMs=delay,SpeedPoints=speeds,Rotations=rotations,
                Ease=source.Ease,ContinuousVelocity=source.ContinuousVelocity&&rotations!=null&&speeds!=null,RampDetail=source.RampDetail,
                Parts=mapParts&&t.IncludeParts?source.Parts:target.Parts,Behavior=null};
            if(t.IncludeTiming) {
                var time=ItemMotionPathEditing.ChangeLoopTime(target,ItemMotionPathProfile.Compile(source).Value!.DurationMs);
                if(!time.Success)return Fail(time.Reason!);target=time.Value!;
            }
        }
        var compiled=ItemMotionPathProfile.Compile(target);
        return compiled.Success?ItemMotionResult<MotionPathPlan>.Ok(target):Fail(compiled.Reason!);
    }
    static float[] Fractions(MotionPathPlan p) {
        var result=new float[p.Legs.Count+1];for(int i=0;i<p.Legs.Count;i++)result[i+1]=result[i]+p.Legs[i].Delta.Length();
        float total=result[^1];for(int i=0;i<result.Length;i++)result[i]/=total;return result;
    }
}
