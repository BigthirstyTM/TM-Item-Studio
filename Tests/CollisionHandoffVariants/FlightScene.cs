using System.Numerics;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.MwFoundations;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Plug;
using GBX.NET.Engines.Meta;
using TM_Item_Studio.Models;
using TmEssentials;
using KC=GBX.NET.Engines.Meta.NPlugDyna_SKinematicConstraint;

// Research exporter: continuous Euler carriers, independent of the production path compiler.
static class FlightScene {
 static Vec3 V(Vector3 v)=>new(v.X,v.Y,v.Z);
 static KC.SubAnimFunc Key(KC.AnimEase ease,int ms,bool reverse=false)=>new(){Ease=ease,Duration=new TimeInt32(ms),Reverse=reverse};
 static KC.SubAnimFunc[] Ramp(int start,int length,int total) {
  var k=new List<KC.SubAnimFunc>();if(start>0)k.Add(Key(KC.AnimEase.Constant,start));k.Add(Key(KC.AnimEase.Linear,length));if(total>start+length)k.Add(Key(KC.AnimEase.Constant,total-start-length,true));return k.ToArray();
 }
 static NPlugDynaObjectModel_SInstanceParams Instance()=>new(){Version=2,IsKinematic=true,PeriodSc=1,PeriodScMax=-1,Phase01Max=-1};
 static T Clone<T>(T value) where T:CMwNod,new(){using var s=new MemoryStream();new Gbx<T>(value).Save(s);s.Position=0;return Gbx.Parse<T>(s).Node;}
 static float[] Unwrap(float[] a){for(int i=1;i<a.Length;i++){while(a[i]-a[i-1]>180)a[i]-=360;while(a[i]-a[i-1]<-180)a[i]+=360;}return a;}
 public static void Verify(string file){ValidateGraph((CPlugPrefab)Gbx.Parse<CGameItemModel>(file).Node.EntityModel!);Console.WriteLine("PASS bindings, unique drivers, shallow acyclic graph, bounded positive timelines");}
 public static void Run(string sourceFile,string frameFile,string output){
  Directory.CreateDirectory(output);
  var item=Gbx.Parse<CGameItemModel>(sourceFile).Node;
  var source=(CPlugPrefab)item.EntityModel!;
  var visual=Clone((CPlugDynaObjectModel)source.Ents.First(e=>e.Model is CPlugDynaObjectModel).Model!);
  var f=JsonDocument.Parse(File.ReadAllText(frameFile)).RootElement.GetProperty("frames").EnumerateArray().ToArray();
  Vector3 Read(JsonElement j,string key){var a=j.GetProperty(key).EnumerateArray().Select(x=>x.GetSingle()).ToArray();return new(a[0],a[1],a[2]);}
  var raw=f.Select(j=>Read(j,"point")).ToArray();var pts=raw.Select(p=>p-raw[0]-new Vector3(0,4.2f,0)).ToArray();
  // Centre the attitude at each waypoint instead of using the outgoing chord's fixed pose.
  var fw=Enumerable.Range(0,pts.Length).Select(i=>Vector3.Normalize(Vector3.Normalize(pts[i]-pts[(i+pts.Length-1)%pts.Length])+Vector3.Normalize(pts[(i+1)%pts.Length]-pts[i]))).ToArray();
  var side=f.Select((j,i)=>{var s=Read(j,"side");return Vector3.Normalize(s-fw[i]*Vector3.Dot(s,fw[i]));}).ToArray();
  var up=side.Select((s,i)=>Vector3.Cross(s,fw[i])).ToArray();
  var yaw=Unwrap(fw.Select(v=>MathF.Atan2(-v.Z,v.X)*180/MathF.PI).Append(MathF.Atan2(-fw[0].Z,fw[0].X)*180/MathF.PI).ToArray());
  var pitch=fw.Select(v=>MathF.Asin(v.Y)*180/MathF.PI).Append(MathF.Asin(fw[0].Y)*180/MathF.PI).ToArray();
  var roll=Unwrap(side.Select((v,i)=>MathF.Atan2(-v.Y,up[i].Y)*180/MathF.PI).Append(MathF.Atan2(-side[0].Y,up[0].Y)*180/MathF.PI).ToArray());
  var basePlan=ItemMotionPath.Plan(pts,MotionPathUse.CollisionRelay,MotionPathRepeat.Closed,10,KC.AnimEase.Linear,0,2000).Value!;
  var heights=pts.Select(p=>p.Y).ToArray();var ceiling=heights.Max();
  var rawSpeed=heights.Select(h=>MathF.Sqrt(36+2*2.2f*(ceiling-h))).ToArray();
  var rawTimes=basePlan.Legs.Select((l,i)=>l.Delta.Length()/((rawSpeed[i]+rawSpeed[(i+1)%pts.Length])/2)).ToArray();
  var factor=rawTimes.Sum()/((basePlan.DurationMs-2000)/1000f);
  var lengths=rawTimes.Select(t=>(int)Math.Round(t/factor*1000)).ToArray();int elapsed=2000;
  var timed=basePlan.Legs.Select((l,i)=>{var next=l with {StartMs=elapsed,MoveMs=lengths[i]};elapsed+=lengths[i];return next;}).ToArray();
  var plan=basePlan with {Legs=timed,DurationMs=elapsed};
  if(!ItemMotionPath.Validate(plan).Success)throw new Exception("Invalid gravity schedule");
  var relay=ItemMotionPath.Build(source,plan);if(!relay.Success)throw new Exception(relay.Reason);
  // Keep the prior experimental collider relay, but render only one continuously rotating cart.
  int ri=0;
  foreach(var ent in relay.Value!.Ents.Where(e=>e.Model is CPlugDynaObjectModel)){
   var body=(CPlugDynaObjectModel)ent.Model!;body.Mesh=ItemKinematicEntityTemplate.CreateCarrierBody().Mesh;
   var q=new Quaternion(ent.Rotation.X,ent.Rotation.Y,ent.Rotation.Z,ent.Rotation.W);var inverse=Quaternion.Inverse(q);
   var ff=Read(f[ri],"forward");var uu=Read(f[ri],"up");var zz=Read(f[ri],"side");
   var target=Quaternion.CreateFromRotationMatrix(new Matrix4x4(ff.X,ff.Y,ff.Z,0,uu.X,uu.Y,uu.Z,0,zz.X,zz.Y,zz.Z,0,0,0,0,1));
   foreach(var sh in new[]{body.StaticShape,body.DynaShape}.Distinct()){
    var a=((CPlugSurface.Mesh)sh!.Surf!).Vertices;
    for(int j=0;j<a.Length;j++){var p=new Vector3(a[j].X,a[j].Y,a[j].Z)-new Vector3(0,64,0);a[j]=V(Vector3.Transform(Vector3.Transform(Vector3.Transform(p,q),target),inverse)+new Vector3(0,64,0));}
   }ri++;
  }
  var scene=RelayVisual(relay.Value!,visual,plan,yaw,pitch,roll);
  item.EntityModel=scene;Save(item,"TMIS_Cart_GravitySmooth",output);
  File.WriteAllText(Path.Combine(output,"cart-timing.json"),JsonSerializer.Serialize(new{averageSpeed=10,segmentSpeeds=plan.Legs.Select(l=>l.Delta.Length()/(l.MoveMs/1000f)),totalMs=plan.DurationMs,delayMs=2000,points=pts.Select(p=>new[]{p.X,p.Y,p.Z}),legs=plan.Legs.Select(l=>new{startMs=l.StartMs,moveMs=l.MoveMs}),yaw,pitch,roll}));
 }
 static CPlugPrefab RelayVisual(CPlugPrefab relay,CPlugDynaObjectModel visual,MotionPathPlan plan,float[] yaw,float[] pitch,float[] roll, CPlugDynaObjectModel? left=null,CPlugDynaObjectModel? right=null,int half=310){
  var bodies=relay.Ents.Where(e=>e.Model is CPlugDynaObjectModel).ToList();var drivers=relay.Ents.Where(e=>e.Model is KC).ToList();
  var micro=ItemKinematicEntityTemplate.CreateCarrierBody();visual.StaticShape=micro.StaticShape;visual.DynaShape=micro.DynaShape;
  var cursor=plan.Start;
  for(int i=0;i<plan.Legs.Count;i++){
   int parent=i;var leg=plan.Legs[i];
   foreach(var (angles,axis) in new[]{(yaw,KC.EAxis.Y),(pitch,KC.EAxis.Z),(roll,KC.EAxis.X)}){
    int child=bodies.Count;
    bodies.Add(new(){Model=ItemKinematicEntityTemplate.CreateCarrierBody(),Position=V(cursor),Rotation=Quat.Identity,Params=Instance()});
    var c=new KC{Version=0,SubVersion=3,TransAxis=KC.EAxis.X,RotAxis=axis,AngleMinDeg=angles[i],AngleMaxDeg=angles[i+1],TransAnimFunc=new(){IsDuration=true,SubFuncs=[Key(KC.AnimEase.Constant,plan.DurationMs)]},RotAnimFunc=new(){IsDuration=true,SubFuncs=Ramp(leg.StartMs,leg.MoveMs,plan.DurationMs)}};
    drivers.Add(new(){Model=c,Rotation=Quat.Identity,Params=new NPlugDyna_SPrefabConstraintParams{Version=0,Ent1=parent,Ent2=child}});parent=child;
   }
   int visible=bodies.Count;bodies.Add(new(){Model=visual,Position=V(cursor),Rotation=Quat.Identity,Params=Instance()});
   KC Hold()=>new(){Version=0,SubVersion=3,TransAxis=KC.EAxis.X,RotAxis=KC.EAxis.X,TransAnimFunc=new(){IsDuration=true,SubFuncs=[Key(KC.AnimEase.Constant,plan.DurationMs)]},RotAnimFunc=new(){IsDuration=true,SubFuncs=[Key(KC.AnimEase.Constant,plan.DurationMs)]}};
   drivers.Add(new(){Model=Hold(),Rotation=Quat.Identity,Params=new NPlugDyna_SPrefabConstraintParams{Version=0,Ent1=parent,Ent2=visible}});
   if(left!=null&&right!=null)foreach(var (wing,sign) in new[]{(left,1f),(right,-1f)}){
    var child=bodies.Count;bodies.Add(new(){Model=wing,Position=V(cursor),Rotation=Quat.Identity,Params=Instance()});var c=Hold();c.AngleMinDeg=-25*sign;c.AngleMaxDeg=40*sign;c.RotAnimFunc!.SubFuncs=[Key(KC.AnimEase.SineInOut,half),Key(KC.AnimEase.SineInOut,half,true)];
    drivers.Add(new(){Model=c,Rotation=Quat.Identity,Params=new NPlugDyna_SPrefabConstraintParams{Version=0,Ent1=visible,Ent2=child}});
   }
   cursor+=leg.Delta;
  }
  relay.Ents=[..bodies,..drivers];return relay;
 }
 public static void Birds(string sourceFile,string meshFile,string output){
  var item=Gbx.Parse<CGameItemModel>(sourceFile).Node;var source=(CPlugPrefab)item.EntityModel!;
  var template=(CPlugDynaObjectModel)source.Ents.First(e=>e.Model is CPlugDynaObjectModel).Model!;
  var mesh=JsonDocument.Parse(File.ReadAllText(meshFile)).RootElement;
  CPlugDynaObjectModel Part(string name){
   var m=Clone(template);var solid=(CPlugSolid2Model)m.Mesh!;var v=(CPlugVisualIndexedTriangles)solid.Visuals![0];var stream=v.VertexStreams[0];var g=mesh.GetProperty(name);
   float[] Floats(string key)=>g.GetProperty(key).EnumerateArray().Select(x=>x.GetSingle()).ToArray();
   Vec3[] Vectors(string key)=>Floats(key).Chunk(3).Select(x=>new Vec3(x[0],x[1],x[2])).ToArray();
   foreach(var d in stream.DataDecls!)d.Data=null;
   stream.Positions=Vectors("positions");stream.Normals=Vectors("normals");stream.VertexCount=stream.Positions.Length;
   var uv=Floats("uvs").Chunk(2).Select(x=>new Vec2(x[0],x[1])).ToArray();foreach(var key in stream.UVs.Keys.ToArray())stream.UVs[key]=uv.ToArray();
   stream.TangentUs=stream.Normals.Select(n=>{var a=new Vector3(n.X,n.Y,n.Z);return V(Vector3.Normalize(Vector3.Cross(a,Math.Abs(a.Y)<.9f?Vector3.UnitY:Vector3.UnitZ)));}).ToArray();
   stream.TangentVs=stream.Normals.Select((n,i)=>V(Vector3.Cross(new(n.X,n.Y,n.Z),new(stream.TangentUs[i].X,stream.TangentUs[i].Y,stream.TangentUs[i].Z)))).ToArray();
   v.IndexBuffer!.Indices=g.GetProperty("indices").EnumerateArray().Select(x=>x.GetInt32()).ToArray();v.BoundingBox=new(0,0,0,2.4f,1,4.5f);
   typeof(CPlugVisual).GetProperty("Count",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.SetValue(v,stream.VertexCount);
   solid.Visuals=[v];solid.ShadedGeoms=[solid.ShadedGeoms![0]];solid.CustomMaterials=[solid.CustomMaterials![0]];
   solid.CustomMaterials[0].MaterialUserInst!.Link="Stadium\\Media\\Material\\RoadTech";
   var micro=ItemKinematicEntityTemplate.CreateCarrierBody();m.StaticShape=micro.StaticShape;m.DynaShape=micro.DynaShape;return m;
  }
  var combined=Clone(source);combined.Ents=[];
  var routeEvidence=new List<object>();
  for(int bird=0;bird<3;bird++){
   const int n=64;var pts=new Vector3[n];
   for(int i=0;i<n;i++){var t=(i/(float)n+bird/3f)*MathF.Tau;
    // Cross the elevated western track around X=357 at high and low altitude.
    pts[i]=new Vector3(380+43*MathF.Cos(t),30+17*MathF.Sin(t)+bird*2+14*MathF.Exp(-MathF.Pow((((t-5.85f+MathF.PI+MathF.Tau)%MathF.Tau)-MathF.PI)/.6f,2)),285+64*MathF.Sin(t))-new Vector3(400,15,400);
   }
   var forward=Enumerable.Range(0,n).Select(i=>Vector3.Normalize(pts[(i+1)%n]-pts[(i+n-1)%n])).ToArray();
   var yaw=Unwrap(forward.Select(v=>MathF.Atan2(-v.Z,v.X)*180/MathF.PI).Append(MathF.Atan2(-forward[0].Z,forward[0].X)*180/MathF.PI).ToArray());
   var pitch=forward.Select(v=>MathF.Asin(v.Y)*180/MathF.PI).Append(MathF.Asin(forward[0].Y)*180/MathF.PI).ToArray();var roll=Enumerable.Repeat(-12f,n+1).ToArray();
   var body=Part("body");var left=Part("left");var right=Part("right");
   var birdSource=Clone(source);((CPlugDynaObjectModel)birdSource.Ents.First(e=>e.Model is CPlugDynaObjectModel).Model!).Mesh=body.Mesh;
   // Relay has only compatibility micro-shapes: these birds are scenery.
   var root=(CPlugDynaObjectModel)birdSource.Ents.First(e=>e.Model is CPlugDynaObjectModel).Model!;var micro=ItemKinematicEntityTemplate.CreateCarrierBody();root.StaticShape=micro.StaticShape;root.DynaShape=micro.DynaShape;
   var plan=ItemMotionPath.Plan(pts,MotionPathUse.CollisionRelay,MotionPathRepeat.Closed,12+bird,KC.AnimEase.Linear).Value!;
   var built=ItemMotionPath.Build(birdSource,plan);if(!built.Success)throw new Exception(built.Reason);
   foreach(var e in built.Value!.Ents.Where(e=>e.Model is CPlugDynaObjectModel))((CPlugDynaObjectModel)e.Model!).Mesh=micro.Mesh;
   var prefab=RelayVisual(built.Value!,body,plan,yaw,pitch,roll,left,right,310+bird*45);
   routeEvidence.Add(new{bird,speed=12+bird,wingPeriodMs=2*(310+bird*45),points=pts.Select(p=>new[]{p.X+400,p.Y+15,p.Z+400})});
   Merge(combined,prefab);
  }
  item.EntityModel=combined;Save(item,"TMIS_Coaster_BirdsFinal",output);
  File.WriteAllText(Path.Combine(output,"bird-routes.json"),JsonSerializer.Serialize(routeEvidence));
 }
 static void Merge(CPlugPrefab target,CPlugPrefab extra){var offset=target.Ents.Count(e=>e.Model is CPlugDynaObjectModel);foreach(var e in extra.Ents)if(e.Params is NPlugDyna_SPrefabConstraintParams p){if(p.Ent1>=0)p.Ent1+=offset;p.Ent2+=offset;}target.Ents=[..target.Ents,..extra.Ents];}
 static void ValidateGraph(CPlugPrefab p){
  var bodies=p.Ents.Where(e=>e.Model is CPlugDynaObjectModel).ToArray();var parents=new Dictionary<int,int>();
  foreach(var entry in p.Ents.Where(e=>e.Model is KC)){
   var c=(KC)entry.Model!;var b=(NPlugDyna_SPrefabConstraintParams)entry.Params!;
   if(b.Ent2<0||b.Ent2>=bodies.Length||b.Ent1>=bodies.Length||!parents.TryAdd(b.Ent2,b.Ent1))throw new Exception("Invalid/duplicate binding");
   foreach(var f in new[]{c.TransAnimFunc,c.RotAnimFunc})if(f?.SubFuncs is not {Length:>0 and <=4}||f.SubFuncs.Any(k=>k.Duration.TotalMilliseconds<=0))throw new Exception("Invalid timeline");
  }
  if(parents.Count!=bodies.Length)throw new Exception("Undriven body");
  foreach(int child in parents.Keys){int at=child,depth=0;while(at>=0){if(++depth>6)throw new Exception("Deep/cyclic chain");at=parents[at];}}
 }
 static void Save(CGameItemModel item,string name,string output){item.Name=name;item.Ident=new(name,26,item.Ident.Author);item.ArchetypeRef="";var path=Path.Combine(output,name+".Item.Gbx");new Gbx<CGameItemModel>(item).Save(path);var re=Gbx.Parse<CGameItemModel>(path).Node;var p=(CPlugPrefab)re.EntityModel!;ValidateGraph(p);foreach(var c in p.Ents.Select(e=>e.Model).OfType<KC>())if(c.TransAnimFunc?.SubFuncs is not {Length:>0 and <=4}||c.RotAnimFunc?.SubFuncs is not {Length:>0 and <=4})throw new Exception("Unsafe key count");Console.WriteLine($"PASS {name}: {p.Ents.Length} entities, {new FileInfo(path).Length} bytes; native observation still required");}
}

