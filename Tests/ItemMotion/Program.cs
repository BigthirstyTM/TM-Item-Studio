using System.Numerics;
using System.Security.Cryptography;
using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;
using GBX.NET.Serialization;
using TM_Item_Studio.Models;
using TmEssentials;
using KC = GBX.NET.Engines.Meta.NPlugDyna_SKinematicConstraint;

// Synthetic authored data only. Expected numbers below are hand-calculated, not produced by the evaluator.
int passed = 0, failed = 0;
Gbx.LZO ??= new GBX.NET.LZO.MiniLZO();
Console.WriteLine("Bundled parser SHA256: " + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Gbx).Assembly.Location))).ToLowerInvariant());

Check("Bezier samples cubic handles and enforces native route limits",()=>{
 var handles=new[]{new BezierHandle(Vector3.Zero,new(0,0,10)),new BezierHandle(new(0,0,10),Vector3.Zero)};
 var settings=new BezierSettings(true,2,true,handles);
 var p=ItemMotionAuthoring.Bezier([Vector3.Zero,new(10,0,0)],settings,MotionPathUse.Scenery,MotionPathRepeat.Reset,5,KC.AnimEase.Linear,0,0,64);
 Require(p.Success,p.Reason??"Bezier failed");Vector(p.Value!.Legs[0].Delta,new(5,0,7.5f));Vector(p.Value.Legs.Aggregate(Vector3.Zero,(a,l)=>a+l.Delta),new(10,0,0));
 Require(!ItemMotionAuthoring.Bezier([Vector3.Zero,new(10,0,0)],settings,MotionPathUse.Scenery,MotionPathRepeat.Reset,5,KC.AnimEase.Linear,100,0,64).Success,"pauses on samples accepted");
 var points=Enumerable.Range(0,20).Select(i=>new Vector3(i*10,0,i%2*10)).ToArray();
 Require(!ItemMotionAuthoring.Bezier(points,new(true,16,true,points.Select(_=>new BezierHandle(Vector3.Zero,Vector3.Zero)).ToArray()),MotionPathUse.Scenery,MotionPathRepeat.Reset,1,KC.AnimEase.Linear,0,0,64).Success,"Bezier exceeded 128 legs");
});
Check("look-at compiles fixed targets into native orientation and shifted followers",()=>{
 var p=ItemMotionPath.Plan([Vector3.Zero,new(10,0,0)],MotionPathUse.Scenery,MotionPathRepeat.PingPong,5,KC.AnimEase.Linear).Value! with{LookAtTarget=new Vector3(0,0,10)};
 var compiled=ItemMotionPathProfile.Compile(p);Require(compiled.Success,compiled.Reason??"look-at");Near(compiled.Value!.Rotations![0].Yaw,-90);Near(compiled.Value.Rotations[1].Yaw,-135);
 var moved=ItemMotionAuthoring.LookAt(compiled.Value with{Start=new(0,10,0)});Near(moved.Rotations![0].Pitch,-45);
 var source=(CPlugPrefab)KinematicTemplate().EntityModel!;source.Ents[0].Rotation=Quat.Identity;((CPlugDynaObjectModel)source.Ents[0].Model!).Mesh=RelaySolid();
 var built=ItemMotionPath.Build(source,p);Require(built.Success,built.Reason??"look-at export");using var stream=new MemoryStream();new Gbx<CPlugPrefab>(built.Value!).Save(stream);stream.Position=0;Require(Gbx.Parse<CPlugPrefab>(stream).Node.Ents.Length>2,"look-at export missing drivers");
 Require(!ItemMotionPathProfile.Compile(p with{LookAtTarget=Vector3.Zero}).Success,"coincident target accepted");
 Require(!ItemMotionPath.Validate(p with{LookAtTarget=new(float.NaN,0,0)}).Success,"nonfinite target accepted");
});
Check("three or more followers survive near-boundary delay splits",()=>{
 var source=(CPlugPrefab)KinematicTemplate().EntityModel!;source.Ents[0].Rotation=Quat.Identity;((CPlugDynaObjectModel)source.Ents[0].Model!).Mesh=RelaySolid();
 foreach(var use in new[]{MotionPathUse.Scenery,MotionPathUse.CollisionRelay}) {
  var p=ItemMotionPath.Plan([Vector3.Zero,new(10.2f,0,0),new(10.2f,0,10.2f),new(0,0,10.2f)],use,MotionPathRepeat.Closed,10,KC.AnimEase.Linear).Value!;
  p=p with{Rotations=Enumerable.Repeat(new MotionPathRotation(),5).ToArray()};
  var shifted=ItemMotionArray.Shift(p,3000);Require(shifted.Legs.Count==4,"60 ms split was not snapped");Near(shifted.Start.X,10.2f);Near(shifted.Start.Z,0);Require(shifted.DurationMs==p.DurationMs&&shifted.Legs.All(l=>l.MoveMs>=80),"snap changed lap or native timing");
  foreach(int count in new[]{2,3,4,8}) {var result=ItemMotionPath.Build(source,p with{Array=new(count,1500)});Require(result.Success,$"{use}, {count} copies: {result.Reason}");}
 }
});
Check("full-detail arrays preserve the segment budget when shifting copies",()=>{
 var points=Enumerable.Range(0,128).Select(i=>new Vector3(100*MathF.Cos(i*MathF.Tau/128),0,100*MathF.Sin(i*MathF.Tau/128))).ToArray();
 var p=ItemMotionPath.Plan(points,MotionPathUse.CollisionRelay,MotionPathRepeat.Closed,5,KC.AnimEase.Linear,0,8000).Value!;
 p=p with{Rotations=Enumerable.Repeat(new MotionPathRotation(),129).ToArray()};
 var shifted=ItemMotionArray.Shift(p,500);Require(shifted.Legs.Count==128,"phase split exceeded budget");Require(shifted.DurationMs==p.DurationMs&&shifted.StartDelayMs==8000,"loop or dwell changed");Require(ItemMotionPath.Validate(shifted).Success,"invalid shifted full-detail route");
});
Check("smoothed routes build arrays with three and eight followers",()=>{
 var source=(CPlugPrefab)KinematicTemplate().EntityModel!;source.Ents[0].Rotation=Quat.Identity;((CPlugDynaObjectModel)source.Ents[0].Model!).Mesh=RelaySolid();
 foreach(var detail in new[]{2,8,16}) {
  var p=ItemMotionSmoothPath.Plan([Vector3.Zero,new(20,0,0),new(20,0,20),new(0,0,20)],MotionPathRepeat.Closed,5,.6f,detail).Value!;
  p=p with{Rotations=Enumerable.Repeat(new MotionPathRotation(),p.Legs.Count+1).ToArray()};
  foreach(var count in new[]{3,8}){var built=ItemMotionPath.Build(source,p with{Array=new(count,1000)});Require(built.Success,built.Reason??"smooth array");}
 }
});
Check("array clock rebasing preserves constant acceleration and route closure",()=>{
 var p=ItemMotionPath.Plan([Vector3.Zero,new(20,0,0),new(20,0,20),new(0,0,20)],MotionPathUse.Scenery,MotionPathRepeat.Closed,5,KC.AnimEase.Linear).Value!;
 p=p with{Rotations=Enumerable.Repeat(new MotionPathRotation(),5).ToArray()};
 var shifted=ItemMotionArray.Shift(p,1000);Near(shifted.Start.Z,5);Near(shifted.Start.X,0);Require(shifted.Legs.Count==5,"split missing");Require(shifted.Legs.Sum(l=>l.MoveMs)==16000,"period changed");
 var ramp=p with{ContinuousVelocity=true,Legs=p.Legs.Select(l=>l with{VelocityRatio=.5f}).ToArray()};
 var rampShift=ItemMotionArray.Shift(ramp,1000);Near(rampShift.Start.Z,6.875f);Near(rampShift.Legs[0].VelocityRatio,10f/11f);
 Require((rampShift.Start+rampShift.Legs.Aggregate(Vector3.Zero,(a,l)=>a+l.Delta)-rampShift.Start).Length()<.001,"route not closed");
 var snapped=ItemMotionArray.Shift(p,1);Require(snapped.Legs.Count==p.Legs.Count&&snapped.Start==p.Start,"near-boundary phase should snap without a tiny leg");
});
Check("arrays share serialized models, remap bindings and leave the source untouched",()=>{
 var source=(CPlugPrefab)KinematicTemplate().EntityModel!;source.Ents[0].Rotation=Quat.Identity;((CPlugDynaObjectModel)source.Ents[0].Model!).Mesh=RelaySolid();
 var p=ItemMotionPath.Plan([Vector3.Zero,new(20,0,0),new(20,0,20),new(0,0,20)],MotionPathUse.Scenery,MotionPathRepeat.Closed,5,KC.AnimEase.Linear).Value!;
 p=p with{Rotations=Enumerable.Repeat(new MotionPathRotation(),5).ToArray(),Array=new(4,1000),Layers=[new("Bob",Kind:MotionLayerKind.Translate,Axis:KC.EAxis.Y,Amplitude:1)]};
 var built=ItemMotionPath.Build(source,p);Require(built.Success,built.Reason??"build");
 using var bytes=new MemoryStream();new Gbx<CPlugPrefab>(built.Value!).Save(bytes);bytes.Position=0;var read=Gbx.Parse<CPlugPrefab>(bytes).Node;
 var bodies=read.Ents.Where(e=>e.Model is CPlugDynaObjectModel).ToArray();Require(bodies.Length>80,"missing followers");Require(bodies.Select(e=>((CPlugDynaObjectModel)e.Model!).Mesh).Distinct().Count()<10,"mesh models duplicated per copy");
 foreach(var e in read.Ents.Where(e=>e.Model is KC)){var c=(NPlugDyna_SPrefabConstraintParams)e.Params!;Require(c.Ent2<bodies.Length&&c.Ent1<bodies.Length,"native body index must exclude constraint entries");Require(c.Ent1==-1||read.Ents[c.Ent1].Model is CPlugDynaObjectModel,"parent remap");Require(read.Ents[c.Ent2].Model is CPlugDynaObjectModel,"child remap");var driver=(KC)e.Model!;Require(driver.TransAnimFunc!.SubFuncs.Length<=4&&driver.RotAnimFunc!.SubFuncs.Length<=4,"native key limit");}
 Require(source.Ents.Length==2,"source changed");
 var settings=p with{Array=new(8,1000,new(2,1,3),.5f,1.5f,3,123,true)};Require(ItemMotionArray.Instances(settings).SequenceEqual(ItemMotionArray.Instances(settings)),"spread nondeterministic");Require(ItemMotionArray.Instances(settings).Select(i=>i.Scale).Distinct().Count()<=3,"too many scale variants");
 Require(ItemMotionPath.Validate(p with{Use=MotionPathUse.CollisionRelay,Layers=[new()]}).Success,"experimental physical layers refused");
});
Check("collision arrays retain dwell, scale physical vertices and remap body bindings",()=>{
 var source=(CPlugPrefab)KinematicTemplate().EntityModel!;source.Ents[0].Rotation=Quat.Identity;var model=(CPlugDynaObjectModel)source.Ents[0].Model!;model.Mesh=RelaySolid();
 var p=ItemMotionPath.Plan([Vector3.Zero,new(20,0,0),new(20,0,20),new(0,0,20)],MotionPathUse.CollisionRelay,MotionPathRepeat.Closed,5,KC.AnimEase.Linear,0,8000).Value!;
 p=p with{Rotations=Enumerable.Repeat(new MotionPathRotation(),5).ToArray(),Array=new(3,1000,default,1.5f,1.5f)};
 var shifted=ItemMotionArray.Shift(p,1000);Require(shifted.StartDelayMs==8000&&shifted.Legs[0].StartMs==8000&&shifted.DurationMs==24000,"dwell changed");Near(shifted.Start.Z,5);
 var built=ItemMotionPath.Build(source,p);Require(built.Success,built.Reason??"build");
 var root=built.Value!.Ents.First();var body=(CPlugDynaObjectModel)root.Model!;var q=new Quaternion(root.Rotation.X,root.Rotation.Y,root.Rotation.Z,root.Rotation.W);
 var actual=((CPlugSurface.Mesh)body.DynaShape!.Surf!).Vertices;var original=((CPlugSurface.Mesh)model.DynaShape!.Surf!).Vertices;
 for(int i=0;i<actual.Length;i++)Vector(Vector3.Transform(new(actual[i].X,actual[i].Y,actual[i].Z),q)+new Vector3(root.Position.X,root.Position.Y,root.Position.Z),new Vector3(original[i].X,original[i].Y,original[i].Z)*1.5f);
 var roots=built.Value.Ents.Where(e=>e.Params is NPlugDyna_SPrefabConstraintParams{Ent1:-1}).ToArray();Require(roots.Length==14,"missing physical branches on followers");
 foreach(var entry in roots){var link=(NPlugDyna_SPrefabConstraintParams)entry.Params!;var physical=(CPlugDynaObjectModel)built.Value.Ents[link.Ent2].Model!;
  foreach(var shape in new[]{physical.StaticShape,physical.DynaShape}){var v=((CPlugSurface.Mesh)shape!.Surf!).Vertices;Require(v.Max(x=>Vector3.Distance(new(x.X,x.Y,x.Z),new(v[0].X,v[0].Y,v[0].Z)))>1.49f,"a follower lost its full-size collision");}}
 var micro=ItemKinematicEntityTemplate.CreateCarrierBody();model.StaticShape=micro.StaticShape;model.DynaShape=micro.DynaShape;
 Require(!ItemMotionPath.Build(source,p).Success,"carrier-only source advertised as passenger collision");
 var bodies=built.Value.Ents.TakeWhile(e=>e.Model is CPlugDynaObjectModel).Count();foreach(var e in built.Value.Ents.Where(e=>e.Model is KC)){var b=(NPlugDyna_SPrefabConstraintParams)e.Params!;Require(b.Ent1<bodies&&b.Ent2<bodies,"bad physical binding");}
});
Check("experimental physical layers use bounded collider poses and preserve phase on copies",()=>{
 var p=ItemMotionPath.Plan([Vector3.Zero,new(8,0,0),new(8,0,8),new(0,0,8)],MotionPathUse.CollisionRelay,MotionPathRepeat.Closed,4,KC.AnimEase.Linear,0,8000).Value!;
 p=p with{Rotations=Enumerable.Repeat(new MotionPathRotation(),5).ToArray()};
 foreach(var kind in Enum.GetValues<MotionLayerKind>()){
  var c=ItemMotionPathProfile.Compile(p with{Layers=[new("Physical layer",Kind:kind,Amplitude:.25f,PeriodMs:4000)]});Require(c.Success,c.Reason??"compile");Require(c.Value!.Layers is null&&c.Value.CollisionPoses?.Length==c.Value.Legs.Count&&c.Value.Legs.Count<=128,"missing baked poses");Require(c.Value.Legs.All(l=>l.MoveMs>=80),"sample too short");
  var shifted=ItemMotionArray.Shift(c.Value,250);Require(shifted.CollisionPoses!.Length==shifted.Legs.Count,"pose split mismatch");Require(shifted.StartDelayMs==8000,"delay lost");
 }
 var source=(CPlugPrefab)KinematicTemplate().EntityModel!;source.Ents[0].Rotation=Quat.Identity;((CPlugDynaObjectModel)source.Ents[0].Model!).Mesh=RelaySolid();
 var angle=Quaternion.CreateFromAxisAngle(Vector3.UnitZ,MathF.PI/2);
 var baked=p with{CollisionPoses=Enumerable.Repeat(angle,4).ToArray()};var result=ItemMotionPath.Build(source,baked);Require(result.Success,result.Reason??"physical pose");
 var root=result.Value!.Ents[0];var shape=((CPlugSurface.Mesh)((CPlugDynaObjectModel)root.Model!).DynaShape!.Surf!).Vertices;var q=new Quaternion(root.Rotation.X,root.Rotation.Y,root.Rotation.Z,root.Rotation.W);
 var edge=Vector3.Transform(new Vector3(shape[1].X-shape[0].X,shape[1].Y-shape[0].Y,shape[1].Z-shape[0].Z),q);Vector(edge,Vector3.UnitY);
 Require(!ItemMotionPath.Validate(p with{CollisionPoses=[new(float.NaN,0,0,1)]}).Success,"malformed pose accepted");
});
foreach(var kind in Enum.GetValues<MotionBehaviorKind>()) Check($"template {kind} compiles and serializes on a closed route",()=>{
    var points=Enumerable.Range(0,32).Select(i=>new Vector3(20*MathF.Cos(i*MathF.PI/16),5+2*MathF.Sin(i*MathF.PI/8),20*MathF.Sin(i*MathF.PI/16))).ToArray();
    var p=ItemMotionPath.Plan(points,MotionPathUse.Scenery,MotionPathRepeat.Closed,5,KC.AnimEase.Linear).Value!;
    var applied=ItemMotionBehavior.Apply(p,ItemMotionBehavior.Defaults(kind));Require(applied.Success,applied.Reason??"apply");
    var compiled=ItemMotionPathProfile.Compile(applied.Value!);Require(compiled.Success,compiled.Reason??"compile");
    var source=(CPlugPrefab)KinematicTemplate().EntityModel!;source.Ents[0].Rotation=Quat.Identity;((CPlugDynaObjectModel)source.Ents[0].Model!).Mesh=RelaySolid();
    var built=ItemMotionPath.Build(source,applied.Value!);Require(built.Success,built.Reason??"build");
    using var bytes=new MemoryStream();new Gbx<CPlugPrefab>(built.Value!).Save(bytes);bytes.Position=0;Require(Gbx.Parse<CPlugPrefab>(bytes).Node.Ents.Length>0,"empty serialized graph");
    if(kind is MotionBehaviorKind.LiftPlatform or MotionBehaviorKind.Drone or MotionBehaviorKind.Conveyor)Require(compiled.Value!.PauseMs==1000 && compiled.Value.SpeedPoints is null && compiled.Value.Ease==KC.AnimEase.QuadInOut,"station timing");
    if(kind==MotionBehaviorKind.Ride)Near(applied.Value!.Rotations![^1].Yaw-applied.Value.Rotations[0].Yaw,720);
});
Check("sparse cable routes gain enough samples to show sway",()=>{
 var p=ItemMotionPath.Plan([Vector3.Zero,new(20,5,0)],MotionPathUse.Scenery,MotionPathRepeat.Closed,4,KC.AnimEase.Linear).Value!;
 var r=ItemMotionBehavior.Apply(p,ItemMotionBehavior.Defaults(MotionBehaviorKind.CableCar));Require(r.Success,r.Reason??"cable");Require(r.Value!.Legs.Count>=16,"missing sway samples");Require(r.Value.Rotations!.Any(r=>Math.Abs(r.Roll)>2),"sway aliased to zero");
});
Check("lift supports exact vertical scenery and retains collision refusal",()=>{
 var p=ItemMotionPath.Plan([Vector3.Zero,new(0,15,0)],MotionPathUse.Scenery,MotionPathRepeat.Closed,4,KC.AnimEase.Linear).Value!;
 var applied=ItemMotionBehavior.Apply(p,ItemMotionBehavior.Defaults(MotionBehaviorKind.LiftPlatform));Require(applied.Success,applied.Reason??"lift");
 var source=(CPlugPrefab)KinematicTemplate().EntityModel!;source.Ents[0].Rotation=Quat.Identity;((CPlugDynaObjectModel)source.Ents[0].Model!).Mesh=RelaySolid();
 Require(ItemMotionPath.Build(source,applied.Value!).Success,"vertical scenery failed");
 Require(!ItemMotionPath.Build(source,applied.Value! with{Use=MotionPathUse.CollisionRelay}).Success,"vertical collider incorrectly allowed");
});
Check("custom templates adapt by distance, restore route, serialize and reject invalid input",()=>{
    var p=ItemMotionPath.Plan([Vector3.Zero,new(10,0,0),new(20,0,0)],MotionPathUse.Scenery,MotionPathRepeat.Reset,5,KC.AnimEase.Linear).Value!;
    p=p with{SpeedPoints=[2,6,10],Rotations=ItemMotionPathProfile.FollowDirection(p),ContinuousVelocity=true};
    var t=new MotionCustomTemplate("Test","description","Custom",p,IncludeTiming:false);
    var q=ItemMotionPath.Plan([Vector3.Zero,new(0,0,5),new(0,0,10),new(0,0,15),new(0,0,20)],MotionPathUse.Scenery,MotionPathRepeat.Reset,5,KC.AnimEase.Linear).Value!;
    var applied=ItemMotionCustomTemplate.Apply(t,q);Require(applied.Success,applied.Reason??"custom");Near(applied.Value!.SpeedPoints![1],4);Near(applied.Value.SpeedPoints[3],8);
    Near(applied.Value.Rotations![0].Yaw,ItemMotionPathProfile.FollowDirection(q)[0].Yaw);
    var json=System.Text.Json.JsonSerializer.Serialize(t,ItemMotionCustomTemplate.Json);
    var loaded=System.Text.Json.JsonSerializer.Deserialize<MotionCustomTemplate>(json,ItemMotionCustomTemplate.Json)!;
    Require(ItemMotionCustomTemplate.Validate(loaded)==null,"JSON roundtrip");
    Require(ItemMotionCustomTemplate.Apply(loaded with{IncludeRoute=true},q).Value!.Legs.Count==2,"route restore");
    Require(ItemMotionCustomTemplate.Validate(t with{Version=9})!=null,"unknown version");
    Require(ItemMotionCustomTemplate.Validate(t with{Name=""})!=null,"empty name");
});

