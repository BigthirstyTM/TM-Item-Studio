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
            var cursor = plan.Start;
            for (int i=0; i<plan.Legs.Count; i++)
            {
                var leg = plan.Legs[i]; int parent = i;
                ((CPlugDynaObjectModel)bodies[i].Model!).Mesh = micro.Mesh;
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
                int visible=bodies.Count;
                bodies.Add(Body(visual,cursor-anchor));
                var instance=(NPlugDynaObjectModel_SInstanceParams)bodies[^1].Params!;
                instance.TextureId=sourceParams.TextureId; instance.CastStaticShadow=sourceParams.CastStaticShadow;
                var visibleDriver=Hold(plan.DurationMs);
                visibleDriver.ShaderTcType=sourceShader.ShaderTcType; visibleDriver.ShaderTcVersion=sourceShader.ShaderTcVersion;
                visibleDriver.ShaderTcAnimFunc=sourceShader.ShaderTcAnimFunc; visibleDriver.ShaderTcDataTransSub=sourceShader.ShaderTcDataTransSub;
                drivers.Add(Driver(visibleDriver,parent,visible));
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
