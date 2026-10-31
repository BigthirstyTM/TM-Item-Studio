using System.Numerics;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Plug;
using GBX.NET.Engines.Meta;
using TM_Item_Studio.Models;
using TmEssentials;
using KC=GBX.NET.Engines.Meta.NPlugDyna_SKinematicConstraint;
Gbx.LZO=new GBX.NET.LZO.MiniLZO();
if(args.Length is <3 or >4 || (args.Length==4 && args[3] is not ("focus" or "full-variants")))throw new ArgumentException("Usage: cube route128.json output-folder [focus|full-variants]");
var focus=args.Length==4&&args[3]=="focus";
var variants=args.Length==4&&args[3]=="full-variants";
Directory.CreateDirectory(args[2]);
var original=JsonSerializer.Deserialize<float[][]>(File.ReadAllText(args[1]))!.Select(p=>new Vector3(p[0],p[1],p[2])).ToArray();
if(original.Length!=128)throw new Exception("Expected exact 128-point coaster fixture");
if(focus)original=original[99..107];
var points=original.Select(p=>p-original[0]).ToArray();
const int dwell=8000;
foreach(var name in focus ? new[]{"Relay5BoundsFocus","Relay5BoundsReverse","Relay5BoundsNear"} : variants ? new[]{"Relay5BoundsFullReverse","Relay5BoundsFullNear"} : new[]{"ChainCollision","Relay20","Relay5","Relay5Bounds"}) {
 var parking=name.EndsWith("Near")?(focus?32:64):128;
 var foldAngle=name.EndsWith("Reverse")?-180:180;
 var item=Converted();var prefab=(CPlugPrefab)item.EntityModel!;
 var model=(CPlugDynaObjectModel)prefab.Ents.Single(e=>e.Model is CPlugDynaObjectModel).Model!;
 var planResult=ItemMotionPath.Plan(points,MotionPathUse.Scenery,(focus?MotionPathRepeat.Reset:MotionPathRepeat.Closed),name.StartsWith("Relay5")?5:20,KC.AnimEase.Linear);
 if(!planResult.Success)throw new Exception(planResult.Reason);
 var plan=planResult.Value!;var total=focus?60000:plan.DurationMs+dwell;
 if(name=="ChainCollision") {
  var built=ItemMotionPath.Build(prefab,plan);
  if(!built.Success)throw new Exception(built.Reason);
  prefab=built.Value!;
  var visible=(CPlugDynaObjectModel)prefab.Ents.First(e=>e.Model is CPlugDynaObjectModel).Model!;
  visible.StaticShape=model.StaticShape;visible.DynaShape=model.DynaShape;
  foreach(var e in prefab.Ents.Where(e=>e.Model is KC)) {
   var c=(KC)e.Model!;
   foreach(var channel in new[]{c.TransAnimFunc!,c.RotAnimFunc!}) {
    if(channel.SubFuncs![0].Ease==KC.AnimEase.Constant&&!channel.SubFuncs[0].Reverse)
     channel.SubFuncs[0].Duration=new TimeInt32(channel.SubFuncs[0].Duration.TotalMilliseconds+dwell);
    else channel.SubFuncs=[Key(KC.AnimEase.Constant,dwell),..channel.SubFuncs];
   }
  }
 } else {
  var bodies=new List<CPlugPrefab.EntRef>();var drivers=new List<CPlugPrefab.EntRef>();
  var cursor=Vector3.Zero;
  for(int i=0;i<plan.Legs.Count;i++) {
   var leg=plan.Legs[i];var fresh=(CPlugPrefab)Converted().EntityModel!;
   var body=fresh.Ents.Single(e=>e.Model is CPlugDynaObjectModel);
   var d=(CPlugDynaObjectModel)body.Model!;
   var x=Vector3.Normalize(leg.Delta);var z=Vector3.Normalize(Vector3.Cross(x,Vector3.UnitY));var y=Vector3.Cross(z,x);
   var rot=Quaternion.CreateFromRotationMatrix(new Matrix4x4(x.X,x.Y,x.Z,0,y.X,y.Y,y.Z,0,z.X,z.Y,z.Z,0,0,0,0,1));
   var q=new Quat(rot.X,rot.Y,rot.Z,rot.W);
   var inverse=Quaternion.Inverse(rot);var offset=new Vector3(0,parking,0);
   // Undo the segment frame in the geometry so every active cube has exactly
   // the original orientation. Rotation only parks inactive bodies below ground.
   Vec3 Transform(Vec3 p){var v=Vector3.Transform(new Vector3(p.X,p.Y,p.Z),inverse)+offset;return new(v.X,v.Y,v.Z);}
   foreach(var s in (((CPlugSolid2Model)d.Mesh!).Visuals??[]).SelectMany(v=>v.VertexStreams??[]).Distinct())
    if(s.Positions is {} ps)for(int j=0;j<ps.Length;j++)ps[j]=Transform(ps[j]);
   if(name.StartsWith("Relay5Bounds")) {
    var solid=(CPlugSolid2Model)d.Mesh!;
    foreach(var visual in solid.Visuals??[]) {
     var ps=(visual.VertexStreams??[]).SelectMany(s=>s.Positions??[]).Select(p=>new Vector3(p.X,p.Y,p.Z)).ToArray();
     if(ps.Length==0)throw new Exception("Missing transformed visual positions");
     var min=ps.Aggregate(Vector3.Min);var max=ps.Aggregate(Vector3.Max);
     var centre=(min+max)*.5f;var extent=(max-min)*.5f+new Vector3(.001f);
     visual.BoundingBox=new(centre.X,centre.Y,centre.Z,extent.X,extent.Y,extent.Z);
    }
    // The cube fixture has no skeletal boxes; do not silently preserve stale
    // bounds on an unsupported skinned source.
    if(solid.Boxes is {Length:>0})throw new Exception("Unexpected solid boxes");
   }
   foreach(var sh in new[]{d.StaticShape,d.DynaShape}.Distinct())
    if(sh?.Surf is CPlugSurface.Mesh m)for(int j=0;j<m.Vertices.Length;j++)m.Vertices[j]=Transform(m.Vertices[j]);
   var pos=cursor-Vector3.Transform(offset,rot);
   // Check each original cube corner in the active frame, not just its centre.
   foreach(var cx in new[]{-4f,4f})foreach(var cy in new[]{-4f,4f})foreach(var cz in new[]{-4f,4f}) {
    var p=new Vector3(cx,cy,cz);var local=Vector3.Transform(p,inverse)+offset;
    if(Vector3.Distance(Vector3.Transform(local,rot)+pos,cursor+p)>.001f)
     throw new Exception("Active collision/visual orientation mismatch");
   }
   body.Position=new(pos.X,pos.Y,pos.Z);body.Rotation=q;
   body.Params=new NPlugDynaObjectModel_SInstanceParams{Version=2,IsKinematic=true,PeriodSc=1,PeriodScMax=-1,Phase01=0,Phase01Max=-1};
   bodies.Add(body);
   var start=dwell+leg.StartMs;var tail=total-start-leg.MoveMs;
   var trans=new List<KC.SubAnimFunc>{Key(KC.AnimEase.Constant,start),Key(KC.AnimEase.Linear,leg.MoveMs)};
   if(tail>0)trans.Add(Key(KC.AnimEase.Constant,tail,true));
   var fold=new List<KC.SubAnimFunc>();
   if(i>0)fold.Add(Key(KC.AnimEase.Constant,start,true));
   fold.Add(Key(KC.AnimEase.Constant,leg.MoveMs+(i==0?dwell:0)+(focus&&i==plan.Legs.Count-1?tail:0)));
   if(tail>0&&!(focus&&i==plan.Legs.Count-1))fold.Add(Key(KC.AnimEase.Constant,tail,true));
   drivers.Add(new(){Model=new KC{Version=0,SubVersion=3,TransAxis=KC.EAxis.X,TransMin=0,TransMax=leg.Delta.Length(),TransAnimFunc=Func(trans.ToArray()),RotAxis=KC.EAxis.X,AngleMinDeg=0,AngleMaxDeg=foldAngle,RotAnimFunc=Func(fold.ToArray())},Rotation=Quat.Identity,Params=new NPlugDyna_SPrefabConstraintParams{Version=0,Ent1=-1,Ent2=i}});
   cursor+=leg.Delta;
   // Inactive cube centre must be below Stadium terrain for this fixture.
   var parked=cursor-2*Vector3.Transform(offset,rot);
   if(parked.Y+15+7>=8)throw new Exception("Parking clearance failed");
  }
  prefab.Ents=[..bodies,..drivers];
 }
 item.EntityModel=prefab;item.Name="TMIS_CC_"+name;item.Ident=new(item.Name,26,item.Ident.Author);item.ArchetypeRef="";
 var path=Path.Combine(args[2],item.Name+".Item.Gbx");new Gbx<CGameItemModel>(item).Save(path);
 var read=(CPlugPrefab)Gbx.Parse<CGameItemModel>(path).Node.EntityModel!;
 var count=read.Ents.Count(e=>e.Model is CPlugDynaObjectModel);
 if(count!=(name=="ChainCollision"?129:plan.Legs.Count))throw new Exception("Body count changed");
 if(name.StartsWith("Relay5Bounds"))foreach(var d in read.Ents.Where(e=>e.Model is CPlugDynaObjectModel).Select(e=>(CPlugDynaObjectModel)e.Model!))
  foreach(var visual in ((CPlugSolid2Model)d.Mesh!).Visuals??[]) {
   var box=visual.BoundingBox;
   foreach(var p in (visual.VertexStreams??[]).SelectMany(s=>s.Positions??[]))
    if(MathF.Abs(p.X-box.X)>box.X2+.001f||MathF.Abs(p.Y-box.Y)>box.Y2+.001f||MathF.Abs(p.Z-box.Z)>box.Z2+.001f)
     throw new Exception("Serialized visual bounds do not enclose transformed geometry");
  }
 foreach(var e in read.Ents.Where(e=>e.Model is KC)) {
  var binding=(NPlugDyna_SPrefabConstraintParams)e.Params!;
  if(binding.Ent1< -1||binding.Ent1>=count||binding.Ent2<0||binding.Ent2>=count)throw new Exception("Invalid binding");
  foreach(var c in new[]{((KC)e.Model!).TransAnimFunc,((KC)e.Model!).RotAnimFunc})
   if(c?.SubFuncs is not {} keys||keys.Length>4||keys.Any(k=>k.Duration.TotalMilliseconds<=0)||keys.Sum(k=>k.Duration.TotalMilliseconds)!=total)throw new Exception("Invalid native timeline");
 }
 File.WriteAllText(Path.Combine(args[2],item.Name+".json"),JsonSerializer.Serialize(new{sourceRoute=args[1],sourcePointStart=focus?99:0,sourcePointEnd=focus?106:127,parking,foldAngle,holdEnd=focus,originalStart=new[]{original[0].X,original[0].Y,original[0].Z},placement=new[]{400,15,400},coasterPlacement=new[]{400-original[0].X,15-original[0].Y,400-original[0].Z},speed=name.StartsWith("Relay5")?5:20,dwellMs=dwell,cycleMs=total,legs=plan.Legs.Select(l=>new{delta=new[]{l.Delta.X,l.Delta.Y,l.Delta.Z},startMs=l.StartMs+dwell,moveMs=l.MoveMs}),nativeTestRequired=true}));
 Console.WriteLine($"{path}: {count} bodies, {total} ms");
}
CGameItemModel Converted(){var result=ItemKinematicEntityTemplate.ConvertStaticToKinematic(Gbx.Parse<CGameItemModel>(args[0]).Node,ItemKinematicEntityTemplate.GetDefaultMovingTemplate());if(!result.Success)throw new Exception(result.Reason);return result.Value!;}
KC.SubAnimFunc Key(KC.AnimEase ease,int ms,bool reverse=false)=>new(){Ease=ease,Duration=new TimeInt32(ms),Reverse=reverse};
KC.AnimFunc Func(params KC.SubAnimFunc[] keys)=>new(){IsDuration=true,SubFuncs=keys};