Check("total loop time preserves pauses and scales speed profiles together", () =>
{
    var p=ItemMotionPath.Plan([Vector3.Zero,new(20,0,0),new(20,0,20)],MotionPathUse.CollisionRelay,MotionPathRepeat.Closed,5,KC.AnimEase.Linear,100,2000).Value!;
    var time=ItemMotionPathEditing.ChangeLoopTime(p,20000);
    Require(time.Success && time.Value!.DurationMs==20000,"loop retime");
    Require(time.Value!.StartDelayMs==2000 && time.Value.PauseMs==100,"waits changed");
    var behavior=ItemMotionBehavior.Apply(p with{PauseMs=0},new());
    var raw=ItemMotionPath.Plan([Vector3.Zero,new(20,0,0),new(20,0,20)],MotionPathUse.Scenery,MotionPathRepeat.Closed,5,KC.AnimEase.Linear).Value!;
    var profile=raw with{SpeedPoints=[5,10,15,5],Rotations=ItemMotionPathProfile.FollowDirection(raw),ContinuousVelocity=true};
    var total=ItemMotionPathProfile.Compile(profile).Value!.DurationMs;
    var slower=ItemMotionPathEditing.ChangeLoopTime(profile,total*2).Value!;
    Near(slower.SpeedPoints![1]/slower.SpeedPoints[0],2);
    Require(Math.Abs(ItemMotionPathProfile.Compile(slower).Value!.DurationMs-total*2)<=3,"profile loop time");
    Require(!ItemMotionPathEditing.ChangeLoopTime(profile,1).Success,"impossible total accepted");
});

Check("separate wing wheel and propeller parts split once and retain source bytes", () =>
{
    var source=(CPlugPrefab)KinematicTemplate().EntityModel!;source.Ents[0].Rotation=Quat.Identity;
    var solid=RelaySolid();solid.Visuals=[solid.Visuals![0],solid.Visuals[0]];solid.ShadedGeoms=[new(){VisualIndex=0,MaterialIndex=0,LodMask=1},new(){VisualIndex=1,MaterialIndex=0,LodMask=1}];
    ((CPlugDynaObjectModel)source.Ents[0].Model!).Mesh=solid;var before=Bytes(KinematicWith(source));
    var p=ItemMotionPath.Plan([Vector3.Zero,new(20,0,0)],MotionPathUse.Scenery,MotionPathRepeat.Reset,5,KC.AnimEase.Linear).Value!;
    foreach(var kind in Enum.GetValues<MotionPartKind>()) {
        var plan=p with{Rotations=ItemMotionPathProfile.FollowDirection(p),Parts=[new(1,kind,new(.2f,.3f,.4f))]};
        var b=ItemMotionPath.Build(source,plan);Require(b.Success,b.Reason??"part build");
        var bodies=b.Value!.Ents.Select(e=>e.Model).OfType<CPlugDynaObjectModel>().ToArray();
        Require(((CPlugSolid2Model)bodies[^1].Mesh!).Visuals!.Count==1,"part visual split");
        var c=b.Value.Ents.Select(e=>e.Model).OfType<KC>().Last();
        Require(c.RotAnimFunc!.SubFuncs.Length<=3,"part key count");
        if(kind==MotionPartKind.Wheel)Near(c.AngleMaxDeg,20/.4f*180/MathF.PI);
        if(kind==MotionPartKind.Wing)Require(c.RotAnimFunc.SubFuncs.Sum(k=>k.Duration.TotalMilliseconds)==700,"wing period");
        using var stream=new MemoryStream();new Gbx<CGameItemModel>(KinematicWith(b.Value)).Save(stream);stream.Position=0;Gbx.Parse<CGameItemModel>(stream);
    }
    Require(before.SequenceEqual(Bytes(KinematicWith(source))),"part split mutated source");
    Require(!ItemMotionPath.Build(source,p with{Rotations=ItemMotionPathProfile.FollowDirection(p),Parts=[new(9)]}).Success,"invalid part accepted");
});

Check("behavior templates carry momentum, brake for lifts and settle loop seams", () =>
{
    var p=ItemMotionPath.Plan([new(0,20,0),new(20,10,0),new(40,10,0),new(60,15,0)],MotionPathUse.Scenery,MotionPathRepeat.Reset,10,KC.AnimEase.Linear).Value!;
    var b=ItemMotionBehavior.Apply(p,new(Speed:10,Resistance:0,MaximumSpeed:60)).Value!;
    Near(b.SpeedPoints![1],Math.Sqrt(100+2*9.81*10));
    Near(b.SpeedPoints[1],b.SpeedPoints[2]);
    Require(b.SpeedPoints[3]<b.SpeedPoints[2],"climb did not consume speed");
    Require(b.ContinuousVelocity,"scenery template did not request continuous ramps");
    var c=ItemMotionPathProfile.Compile(b).Value!;
    Require(c.Legs.Count==3,"continuous ramp consumed point budget");
    Require(Math.Abs(c.Legs[0].MoveMs-2000*p.Legs[0].Delta.Length()/(10+b.SpeedPoints[1]))<=1,"constant acceleration travel time");
    Require(c.Legs[0].VelocityRatio<1 && c.Legs[2].VelocityRatio>1,"acceleration/deceleration decomposition");
    var lift=ItemMotionBehavior.Apply(p,new(Speed:10,LiftFrom:2,LiftTo:2)).Value!;
    Near(lift.SpeedPoints![2],4);Near(lift.SpeedPoints[3],4);
    var loop=ItemMotionPath.Plan([new(0,0,0),new(30,20,0),new(60,0,0)],MotionPathUse.Scenery,MotionPathRepeat.Closed,10,KC.AnimEase.Linear).Value!;
    var looped=ItemMotionBehavior.Apply(loop,new(LiftFrom:0,LiftTo:0)).Value!;
    Require(looped.SpeedPoints![0]==looped.SpeedPoints[^1] && looped.SpeedPoints[2]>looped.SpeedPoints[1],"loop seam or descent");
    foreach(var kind in Enum.GetValues<MotionBehaviorKind>())Require(ItemMotionBehavior.Apply(p,ItemMotionBehavior.Defaults(kind)).Success,$"template {kind}");
    Require(!ItemMotionBehavior.Apply(p,new(Gravity:float.NaN)).Success,"nonfinite template accepted");
    Require(!ItemMotionBehavior.Apply(p,new(LiftFrom:8,LiftTo:9)).Success,"invalid lift accepted");
    Require(!ItemMotionPathProfile.Compile(b with{Use=MotionPathUse.CollisionRelay}).Success,"visual acceleration advertised as collision");
});

