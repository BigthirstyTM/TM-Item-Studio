using System.Numerics;
using GBX.NET;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;
using TmEssentials;
using KC = GBX.NET.Engines.Meta.NPlugDyna_SKinematicConstraint;

namespace TM_Item_Studio.Models;

/// <summary>Shallow visual rotation branches driven by world-bound relay bodies.</summary>
public static class ItemMotionPathPose
{
    internal static ItemMotionResult<CPlugPrefab> Build(CPlugPrefab original, MotionPathPlan plan, Vector3 anchor)
    {
        try
        {
            using var stream = new MemoryStream();
            new Gbx<CPlugPrefab>(original) { BodyCompression = GbxCompression.Uncompressed }.Save(stream);
            stream.Position = 0;
            var source = Gbx.Parse<CPlugPrefab>(stream).Node;
            var sourceShader = (KC)source.Ents.Single(e => e.Model is KC).Model!;
            var sourceParams = (NPlugDynaObjectModel_SInstanceParams)source.Ents.Single(e => e.Model is CPlugDynaObjectModel).Params!;
            var visual = (CPlugDynaObjectModel)source.Ents.Single(e => e.Model is CPlugDynaObjectModel).Model!;
            var micro = ItemKinematicEntityTemplate.CreateCarrierBody();
            var root = ItemKinematicEntityTemplate.CreateCarrierBody();
            if (plan.Use == MotionPathUse.CollisionRelay) { root.StaticShape = visual.StaticShape; root.DynaShape = visual.DynaShape; }
            if (plan.Use == MotionPathUse.Scenery) { visual.StaticShape = micro.StaticShape; visual.DynaShape = micro.DynaShape; }
            ItemMotionCollisionRelay.CheckMesh(visual);
            // Source visual stays shared and untouched; only tiny relay meshes are duplicated.
            source.Ents.Single(e => e.Model is CPlugDynaObjectModel).Model = root;
            var built = ItemMotionCollisionRelay.Build(source, plan, anchor);
            if (!built.Success) return built;
            var prefab = built.Value!;
            var bodies = prefab.Ents.Where(e => e.Model is CPlugDynaObjectModel).ToList();
            var drivers = prefab.Ents.Where(e => e.Model is KC).ToList();
            var extras = prefab.Ents.Where(e => e.Model is not (KC or CPlugDynaObjectModel)).ToArray();
            visual.StaticShape = micro.StaticShape; visual.DynaShape = micro.DynaShape;
            var parts=new List<(MotionPathPart Settings,CPlugDynaObjectModel Body)>();
            if(plan.Parts is {Length:>0}) {
                var solid=(CPlugSolid2Model)visual.Mesh!;
                if(plan.Parts.Any(p=>p.VisualIndex>=solid.Visuals!.Count) || solid.LightInsts is {Length:>0})
                    return ItemMotionResult<CPlugPrefab>.Fail(ItemMotionStatus.Unsupported,"Part selection is outside this mesh, or the mesh contains lights that cannot be split safely.");
                foreach(var p in plan.Parts)parts.Add((p,ItemMotionParts.Extract(visual,new HashSet<int>{p.VisualIndex},p.Pivot)));
                var remaining=Enumerable.Range(0,solid.Visuals!.Count).Except(plan.Parts.Select(p=>p.VisualIndex)).ToHashSet();
                if(remaining.Count==0)return ItemMotionResult<CPlugPrefab>.Fail(ItemMotionStatus.Unsupported,"Keep at least one visual mesh as the main body.");
                visual=ItemMotionParts.Extract(visual,remaining,Vector3.Zero);
            }
            var cursor = plan.Start;
            float travelled=0;
            for (int i=0; i<plan.Legs.Count; i++)
            {
                var leg = plan.Legs[i]; int parent = i;
                ((CPlugDynaObjectModel)bodies[i].Model!).Mesh = micro.Mesh;
                if(leg.VelocityRatio!=1) {
                    // x(t)=v0*T*u + (v1-v0)*T*u²/2. A second shallow scalar
                    // carrier supplies the quadratic term, including signed deceleration.
                    ((KC)drivers[i].Model!).TransMax=leg.Delta.Length()*leg.VelocityRatio;
                    int correction=bodies.Count;
                    var carrier=Body(ItemKinematicEntityTemplate.CreateCarrierBody(),cursor);
                    carrier.Rotation=bodies[i].Rotation;bodies.Add(carrier);
                    var velocity=Hold(plan.DurationMs);velocity.TransMax=leg.Delta.Length()*(1-leg.VelocityRatio);
                    var vk=new List<KC.SubAnimFunc>();
                    if(leg.StartMs>0)vk.Add(Key(KC.AnimEase.Constant,leg.StartMs));
                    vk.Add(Key(KC.AnimEase.QuadIn,leg.MoveMs));
                    if(plan.DurationMs>leg.StartMs+leg.MoveMs)vk.Add(Key(KC.AnimEase.Constant,plan.DurationMs-leg.StartMs-leg.MoveMs,true));
                    velocity.TransAnimFunc!.SubFuncs=vk.ToArray();drivers.Add(Driver(velocity,parent,correction));parent=correction;
                }
                var a = plan.Rotations![i]; var b = plan.Rotations[i+1];
                foreach(var (from,to,axis) in new[] { (a.Yaw,b.Yaw,KC.EAxis.Y), (a.Pitch,b.Pitch,KC.EAxis.Z), (a.Roll,b.Roll,KC.EAxis.X) })
                {
                    int child = bodies.Count;
                    bodies.Add(Body(ItemKinematicEntityTemplate.CreateCarrierBody(),cursor));
                    var c = Hold(plan.DurationMs); c.RotAxis=axis; c.AngleMinDeg=from; c.AngleMaxDeg=to;
                    var keys = new List<KC.SubAnimFunc>();
                    if(leg.StartMs>0) keys.Add(Key(KC.AnimEase.Constant,leg.StartMs));
                    keys.Add(Key(plan.Ease,leg.MoveMs));
                    if(plan.DurationMs>leg.StartMs+leg.MoveMs) keys.Add(Key(KC.AnimEase.Constant,plan.DurationMs-leg.StartMs-leg.MoveMs,true));
                    c.RotAnimFunc!.SubFuncs=keys.ToArray();
                    drivers.Add(Driver(c,parent,child)); parent=child;
                }
                foreach(var layer in plan.Layers??[]) {
                    if(!layer.Enabled)continue;
                    int child=bodies.Count;bodies.Add(Body(ItemKinematicEntityTemplate.CreateCarrierBody(),cursor));
                    var c=Hold(plan.DurationMs);float amplitude=layer.Amplitude*(layer.Reverse?-1:1);
                    var keys=new[]{Key(KC.AnimEase.QuadInOut,layer.PeriodMs/2),Key(KC.AnimEase.QuadInOut,layer.PeriodMs-layer.PeriodMs/2,true)};
                    if(layer.Kind==MotionLayerKind.Translate){c.TransAxis=layer.Axis;c.TransMin=-amplitude;c.TransMax=amplitude;c.TransAnimFunc!.SubFuncs=keys;}
                    else {c.RotAxis=layer.Axis;c.AngleMinDeg=layer.Kind==MotionLayerKind.Spin?0:-amplitude;c.AngleMaxDeg=layer.Kind==MotionLayerKind.Spin?360*(layer.Reverse?-1:1):amplitude;
                        c.RotAnimFunc!.SubFuncs=layer.Kind==MotionLayerKind.Spin?[Key(KC.AnimEase.Linear,layer.PeriodMs)]:keys;}
                    drivers.Add(Driver(c,parent,child));parent=child;
                }
                int visible=bodies.Count;
                bodies.Add(Body(visual,cursor-anchor));
                var instance=(NPlugDynaObjectModel_SInstanceParams)bodies[^1].Params!;
                instance.TextureId=sourceParams.TextureId; instance.CastStaticShadow=sourceParams.CastStaticShadow;
                var visibleDriver=Hold(plan.DurationMs);
                visibleDriver.ShaderTcType=sourceShader.ShaderTcType; visibleDriver.ShaderTcVersion=sourceShader.ShaderTcVersion;
                visibleDriver.ShaderTcAnimFunc=sourceShader.ShaderTcAnimFunc; visibleDriver.ShaderTcDataTransSub=sourceShader.ShaderTcDataTransSub;
                drivers.Add(Driver(visibleDriver,parent,visible));
                foreach(var (part,model) in parts) {
                    var partBody=bodies.Count;bodies.Add(Body(model,cursor+part.Pivot-anchor));
                    var partInstance=(NPlugDynaObjectModel_SInstanceParams)bodies[^1].Params!;
                    partInstance.TextureId=sourceParams.TextureId;partInstance.CastStaticShadow=sourceParams.CastStaticShadow;
                    var pc=Hold(plan.DurationMs);pc.RotAxis=part.Axis;float sign=part.Reverse?-1:1;
                    pc.ShaderTcType=sourceShader.ShaderTcType;pc.ShaderTcVersion=sourceShader.ShaderTcVersion;
                    pc.ShaderTcAnimFunc=sourceShader.ShaderTcAnimFunc;pc.ShaderTcDataTransSub=sourceShader.ShaderTcDataTransSub;
                    if(part.Kind==MotionPartKind.Wing) {
                        pc.AngleMinDeg=part.MinimumAngle*sign;pc.AngleMaxDeg=part.MaximumAngle*sign;
                        pc.RotAnimFunc!.SubFuncs=[Key(KC.AnimEase.QuadInOut,part.PeriodMs/2),Key(KC.AnimEase.QuadInOut,part.PeriodMs-part.PeriodMs/2,true)];
                    } else if(part.Kind==MotionPartKind.Propeller) {
                        pc.AngleMaxDeg=360*sign;pc.RotAnimFunc!.SubFuncs=[Key(KC.AnimEase.Linear,part.PeriodMs)];
                    } else {
                        pc.AngleMinDeg=travelled/part.Radius*180/MathF.PI*sign;pc.AngleMaxDeg=(travelled+leg.Delta.Length())/part.Radius*180/MathF.PI*sign;
                        var wk=new List<KC.SubAnimFunc>();if(leg.StartMs>0)wk.Add(Key(KC.AnimEase.Constant,leg.StartMs));wk.Add(Key(KC.AnimEase.Linear,leg.MoveMs));if(plan.DurationMs>leg.StartMs+leg.MoveMs)wk.Add(Key(KC.AnimEase.Constant,plan.DurationMs-leg.StartMs-leg.MoveMs,true));pc.RotAnimFunc!.SubFuncs=wk.ToArray();
                    }
                    // Sibling of the body: local pivot translates after rotation, preserving the hinge.
                    drivers.Add(Driver(pc,parent,partBody));
                }
                travelled+=leg.Delta.Length();
                cursor+=leg.Delta;
            }
            prefab.Ents=[..bodies,..drivers,..extras];
            return ItemMotionResult<CPlugPrefab>.Ok(prefab);
        }
        catch(Exception e) { return ItemMotionResult<CPlugPrefab>.Fail(ItemMotionStatus.Unsupported,"Cannot build route rotation: " + e.Message); }
    }
    private static KC.SubAnimFunc Key(KC.AnimEase ease,int ms,bool reverse=false) => new(){Ease=ease,Duration=new TimeInt32(ms),Reverse=reverse};
    private static KC Hold(int ms) => new(){Version=0,SubVersion=3,TransAxis=KC.EAxis.X,RotAxis=KC.EAxis.X,
        TransAnimFunc=new(){IsDuration=true,SubFuncs=[Key(KC.AnimEase.Constant,ms)]},RotAnimFunc=new(){IsDuration=true,SubFuncs=[Key(KC.AnimEase.Constant,ms)]}};
    private static CPlugPrefab.EntRef Body(CPlugDynaObjectModel model,Vector3 p) => new(){Model=model,Position=new(p.X,p.Y,p.Z),Rotation=Quat.Identity,
        Params=new NPlugDynaObjectModel_SInstanceParams{Version=2,IsKinematic=true,PeriodSc=1,PeriodScMax=-1,Phase01Max=-1}};
    private static CPlugPrefab.EntRef Driver(KC model,int parent,int child) => new(){Model=model,Rotation=Quat.Identity,Params=new NPlugDyna_SPrefabConstraintParams{Version=0,Ent1=parent,Ent2=child}};
}
