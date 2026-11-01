using System.Numerics;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Plug;
using TM_Item_Studio.Models;
using TmEssentials;
using KC=GBX.NET.Engines.Meta.NPlugDyna_SKinematicConstraint;
Gbx.LZO=new GBX.NET.LZO.MiniLZO();
if(args.Length==4 && args[0]=="behavior"){BehaviorScene.Run(args[1],args[2],args[3]);return;}
if(args.Length==4 && args[0]=="parts-source"){BehaviorScene.Parts(args[1],args[2],args[3]);return;}
if(args.Length==2 && args[0]=="verify-flight-map"){
 var map=Gbx.Parse<GBX.NET.Engines.Game.CGameCtnChallenge>(args[1]).Node;
 using var zip=map.OpenReadEmbeddedZipData();
 if(map.AnchoredObjects?.Count!=3)throw new Exception("Expected cart, birds and coaster");
 foreach(var entry in zip.Entries)Console.WriteLine($"Embedded: {entry.FullName} ({entry.Length} bytes)");
 if(!zip.Entries.Any(e=>e.FullName.Contains("TMIS_Cart_GravitySmooth"))||!zip.Entries.Any(e=>e.FullName.Contains("TMIS_Coaster_Birds"))||!zip.Entries.Any(e=>e.FullName.Contains("NewCoaster")))throw new Exception("Missing embedded models");
 Console.WriteLine("PASS three placed and embedded models");return;
}