Check("speed ramps integrate travel time, preserve route and reject invalid profiles", () =>
{
    var p=ItemMotionPath.Plan([Vector3.Zero,new(20,0,0)],MotionPathUse.Scenery,MotionPathRepeat.Reset,5,KC.AnimEase.Linear).Value!;
    var ramp=p with {SpeedPoints=[5,15],RampDetail=4,Rotations=[new(0,0,0),new(90,20,30)]};
    var c=ItemMotionPathProfile.Compile(ramp);
    Require(c.Success,c.Reason ?? "profile failed");
    Require(c.Value!.Legs.Count==4 && Math.Abs(c.Value.DurationMs-2197)<=2,"logarithmic ramp travel time");
    Vector(c.Value.Legs.Aggregate(Vector3.Zero,(v,l)=>v+l.Delta),new(20,0,0));
    Require(c.Value.Legs[0].MoveMs>c.Value.Legs[^1].MoveMs,"ramp did not accelerate");
    Require(c.Value.Rotations![2]==new MotionPathRotation(45,10,15),"rotation sample");
    Require(!ItemMotionPathEditing.ChangeDuration(ramp,0,1000).Success,"duration silently ignored speed curve");
    Require(!ItemMotionPathProfile.Compile(ramp with{SpeedPoints=[float.NaN,15]}).Success,"NaN accepted");
    Require(!ItemMotionPathProfile.Compile(ramp with{SpeedPoints=[5]}).Success,"missing point accepted");
    Require(!ItemMotionPathProfile.Compile(ramp with{Rotations=[new(),new(float.PositiveInfinity)]}).Success,"infinite angle accepted");
    var uniform=ItemMotionPathProfile.Compile(p with{SpeedPoints=[10,10]}).Value!;
    Require(uniform.DurationMs==2000 && uniform.Legs.Count==1,"constant speed subdivided");
    var json=System.Text.Json.JsonSerializer.Serialize(ramp,new System.Text.Json.JsonSerializerOptions{IncludeFields=true});
    var back=System.Text.Json.JsonSerializer.Deserialize<MotionPathPlan>(json,new System.Text.Json.JsonSerializerOptions{IncludeFields=true})!;
    Require(ItemMotionPathProfile.Compile(back).Value!.DurationMs==c.Value.DurationMs,"profile JSON roundtrip");
});

Check("rotating route creates shallow isolated visual branches with native key limits", () =>
{
    var source=(CPlugPrefab)KinematicTemplate().EntityModel!;source.Ents[0].Rotation=Quat.Identity;
    ((CPlugDynaObjectModel)source.Ents[0].Model!).Mesh=RelaySolid();
    var before=Bytes(KinematicWith(source));
    foreach(var use in new[]{MotionPathUse.Scenery,MotionPathUse.CollisionRelay}) {
        var p=ItemMotionPath.Plan([Vector3.Zero,new(20,2,0),new(20,2,20)],use,MotionPathRepeat.Reset,5,KC.AnimEase.Linear).Value!;
        p=p with{Rotations=ItemMotionPathProfile.FollowDirection(p)};
        var b=ItemMotionPath.Build(source,p,new(1,2,3));Require(b.Success,b.Reason ?? "pose failed");
        var bodies=b.Value!.Ents.Where(e=>e.Model is CPlugDynaObjectModel).ToArray();
        Require(bodies.Length==10,"expected five shallow bodies per leg");
        var parents=new Dictionary<int,int>();
        foreach(var e in b.Value.Ents.Where(e=>e.Model is KC)) {
            var c=(KC)e.Model!;var binding=(NPlugDyna_SPrefabConstraintParams)e.Params!;
            Require(parents.TryAdd(binding.Ent2,binding.Ent1),"duplicate driver");
            foreach(var f in new[]{c.TransAnimFunc,c.RotAnimFunc}) Require(f!.SubFuncs.Length<=4 && f.SubFuncs.Sum(k=>k.Duration.TotalMilliseconds)==p.DurationMs,"native timeline");
        }
        foreach(var child in parents.Keys) {int at=child,depth=0;while(at>=0){Require(++depth<=5,"deep/cyclic chain");at=parents[at];}}
        using var output=new MemoryStream();new Gbx<CGameItemModel>(KinematicWith(b.Value)).Save(output);output.Position=0;
        Require(((CPlugPrefab)Gbx.Parse<CGameItemModel>(output).Node.EntityModel!).Ents.Length==b.Value.Ents.Length,"pose serialization");
    }
    Require(before.SequenceEqual(Bytes(KinematicWith(source))),"source mutated by pose");
});

Check("collision relay preserves geometry, schedule, collider topology and source snapshot", () =>
{
    var prefab = (CPlugPrefab)KinematicTemplate().EntityModel!;
    prefab.Ents[0].Rotation = Quat.Identity;
    ((CPlugDynaObjectModel)prefab.Ents[0].Model!).Mesh = RelaySolid();
    var before = Bytes(KinematicWith(prefab));
    var plan = ItemMotionPath.Plan([new(2,3,4),new(12,5,4),new(12,5,14)],MotionPathUse.CollisionRelay,MotionPathRepeat.Closed,5,KC.AnimEase.Linear,100,8000).Value!;
    var result = ItemMotionPath.Build(prefab,plan,new(1,2,3));
    Require(result.Success,result.Reason ?? "relay failed");
    var built = result.Value!;
    Require(built.Ents.Length==6,"body/driver count");
    var cursor=plan.Start;
    for(int i=0;i<3;i++) {
        var e=built.Ents[i]; var b=(CPlugDynaObjectModel)e.Model!;
        var q=new Quaternion(e.Rotation.X,e.Rotation.Y,e.Rotation.Z,e.Rotation.W);
        var local=((CPlugSurface.Mesh)b.DynaShape!.Surf!).Vertices[0];
        Vector(Vector3.Transform(new(local.X,local.Y,local.Z),q)+new Vector3(e.Position.X,e.Position.Y,e.Position.Z),cursor-new Vector3(1,2,3));
        Require(((CPlugSurface.Mesh)b.DynaShape.Surf!).Triangles!.Length==1,"collision topology lost");
        var c=(KC)built.Ents[3+i].Model!;
        Require(built.Ents[3+i].Params is NPlugDyna_SPrefabConstraintParams {Ent1:-1} binding && binding.Ent2==i,"driver binding");
        foreach(var f in new[]{c.TransAnimFunc,c.RotAnimFunc}) Require(f!.SubFuncs!.Length<=4 && f.SubFuncs.Sum(k=>k.Duration.TotalMilliseconds)==plan.DurationMs,"timeline mismatch");
        cursor+=plan.Legs[i].Delta;
    }
    using var output = new MemoryStream();
    new Gbx<CGameItemModel>(KinematicWith(built)).Save(output); output.Position=0;
    var read=(CPlugPrefab)Gbx.Parse<CGameItemModel>(output).Node.EntityModel!;
    Require(read.Ents.Length==6,"serialized relay lost entities");
    Require(before.SequenceEqual(Bytes(KinematicWith(prefab))),"source mutated");
    Require(!ItemMotionPath.Build(prefab,ItemMotionPath.Plan([Vector3.Zero,new(0,20,0)],MotionPathUse.CollisionRelay,MotionPathRepeat.Reset,5,KC.AnimEase.Linear).Value!).Success,"vertical accepted");
    var edited=ItemMotionPathEditing.ChangeRelaySettings(plan,4000,48).Value!;
    Require(edited.Legs[0].StartMs==4100 && edited.DurationMs==plan.DurationMs-4000,"delay shift");
    var duration=ItemMotionPathEditing.ChangeDuration(edited,0,6000).Value!;
    Require(duration.Legs[0].StartMs==4100 && ItemMotionPath.Validate(duration).Success,"duration lost delay");
    Require(!ItemMotionPathEditing.ChangeRelaySettings(plan,8000,float.NaN).Success,"invalid parking");
    var options=new System.Text.Json.JsonSerializerOptions { IncludeFields=true };
    var saved=System.Text.Json.JsonSerializer.Serialize(duration,options);
    var restored=System.Text.Json.JsonSerializer.Deserialize<MotionPathPlan>(saved,options)!;
    Require(restored.StartDelayMs==4000 && restored.ParkingDistance==48 && ItemMotionPath.Validate(restored).Success,"draft lost relay settings");
    var broken=((CPlugSurface.Mesh)((CPlugDynaObjectModel)prefab.Ents[0].Model!).DynaShape!.Surf!);
    var triangles=broken.Triangles;
    broken.Triangles=[new(new Int3(0,1,999),0,0,0)];
    Require(!ItemMotionPath.Build(prefab,plan).Success,"invalid collision indices accepted");
    broken.Triangles=triangles;
    ((CPlugDynaObjectModel)prefab.Ents[0].Model!).Mesh = new CPlugSolid2Model();
    Require(!ItemMotionPath.Build(prefab,plan).Success,"undecoded mesh accepted");
});

Check("collision relay supports each repeat/easing and pause/delay combination within native key limits", () =>
{
    var prefab=(CPlugPrefab)KinematicTemplate().EntityModel!;
    prefab.Ents[0].Rotation=Quat.Identity;
    ((CPlugDynaObjectModel)prefab.Ents[0].Model!).Mesh=RelaySolid();
    foreach(var repeat in Enum.GetValues<MotionPathRepeat>())
    foreach(var ease in new[]{KC.AnimEase.Linear,KC.AnimEase.QuadIn,KC.AnimEase.QuadOut,KC.AnimEase.QuadInOut})
    foreach(var pause in new[]{0,100})
    foreach(var delay in new[]{0,8000}) {
        var plan=ItemMotionPath.Plan([Vector3.Zero,new(20,0,0),new(20,4,20)],MotionPathUse.CollisionRelay,repeat,5,ease,pause,delay).Value!;
        var built=ItemMotionPath.Build(prefab,plan);
        Require(built.Success,built.Reason ?? "relay options failed");
        foreach(var c in built.Value!.Ents.Select(e=>e.Model).OfType<KC>())
        foreach(var f in new[]{c.TransAnimFunc,c.RotAnimFunc})
            Require(f!.SubFuncs is {Length: > 0 and <= 4} keys && keys.All(k=>k.Duration.TotalMilliseconds>0) && keys.Sum(k=>k.Duration.TotalMilliseconds)==plan.DurationMs,"native schedule invalid");
    }
});

Check("smooth routes preserve endpoints, close the seam and respect timing and budget", () =>
{
    Vector3[] points = [new(0,0,0), new(20,0,0), new(20,0,20), new(0,0,20)];
    var zero = ItemMotionSmoothPath.Plan(points, MotionPathRepeat.Reset, 5, 0, 8);
    Require(zero.Success && zero.Value!.Legs.Count == 3, "zero smoothness must retain the polygon");
    var open = ItemMotionSmoothPath.Plan(points, MotionPathRepeat.Reset, 5, 1, 8);
    Require(open.Success, open.Reason ?? "rounding failed");
    var plan = open.Value!;
    Require(plan.Start == points[0] && Vector3.Distance(plan.Legs.Aggregate(plan.Start, (p,l)=>p+l.Delta),points[^1]) < .001f, "open endpoints moved");
    Require(plan.Legs.Count == 19 && plan.Legs.All(l=>l.MoveMs>=80), "unexpected rounded geometry or timing");
    var closed = ItemMotionSmoothPath.Plan(points, MotionPathRepeat.Closed, 5, 1, 8);
    Require(closed.Success && closed.Value!.Legs.Count == 36, "closed corner count");
    Require(closed.Value!.Legs.Aggregate(Vector3.Zero,(p,l)=>p+l.Delta).Length()<.001f, "loop seam did not close");
    var back = ItemMotionSmoothPath.Plan(points, MotionPathRepeat.PingPong, 5, 1, 8);
    Require(back.Success && back.Value!.Legs.Count == 38, "return budget");
    Require(!ItemMotionSmoothPath.Plan(points, MotionPathRepeat.Reset, 5, float.NaN, 8).Success, "NaN accepted");
    Require(!ItemMotionSmoothPath.Plan(points, MotionPathRepeat.Reset, 5, .5f, 17).Success, "invalid detail accepted");
    var many = Enumerable.Range(0,20).Select(i=>new Vector3(i*20,0,i%2*20)).ToArray();
    Require(!ItemMotionSmoothPath.Plan(many, MotionPathRepeat.PingPong, 5, 1, 16).Success,"oversized curve accepted");
    var fast = ItemMotionSmoothPath.Plan(points, MotionPathRepeat.Reset, 20, .2f, 16);
    Require(fast.Success && fast.Value!.Legs.All(l=>l.MoveMs>=80),"detail reduction failed");
});

