using System.Numerics;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Plug;
using TM_Item_Studio.Models;
using KC=GBX.NET.Engines.Meta.NPlugDyna_SKinematicConstraint;

static class BehaviorScene
{
 public static void Parts(string sourceFile,string birdFile,string output) {
    Directory.CreateDirectory(output);var item=Gbx.Parse<CGameItemModel>(sourceFile).Node;
    var source=(CPlugPrefab)item.EntityModel!;
    var birds=(CPlugPrefab)Gbx.Parse<CGameItemModel>(birdFile).Node.EntityModel!;
    var models=birds.Ents.Select(e=>e.Model).OfType<CPlugDynaObjectModel>().Where(b=>b.Mesh is CPlugSolid2Model s && s.Visuals!.OfType<CPlugVisualIndexedTriangles>().Any(v=>v.VertexStreams.Any(st=>st.Positions?.Length>10))).DistinctBy(b=>b.Mesh).Take(3).ToArray();
    if(models.Length!=3)throw new Exception("Expected body and two wing visuals");
    var solid=(CPlugSolid2Model)models[0].Mesh!;
    solid.Visuals=[..models.Select(m=>((CPlugSolid2Model)m.Mesh!).Visuals![0])];
    solid.ShadedGeoms=[new(){VisualIndex=0,MaterialIndex=0,LodMask=1},new(){VisualIndex=1,MaterialIndex=0,LodMask=1},new(){VisualIndex=2,MaterialIndex=0,LodMask=1}];
    var body=(CPlugDynaObjectModel)source.Ents.First(e=>e.Model is CPlugDynaObjectModel).Model!;body.Mesh=solid;
    var micro=ItemKinematicEntityTemplate.CreateCarrierBody();body.DynaShape=micro.DynaShape;body.StaticShape=micro.StaticShape;
    item.Name="TMIS_Bird_PartsSource";item.Ident=new(item.Name,26,item.Ident.Author);item.ArchetypeRef="";
    new Gbx<CGameItemModel>(item).Save(Path.Combine(output,item.Name+".Item.Gbx"));
    var p=ItemMotionPath.Plan([new(0,10,0),new(30,15,0),new(30,15,30),new(0,10,30)],MotionPathUse.Scenery,MotionPathRepeat.Closed,10,KC.AnimEase.Linear).Value!;
    var behavior=ItemMotionBehavior.Apply(p,ItemMotionBehavior.Defaults(MotionBehaviorKind.BirdFlight)).Value!;
    p=behavior with{Parts=[new(1,PeriodMs:700),new(2,PeriodMs:700,Reverse:true)]};
    var built=ItemMotionPath.Build(source,p);if(!built.Success)throw new Exception(built.Reason);
    item.EntityModel=built.Value;item.Name="TMIS_Bird_TemplateParts";item.Ident=new(item.Name,26,item.Ident.Author);
    new Gbx<CGameItemModel>(item).Save(Path.Combine(output,item.Name+".Item.Gbx"));
    Console.WriteLine("PASS separate wing meshes generated with shared body route");
 }
 public static void Run(string sourceFile,string frameFile,string output) {
  Directory.CreateDirectory(output);
  var item=Gbx.Parse<CGameItemModel>(sourceFile).Node;
  var source=(CPlugPrefab)item.EntityModel!;
  var frames=JsonDocument.Parse(File.ReadAllText(frameFile)).RootElement.GetProperty("frames").EnumerateArray().ToArray();
  Vector3 Read(JsonElement j,string key){var a=j.GetProperty(key).EnumerateArray().Select(x=>x.GetSingle()).ToArray();return new(a[0],a[1],a[2]);}
  var raw=frames.Select(j=>Read(j,"point")).ToArray();var pts=raw.Select(p=>p-raw[0]-new Vector3(0,4.2f,0)).ToArray();
  var measures=new List<object>();
  foreach(int count in new[]{16,32,64,128}) {
   var points=Enumerable.Range(0,count).Select(i=>pts[i*pts.Length/count]).ToArray();
   var original=ItemMotionPath.Plan(points,MotionPathUse.Scenery,MotionPathRepeat.Closed,10,KC.AnimEase.Linear).Value!;
   // Find the longest climb rather than the small approach humps.
   int from=-1,to=-1;float gain=0;
   for(int i=0;i<original.Legs.Count;i++) {if(original.Legs[i].Delta.Y<=.05f)continue;int end=i;float up=0;while(end<original.Legs.Count&&original.Legs[end].Delta.Y>.05f){up+=original.Legs[end].Delta.Y;end++;}if(up>gain){gain=up;from=i;to=end-1;}i=end-1;}
   var settings=new MotionBehaviorSettings(Speed:25,MaximumSpeed:55,Gravity:14,Resistance:.03f,LiftSpeed:4,LiftFrom:from,LiftTo:to,Braking:3);
   var behavior=ItemMotionBehavior.Apply(original,settings);if(!behavior.Success)throw new Exception(behavior.Reason);
   var plan=behavior.Value!;
   // Preserve the actual bank of the coaster deck, not generic turn-derived banking.
   var angles=plan.Rotations!.ToArray();
   for(int i=0;i<count;i++) {var j=frames[i*frames.Length/count];var side=Read(j,"side");var up=Read(j,"up");angles[i]=angles[i] with{Roll=MathF.Atan2(-side.Y,up.Y)*180/MathF.PI};}
   angles[^1]=angles[^1] with{Roll=angles[0].Roll};plan=plan with{Rotations=angles};
   var compiled=ItemMotionPathProfile.Compile(plan).Value!;
   var built=ItemMotionPath.Build(source,plan);if(!built.Success)throw new Exception(built.Reason);
   item.EntityModel=built.Value;item.Name=$"TMIS_Cart_Inertia_{count}";item.Ident=new(item.Name,26,item.Ident.Author);item.ArchetypeRef="";
   var file=Path.Combine(output,item.Name+".Item.Gbx");new Gbx<CGameItemModel>(item).Save(file);
   var reread=Gbx.Parse<CGameItemModel>(file).Node;
   measures.Add(new{count,bytes=new FileInfo(file).Length,entities=((CPlugPrefab)reread.EntityModel!).Ents.Length,totalMs=compiled.DurationMs,averageSpeed=points.Select((p,i)=>Vector3.Distance(p,points[(i+1)%count])).Sum()/(compiled.DurationMs/1000f),minSpeed=plan.SpeedPoints!.Min(),maxSpeed=plan.SpeedPoints.Max(),liftFrom=from,liftTo=to});
   if(count==128) {
    File.WriteAllText(Path.Combine(output,"inertia-route.motion-path.json"),JsonSerializer.Serialize(plan,new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));
    File.WriteAllText(Path.Combine(output,"inertia-timing.json"),JsonSerializer.Serialize(new{totalMs=compiled.DurationMs,points=points.Select(p=>new[]{p.X,p.Y,p.Z}),speeds=plan.SpeedPoints,legs=compiled.Legs.Select(l=>new{l.StartMs,l.MoveMs,l.VelocityRatio}),settings}));
   }
  }
  File.WriteAllText(Path.Combine(output,"size-detail.json"),JsonSerializer.Serialize(measures,new JsonSerializerOptions{WriteIndented=true}));
  Console.WriteLine(JsonSerializer.Serialize(measures));
 }
}
