using System.Numerics;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Plug;
using TM_Item_Studio.Models;
using KC=GBX.NET.Engines.Meta.NPlugDyna_SKinematicConstraint;
static class ArrayGallery {
 public static void Run(string sourceFolder,string output){
  Directory.CreateDirectory(output);var entries=new List<object>();
  foreach(var (label,index) in new[]{("CarBody",0),("BirdFlight",1),("Fish",2)}){
   var item=Gbx.Parse<CGameItemModel>(Path.Combine(sourceFolder,"Sources","TMIS_Demo_"+label+"_Source.Item.Gbx")).Node;
   var template=JsonSerializer.Deserialize<MotionPathPlan>(File.ReadAllText(Path.Combine(sourceFolder,"Templates",label+".motion-path.json")),ItemMotionCustomTemplate.Json)!;
   var points=Enumerable.Range(0,32).Select(i=>new Vector3(14*MathF.Cos(i*MathF.PI/16),6+2*MathF.Sin(i*MathF.PI/8),14*MathF.Sin(i*MathF.PI/16))).ToArray();
   var p=ItemMotionPath.Plan(points,MotionPathUse.Scenery,MotionPathRepeat.Closed,4,KC.AnimEase.Linear).Value!;
   p=p with{Legs=p.Legs.Select((l,i)=>l with{StartMs=i*1000,MoveMs=1000}).ToArray(),DurationMs=32000,Parts=template.Parts,
    Rotations=ItemMotionPathProfile.FollowDirection(p),Array=index==0?new(4,2000):new(8,1500,new(2,1,2),.7f,1.2f,3,42,true),
    Layers=index==0?null:[new("Bank",Amplitude:10,PeriodMs:3000),new("Bob",Kind:MotionLayerKind.Translate,Axis:KC.EAxis.Y,Amplitude:.5f,PeriodMs:2000)]};
   var built=ItemMotionPath.Build((CPlugPrefab)item.EntityModel!,p);if(!built.Success)throw new Exception(built.Reason);item.EntityModel=built.Value;
   var name="TMIS_Array_"+label;item.Name=name;item.Ident=new(name+".Item.Gbx",26,item.Ident.Author);item.ArchetypeRef="";
   new Gbx<CGameItemModel>(item).Save(Path.Combine(output,name+".Item.Gbx"));
   File.WriteAllText(Path.Combine(output,label+".motion-path.json"),JsonSerializer.Serialize(p,ItemMotionCustomTemplate.Json));
   entries.Add(new{label,item=name+".Item.Gbx",x=350+index*45,y=8,z=400,durationMs=32000,parts=p.Parts?.Length??0});
  }
  File.WriteAllText(Path.Combine(output,"array-gallery.json"),JsonSerializer.Serialize(entries,new JsonSerializerOptions{WriteIndented=true}));
 }
}