Check("route edits reschedule all channels, preserve neighbours and reject invalid durations", () =>
{
    var original = ItemMotionPath.Plan([Vector3.Zero,new(20,0,0),new(20,0,20)],MotionPathUse.Scenery,MotionPathRepeat.Closed,5,KC.AnimEase.Linear,100).Value!;
    var edited = ItemMotionPathEditing.ChangeDuration(original,0,8000);
    Require(edited.Success && edited.Value!.Legs[1].StartMs == 8200,"following channel not shifted");
    Require(original.Legs[0].MoveMs == 4000,"source changed");
    Require(ItemMotionPath.Validate(edited.Value!).Success,"edited plan invalid");
    Require(!ItemMotionPathEditing.ChangeDuration(original,0,79).Success,"short duration accepted");
    Require(!ItemMotionPathEditing.ChangeDuration(original,0,int.MaxValue).Success,"overflow accepted");
    var moved = ItemMotionPathEditing.MovePoint(original,1,new(30,0,0));
    Require(moved.Success && moved.Value!.Legs[0].Delta.X==30 && moved.Value.Legs[1].Delta.X==-10,"shared endpoint broken");
    Require(!ItemMotionPathEditing.MovePoint(original,1,Vector3.Zero).Success,"zero-length leg accepted");
    var obstacle = ItemMotionPath.Plan([Vector3.Zero,new(20,0,0)],MotionPathUse.Obstacle,MotionPathRepeat.PingPong,5,KC.AnimEase.Linear).Value!;
    var linked = ItemMotionPathEditing.ChangeDuration(obstacle,1,7000).Value!;
    Require(linked.Legs.All(l=>l.MoveMs==7000) && ItemMotionPath.Validate(linked).Success,"obstacle return timing diverged");
    var json = System.Text.Json.JsonSerializer.Serialize(edited.Value,new System.Text.Json.JsonSerializerOptions{IncludeFields=true});
    var loaded = System.Text.Json.JsonSerializer.Deserialize<MotionPathPlan>(json,new System.Text.Json.JsonSerializerOptions{IncludeFields=true})!;
    Require(ItemMotionPath.Validate(loaded).Success && loaded.Legs[1].Delta == edited.Value!.Legs[1].Delta,"draft roundtrip lost geometry");
});

Check("static-to-kinematic conversion requires a valid kinematic template body", () =>
{
    var staticItem = new CGameItemModel
    {
        Ident = new Ident("ConvertedMotion", 26, "FixtureGenerator"),
        Name = "Converted Motion",
        Description = "Preserve this description",
        PageName = "CustomFolder",
        CopperPrice = 7,
        ItemType = CGameItemModel.EItemType.Ornament,
        ItemTypeE = CGameItemModel.EItemType.Ornament,
        EntityModel = new CPlugStaticObjectModel { Mesh = new CPlugSolid2Model() }
    };
    var result = ItemKinematicEntityTemplate.ConvertStaticToKinematic(staticItem, movingTemplate: null);
    Require(!result.Success && result.Status == ItemMotionStatus.Unsupported, "conversion without template should be rejected");

    var template = KinematicTemplate();
    var explicitConstraint = new KC
    {
        Version = 0,
        SubVersion = 3,
        TransAxis = KC.EAxis.Z,
        TransMin = -2,
        TransMax = 5,
        RotAxis = KC.EAxis.X,
        AngleMinDeg = -45,
        AngleMaxDeg = 45,
        TransAnimFunc = Timeline(Key(KC.AnimEase.Constant, 500)),
        RotAnimFunc = Timeline(Key(KC.AnimEase.QuadInOut, 1200))
    };
    result = ItemKinematicEntityTemplate.ConvertStaticToKinematic(staticItem, template, explicitConstraint);
    Require(result.Success, result.Reason ?? "conversion failed");
    Require(result.Value!.Description == staticItem.Description && result.Value.PageName == staticItem.PageName
        && result.Value.CopperPrice == 7, "conversion lost authored collector metadata");
    var prefab = (CPlugPrefab)result.Value!.EntityModel!;
    Require(prefab.Ents!.Length == 2, "conversion did not build a two-entry prefab");
    var body = (CPlugDynaObjectModel)prefab.Ents[0].Model!;
    Require(!body.IsStatic && body.Mesh is not null && body.StaticShape is CPlugSurface && body.DynaShape is CPlugSurface,
        "dyna body did not reuse the static mesh and surface nodes");
    Require(((CPlugSurface)body.StaticShape).Surf is not null && ((CPlugSurface)body.DynaShape).Surf is not null,
        "converted body lost required collision surface payload");
    var instance = (NPlugDynaObjectModel_SInstanceParams)prefab.Ents[0].Params!;
    Require(instance.Version == 2 && instance.IsKinematic, "instance params did not set kinematic mode");
    Require(instance.PeriodSc == 1 && instance.PeriodScMax == -1 && instance.Phase01 == -1 && instance.Phase01Max == -1,
        "template timing sentinels were not preserved");
    var constraint = (NPlugDyna_SKinematicConstraint)prefab.Ents[1].Model!;
    Require(constraint.SubVersion == 3 && constraint.TransAxis == NPlugDyna_SKinematicConstraint.EAxis.Z
        && constraint.RotAxis == NPlugDyna_SKinematicConstraint.EAxis.X
        && constraint.TransMin == -2 && constraint.TransMax == 5
        && constraint.AngleMinDeg == -45 && constraint.AngleMaxDeg == 45,
        "explicit constraint template was not applied");
    var binding = ItemMotionBindings.Resolve(constraint, prefab, (NPlugDyna_SPrefabConstraintParams)prefab.Ents[1].Params!, "doc:0/variant:none/root");
    Require(binding.Status == ItemMotionStatus.Supported, binding.Reason ?? "binding failed");
});

Check("segment count edits retain existing keys and timing mode, append in the right units, and reject atomically", () =>
{
    foreach (var mode in new[] { true, false })
    {
        var key = Key(KC.AnimEase.QuadOut, 2000, true);
        var timeline = new KC.AnimFunc { IsDuration = mode, SubFuncs = new[] { key } };
        Require(ItemMotion.ResizeTimeline(timeline, 3).Success, "Count increase rejected.");
        Require(ReferenceEquals(timeline.SubFuncs[0], key) && timeline.IsDuration == mode, "Existing key or flag replaced.");
        Require(timeline.SubFuncs[1].Duration.TotalMilliseconds == (mode ? 1000 : 3000)
            && timeline.SubFuncs[2].Duration.TotalMilliseconds == (mode ? 1000 : 4000), "New segment uses wrong timing representation.");
        Require(timeline.SubFuncs.Skip(1).All(k => k.Ease == KC.AnimEase.Linear && !k.Reverse), "New defaults differ.");
        Require(ItemMotion.ResizeTimeline(timeline, 1).Success && timeline.SubFuncs.Length == 1
            && ReferenceEquals(timeline.SubFuncs[0], key), "Count reduction rewrote retained key.");
        timeline.SubFuncs[0].Duration = new TimeInt32(int.MaxValue);
        var before = timeline.SubFuncs;
        Require(!ItemMotion.ResizeTimeline(timeline, 2).Success && ReferenceEquals(timeline.SubFuncs, before), "Overflow partially mutated timeline.");
        Require(!ItemMotion.ResizeTimeline(timeline, 5).Success && ReferenceEquals(timeline.SubFuncs, before), "Count overflow mutated timeline.");
    }
    Require(!ItemMotion.ResizeTimeline(null, 1).Success, "Absent timeline created implicitly.");
    var empty = new KC.AnimFunc { IsDuration = false, SubFuncs = Array.Empty<KC.SubAnimFunc>() };
    Require(ItemMotion.ResizeTimeline(empty, 1).Success && empty.SubFuncs[0].Duration.TotalMilliseconds == 1000, "Explicit creation from empty timeline failed.");
});

Check("the engine's four-segment limit is enforced before any write", () =>
{
    // Measured in game: four segments load, five are refused by the native loader, and eight or more
    // end the process. See docs/item-gbx-chunks.md §16.
    Require(ItemMotion.MaxSegments == 4, "segment limit is no longer the engine's four");

    var four = Enumerable.Range(0, 4).Select(_ => Key(KC.AnimEase.Linear, 1000)).ToArray();
    var timeline = new KC.AnimFunc { IsDuration = true, SubFuncs = four };
    var refused = ItemMotion.ResizeTimeline(timeline, 5);
    Require(refused.Status == ItemMotionStatus.Invalid && ReferenceEquals(timeline.SubFuncs, four),
        "a fifth segment was accepted or written");
    Require(ItemMotion.ReadTimeline(timeline).Success, "four segments are no longer readable");

    timeline.SubFuncs = [.. four, Key(KC.AnimEase.Linear, 1000)];
    Require(ItemMotion.ReadTimeline(timeline).Status == ItemMotionStatus.Unsupported,
        "a five-segment timeline is not reported as unsupported");

    var model = Model();
    var before = Bytes(model);
    var fiveKeys = new ItemMotionTimeline(true, Enumerable.Range(0, 5)
        .Select(_ => new ItemMotionKey(KC.AnimEase.Linear, false, 1000)).ToArray());
    var fields = ItemMotion.Read(model).Fields;
    Require(!ItemMotion.Apply(model, fields with { Translation = fiveKeys }).Success, "a five-segment edit was accepted");
    Require(before.SequenceEqual(Bytes(model)), "a refused five-segment edit mutated the source");
    Require(ItemMotion.Apply(model, fields with { Translation = new(true, [.. four.Select(k => new ItemMotionKey(k.Ease, k.Reverse, 1000))]) }).Success,
        "a four-segment edit was refused");
});

Check("duration and endpoint archives sample equally without rewriting authored data", () =>
{
    foreach (var isDuration in new[] { true, false })
    {
        var model = Model(); model.TransMin = 0; model.TransMax = 10;
        model.TransAnimFunc = new() { IsDuration = isDuration, SubFuncs = new[] {
            Key(KC.AnimEase.Linear, 1000), Key(KC.AnimEase.Linear, isDuration ? 1000 : 2000, true) } };
        var before = Bytes(model);
        foreach (var (seconds, expected) in new[] { (0d, 0d), (.5, 5d), (1d, 10d), (1.5, 5d), (2d, 0d) })
            Near(Value(ItemMotion.Evaluate(model, seconds)).TranslationMetres, expected);
        Require(Bytes(model).SequenceEqual(before), "Evaluation rewrote the archive representation");
    }
    var longer = Model(); longer.TransMin = 0; longer.TransMax = 10;
    longer.TransAnimFunc = new() { IsDuration = true, SubFuncs = new[] {
        Key(KC.AnimEase.Linear, 1000), Key(KC.AnimEase.Linear, 2000, true) } };
    Near(Value(ItemMotion.Evaluate(longer, 1.5)).TranslationMetres, 7.5);
});

Check("endpoint duplicates, decreases and normalized limits retain native segment boundaries", () =>
{
    foreach (var middle in new[] { 500, 1000 })
    {
        var model = Model(); model.TransMin = 0; model.TransMax = 10;
        model.TransAnimFunc = new() { IsDuration = false, SubFuncs = new[] {
            Key(KC.AnimEase.Linear, 1000), Key(KC.AnimEase.Constant, middle),
            Key(KC.AnimEase.Linear, middle + 1000, true) } };
        Near(Value(ItemMotion.Evaluate(model, 1)).TranslationMetres, 10);
        Near(Value(ItemMotion.Evaluate(model, 1.5)).TranslationMetres, 5);
        Near(Value(ItemMotion.Evaluate(model, 2)).TranslationMetres, 0);
        var edit = ItemMotion.Read(model).Fields;
        var before = Bytes(model);
        Require(ItemMotion.Apply(model, edit).Success, "raw endpoint edit refused");
        Require(Bytes(model).SequenceEqual(before), "unchanged endpoint edit rewrote storage");
    }
    var limit = Model();
    limit.TransAnimFunc = new() { IsDuration = false, SubFuncs = new[] {
        Key(KC.AnimEase.Linear, int.MaxValue), Key(KC.AnimEase.Linear, int.MaxValue) } };
    Require(ItemMotion.Evaluate(limit, .5).Success, "validated raw sum rather than normalized duration");
    limit.TransAnimFunc.SubFuncs = new[] { Key(KC.AnimEase.Linear, int.MaxValue),
        Key(KC.AnimEase.Linear, 0), Key(KC.AnimEase.Linear, int.MaxValue) };
    Require(ItemMotion.Evaluate(limit, .5).Status == ItemMotionStatus.Invalid, "normalized period overflow accepted");
    foreach (var mode in new[] { false, true })
    {
        limit.TransAnimFunc = new() { IsDuration = mode, SubFuncs = Array.Empty<KC.SubAnimFunc>() };
        Near(Value(ItemMotion.Evaluate(limit, 1)).TranslationMetres, 2);
        limit.TransAnimFunc.SubFuncs = new[] { Key(KC.AnimEase.Linear, 0) };
        Near(Value(ItemMotion.Evaluate(limit, 1)).TranslationMetres, 0);
    }
});

Check("independent timelines, scalar units and phase", () =>
{
    var model = Model();
    var sample = Value(ItemMotion.Evaluate(model, .5));
    Near(sample.TranslationMetres, 4); // 2 + (10 - 2) * .25
    Near(sample.AngleDegrees, 90); // independent one-second rotation cycle
    Vector(sample.Translation, new(4, 0, 0));
    Vector(Vector3.Transform(Vector3.UnitX, sample.Rotation), Vector3.UnitY);
    sample = Value(ItemMotion.Evaluate(model, 1));
    Near(sample.TranslationMetres, 6);
    Near(sample.AngleDegrees, 0);
    sample = Value(ItemMotion.Evaluate(model, 0, .25));
    Near(sample.TranslationMetres, 4);
    Near(sample.AngleDegrees, 45);
});

Check("empty and zero-total timelines are different", () =>
{
    var model = Model();
    model.TransAnimFunc = Timeline();
    model.AngleMinDeg = 30;
    model.RotAnimFunc = Timeline(Key(KC.AnimEase.Linear, 0));
    var sample = Value(ItemMotion.Evaluate(model, 3));
    Near(sample.TranslationMetres, 2);
    Near(sample.AngleDegrees, 0);
    model.TransAnimFunc = Timeline(Key(KC.AnimEase.Constant, 0, true));
    model.RotAnimFunc = Timeline();
    sample = Value(ItemMotion.Evaluate(model, 0));
    Near(sample.TranslationMetres, 0);
    Near(sample.AngleDegrees, 30);
});

