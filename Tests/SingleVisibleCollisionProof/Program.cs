using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Plug;
using GBX.NET.Engines.Meta;
using TM_Item_Studio.Models;
using TmEssentials;
using V=System.Numerics.Vector3;
using KC=GBX.NET.Engines.Meta.NPlugDyna_SKinematicConstraint;
Gbx.LZO=new GBX.NET.LZO.MiniLZO();
if(args.Length!=3 && !(args.Length==4&&args[3]=="--include-crash-fixture"))throw new ArgumentException("Usage: cube prior-fixture-root output-folder [--include-crash-fixture]");
Directory.CreateDirectory(args[2]);
var names=new List<string>{"OverlayHard","OverlayOverlap","OverlaySoft1","OverlaySoft2","OverlaySoftOut","OrbitCage","OrbitCompoundCage","CurvedLoopCage","ChainBaseline","ChainPhysicalFlag","ChainPhysicalZeroAnim"};
// Native placement crashed for this diagnostic. Never generate it by default.
if(args.Length==4)names.Add("ChainNonKinematic");
foreach(var name in names) {
 var item=Converted();
 if(name.StartsWith("Chain")) {
  var source=(CPlugPrefab)item.EntityModel!;
  var originalBody=(CPlugDynaObjectModel)source.Ents.Single(e=>e.Model is CPlugDynaObjectModel).Model!;
  Transform(originalBody,p=>new(p.X*2,p.Y*.25f,p.Z*2));
  V[] points=[new(0,0,0),new(16,0,0),new(16,0,16),new(0,0,16)];
  var plan=ItemMotionPath.Plan(points,MotionPathUse.Scenery,MotionPathRepeat.Closed,2,KC.AnimEase.Linear);
  var visual=ItemMotionPath.Build(source,plan.Value!);
  if(!visual.Success)throw new Exception(visual.Reason);
  var body=visual.Value!.Ents.First(e=>e.Model is CPlugDynaObjectModel);
  var model=(CPlugDynaObjectModel)body.Model!;
  model.StaticShape=originalBody.StaticShape;model.DynaShape=originalBody.DynaShape;
  model.LocAnimIsPhysical=name is "ChainPhysicalFlag" or "ChainPhysicalZeroAnim";
  if(name=="ChainPhysicalZeroAnim") {
   var loc=new CPlugAnimLocSimple{AnimationVersion=4,TransPeriod=10000,TransPeriodMax=10000,TransY=0,RotPeriod=0,RotPeriodMax=0,RotAngle=0,Axis=1};
   loc.CreateChunk<CPlugAnimLocSimple.Chunk090F8000>().Version=0;
   model.LocAnim=loc;
  }
  if(name=="ChainNonKinematic")((NPlugDynaObjectModel_SInstanceParams)body.Params!).IsKinematic=false;
  source.Ents=[..visual.Value.Ents.Where(e=>e.Model is CPlugDynaObjectModel),..visual.Value.Ents.Where(e=>e.Model is KC)];
 } else if(name.StartsWith("Overlay")) {
  var hard=name!="OverlayOverlap";
  var prior=Path.Combine(args[1],hard?"TMIS_CollisionFollow2/TMIS_CF2_RelaySquare.Item.Gbx":"TMIS_CollisionHandoff/TMIS_CH_SquareOverlap.Item.Gbx");
  var physics=(CPlugPrefab)Gbx.Parse<CGameItemModel>(prior).Node.EntityModel!;
  var collisionBodies=physics.Ents.Where(e=>e.Model is CPlugDynaObjectModel).ToArray();
  var collisionDrivers=physics.Ents.Where(e=>e.Model is KC).ToArray();
  foreach(var m in collisionBodies.Select(e=>(CPlugDynaObjectModel)e.Model!).Distinct())
   m.Mesh=ItemKinematicEntityTemplate.CreateCarrierBody().Mesh;
  if(name is "OverlaySoft1" or "OverlaySoft2") {
   var soft=name=="OverlaySoft1"?1000:2000;
   for(int i=0;i<collisionBodies.Length;i++) {
    // Experimental phase-offset hypothesis, NOT native-validated. These
    // variants desynchronize in game; retain only as failed research fixtures.
    collisionBodies[i].Params=new NPlugDynaObjectModel_SInstanceParams{Version=2,IsKinematic=true,PeriodSc=1,PeriodScMax=-1,Phase01=i==0?0:1-i*.25f,Phase01Max=-1};
    var driver=(KC)collisionDrivers[i].Model!;
    driver.TransAnimFunc=Func(Key(KC.AnimEase.Linear,8000),Key(KC.AnimEase.Constant,soft,true),Key(KC.AnimEase.Constant,24000-soft));
    driver.RotAnimFunc=Func(Key(KC.AnimEase.Constant,8000),Key(KC.AnimEase.QuadInOut,soft),Key(KC.AnimEase.Constant,24000-2*soft,true),Key(KC.AnimEase.QuadInOut,soft,true));
   }
  }
  if(name=="OverlaySoftOut") {
   for(int i=0;i<collisionBodies.Length;i++) {
    var start=1000+i*9000;var driver=(KC)collisionDrivers[i].Model!;
    driver.TransAnimFunc=i==3
     ?Func(Key(KC.AnimEase.Constant,1000,true),Key(KC.AnimEase.Constant,27000),Key(KC.AnimEase.Linear,8000))
     :Func(Key(KC.AnimEase.Constant,start),Key(KC.AnimEase.Linear,8000),Key(KC.AnimEase.Constant,1000,true),Key(KC.AnimEase.Constant,27000-start));
    driver.RotAnimFunc=i switch {
     0=>Func(Key(KC.AnimEase.Constant,9000),Key(KC.AnimEase.QuadInOut,1000),Key(KC.AnimEase.Constant,26000,true)),
     3=>Func(Key(KC.AnimEase.QuadInOut,1000),Key(KC.AnimEase.Constant,26000,true),Key(KC.AnimEase.Constant,9000)),
     _=>Func(Key(KC.AnimEase.Constant,start-1000,true),Key(KC.AnimEase.Constant,9000),Key(KC.AnimEase.QuadInOut,1000),Key(KC.AnimEase.Constant,27000-start,true))
    };
   }
  }
  var source=(CPlugPrefab)item.EntityModel!;
  Transform((CPlugDynaObjectModel)source.Ents.Single(e=>e.Model is CPlugDynaObjectModel).Model!,p=>new(p.X*2,p.Y*.25f,p.Z*2));
  V[] points=[new(0,0,0),new(16,0,0),new(16,0,16),new(0,0,16)];
  var plan=ItemMotionPath.Plan(points,MotionPathUse.Scenery,MotionPathRepeat.Closed,2,KC.AnimEase.Linear,name=="OverlaySoftOut"?1000:hard?0:2000);
  if(!plan.Success)throw new Exception(plan.Reason);
  var visual=ItemMotionPath.Build(source,plan.Value!);
  if(!visual.Success)throw new Exception(visual.Reason);
  var visualBodies=visual.Value!.Ents.Where(e=>e.Model is CPlugDynaObjectModel).ToArray();
  var visualDrivers=visual.Value.Ents.Where(e=>e.Model is KC).ToArray();
  foreach(var e in visualBodies) {
   var ip=(NPlugDynaObjectModel_SInstanceParams)e.Params!;
   // Hypothesized 2-second phase advance for the overlap visual. Persisted
   // Phase01 semantics are unverified; this fixture exhibits ghost support.
   ip.Phase01=name=="OverlayOverlap"?.05f:0;ip.Phase01Max=-1;
  }
  foreach(var e in visualDrivers) {
   var b=(NPlugDyna_SPrefabConstraintParams)e.Params!;
   if(b.Ent1>=0)b.Ent1+=collisionBodies.Length;
   b.Ent2+=collisionBodies.Length;
  }
  source.Ents=[..collisionBodies,..visualBodies,..collisionDrivers,..visualDrivers];
 } else {
  var prefab=(CPlugPrefab)item.EntityModel!;var bodies=new List<CPlugPrefab.EntRef>();var drivers=new List<CPlugPrefab.EntRef>();
  for(int piece=0;piece<5;piece++) {
   var part=(CPlugPrefab)Converted().EntityModel!;
   var body=part.Ents.Single(e=>e.Model is CPlugDynaObjectModel);
   var d=(CPlugDynaObjectModel)body.Model!;
   Vec3 Geometry(Vec3 p)=>piece switch {
    0=>new(p.X*2+12,p.Y*.25f,p.Z*2),
    1=>new(p.X*.125f+4,p.Y*.5f+2,p.Z*2),
    2=>new(p.X*.125f+20,p.Y*.5f+2,p.Z*2),
    3=>new(p.X*2+12,p.Y*.5f+2,p.Z*.125f-8),
    _=>new(p.X*2+12,p.Y*.5f+2,p.Z*.125f+8)
   };
   Transform(d,p=>name=="CurvedLoopCage"?new Vec3(Geometry(p).X-12,Geometry(p).Y,Geometry(p).Z+12):Geometry(p));
   body.Position=name=="CurvedLoopCage"?new(0,0,-12):new(-12,0,0);body.Rotation=Quat.Identity;
   var ip=(NPlugDynaObjectModel_SInstanceParams)body.Params!;ip.Phase01=0;ip.Phase01Max=-1;ip.PeriodSc=1;ip.PeriodScMax=-1;
   bodies.Add(body);
   drivers.Add(new(){Model=new KC{Version=0,SubVersion=3,TransAxis=KC.EAxis.X,TransMin=0,TransMax=name=="OrbitCompoundCage"?16:0,TransAnimFunc=name=="OrbitCompoundCage"?Func(Key(KC.AnimEase.QuadInOut,12000),Key(KC.AnimEase.QuadInOut,12000,true)):Func(Key(KC.AnimEase.Constant,24000)),RotAxis=KC.EAxis.Y,AngleMinDeg=0,AngleMaxDeg=360,RotAnimFunc=Func(Key(KC.AnimEase.Linear,24000))},Rotation=Quat.Identity,Params=new NPlugDyna_SPrefabConstraintParams{Version=0,Ent1=-1,Ent2=piece}});
   if(name=="CurvedLoopCage") {
    var driver=(KC)drivers[^1].Model!;
    driver.TransMax=32;driver.AngleMaxDeg=180;
    driver.TransAnimFunc=Func(Key(KC.AnimEase.QuadInOut,8000),Key(KC.AnimEase.Constant,8000,true),Key(KC.AnimEase.QuadInOut,8000,true),Key(KC.AnimEase.Constant,8000));
    driver.RotAnimFunc=Func(Key(KC.AnimEase.Constant,8000),Key(KC.AnimEase.QuadInOut,8000),Key(KC.AnimEase.Constant,8000,true),Key(KC.AnimEase.QuadInOut,8000,true));
   }
  }
  prefab.Ents=[..bodies,..drivers];
 }
 item.Name="TMIS_SV_"+name;item.Ident=new(item.Name,26,item.Ident.Author);item.ArchetypeRef="";
 var path=Path.Combine(args[2],item.Name+".Item.Gbx");new Gbx<CGameItemModel>(item).Save(path);
 var read=(CPlugPrefab)Gbx.Parse<CGameItemModel>(path).Node.EntityModel!;
 var bodyCount=read.Ents.Count(e=>e.Model is CPlugDynaObjectModel);
 if(bodyCount!=(name.StartsWith("Overlay")?9:5))throw new Exception("Body count mismatch");
 foreach(var e in read.Ents.Where(e=>e.Model is KC)) {
  var b=(NPlugDyna_SPrefabConstraintParams)e.Params!;
  if(b.Ent1 < -1||b.Ent1>=bodyCount||b.Ent2<0||b.Ent2>=bodyCount)throw new Exception("Binding mismatch");
  foreach(var channel in new[]{((KC)e.Model!).TransAnimFunc,((KC)e.Model!).RotAnimFunc}) {
   if(channel is null||!channel.IsDuration||channel.SubFuncs is not {} keys||keys.Length is <1 or >4)
    throw new Exception("Expected one to four duration keys");
   if(keys.Any(k=>k.Duration.TotalMilliseconds<=0))
    throw new Exception("Nonpositive key duration");
  }
 }
 Console.WriteLine(path);
}
CGameItemModel Converted(){var r=ItemKinematicEntityTemplate.ConvertStaticToKinematic(Gbx.Parse<CGameItemModel>(args[0]).Node,ItemKinematicEntityTemplate.GetDefaultMovingTemplate());if(!r.Success)throw new Exception(r.Reason);return r.Value!;}
void Transform(CPlugDynaObjectModel model,Func<Vec3,Vec3> transform) {
 foreach(var s in (((CPlugSolid2Model)model.Mesh!).Visuals??[]).SelectMany(v=>v.VertexStreams??[]).Distinct())
  if(s.Positions is {} p)for(int i=0;i<p.Length;i++)p[i]=transform(p[i]);
 foreach(var shape in new[]{model.StaticShape,model.DynaShape}.Distinct())
  if(shape?.Surf is CPlugSurface.Mesh m)for(int i=0;i<m.Vertices.Length;i++)m.Vertices[i]=transform(m.Vertices[i]);
}
KC.SubAnimFunc Key(KC.AnimEase ease,int ms,bool reverse=false)=>new(){Ease=ease,Duration=new TimeInt32(ms),Reverse=reverse};
KC.AnimFunc Func(params KC.SubAnimFunc[] keys)=>new(){IsDuration=true,SubFuncs=keys};
