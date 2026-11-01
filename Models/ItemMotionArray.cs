using System.Numerics;
using System.Security.Cryptography;
using GBX.NET;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;
using KC = GBX.NET.Engines.Meta.NPlugDyna_SKinematicConstraint;

namespace TM_Item_Studio.Models;

public sealed record MotionPathArray(int Count=1, int DelayMs=1000, Vector3 Spread=default,
    float MinScale=1, float MaxScale=1, int ScaleSteps=1, int Seed=1, bool AlternateWings=false);
public enum MotionLayerKind { Translate, Rotate, Spin }
public sealed record MotionPathLayer(string Name="Sway", bool Enabled=true, MotionLayerKind Kind=MotionLayerKind.Rotate,
    KC.EAxis Axis=KC.EAxis.X, float Amplitude=5, int PeriodMs=2000, bool Reverse=false);
public sealed record MotionArrayInstance(int DelayMs, Vector3 Offset, float Scale, bool ReverseWings);

/// <summary>Time-offset route arrays. Geometry is interned across instances; schedules stay independent.</summary>
public static class ItemMotionArray
{
    public static string? Validate(MotionPathPlan p)
    {
        if(p.Layers is { } layers && (layers.Length>4 || layers.Any(l=>l is null || l.Name is null || l.Name.Length>80 || !Enum.IsDefined(l.Kind) || !Enum.IsDefined(l.Axis)
            || !float.IsFinite(l.Amplitude) || Math.Abs(l.Amplitude)>360 || l.PeriodMs is <160 or >60000)))return "Use at most four layers with finite amplitude up to 360 and periods of 160–60000 ms.";
        if((p.Layers is {Length:>0} || p.SampleCollisionRotation) && (p.Use==MotionPathUse.Obstacle || p.Rotations is null))return "Layers and sampled collision rotation require route orientation in scenery or experimental collision mode.";
        if(p.Use==MotionPathUse.CollisionRelay && (p.SampleCollisionRotation||p.Layers?.Any(l=>l.Enabled)==true) && (p.PauseMs!=0||p.Repeat==MotionPathRepeat.Reset))return "Sampled physical layers require a looping route with zero waypoint pauses.";
        if(p.CollisionPoses is {} poses && (p.Use!=MotionPathUse.CollisionRelay || p.Rotations is null || p.Legs is null || poses.Length!=p.Legs.Count || p.Layers is {Length:>0} || p.SpeedPoints is not null || poses.Any(q=>!float.IsFinite(q.LengthSquared())||Math.Abs(q.LengthSquared()-1)>.001f)))return "Invalid sampled collision poses.";
        if(p.Array is not {} a)return null;
        if(a.Count is <1 or >32 || a.DelayMs is <0 or >600000 || !float.IsFinite(a.Spread.Length()) || a.Spread.X<0 || a.Spread.Y<0 || a.Spread.Z<0 || a.Spread.Length()>1000
            || !float.IsFinite(a.MinScale) || !float.IsFinite(a.MaxScale) || a.MinScale<.1f || a.MaxScale>10 || a.MaxScale<a.MinScale || a.ScaleSteps is <1 or >4)return "Array: 1–32 copies, delay 0–600 s, positive spread up to 1000 m, scale 0.1–10, 1–4 shared sizes.";
        if((p.Use==MotionPathUse.Obstacle || p.Repeat==MotionPathRepeat.Reset || p.PauseMs!=0 || p.Rotations is null || (p.Ease!=KC.AnimEase.Linear && p.SpeedPoints is null)))return "Arrays require a looping scenery or experimental collision route, zero pauses, route rotation and Linear easing (or a speed curve).";
        if(a.Count>1 && p.Legs is {} legs && (long)(legs.Count+1)*a.Count*(6+(p.Parts?.Length??0)+(p.Layers?.Length??0))>16384)return "Array exceeds 16,384 animated bodies. Reduce copies, route samples, parts or layers.";
        return null;
    }

    public static MotionArrayInstance[] Instances(MotionPathPlan p)
    {
        var a=p.Array??new();int cycle=p.DurationMs-p.StartDelayMs;uint state=unchecked((uint)a.Seed);
        float Random(){state=unchecked(state*1664525+1013904223);return (state>>8)/16777216f;}
        return Enumerable.Range(0,a.Count).Select(i=>{
            var offset=i==0?Vector3.Zero:new Vector3((Random()*2-1)*a.Spread.X,(Random()*2-1)*a.Spread.Y,(Random()*2-1)*a.Spread.Z);
            int level=a.ScaleSteps==1?0:Math.Min(a.ScaleSteps-1,(int)(Random()*a.ScaleSteps));
            float scale=a.ScaleSteps==1?a.MinScale:a.MinScale+(a.MaxScale-a.MinScale)*level/(a.ScaleSteps-1);
            return new MotionArrayInstance((int)((long)i*a.DelayMs%cycle),offset,scale,a.AlternateWings&&i%2==1);
        }).ToArray();
    }