Check("easing quarter points and reversal", () =>
{
    foreach (var (ease, expected) in new[] { (KC.AnimEase.Constant, 0d), (KC.AnimEase.Linear, .25),
        (KC.AnimEase.QuadIn, .0625), (KC.AnimEase.QuadOut, .4375), (KC.AnimEase.QuadInOut, .125) })
    {
        var model = Model(); model.TransMin = 0; model.TransMax = 1;
        model.TransAnimFunc = Timeline(Key(ease, 1000));
        Near(Value(ItemMotion.Evaluate(model, .25)).TranslationMetres, expected);
        model.TransAnimFunc.SubFuncs![0].Reverse = true;
        Near(Value(ItemMotion.Evaluate(model, .25)).TranslationMetres, 1 - expected);
    }
    var quad = Model(); quad.TransMin = 0; quad.TransMax = 1;
    quad.TransAnimFunc = Timeline(Key(KC.AnimEase.QuadInOut, 1000));
    Near(Value(ItemMotion.Evaluate(quad, .5)).TranslationMetres, .5);
    Near(Value(ItemMotion.Evaluate(quad, .75)).TranslationMetres, .875);
});

Check("key boundary, zero-duration skip, wrap, constant reverse", () =>
{
    var model = Model(); model.TransMin = 0; model.TransMax = 1;
    model.TransAnimFunc = Timeline(Key(KC.AnimEase.Linear, 1000), Key(KC.AnimEase.Constant, 0),
        Key(KC.AnimEase.Linear, 1000, true));
    Near(Value(ItemMotion.Evaluate(model, 0)).TranslationMetres, 0);
    Near(Value(ItemMotion.Evaluate(model, 1)).TranslationMetres, 1);
    Near(Value(ItemMotion.Evaluate(model, 1.5)).TranslationMetres, .5);
    Near(Value(ItemMotion.Evaluate(model, 2)).TranslationMetres, 0);
    model.TransAnimFunc = Timeline(Key(KC.AnimEase.Constant, 1000, true));
    Near(Value(ItemMotion.Evaluate(model, 99)).TranslationMetres, 1);
    Require(ItemMotion.Evaluate(model, double.MaxValue).Success, "large finite times overflowed");
});

Check("fractional-second exact loop boundaries", () =>
{
    var model = Model(); model.TransMin = 0; model.TransMax = 1;
    model.TransAnimFunc = Timeline(Key(KC.AnimEase.Linear, 100));
    foreach (var seconds in new[] { .3, .6, .7 })
        foreach (var phase in new[] { 0d, 1d })
        {
            Near(Value(ItemMotion.Evaluate(model, seconds, phase)).TranslationMetres, 0);
            Require(Value(ItemMotion.Evaluate(model, seconds - .000001, phase)).TranslationMetres > .99f,
                "preceding microsecond selected the wrong loop");
            Require(Value(ItemMotion.Evaluate(model, seconds + .000001, phase)).TranslationMetres > 0,
                "following microsecond selected the wrong loop");
            Near(Value(ItemMotion.Evaluate(model, seconds - .0000001, phase)).TranslationMetres, 0);
            Near(Value(ItemMotion.Evaluate(model, seconds + .0000001, phase)).TranslationMetres, 0);
        }
    model.TransAnimFunc = Timeline(Key(KC.AnimEase.Linear, 1));
    Near(Value(ItemMotion.Evaluate(model, 1.001)).TranslationMetres, 0); // 1001 ms; binary seconds*1000 is just below 1001.
});

Check("microsecond step boundaries and phase retain neighboring intervals", () =>
{
    var model = Model(); model.TransMin = 0; model.TransMax = 1;
    model.TransAnimFunc = Timeline(Key(KC.AnimEase.Constant, 100), Key(KC.AnimEase.Constant, 100, true));
    foreach (var (seconds, phase, expected) in new[] { (.6, 0d, 0d), (.7, 0d, 1d),
        (.6, 1d, 0d), (.7, 1d, 1d), (.6, .5, 1d), (.7, .5, 0d), (.55, .25, 0d), (.65, .25, 1d) })
    {
        Near(Value(ItemMotion.Evaluate(model, seconds, phase)).TranslationMetres, expected);
        Near(Value(ItemMotion.Evaluate(model, seconds - .000001, phase)).TranslationMetres, 1 - expected);
        Near(Value(ItemMotion.Evaluate(model, seconds + .000001, phase)).TranslationMetres, expected);
        Near(Value(ItemMotion.Evaluate(model, seconds - .0000001, phase)).TranslationMetres, expected);
        Near(Value(ItemMotion.Evaluate(model, seconds + .0000001, phase)).TranslationMetres, expected);
    }
    // Huge finite times still yield a finite sample; integer seconds are exact 100 ms loop boundaries.
    Near(Value(ItemMotion.Evaluate(model, double.MaxValue)).TranslationMetres, 0);
});

Check("preview quantizes time and phase to nearest microsecond away from midpoint zero", () =>
{
    var model = Model(); model.TransMin = 0; model.TransMax = 1000;
    model.TransAnimFunc = Timeline(Key(KC.AnimEase.Linear, 1));
    foreach (var (seconds, expected) in new[] { (.00000049, 0d), (.0000005, 1d), (.00000051, 1d), (.0000015, 2d) })
        Near(Value(ItemMotion.Evaluate(model, seconds)).TranslationMetres, expected);
    foreach (var (phase, expected) in new[] { (.00049, 0d), (.0005, 1d), (.00051, 1d), (.0015, 2d), (1d, 0d) })
        Near(Value(ItemMotion.Evaluate(model, 0, phase)).TranslationMetres, expected);
    // Quantize clock and phase separately, so .49us + .49us remains 0us, not a rounded 1us sum.
    Near(Value(ItemMotion.Evaluate(model, .00000049, .00049)).TranslationMetres, 0);
    Near(Value(ItemMotion.Evaluate(model, .0000005, .0005)).TranslationMetres, 2);
    model.TransMax = 1;
    model.TransAnimFunc = Timeline(Key(KC.AnimEase.Constant, 1), Key(KC.AnimEase.Constant, 2, true));
    // 98800us + .4*3000us = 100000us; modulo 3000 is exactly the second key's start (1000us).
    Near(Value(ItemMotion.Evaluate(model, .0988, .4)).TranslationMetres, 1);
    Near(Value(ItemMotion.Evaluate(model, .098799, .4)).TranslationMetres, 0);
    Near(Value(ItemMotion.Evaluate(model, .098801, .4)).TranslationMetres, 1);
    model.TransAnimFunc = Timeline(Key(KC.AnimEase.Linear, 3000)); model.TransMax = 3;
    foreach (var seconds in new[] { 1e13, 1e20, double.MaxValue })
    {
        var result = ItemMotion.Evaluate(model, seconds);
        Require(result.Success && double.IsFinite(Value(result).TranslationMetres), "huge-time evaluation must remain finite");
    }
    // The longest supported period exercises the safe-integer fallback's upper bound.
    model.TransAnimFunc = Timeline(Key(KC.AnimEase.Linear, int.MaxValue));
    Require(ItemMotion.Evaluate(model, double.MaxValue, 1).Success, "maximum supported duration overflowed");
});

Check("unsupported modes and missing data stay visible", () =>
{
    var model = Model();
    foreach (var ease in new[] { KC.AnimEase.CubicIn, (KC.AnimEase)255 })
    {
        model.TransAnimFunc = Timeline(Key(ease, 1000));
        Require(ItemMotion.Read(model).Fields.Translation!.Keys[0].Ease == ease, "raw easing lost");
        Require(ItemMotion.Evaluate(model, 0).Status == ItemMotionStatus.Unsupported, "unsupported easing coerced");
    }
    model.TransAnimFunc = null;
    Require(ItemMotion.Evaluate(model, 0).Status == ItemMotionStatus.Absent, "absent timeline defaulted");
    model.TransAnimFunc = new();
    Require(ItemMotion.Evaluate(model, 0).Status == ItemMotionStatus.Unresolved, "null key array defaulted");
});

Check("invalid edits are atomic across fields and both timelines", () =>
{
    var model = Model();
    var before = Bytes(model);
    var fields = ItemMotion.Read(model).Fields;
    foreach (var invalid in new[] { fields with { TranslationMin = float.NaN }, fields with { AngleMaxDegrees = float.PositiveInfinity },
        fields with { RotationAxis = (KC.EAxis)255 }, fields with { Translation = new(false, [new(KC.AnimEase.Linear, false, -1)]) },
        fields with { Rotation = new(true, [new(KC.AnimEase.Linear, false, int.MaxValue), new(KC.AnimEase.Linear, false, 1)]) },
        fields with { TranslationMin = 9, Rotation = new(false, [new(KC.AnimEase.CubicIn, false, 500)]) },
        fields with { Translation = new(false, Enumerable.Repeat(new ItemMotionKey(KC.AnimEase.Linear, false, 1), 5).ToArray()) } })
    {
        Require(!ItemMotion.Apply(model, invalid).Success, "invalid edit accepted");
        Require(before.SequenceEqual(Bytes(model)), "invalid edit partially mutated source");
    }
    foreach (double time in new[] { -1, double.NaN, double.PositiveInfinity })
        Require(ItemMotion.Evaluate(model, time).Status == ItemMotionStatus.Invalid, "invalid time accepted");
    Require(!ItemMotion.Evaluate(model, 0, double.NaN).Success, "invalid phase accepted");
    Require(ItemMotion.Apply(model, fields with { TranslationMin = 10, TranslationMax = -10 }).Success, "directed endpoints refused");
});

Check("save/reparse preserves unknown shader fields and independent timeline edits", () =>
{
    var model = Model();
    model.Version = 7; model.SubVersion = 23;
    model.ShaderTcType = KC.EShaderTcType.TransSubTexture; model.ShaderTcVersion = 9;
    model.ShaderTcAnimFunc = [new() { Duration = new TimeInt32(123), TextureId = 71 }];
    model.ShaderTcDataTransSub = new() { NbSubTexture = 8, NbSubTexturePerLine = 4, NbSubTexturePerColumn = 2, TopToBottom = true };
    model.RotAnimFunc!.SubFuncs![0].Ease = (KC.AnimEase)255;
    var oldRot = model.RotAnimFunc;
    var edit = ItemMotion.Read(model).Fields with { TranslationMin = -4, TranslationMax = 6,
        Translation = new(false, [new(KC.AnimEase.QuadOut, true, 450)]), Rotation = null };
    Require(ItemMotion.Apply(model, edit).Success, "valid scalar/timeline edit refused");
    Require(ReferenceEquals(oldRot, model.RotAnimFunc), "untouched unknown timeline replaced");
    var reloaded = Reparse<KC>(Bytes(model));
    Require(reloaded.Version == 7 && reloaded.SubVersion == 23 && reloaded.ShaderTcVersion == 9, "unknown versions changed");
    Require(reloaded.ShaderTcAnimFunc![0].TextureId == 71 && reloaded.ShaderTcAnimFunc[0].Duration.TotalMilliseconds == 123, "shader key changed");
    Require(reloaded.ShaderTcDataTransSub!.TopToBottom && reloaded.ShaderTcDataTransSub.NbSubTexture == 8, "shader payload lost");
    Require(reloaded.RotAnimFunc!.SubFuncs![0].Ease == (KC.AnimEase)255, "untouched unknown easing changed");
    Require(reloaded.TransAnimFunc!.SubFuncs![0].Duration.TotalMilliseconds == 450 && reloaded.TransAnimFunc.SubFuncs[0].Reverse, "edited key lost");
    Near(reloaded.TransMin, -4); Near(reloaded.TransMax, 6);
});

Check("filtered slots ignore interleaved static, free and missing-params entries", () =>
{
    var (prefab, model, parameters) = Prefab();
    var binding = ItemMotionBindings.Resolve(model, prefab, parameters, ItemMotionBindings.RootPath(2, 1));
    Require(binding.Status == ItemMotionStatus.Supported, binding.Reason ?? "binding failed");
    Require(binding.Slots.Count == 2 && binding.Slots[0].OriginalArrayIndex == 1 && binding.Slots[1].OriginalArrayIndex == 4, "used raw indexes");
    Require(binding.Parent.RawSlot == 0 && binding.Parent.Path == "doc:2/variant:1/root/ent:1", "parent wrong");
    Require(binding.Child.RawSlot == 1 && binding.Child.Path == "doc:2/variant:1/root/ent:4", "child wrong");
    Require(ReferenceEquals(binding.Child.SourceEntry, prefab.Ents[4]), "source handle lost");
});

Check("two shared nested occurrences refuse guessed targets, preserve topology on reparse", () =>
{
    var (nested, model, parameters) = Prefab();
    var root = new CPlugPrefab { Ents = [new() { Model = nested, Position = new(10, 0, 0), Rotation = new(0, 0, .70710677f, .70710677f) },
        new() { Model = nested, Position = new(20, 0, 0), Rotation = new(0, 0, 0, 1) }] };
    var a = ItemMotionBindings.Resolve(model, nested, parameters, "doc:0/variant:none/root/ent:0");
    var b = ItemMotionBindings.Resolve(model, nested, parameters, "doc:0/variant:none/root/ent:1");
    Require(a.Status == ItemMotionStatus.Unsupported && b.Status == ItemMotionStatus.Unsupported, "nested bindings guessed");
    Require(a.Child.Path is null && b.Child.Path is null && a.Child.SourceEntry is null && b.Child.SourceEntry is null, "wrong nested target exposed");
    Require(a.Slots[1].Path != b.Slots[1].Path && ReferenceEquals(a.Source, b.Source), "inventory scope or shared source lost");
    Require(ItemMotionBindings.Resolve(model, nested, parameters, "custom-owned-edge", isNestedPrefabOccurrence: true).Status == ItemMotionStatus.Unsupported, "explicit nested guard ignored");
    var saved = Reparse<CPlugPrefab>(Bytes(root));
    Require(ReferenceEquals(saved.Ents[0].Model, saved.Ents[1].Model), "shared prefab topology lost");
    var loaded = (CPlugPrefab)saved.Ents[0].Model!;
    Require(loaded.Ents.Length == 6 && loaded.Ents[0].U01 == "static-sentinel", "untouched entries changed");
    var loadedParams = (NPlugDyna_SPrefabConstraintParams)loaded.Ents[5].Params!;
    Require(loadedParams.Ent1 == 0 && loadedParams.Ent2 == 1, "raw slots changed on save");
});