if(args.Length==2 && args[0]=="verify-flight"){FlightScene.Verify(args[1]);return;}
if(args.Length==4 && args[0]=="birds"){FlightScene.Birds(args[1],args[2],args[3]);return;}
if(args.Length==4 && args[0]=="flight"){FlightScene.Run(args[1],args[2],args[3]);return;}
if(args.Length==5 && args[0]=="cart"){CartBuilder.Run(args[1],args[2],args[3],args[4]);return;}
if(args.Length>=3 && args[0]=="inspect") {
 Directory.CreateDirectory(args[1]);
 foreach(var file in args.Skip(2)) {
  var item=Gbx.Parse<CGameItemModel>(file).Node;
  var name=Path.GetFileName(file);
  if(item.IconWebP is {Length:>0} webp)File.WriteAllBytes(Path.Combine(args[1],name+".webp"),webp);
  if(item.Icon is {} icon) {
   var bytes=new List<byte>();
   for(int y=0;y<icon.GetLength(1);y++)for(int x=0;x<icon.GetLength(0);x++){var c=icon[x,y];bytes.AddRange([(byte)c.R,(byte)c.G,(byte)c.B,(byte)c.A]);}
   File.WriteAllText(Path.Combine(args[1],name+".icon.json"),JsonSerializer.Serialize(new{width=icon.GetLength(0),height=icon.GetLength(1),rgba=bytes}));
  }
  Console.WriteLine($"{file}: {item.Name}, {item.EntityModel?.GetType().Name}");
  var converted=ItemKinematicEntityTemplate.ConvertStaticToKinematic(item,ItemKinematicEntityTemplate.GetDefaultMovingTemplate());
  if(converted.Success) {
   var body=(CPlugDynaObjectModel)((CPlugPrefab)converted.Value!.EntityModel!).Ents.First(e=>e.Model is CPlugDynaObjectModel).Model!;
   var solid=(CPlugSolid2Model)body.Mesh!;
   Console.WriteLine(JsonSerializer.Serialize(solid.CustomMaterials?.Select(m=>new{m.MaterialUserInst?.Link,m.MaterialUserInst?.Model,m.MaterialUserInst?.Color})));
  }
 }
 return;
}
if(args.Length!=3)throw new ArgumentException("cube route output");
Directory.CreateDirectory(args[2]);
var points=JsonSerializer.Deserialize<float[][]>(File.ReadAllText(args[1]))!.Select(p=>new Vector3(p[0],p[1],p[2])).ToArray();
var origin=points[0];points=points.Select(p=>p-origin).ToArray();
foreach(var variant in new[]{"Parking48","Slow25","Overlap1500"}) {
 var converted=ItemKinematicEntityTemplate.ConvertStaticToKinematic(Gbx.Parse<CGameItemModel>(args[0]).Node,ItemKinematicEntityTemplate.GetDefaultMovingTemplate());
 if(!converted.Success)throw new Exception(converted.Reason);
 var item=converted.Value!;
 var plan=ItemMotionPath.Plan(points,MotionPathUse.CollisionRelay,MotionPathRepeat.Closed,variant=="Slow25"?2.5f:5,KC.AnimEase.Linear,0,8000,variant=="Parking48"?48:64).Value!;
 var built=ItemMotionPath.Build((CPlugPrefab)item.EntityModel!,plan);
 if(!built.Success)throw new Exception(built.Reason);
 var prefab=built.Value!;
 if(variant=="Overlap1500") {
  var drivers=prefab.Ents.Select(e=>e.Model).OfType<KC>().ToArray();
  for(int i=0;i<drivers.Length;i++) {
   var begin=i==0?0:Math.Max(0,plan.Legs[i].StartMs-1500);
   var end=i+1==drivers.Length?plan.DurationMs:Math.Min(plan.DurationMs,plan.Legs[i+1].StartMs+1500);
   var keys=new List<KC.SubAnimFunc>();
   void Add(int ms,bool reverse){if(ms>0)keys.Add(new(){Ease=KC.AnimEase.Constant,Duration=new TimeInt32(ms),Reverse=reverse});}
   Add(begin,true);Add(end-begin,false);Add(plan.DurationMs-end,true);
   drivers[i].RotAnimFunc=new(){IsDuration=true,SubFuncs=keys.ToArray()};
  }
 }
 // Clearance at both endpoints, including the actual collision vertices, in the inactive pose.
 var highest=float.NegativeInfinity;
 var bodies=prefab.Ents.Where(e=>e.Model is CPlugDynaObjectModel).ToArray();
 for(int i=0;i<bodies.Length;i++) {
  var e=bodies[i];var q=new Quaternion(e.Rotation.X,e.Rotation.Y,e.Rotation.Z,e.Rotation.W);
  var pos=new Vector3(e.Position.X,e.Position.Y,e.Position.Z);
  var model=(CPlugDynaObjectModel)e.Model!;
  foreach(var v in ((CPlugSurface.Mesh)model.DynaShape!.Surf!).Vertices) {
   var folded=Vector3.Transform(new Vector3(v.X,v.Y,v.Z),Quaternion.CreateFromAxisAngle(Vector3.UnitX,MathF.PI));
   var world=Vector3.Transform(folded,q)+pos;
   highest=Math.Max(highest,15+Math.Max(world.Y,world.Y+plan.Legs[i].Delta.Y));
  }
 }
 if(highest>=8)throw new Exception("Parked collision exposed above terrain: "+highest);
 item.EntityModel=prefab;item.Name="TMIS_HV_"+variant;item.Ident=new(item.Name,26,item.Ident.Author);item.ArchetypeRef="";
 var file=Path.Combine(args[2],item.Name+".Item.Gbx");new Gbx<CGameItemModel>(item).Save(file);
 var read=(CPlugPrefab)Gbx.Parse<CGameItemModel>(file).Node.EntityModel!;
 foreach(var c in read.Ents.Select(e=>e.Model).OfType<KC>()) foreach(var f in new[]{c.TransAnimFunc,c.RotAnimFunc})
  if(f!.SubFuncs!.Length>4 || f.SubFuncs.Sum(k=>k.Duration.TotalMilliseconds)!=plan.DurationMs)throw new Exception("Bad timeline");
 Console.WriteLine($"PASS {variant}: cycle {plan.DurationMs} ms, highest parked collision Y {highest}");
}