    // Rebase a closed route's clock. A split preserves the quadratic distance function
    // exactly; the native per-channel four-key limit is never increased.
    public static MotionPathPlan Shift(MotionPathPlan p,int delayMs)
    {
        if(p.StartDelayMs>0){
            int dwell=p.StartDelayMs;
            var moving=p with{StartDelayMs=0,DurationMs=p.DurationMs-dwell,Legs=p.Legs.Select(l=>l with{StartMs=l.StartMs-dwell}).ToArray()};
            var shifted=Shift(moving,delayMs);
            return shifted with{StartDelayMs=dwell,DurationMs=shifted.DurationMs+dwell,Legs=shifted.Legs.Select(l=>l with{StartMs=l.StartMs+dwell}).ToArray()};
        }
        int cut=(p.DurationMs-delayMs%p.DurationMs)%p.DurationMs;
        if(cut==0)return p;
        // A later follower can land just beside a waypoint even when earlier copies
        // are valid. Snap that phase to the boundary instead of rejecting the array.
        // Use the same rebasing for browser preview and export.
        foreach(var leg in p.Legs) {
            int end=leg.StartMs+leg.MoveMs;
            if(cut<=leg.StartMs || cut>=end)continue;
            float u=(cut-leg.StartMs)/(float)leg.MoveMs;
            float fraction=leg.VelocityRatio*u+(1-leg.VelocityRatio)*u*u;
            if(cut-leg.StartMs<80 || end-cut<80 || leg.Delta.Length()*Math.Min(fraction,1-fraction)<.01f || p.Legs.Count>=ItemMotionPath.MaxLegs)
                cut=(cut-leg.StartMs<=end-cut?leg.StartMs:end)%p.DurationMs;
            break;
        }
        if(cut==0)return p;
        var pieces=new List<(int Time,Vector3 Start,MotionPathLeg Leg,MotionPathRotation A,MotionPathRotation B,Quaternion Pose)>();
        var cursor=p.Start;
        for(int i=0;i<p.Legs.Count;i++){
            var l=p.Legs[i];int end=l.StartMs+l.MoveMs;
            void Piece(int from,int to){
                if(to<=from)return;
                float u=(from-l.StartMs)/(float)l.MoveMs,v=(to-l.StartMs)/(float)l.MoveMs,r=l.VelocityRatio;
                float F(float x)=>r*x+(1-r)*x*x;
                float distance=F(v)-F(u);var delta=l.Delta*distance;
                float ratio=(r+2*(1-r)*u)*(v-u)/distance;
                if(to-from<80 || delta.Length()<.01f)throw new InvalidOperationException("This copy's delay splits a segment below 80 ms or 1 cm. Adjust the copy delay or slow the route.");
                pieces.Add(((from-cut+p.DurationMs)%p.DurationMs,cursor+l.Delta*F(u),new(delta,0,to-from,l.SourceIndex,ratio),ItemMotionPathProfile.Lerp(p.Rotations![i],p.Rotations[i+1],u),ItemMotionPathProfile.Lerp(p.Rotations[i],p.Rotations[i+1],v),p.CollisionPoses?[i]??Quaternion.Identity));
            }
            if(cut>l.StartMs&&cut<end){Piece(l.StartMs,cut);Piece(cut,end);}else Piece(l.StartMs,end);
            cursor+=l.Delta;
        }
        pieces.Sort((a,b)=>a.Time.CompareTo(b.Time));
        var rotations=new List<MotionPathRotation>{pieces[0].A};var legs=new List<MotionPathLeg>();int elapsed=0;
        foreach(var s in pieces){
            // Whole-turn continuity across the original cycle boundary.
            var prev=rotations[^1];var a=s.A;var b=s.B;
            rotations.Add(new(b.Yaw+prev.Yaw-a.Yaw,b.Pitch+prev.Pitch-a.Pitch,b.Roll+prev.Roll-a.Roll));
            legs.Add(s.Leg with{StartMs=elapsed});elapsed+=s.Leg.MoveMs;
        }
        return p with{Start=pieces[0].Start,Legs=legs,Rotations=rotations.ToArray(),CollisionPoses=p.CollisionPoses is null?null:pieces.Select(s=>s.Pose).ToArray(),Array=null};
    }