Check("invalid child and world parent never select fallback mesh", () =>
{
    var (prefab, model, parameters) = Prefab();
    foreach (int world in new[] { -1, 2, int.MaxValue })
    {
        parameters.Ent1 = world;
        var binding = ItemMotionBindings.Resolve(model, prefab, parameters, "doc:2/variant:none/root");
        Require(binding.Status == ItemMotionStatus.Supported && binding.Parent.IsWorld, "world sentinel rejected");
        Require(binding.Parent.Path == "doc:2/variant:none/root" && binding.Parent.SourceEntry is null, "wrong world scope");
    }
    parameters.Ent2 = -1;
    var invalid = ItemMotionBindings.Resolve(model, prefab, parameters, "doc:0/variant:none/root");
    Require(invalid.Status == ItemMotionStatus.Unresolved && invalid.Child.Path is null && invalid.Child.SourceEntry is null, "fallback mesh animated");
    Require(ItemMotionBindings.Resolve(model, prefab, null, "doc:0/variant:none/root").Status == ItemMotionStatus.Absent, "missing params guessed");
    parameters.Ent1 = 0; parameters.Ent2 = 0;
    Require(ItemMotionBindings.Resolve(model, prefab, parameters, "root").Status == ItemMotionStatus.Unsupported, "self-parent accepted");
});

Check("external and nested/cyclic table ambiguity fail visibly", () =>
{
    var (prefab, model, parameters) = Prefab();
    prefab.Ents[0].References.Model.Descriptor = new GbxRefTableFile(new GbxRefTable(), 0, true, "never-resolve.Gbx");
    var external = ItemMotionBindings.Resolve(model, prefab, parameters, "root");
    Require(external.Status == ItemMotionStatus.Unresolved && external.Child.Path is null, "external classification guessed");
    prefab.Ents[0].References.Model.Clear(); prefab.Ents[0].Model = prefab;
    Require(ItemMotionBindings.Resolve(model, prefab, parameters, "root").Status == ItemMotionStatus.Unsupported, "cyclic nesting not bounded");
    parameters.Pos1 = new(float.NaN, 0, 0);
    prefab.Ents[0].Model = new CPlugStaticObjectModel();
    Require(!ItemMotionBindings.ApplyTargets(model, prefab, parameters, "root", -1, 1).Success, "unverified anchors accepted");
});

Check("target editing validates before either slot changes", () =>
{
    var (prefab, model, parameters) = Prefab();
    Require(!ItemMotionBindings.ApplyTargets(model, prefab, parameters, "root", -1, 99).Success, "invalid target accepted");
    Require(parameters.Ent1 == 0 && parameters.Ent2 == 1, "partial target mutation");
    Require(ItemMotionBindings.ApplyTargets(model, prefab, parameters, "root", -1, 1).Success, "world binding refused");
    Require(parameters.Ent1 == -1 && parameters.Ent2 == 1, "valid targets not persisted");
});

Check("kinematic entity template appends one independently bound body and constraint atomically", () =>
{
    var (prefab, source, parameters) = Prefab();
    source.Version = 0;
    source.SubVersion = 3;
    var result = ItemKinematicEntityTemplate.AppendFromConstraint(prefab, source, "doc:0/variant:none/root");
    Require(result.Success && result.Value == 2, result.Reason ?? "template append failed");
    Require(prefab.Ents!.Length == 8, "template did not append exactly two entries");
    var body = prefab.Ents[6];
    var instance = (NPlugDynaObjectModel_SInstanceParams)body.Params!;
    Require(ReferenceEquals(body.Model, prefab.Ents[4].Model) && instance.Version == 2 && instance.IsKinematic,
        "template did not preserve the proven shared dyna model and kinematic instance classification");
    var constraint = (KC)prefab.Ents[7].Model!;
    var binding = (NPlugDyna_SPrefabConstraintParams)prefab.Ents[7].Params!;
    Require(binding.Ent1 == parameters.Ent1 && binding.Ent2 == 2 && binding.Pos1 == default && binding.Pos2 == default,
        "template binding did not target the new filtered slot");
    Require(constraint.SubVersion == 3 && constraint.TransAnimFunc!.SubFuncs![0].Duration == source.TransAnimFunc!.SubFuncs![0].Duration,
        "template did not copy typed constraint fields");
    Require(ItemMotionBindings.Resolve(constraint, prefab, binding, "doc:0/variant:none/root").Status == ItemMotionStatus.Supported,
        "appended binding is not independently resolvable");

    var length = prefab.Ents.Length;
    source.SubVersion = 2;
    var rejected = ItemKinematicEntityTemplate.AppendFromConstraint(prefab, source, "doc:0/variant:none/root");
    Require(!rejected.Success && prefab.Ents.Length == length, "unsupported template partially mutated the entity list");
});

Check("visible path proxy inserts a complete body before the original visible constraint", () =>
{
    var (prefab, source, parameters) = Prefab();
    source.Version = 0;
    source.SubVersion = 3;
    var result = ItemKinematicEntityTemplate.InsertVisiblePathProxy(prefab, source, "doc:0/variant:none/root");
    Require(result.Success, result.Reason ?? "visible path proxy insertion failed");
    Require(prefab.Ents!.Length == 8 && parameters.Ent1 == 2 && parameters.Ent2 == 1,
        "original visible constraint was not rebound to the proxy");
    Require(ReferenceEquals(prefab.Ents[6].Model, prefab.Ents[4].Model),
        "path proxy did not reuse the proven complete dyna model");
    var proxyParameters = (NPlugDyna_SPrefabConstraintParams)prefab.Ents[7].Params!;
    Require(proxyParameters.Ent1 == 0 && proxyParameters.Ent2 == 2,
        "path proxy constraint did not preserve the old parent and target the new slot");
    Require(ItemMotionBindings.Resolve(source, prefab, parameters, "doc:0/variant:none/root").Status == ItemMotionStatus.Supported,
        "original child constraint is not resolvable after proxy insertion");
    Require(ItemMotionBindings.Resolve(result.Value!, prefab, proxyParameters, "doc:0/variant:none/root").Status == ItemMotionStatus.Supported,
        "new proxy constraint is not independently resolvable");
});

Check("hidden carrier parent inserts one meshless helper and keeps chain bindings supported", () =>
{
    var (prefab, source, parameters) = Prefab();
    source.Version = 0;
    source.SubVersion = 3;
    var sourceBody = (CPlugDynaObjectModel)prefab.Ents![4].Model!;
    sourceBody.StaticShape = ReopenSurface(new CPlugSurface.Mesh
    {
        Version = 6,
        Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
        Triangles = [new(new Int3(0, 1, 2), 0, 0, 0)]
    });
    sourceBody.DynaShape = sourceBody.StaticShape;

    var result = ItemKinematicEntityTemplate.InsertHiddenCarrierParent(prefab, source, "doc:0/variant:none/root");
    Require(result.Success, result.Reason ?? "hidden carrier insertion failed");
    Require(prefab.Ents.Length == 8 && parameters.Ent1 == 2 && parameters.Ent2 == 1,
        "visible constraint was not rebound to the carrier slot");
    var carrier = (CPlugDynaObjectModel)prefab.Ents[6].Model!;
    Require(carrier.Mesh is not null && carrier.StaticShape is CPlugSurface { Surf: not null } && carrier.DynaShape is CPlugSurface { Surf: not null },
        "carrier body is not safely mesh-backed and collision-complete");
    var carrierParameters = (NPlugDyna_SPrefabConstraintParams)prefab.Ents[7].Params!;
    Require(carrierParameters.Ent1 == 0 && carrierParameters.Ent2 == 2,
        "carrier constraint did not preserve parent slot and target new helper slot");
    Require(ItemMotionBindings.Resolve(source, prefab, parameters, "doc:0/variant:none/root").Status == ItemMotionStatus.Supported,
        "visible child constraint is not resolvable after carrier insertion");
    Require(ItemMotionBindings.Resolve(result.Value!, prefab, carrierParameters, "doc:0/variant:none/root").Status == ItemMotionStatus.Supported,
        "carrier constraint is not independently resolvable");
});

Check("binding preview serialization excludes source graph", () =>
{
    var (prefab, model, parameters) = Prefab();
    var binding = ItemMotionBindings.Resolve(model, prefab, parameters, "doc:0/variant:none/root");
    using var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(binding));
    Require(!json.RootElement.TryGetProperty("Source", out _) && !json.RootElement.TryGetProperty("Parameters", out _), "source graph serialized");
    var child = json.RootElement.GetProperty("Child");
    Require(!child.TryGetProperty("SourceEntry", out _) && child.GetProperty("Path").GetString() == "doc:0/variant:none/root/ent:4", "target DTO lost path or leaked graph");
    parameters.Version = 1;
    Require(!ItemMotionBindings.ApplyTargets(model, prefab, parameters, "root", 0, 1).Success, "unknown constraint params version rewritten");
});

Check("rest-relative motion uses parent and child rotations independently", () =>
{
    var owner = Matrix4x4.CreateRotationZ(MathF.PI / 2) * Matrix4x4.CreateTranslation(10, 0, 0);
    var child = Matrix4x4.CreateTranslation(2, 0, 0) * owner;
    var signal = Matrix4x4.CreateTranslation(3, 0, 0);
    var world = Value(ItemMotionTransforms.ComposeVisual(child, owner, owner, signal));
    Vector(Vector3.Transform(Vector3.Zero, world), new(10, 5, 0));
    // Parent now rotates 180 degrees and moves to (20, 0); child's relative rest is still +2 X.
    var live = Matrix4x4.CreateRotationZ(MathF.PI) * Matrix4x4.CreateTranslation(20, 0, 0);
    world = Value(ItemMotionTransforms.ComposeVisual(child, owner, live, signal));
    Vector(Vector3.Transform(Vector3.Zero, world), new(15, 0, 0));
    var childRotated = Matrix4x4.CreateRotationZ(MathF.PI / 2) * Matrix4x4.CreateTranslation(2, 0, 0);
    world = Value(ItemMotionTransforms.ComposeVisual(childRotated, Matrix4x4.Identity, Matrix4x4.Identity, signal));
    Vector(Vector3.Transform(Vector3.Zero, world), new(2, 3, 0));
    var bad = Matrix4x4.Identity; bad.M41 = float.NaN;
    Require(!ItemMotionTransforms.ComposeVisual(bad, owner, live, signal).Success, "nonfinite child accepted");
    Require(!ItemMotionTransforms.ComposeVisual(child, new(), live, signal).Success, "singular parent accepted");
    Require(!ItemMotionTransforms.ComposeVisual(child, owner, Matrix4x4.CreateScale(2), signal).Success, "scaled parent accepted");
});

Check("multi-constraint chain follows A-to-E axis path with supported slot bindings", () =>
{
    static CPlugPrefab.EntRef Body() => new()
    {
        Model = new CPlugDynaObjectModel { IsStatic = false, Mesh = new CPlugSolid2Model() },
        Params = new NPlugDynaObjectModel_SInstanceParams { Version = 2, IsKinematic = true },
        Position = default,
        Rotation = Quat.Identity
    };
    static KC Constraint(KC.EAxis axis) => new()
    {
        Version = 0,
        SubVersion = 3,
        TransAxis = axis,
        TransMin = 0,
        TransMax = 5,
        RotAxis = KC.EAxis.Y,
        AngleMinDeg = 0,
        AngleMaxDeg = 0,
        TransAnimFunc = new KC.AnimFunc { IsDuration = true, SubFuncs = [new() { Ease = KC.AnimEase.Linear, Duration = new TimeInt32(1000) }] },
        RotAnimFunc = new KC.AnimFunc { IsDuration = true, SubFuncs = [new() { Ease = KC.AnimEase.Linear, Duration = new TimeInt32(1000) }] }
    };

    var a = Body(); var b = Body(); var c = Body(); var d = Body(); var e = Body();
    var ab = Constraint(KC.EAxis.X);
    var bc = Constraint(KC.EAxis.Y);
    var cd = Constraint(KC.EAxis.Z);
    var de = Constraint(KC.EAxis.X);
    var pAb = new NPlugDyna_SPrefabConstraintParams { Version = 0, Ent1 = -1, Ent2 = 0 };
    var pBc = new NPlugDyna_SPrefabConstraintParams { Version = 0, Ent1 = 0, Ent2 = 1 };
    var pCd = new NPlugDyna_SPrefabConstraintParams { Version = 0, Ent1 = 1, Ent2 = 2 };
    var pDe = new NPlugDyna_SPrefabConstraintParams { Version = 0, Ent1 = 2, Ent2 = 3 };
    var prefab = new CPlugPrefab
    {
        Ents =
        [
            a, b, c, d, e,
            new() { Model = ab, Params = pAb, Rotation = Quat.Identity },
            new() { Model = bc, Params = pBc, Rotation = Quat.Identity },
            new() { Model = cd, Params = pCd, Rotation = Quat.Identity },
            new() { Model = de, Params = pDe, Rotation = Quat.Identity }
        ]
    };

    foreach (var (constraint, parameters, parent, child) in new[] { (ab, pAb, -1, 0), (bc, pBc, 0, 1), (cd, pCd, 1, 2), (de, pDe, 2, 3) })
    {
        var binding = ItemMotionBindings.Resolve(constraint, prefab, parameters, "doc:0/variant:none/root");
        Require(binding.Status == ItemMotionStatus.Supported, binding.Reason ?? "binding rejected");
        Require(binding.Parent.RawSlot == parent && binding.Child.RawSlot == child, "unexpected slot table mapping");
    }

    var aLive = Value(ItemMotion.Evaluate(ab, 0.5)).Signal;
    var bSignal = Value(ItemMotion.Evaluate(bc, 0.5)).Signal;
    var cSignal = Value(ItemMotion.Evaluate(cd, 0.5)).Signal;
    var dSignal = Value(ItemMotion.Evaluate(de, 0.5)).Signal;
    var aWorld = Value(ItemMotionTransforms.ComposeVisual(Matrix4x4.Identity, Matrix4x4.Identity, Matrix4x4.Identity, aLive));
    var bWorld = Value(ItemMotionTransforms.ComposeVisual(Matrix4x4.Identity, Matrix4x4.Identity, aWorld, bSignal));
    var cWorld = Value(ItemMotionTransforms.ComposeVisual(Matrix4x4.Identity, Matrix4x4.Identity, bWorld, cSignal));
    var dWorld = Value(ItemMotionTransforms.ComposeVisual(Matrix4x4.Identity, Matrix4x4.Identity, cWorld, dSignal));

    Vector(Vector3.Transform(Vector3.Zero, aWorld), new(2.5f, 0, 0));
    Vector(Vector3.Transform(Vector3.Zero, bWorld), new(2.5f, 2.5f, 0));
    Vector(Vector3.Transform(Vector3.Zero, cWorld), new(2.5f, 2.5f, 2.5f));
    Vector(Vector3.Transform(Vector3.Zero, dWorld), new(5f, 2.5f, 2.5f));
});

