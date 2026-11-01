using System.Numerics;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Plug;
using TM_Item_Studio.Models;
using TmEssentials;
using KC=GBX.NET.Engines.Meta.NPlugDyna_SKinematicConstraint;
static class CollisionArrayProbe {
 public static void Run(string file,string output){
  Directory.CreateDirectory(output);
  foreach(var kind in new[]{"Control","Array","Scaled","Bob","RotateLayer","SpinLayer","RootSpin"}){
   var item=Gbx.Parse<CGameItemModel>(file).Node;
   var p=ItemMotionPath.Plan([Vector3.Zero,new(32,0,0),new(32,0,32),new(0,0,32)],MotionPathUse.CollisionRelay,MotionPathRepeat.Closed,2,KC.AnimEase.Linear,0,8000).Value!;
   p=p with{Rotations=Enumerable.Repeat(new MotionPathRotation(),5).ToArray()};
   if(kind is "Array" or "Scaled" or "Bob" or "RotateLayer" or "SpinLayer")p=p with{Array=new(3,16000,default,kind=="Scaled"?1.25f:1,kind=="Scaled"?1.5f:1,kind=="Scaled"?2:1)};
   if(kind=="Bob")p=p with{Layers=[new("Physical bob",Kind:MotionLayerKind.Translate,Axis:KC.EAxis.Y,Amplitude:.15f,PeriodMs:8000)]};
   if(kind=="RotateLayer")p=p with{Layers=[new("Physical roll",Axis:KC.EAxis.X,Amplitude:10,PeriodMs:8000)]};
   if(kind=="SpinLayer")p=p with{Layers=[new("Physical yaw",Kind:MotionLayerKind.Spin,Axis:KC.EAxis.Y,PeriodMs:32000)]};
   var requested=p;
   if(kind=="Bob"){var sampled=ItemMotionCollisionLayers.Compile(p);if(!sampled.Success)throw new Exception(sampled.Reason);p=sampled.Value!;}
   var result=ItemMotionPath.Build((CPlugPrefab)item.EntityModel!,p);if(!result.Success)throw new Exception(kind+": "+result.Reason);
   if(kind=="RootSpin"){
    var straight=ItemMotionPath.Plan([Vector3.Zero,new(24,0,0)],MotionPathUse.Obstacle,MotionPathRepeat.PingPong,2,KC.AnimEase.QuadInOut).Value!;
    result=ItemMotionPath.Build((CPlugPrefab)item.EntityModel!,straight);if(!result.Success)throw new Exception(result.Reason);
    var c=result.Value!.Ents.Select(e=>e.Model).OfType<KC>().Single();c.RotAxis=KC.EAxis.Y;c.AngleMaxDeg=360;c.RotAnimFunc=new(){IsDuration=true,SubFuncs=[new(){Ease=KC.AnimEase.Linear,Duration=new TimeInt32(24000)}]};
   }
   item.EntityModel=result.Value;var name="TMIS_CA_"+kind;item.Name=name;item.Ident=new(name+".Item.Gbx",26,item.Ident.Author);item.ArchetypeRef="";
   new Gbx<CGameItemModel>(item).Save(Path.Combine(output,name+".Item.Gbx"));
   File.WriteAllText(Path.Combine(output,name+".json"),kind=="RootSpin"?JsonSerializer.Serialize(new{ResearchOnly=true,Mode="One world-bound body: 24 m straight ping-pong, QuadInOut, 2 m/s; Y rotation 360 degrees per 24 seconds"}):JsonSerializer.Serialize(requested,ItemMotionCustomTemplate.Json));
   Console.WriteLine(name+": "+result.Value!.Ents.Length+" entries");
  }
 }
}