    internal static ItemMotionResult<CPlugPrefab> Build(CPlugPrefab source,MotionPathPlan p,Vector3 anchor)
    {
        try{
            if(p.Use==MotionPathUse.CollisionRelay){
                var model=(CPlugDynaObjectModel)source.Ents.Single(e=>e.Model is CPlugDynaObjectModel).Model!;
                ItemMotionCollisionRelay.CheckMesh(model);
                bool CarrierOnly(CPlugSurface? shape){var v=((CPlugSurface.Mesh)shape!.Surf!).Vertices;return v.All(p=>Math.Abs(p.X)<=.001f&&Math.Abs(p.Y)<=.001f&&Math.Abs(p.Z)<=.001f);}
                if(CarrierOnly(model.StaticShape)&&CarrierOnly(model.DynaShape))return ItemMotionResult<CPlugPrefab>.Fail(ItemMotionStatus.Unsupported,"The source has only microscopic carrier collision. Load a collidable source; arrays do not generate collision from visuals.");
                long vertices=new[]{model.StaticShape,model.DynaShape}.Distinct().Sum(shape=>((CPlugSurface.Mesh)shape!.Surf!).Vertices.Length);
                if(vertices*(p.Legs.Count+1)*(p.Array?.Count??1)>ItemMotionCollisionRelay.MaxExpandedVertices)
                    return ItemMotionResult<CPlugPrefab>.Fail(ItemMotionStatus.Unsupported,"The collision array exceeds the collision vertex budget. Reduce copies, route samples or simplify the source collider.");
            }
            var bodies=new List<CPlugPrefab.EntRef>();var drivers=new List<CPlugPrefab.EntRef>();var extras=new List<CPlugPrefab.EntRef>();CPlugPrefab? result=null;
            var pool=new Dictionary<string,CPlugDynaObjectModel>();
            foreach(var instance in Instances(p)){
                var route=Shift(p,instance.DelayMs) with{Array=null};
                route=route with{Start=route.Start+instance.Offset,Parts=route.Parts?.Select(part=>part with{Pivot=part.Pivot*instance.Scale,Radius=part.Radius*instance.Scale,Reverse=part.Reverse^(instance.ReverseWings&&part.Kind==MotionPartKind.Wing)}).ToArray()};
                route=ItemMotionAuthoring.LookAt(route);
                var scaled=Clone(source);Scale((CPlugDynaObjectModel)scaled.Ents.Single(e=>e.Model is CPlugDynaObjectModel).Model!,instance.Scale,p.Use==MotionPathUse.CollisionRelay);
                var built=ItemMotionPathPose.Build(scaled,route,anchor*instance.Scale);
                if(!built.Success)return built;result??=built.Value!;
                int offset=bodies.Count;
                var interned=new Dictionary<CPlugDynaObjectModel,CPlugDynaObjectModel>();
                foreach(var e in built.Value!.Ents){
                    if(e.Model is CPlugDynaObjectModel body){
                        if(!interned.TryGetValue(body,out var shared)){
                            using var stream=new MemoryStream();new Gbx<CPlugDynaObjectModel>(body){BodyCompression=GbxCompression.Uncompressed}.Save(stream);
                            string hash=Convert.ToHexString(SHA256.HashData(stream.ToArray()));
                            if(!pool.TryGetValue(hash,out shared))pool[hash]=shared=body;interned[body]=shared;
                        }
                        body.Mesh=shared.Mesh; // Share geometry, not native body model state.
                        bodies.Add(e);
                    }else if(e.Params is NPlugDyna_SPrefabConstraintParams c){if(c.Ent1>=0)c.Ent1+=offset;if(c.Ent2>=0)c.Ent2+=offset;drivers.Add(e);}
                    else if(offset==0)extras.Add(e); // Original static extras appear only once.
                }
            }
            result!.Ents=[..bodies,..drivers,..extras];return ItemMotionResult<CPlugPrefab>.Ok(result);
        }catch(Exception e){return ItemMotionResult<CPlugPrefab>.Fail(ItemMotionStatus.Unsupported,"Cannot build path array: "+e.Message);}
    }
    private static CPlugPrefab Clone(CPlugPrefab source){using var s=new MemoryStream();new Gbx<CPlugPrefab>(source){BodyCompression=GbxCompression.Uncompressed}.Save(s);s.Position=0;return Gbx.Parse<CPlugPrefab>(s).Node;}
    private static void Scale(CPlugDynaObjectModel body,float scale,bool collision){
        if(scale==1)return;var solid=ItemMotionCollisionRelay.CheckMesh(body);if(solid.LightInsts is {Length:>0})throw new NotSupportedException("Scale variants currently require meshes without embedded lights.");var seen=new HashSet<Vec3[]>();
        Vec3 S(Vec3 v)=>new(v.X*scale,v.Y*scale,v.Z*scale);
        if(collision)foreach(var shape in new[]{body.StaticShape,body.DynaShape}.Distinct()){
            var vertices=((CPlugSurface.Mesh)shape!.Surf!).Vertices;
            if(seen.Add(vertices))for(int i=0;i<vertices.Length;i++)vertices[i]=S(vertices[i]);
        }
        foreach(var v in solid.Visuals!.Cast<CPlugVisualIndexedTriangles>().Distinct()){
            for(int i=0;i<v.Vertices.Length;i++)v.Vertices[i]=v.Vertices[i] with{Position=S(v.Vertices[i].Position)};
            foreach(var stream in v.VertexStreams)if(stream.Positions is {} pos&&seen.Add(pos))for(int i=0;i<pos.Length;i++)pos[i]=S(pos[i]);
            var b=v.BoundingBox;v.BoundingBox=new(b.X*scale,b.Y*scale,b.Z*scale,b.X2*scale,b.Y2*scale,b.Z2*scale);
        }
    }
}