Check("version-gated exact timing storage and save/reparse", () =>
{
    for (int version = 0; version <= 2; version++)
    {
        var timing = new NPlugDynaObjectModel_SInstanceParams { Version = version, PeriodSc = 2, PeriodScMax = 4,
            Phase01 = .25f, Phase01Max = .75f, IsKinematic = true, TextureId = 991, CastStaticShadow = true };
        var read = Value(ItemMotionTiming.Read(timing));
        Require((read.Phase01 is null) == (version == 0), "absent phase defaulted");
        Require((read.CastStaticShadow is null) == (version < 2), "absent shadow defaulted");
        var edit = version == 0 ? new ItemMotionTimingEdit(3) : new(3, 8, .125f, .625f);
        Require(ItemMotionTiming.Apply(timing, edit).Success, "valid timing rejected");
        var loaded = Reparse<NPlugDynaObjectModel_SInstanceParams>(Bytes(timing));
        Near(loaded.PeriodSc, 3);
        Require(loaded.Version == version && loaded.TextureId == 991 && loaded.IsKinematic, "timing metadata changed");
        if (version > 0) { Near(loaded.PeriodScMax, 8); Near(loaded.Phase01, .125); Near(loaded.Phase01Max, .625); }
        if (version == 2) Require(loaded.CastStaticShadow, "shadow lost");
        Require(ItemMotionTiming.ResolvePreviewPhase(timing).Status == ItemMotionStatus.Unsupported, "persisted phase silently became preview phase");
    }
    Require(ItemMotionTiming.Read(null).Status == ItemMotionStatus.Absent, "missing params defaulted");
    var sentinels = new NPlugDynaObjectModel_SInstanceParams { Version = 2, PeriodSc = 1, PeriodScMax = -1, Phase01 = -1, Phase01Max = -1 };
    var sentinelRead = Value(ItemMotionTiming.Read(sentinels));
    Require(sentinelRead.InheritsPhase == true && sentinelRead.RandomizesPeriod == false && sentinelRead.RandomizesPhase == false, "native timing sentinels rejected or mislabelled");
    Require(ItemMotionTiming.Apply(sentinels, new(2, -1, -1, -1)).Success, "canonical sentinels rejected");
    var savedSentinels = Reparse<NPlugDynaObjectModel_SInstanceParams>(Bytes(sentinels));
    Require(savedSentinels.PeriodScMax == -1 && savedSentinels.Phase01 == -1 && savedSentinels.Phase01Max == -1, "sentinels lost on reparse");
});

Check("timing edits reject absent and invalid fields atomically", () =>
{
    var timing = new NPlugDynaObjectModel_SInstanceParams { Version = 0, PeriodSc = 2 };
    Require(!ItemMotionTiming.Apply(timing, new(3, Phase01: .5f)).Success && timing.PeriodSc == 2, "version gate partial mutation");
    timing.Version = 2;
    foreach (var edit in new[] { new ItemMotionTimingEdit(float.NaN), new(float.PositiveInfinity), new(-1),
        new(3, float.NaN), new(3, Phase01: float.NaN), new(3, PhaseMax01: 2), new(3, Phase01: -.1f) })
    {
        var before = Bytes(timing);
        Require(!ItemMotionTiming.Apply(timing, edit).Success, "invalid timing accepted");
        Require(Bytes(timing).SequenceEqual(before), "invalid timing mutated source");
    }
    timing.Version = 3;
    Require(!ItemMotionTiming.Apply(timing, new(9)).Success, "unknown version rewritten");
    timing.Version = 2; timing.Phase01 = float.NaN;
    var read = ItemMotionTiming.Read(timing);
    Require(read.Status == ItemMotionStatus.Invalid && float.IsNaN(read.Value!.Phase01!.Value), "stored invalid timing hidden");
});

Check("default template fallback and collision generation from visual mesh", () =>
{
    var defaultTemplate = ItemKinematicEntityTemplate.GetDefaultMovingTemplate();
    Require(defaultTemplate is not null, "default moving template should load from bundled resources/disk");
    var prefab = defaultTemplate!.EntityModel as CPlugPrefab;
    Require(prefab?.Ents?.Length >= 2, "template should contain valid prefab entities");

    var stream = new CPlugVertexStream { Positions = [new(0, 0, 0), new(2, 0, 0), new(0, 2, 0)] };
    var indexBuffer = new CPlugIndexBuffer { Indices = [0, 1, 2] };
    var vit = new CPlugVisualIndexedTriangles
    {
        VertexStreams = [stream],
        IndexBuffer = indexBuffer,
        BoundingBox = new BoxAligned(0, 0, 0, 2, 2, 0)
    };
    var solid = new CPlugSolid2Model { Visuals = [vit] };
    var generatedSurface = ItemKinematicEntityTemplate.GenerateCollisionSurfaceFromMesh(solid);
    var sm = generatedSurface?.Surf as CPlugSurface.Mesh;
    Require(sm is not null, "collision generation failed");
    Require(sm.Vertices?.Length == 3 && sm.Triangles?.Length == 1, "generated surface mesh did not match source");
});

Check("relay path configures two independent root bindings (native contact tested separately)", () =>
{
    var template = KinematicTemplate();
    var prefab = (CPlugPrefab)template.EntityModel!;
    var constraint = (KC)prefab.Ents![1].Model!;
    var result = ItemKinematicEntityTemplate.ConfigureRelayCollisionPath(
        prefab, constraint, "doc:0/variant:none/root",
        KC.EAxis.X, 5f, 2000,
        KC.EAxis.Y, 5f, 2000);
    Require(result.Success, result.Reason ?? "relay configuration failed");
    Require(prefab.Ents.Length == 4, "relay path did not build 4 entries (2 bodies + 2 constraints)");
    var p0 = (NPlugDyna_SPrefabConstraintParams)prefab.Ents[1].Params!;
    var p1 = (NPlugDyna_SPrefabConstraintParams)prefab.Ents[3].Params!;
    Require(p0.Ent1 == -1 && p0.Ent2 == 0, "body 0 constraint must be root-bound for full collision");
    Require(p1.Ent1 == -1 && p1.Ent2 == 1, "body 1 constraint must be root-bound for full collision");
    var c0 = (KC)prefab.Ents[1].Model!;
    var c1 = (KC)prefab.Ents[3].Model!;
    Require(c0.TransAxis == KC.EAxis.X && c0.TransMax == 5f, "c0 axis/max mismatch");
    Require(c1.TransAxis == KC.EAxis.Y && c1.TransMax == 5f, "c1 axis/max mismatch");
});

Check("chained L-path configures carrier helper and synchronized child", () =>
{
    var template = KinematicTemplate();
    var prefab = (CPlugPrefab)template.EntityModel!;
    var constraint = (KC)prefab.Ents![1].Model!;
    var result = ItemKinematicEntityTemplate.ConfigureChainedLPath(
        prefab, constraint, "doc:0/variant:none/root",
        KC.EAxis.X, 5f, 2000,
        KC.EAxis.Y, 5f, 2000, isPingPong: true);
    Require(result.Success, result.Reason ?? "chained L-path configuration failed");
    Require(prefab.Ents.Length == 4, "chained path did not insert carrier body and constraint");
    var carrierParams = (NPlugDyna_SPrefabConstraintParams)prefab.Ents[3].Params!;
    var childParams = (NPlugDyna_SPrefabConstraintParams)prefab.Ents[1].Params!;
    Require(carrierParams.Ent1 == -1, "carrier constraint must be attached to world");
    Require(childParams.Ent1 == 1, "visible constraint must be attached to carrier body");
});

Check("drawn path planner validates shape, repeat budget, speed and finite input", () =>
{
    var points = new Vector3[] { new(3,2,5), new(19,2,5), new(19,2,21), new(3,2,21) };
    var plan = Value(ItemMotionPath.Plan(points, MotionPathUse.Scenery, MotionPathRepeat.Closed, 4, KC.AnimEase.Linear));
    Require(plan.Legs.Count == 4 && plan.DurationMs == 16000, "closed square schedule differs");
    Vector(plan.Start, new(3,2,5));
    Vector(plan.Legs.Aggregate(Vector3.Zero, (v,l) => v+l.Delta), Vector3.Zero);
    Require(!ItemMotionPath.Plan(points, MotionPathUse.Obstacle, MotionPathRepeat.Closed, 4, KC.AnimEase.Linear).Success, "polyline accepted as obstacle");
    var overReturn = Enumerable.Range(0, ItemMotionPath.MaxLegs / 2 + 2).Select(i => new Vector3(i, 2, 0)).ToArray();
    Require(!ItemMotionPath.Plan(overReturn, MotionPathUse.Scenery, MotionPathRepeat.PingPong, 4, KC.AnimEase.Linear).Success, "over-budget return accepted");
    var loop = Enumerable.Range(0, ItemMotionPath.MaxLegs).Select(i => new Vector3(100 * MathF.Cos(i * MathF.Tau / ItemMotionPath.MaxLegs), 0, 100 * MathF.Sin(i * MathF.Tau / ItemMotionPath.MaxLegs))).ToArray();
    Require(Value(ItemMotionPath.Plan(loop, MotionPathUse.Scenery, MotionPathRepeat.Closed, 4, KC.AnimEase.Linear)).Legs.Count == ItemMotionPath.MaxLegs, "maximum loop rejected");
    Require(!ItemMotionPath.Plan([..loop, new(101,0,0)], MotionPathUse.Scenery, MotionPathRepeat.Closed, 4, KC.AnimEase.Linear).Success, "over-budget closing leg accepted");
    foreach (var speed in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, 1001f })
        Require(!ItemMotionPath.Plan(points, MotionPathUse.Scenery, MotionPathRepeat.Reset, speed, KC.AnimEase.Linear).Success, "invalid speed accepted");
    foreach (var point in new[] { new Vector3(float.NaN,0,0), new Vector3(10001,0,0), Vector3.Zero })
        Require(!ItemMotionPath.Plan([Vector3.Zero,point], MotionPathUse.Obstacle, MotionPathRepeat.Reset, 4, KC.AnimEase.Linear).Success, "invalid point accepted");
});

Check("drawn obstacle timelines cover every repeat/ease/pause option within four keys", () =>
{
    foreach (var repeat in Enum.GetValues<MotionPathRepeat>())
    foreach (var ease in new[] { KC.AnimEase.Linear, KC.AnimEase.QuadIn, KC.AnimEase.QuadOut, KC.AnimEase.QuadInOut })
    foreach (var pause in new[] { 0, 500 })
    {
        var original = (CPlugPrefab)KinematicTemplate().EntityModel!;
        var plan = Value(ItemMotionPath.Plan([new(3,2,5),new(3,10,5)], MotionPathUse.Obstacle, repeat, 4, ease, pause));
        var result = Value(ItemMotionPath.Build(original, plan));
        Require(!ReferenceEquals(original,result) && original.Ents.Length == 2, "source graph mutated");
        var body = result.Ents.Single(e=>e.Model is CPlugDynaObjectModel);
        var q=body.Rotation;
        Vector(Vector3.Transform(Vector3.UnitX,new Quaternion(q.X,q.Y,q.Z,q.W)),Vector3.UnitY);
        Require(body.Position == new Vec3(3,2,5), "path start lost");
        var constraint=(KC)result.Ents.Single(e=>e.Model is KC).Model!;
        Require(constraint.TransAnimFunc.SubFuncs.Length <= 4, "native timeline overflow");
        Near(constraint.TransAnimFunc.SubFuncs.Sum(k=>k.Duration.TotalMilliseconds),plan.DurationMs);
        Near(Value(ItemMotion.Evaluate(constraint,pause / 1000d + 1)).TranslationMetres,
            ease == KC.AnimEase.QuadIn ? 2 : ease == KC.AnimEase.QuadOut ? 6 : 4);
        var instance=(NPlugDynaObjectModel_SInstanceParams)body.Params!;
        Require(instance.PeriodSc == 1 && instance.Phase01 == 0 && instance.PeriodScMax == -1 && instance.Phase01Max == -1, "unsynchronized instance settings");
    }
});

Check("scenery schedule adds world directions without accumulating body rotation", () =>
{
    var original = (CPlugPrefab)KinematicTemplate().EntityModel!;
    var points = new Vector3[] { new(3,2,5),new(15,10,5),new(15,2,17) };
    var plan = Value(ItemMotionPath.Plan(points, MotionPathUse.Scenery, MotionPathRepeat.Closed, 4, KC.AnimEase.Linear, 500));
    var result = Value(ItemMotionPath.Build(original,plan));
    Require(result.Ents.Length == 8 && original.Ents.Length == 2, "source mutated or wrong topology");
    var bodies = result.Ents.Where(e=>e.Model is CPlugDynaObjectModel).ToArray();
    var constraints = result.Ents.Where(e=>e.Model is KC).ToArray();
    foreach(var e in constraints) {
        var c=(KC)e.Model!;
        Require(c.TransAnimFunc.SubFuncs.Length <= 3, "carrier timeline too long");
        Near(c.TransAnimFunc.SubFuncs.Sum(k=>k.Duration.TotalMilliseconds),plan.DurationMs);
        Require(c.AngleMinDeg == 0 && c.AngleMaxDeg == 0, "signal yaw accumulated");
    }
    for(var i=0;i<plan.Legs.Count;i++) {
        var time=(plan.Legs[i].StartMs + plan.Legs[i].MoveMs/2d)/1000;
        var position=plan.Start;
        for(var j=0;j<plan.Legs.Count;j++) {
            var q=bodies[j+1].Rotation;
            var sample=Value(ItemMotion.Evaluate((KC)constraints[j].Model!,time));
            position+=Vector3.Transform(sample.Translation,new Quaternion(q.X,q.Y,q.Z,q.W));
        }
        var expected=plan.Start+plan.Legs.Take(i).Aggregate(Vector3.Zero,(v,l)=>v+l.Delta)+plan.Legs[i].Delta/2;
        Vector(position,expected);
    }
    var collider=(CPlugSurface)((CPlugDynaObjectModel)bodies[0].Model!).DynaShape!;
    Require(((CPlugSurface.Mesh)collider.Surf!).Vertices.Max(v=>Math.Abs(v.X)+Math.Abs(v.Y)+Math.Abs(v.Z)) < .001f, "full-size invisible collision remains");
    Require(((CPlugSurface.Mesh)((CPlugSurface)((CPlugDynaObjectModel)original.Ents[0].Model!).DynaShape!).Surf!).Vertices.Max(v=>v.X) == 1, "source collision changed");
    Require(!ItemMotionPath.Build(result,plan).Success, "generated chain stacked without baseline");
    original.Ents[1].Params = new NPlugDyna_SPrefabConstraintParams { Ent1=-1,Ent2=0,Pos1=new(1,0,0) };
    Require(!ItemMotionPath.Build(original,plan).Success && original.Ents.Length == 2, "unsupported anchor partially mutated");
});

Check("drawn paths preserve static entries and shader animation", () =>
{
    var original = (CPlugPrefab)KinematicTemplate().EntityModel!;
    var shader = (KC)original.Ents[1].Model!;
    shader.ShaderTcType = KC.EShaderTcType.TransSubTexture; shader.ShaderTcVersion = 9;
    shader.ShaderTcAnimFunc = [new() { Duration = new TimeInt32(123), TextureId = 71 }];
    shader.ShaderTcDataTransSub = new() { NbSubTexture = 8, NbSubTexturePerLine = 4, NbSubTexturePerColumn = 2, TopToBottom = true };
    original.Ents = [..original.Ents, new() { Model = new CPlugStaticObjectModel { Mesh = ((CPlugDynaObjectModel)original.Ents[0].Model!).Mesh }, Position = new(7,8,9), Rotation = Quat.Identity }];
    foreach(var use in new[]{MotionPathUse.Obstacle,MotionPathUse.Scenery}) {
        var plan = Value(ItemMotionPath.Plan([Vector3.Zero,new(16,0,0)],use,MotionPathRepeat.PingPong,4,KC.AnimEase.Linear));
        var result = Value(ItemMotionPath.Build(original,plan));
        Require(result.Ents.Single(e=>e.Model is CPlugStaticObjectModel).Position == new Vec3(7,8,9), "static part moved");
        var c = result.Ents.Select(e=>e.Model).OfType<KC>().Single(c=>c.ShaderTcType == KC.EShaderTcType.TransSubTexture);
        Require(c.ShaderTcAnimFunc![0].TextureId == 71 && c.ShaderTcDataTransSub!.NbSubTexture == 8 && c.ShaderTcVersion == 9, "shader animation changed");
    }
});

Check("path mesh anchor follows the first point for rotated obstacles and scenery", () =>
{
    foreach(var use in new[]{MotionPathUse.Obstacle,MotionPathUse.Scenery}) {
        var original=(CPlugPrefab)KinematicTemplate().EntityModel!;
        var plan=Value(ItemMotionPath.Plan([new(3,4,5),new(3,12,5)],use,MotionPathRepeat.PingPong,4,KC.AnimEase.Linear));
        var anchor=new Vector3(2,3,4);
        var result=Value(ItemMotionPath.Build(original,plan,anchor));
        var body=result.Ents.First(e=>e.Model is CPlugDynaObjectModel);
        var q=body.Rotation;
        Vector(new Vector3(body.Position.X,body.Position.Y,body.Position.Z)+Vector3.Transform(anchor,new Quaternion(q.X,q.Y,q.Z,q.W)),plan.Start);
        Require(original.Ents[0].Position == default,"source anchor mutated");
    }
});

Check("path compiler refuses forged plans atomically and aligns near negative X", () =>
{
    var original = (CPlugPrefab)KinematicTemplate().EntityModel!;
    var plan = Value(ItemMotionPath.Plan([Vector3.Zero,new(-16,.001f,0)], MotionPathUse.Obstacle, MotionPathRepeat.PingPong, 4, KC.AnimEase.Linear));
    Require(!ItemMotionPath.Build(original,plan with { DurationMs = 1 }).Success, "forged duration accepted");
    Require(!ItemMotionPath.Build(original,plan with { Legs = [] }).Success, "empty legs accepted");
    Require(original.Ents.Length == 2 && original.Ents[0].Position == default, "failed build mutated source");
    var q = ItemMotionPath.AlignX(new(-16,.001f,0));
    Vector(Vector3.Transform(Vector3.UnitX,new Quaternion(q.X,q.Y,q.Z,q.W)), Vector3.Normalize(new(-16,.001f,0)));
    var constraint = (KC)original.Ents[1].Model!;
    var handover = ItemKinematicEntityTemplate.ConfigureParkedHandoverCollisionPath(original,constraint,"doc:0/variant:none/root",KC.EAxis.X,16,2000,KC.EAxis.Z,16,2000);
    Require(!handover.Success && original.Ents.Length == 2 && ReferenceEquals(original.Ents[1].Model,constraint), "unfinished handover changed source");
});

Console.WriteLine($"{passed} passed, {failed} failed.");
return failed == 0 ? 0 : 1;

void Check(string name, Action action) { try { action(); passed++; Console.WriteLine("PASS: " + name); } catch (Exception e) { failed++; Console.Error.WriteLine("FAIL: " + name + ": " + e); } }
static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
static T Value<T>(ItemMotionResult<T> result) { Require(result.Success, result.Reason ?? "operation failed"); return result.Value!; }
static void Near(double actual, double expected) => Require(Math.Abs(actual - expected) < .0001, $"Expected {expected}, got {actual}");
static void Vector(Vector3 actual, Vector3 expected) { Near(actual.X, expected.X); Near(actual.Y, expected.Y); Near(actual.Z, expected.Z); }
static KC.SubAnimFunc Key(KC.AnimEase ease, int duration, bool reverse = false) => new() { Ease = ease, Duration = new TimeInt32(duration), Reverse = reverse };
static KC.AnimFunc Timeline(params KC.SubAnimFunc[] keys) => new() { IsDuration = true, SubFuncs = keys };
static KC Model() => new() { TransAxis = KC.EAxis.X, TransMin = 2, TransMax = 10, RotAxis = KC.EAxis.Z, AngleMinDeg = 0, AngleMaxDeg = 180,
    TransAnimFunc = Timeline(Key(KC.AnimEase.Linear, 2000)), RotAnimFunc = Timeline(Key(KC.AnimEase.Linear, 1000)) };
static CGameItemModel KinematicTemplate()
{
    var surfaceMesh = new CPlugSurface.Mesh
    {
        Version = 6,
        Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
        Triangles = [new(new Int3(0, 1, 2), 0, 0, 0)]
    };
    var surface = ReopenSurface(surfaceMesh);
    var item = new CGameItemModel
    {
        Ident = new Ident("KinematicTemplate", 26, "FixtureGenerator"),
        ItemType = CGameItemModel.EItemType.Ornament,
        ItemTypeE = CGameItemModel.EItemType.Ornament,
        EntityModel = new CPlugPrefab
        {
            Ents =
            [
                new()
                {
                    Model = new CPlugDynaObjectModel
                    {
                        Version = 13,
                        IsStatic = false,
                        Mesh = new CPlugSolid2Model(),
                        StaticShape = surface,
                        DynaShape = surface
                    },
                    Params = new NPlugDynaObjectModel_SInstanceParams
                    {
                        Version = 2,
                        PeriodSc = 1,
                        PeriodScMax = -1,
                        Phase01 = -1,
                        Phase01Max = -1,
                        IsKinematic = true
                    }
                },
                new()
                {
                    Model = new KC
                    {
                        Version = 0,
                        SubVersion = 3,
                        TransAxis = KC.EAxis.Y,
                        TransMin = 0,
                        TransMax = 1,
                        RotAxis = KC.EAxis.Y,
                        TransAnimFunc = Timeline(Key(KC.AnimEase.Linear, 1000)),
                        RotAnimFunc = Timeline(Key(KC.AnimEase.Linear, 1000))
                    },
                    Params = new NPlugDyna_SPrefabConstraintParams { Version = 0, Ent1 = -1, Ent2 = 0 }
                }
            ]
        }
    };
    item.CreateChunk<CGameCtnCollector.HeaderChunk2E001003>().Version = 8;
    item.CreateChunk<CGameItemModel.HeaderChunk2E002000>();
    item.CreateChunk<CGameItemModel.Chunk2E002015>();
    item.CreateChunk<CGameItemModel.Chunk2E002019>().Version = 15;
    return item;
}
static (CPlugPrefab Prefab, KC Model, NPlugDyna_SPrefabConstraintParams Parameters) Prefab()
{
    var model = Model();
    var parameters = new NPlugDyna_SPrefabConstraintParams { Ent1 = 0, Ent2 = 1 };
    var prefab = new CPlugPrefab { Ents = [
        new() { Model = new CPlugStaticObjectModel(), U01 = "static-sentinel" },
        new() { Model = new CPlugDynaObjectModel(), Params = new NPlugDynaObjectModel_SInstanceParams { Version = 2, IsKinematic = true } },
        new() { Model = new CPlugDynaObjectModel(), Params = new NPlugDynaObjectModel_SInstanceParams { IsKinematic = false } },
        new() { Model = new CPlugDynaObjectModel() },
        new() { Model = new CPlugDynaObjectModel(), Params = new NPlugDynaObjectModel_SInstanceParams { Version = 2, IsKinematic = true } },
        new() { Model = model, Params = parameters } ] };
    return (prefab, model, parameters);
}
static byte[] Bytes(GBX.NET.Engines.MwFoundations.CMwNod model)
{
    using var stream = new MemoryStream();
    using var writer = new GbxWriter(stream);
    using var rw = new GbxReaderWriter(writer);
    model.ReadWrite(rw);
    return stream.ToArray();
}
static CPlugSurface ReopenSurface(CPlugSurface.Mesh mesh)
{
    var surface = new CPlugSurface { Surf = mesh };
    surface.CreateChunk<CPlugSurface.Chunk0900C003>().Version = 2;
    using var stream = new MemoryStream();
    new Gbx<CPlugSurface>(surface) { BodyCompression = GbxCompression.Uncompressed }.Save(stream);
    stream.Position = 0;
    return Gbx.Parse<CPlugSurface>(stream).Node;
}
static T Reparse<T>(byte[] bytes) where T : GBX.NET.Engines.MwFoundations.CMwNod, new()
{
    using var stream = new MemoryStream(bytes);
    using var reader = new GbxReader(stream);
    using var rw = new GbxReaderWriter(reader);
    var result = new T(); result.ReadWrite(rw);
    Require(stream.Position == stream.Length, "trailing bytes after reparse");
    return result;
}


static CPlugSolid2Model RelaySolid()
{
    const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
    var stream = new CPlugVertexStream { Positions = [new(), new(1, 0, 0), new(0, 1, 0)] };
    var declaration = new CPlugVertexStream.DataDecl();
    typeof(CPlugVertexStream.DataDecl).GetField("flags1", flags)!.SetValue(declaration,
        (uint)CPlugVertexStream.EPlugVDcl.Position | ((uint)CPlugVertexStream.EPlugVDclType.Float3 << 9) | (12u << 18));
    typeof(CPlugVertexStream).GetField("dataDecls", flags)!.SetValue(stream, new[] { declaration });
    typeof(CPlugVertexStream).GetField("vertexCount", flags)!.SetValue(stream, 3);
    stream.CreateChunk<CPlugVertexStream.Chunk09056000>().Version = 1;
    var indexBuffer = new CPlugIndexBuffer { Indices = [0, 1, 2] };
    indexBuffer.CreateChunk<CPlugIndexBuffer.Chunk09057000>();
    var visual = new CPlugVisualIndexedTriangles
    {
        VertexStreams = [stream], IndexBuffer = indexBuffer,
        IsGeometryStatic = true, IsIndexationStatic = true,
        BoundingBox = new BoxAligned(0, 0, 0, 1, 1, 0)
    };
    typeof(CPlugVisual).GetProperty("Count", flags)!.SetValue(visual, 3);
    visual.CreateChunk<CPlugVisual.Chunk0900600F>().Version = 6;
    visual.CreateChunk<CPlugVisualIndexed.Chunk0906A001>();
    var solid = new CPlugSolid2Model { Visuals = [visual], CustomMaterials = [], ShadedGeoms = [] };
    solid.CreateChunk<CPlugSolid2Model.Chunk090BB000>().Version = 34;
    return solid;
}


static CGameItemModel KinematicWith(CPlugPrefab prefab) { var item=KinematicTemplate(); item.EntityModel=prefab; return item; }
