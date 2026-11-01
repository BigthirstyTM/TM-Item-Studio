using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Plug;
using GBX.NET.Engines.Meta;
using System.Text.Json;
using KC=GBX.NET.Engines.Meta.NPlugDyna_SKinematicConstraint;
Gbx.LZO=new GBX.NET.LZO.MiniLZO();
if(args.Length is <2 or >3)throw new ArgumentException("ItemCapabilities <source item> <output directory> [checkpoint shape]");
if(args[0]=="--inspect"){
 var root=Gbx.Parse(args[1]).Node;var seen=new HashSet<object>(ReferenceEqualityComparer.Instance);
 void Walk(object? obj,string path,int depth){if(obj is null||depth>8||obj is string||obj.GetType().IsValueType||!seen.Add(obj))return;
 if(obj is GBX.NET.Engines.MwFoundations.CMwNod)Console.WriteLine(path+" "+obj.GetType().Name);
 if(obj is NPlugTrigger_SWaypoint w)Console.WriteLine(" TRIGGER version="+w.Version+" type="+w.Type+" noRespawn="+w.NoRespawn);
 if(obj is CPlugSurface sh)Console.WriteLine(" SURFACE version="+sh.SurfVersion+" type="+sh.Surf?.GetType().Name);
 if(obj is System.Collections.IEnumerable seq){int i=0;foreach(var v in seq)Walk(v,path+"/"+i++,depth+1);return;}
 foreach(var prop in obj.GetType().GetProperties())if(prop.GetIndexParameters().Length==0&&prop.Name!="Chunks"&&prop.Name!="References"){try{Walk(prop.GetValue(obj),path+"/"+prop.Name,depth+1);}catch{}}
 }
 Walk(root,"root",0);return;
}
Directory.CreateDirectory(args[1]);var report=new List<object>();
foreach(var type in Enum.GetValues<CGameItemModel.EItemType>()){
 try{var doc=Gbx.Parse<CGameItemModel>(args[0]);doc.Node.ItemType=type;doc.Node.Name="TMIS_Type_"+type;doc.Node.Ident=new(doc.Node.Name,26,doc.Node.Ident.Author);using var ms=new MemoryStream();doc.Save(ms);ms.Position=0;var read=Gbx.Parse<CGameItemModel>(ms);if(read.Node.ItemType!=type)throw new Exception("type mismatch");File.WriteAllBytes(Path.Combine(args[1],"TMIS_Type_"+type+".Item.Gbx"),ms.ToArray());report.Add(new{type=type.ToString(),roundtrip=true,gameplay="Not established by enum serialization"});}
 catch(Exception e){report.Add(new{type=type.ToString(),roundtrip=false,error=e.Message});}
}
File.WriteAllText(Path.Combine(args[1],"item-types.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
if(args.Length<3)return;
foreach(var variant in new[]{"NoTrigger","Static","DrivenTrigger","NestedTrigger","CommonTrigger","CommonRoot"}){
 var doc=Gbx.Parse<CGameItemModel>(args[0]);var item=doc.Node;var prefab=(CPlugPrefab)item.EntityModel!;
 var ents=prefab.Ents.ToList();var body=ents.FindIndex(e=>e.Model is CPlugDynaObjectModel);var driver=ents.Single(e=>e.Model is KC);var kc=(KC)driver.Model!;
 kc.TransAxis=KC.EAxis.X;kc.TransMin=0;kc.TransMax=16;kc.TransAnimFunc=new(){IsDuration=true,SubFuncs=[new(){Ease=KC.AnimEase.Linear,Duration=new(4000)},new(){Ease=KC.AnimEase.Linear,Duration=new(4000),Reverse=true}]};
 kc.AngleMinDeg=kc.AngleMaxDeg=0;kc.RotAnimFunc=new(){IsDuration=true,SubFuncs=[new(){Ease=KC.AnimEase.Constant,Duration=new(8000)}]};
 var surface=Gbx.Parse<CPlugSurface>(args[2]).Node;
 var trigger=new NPlugTrigger_SWaypoint{Version=0,Type=NPlugTrigger_SWaypoint.EGameItemWaypointType.Checkpoint,TriggerShape=surface};
 var triggerEnt=new CPlugPrefab.EntRef{Model=trigger,Rotation=Quat.Identity};
 if(variant=="NoTrigger"){}
 else if(variant=="CommonTrigger"){var common=new CGameCommonItemEntityModel{TriggerShape=surface};common.CreateChunk<CGameCommonItemEntityModel.Chunk2E027000>().Version=6;ents.Add(new(){Model=common,Rotation=Quat.Identity});}
 else if(variant=="NestedTrigger"){
  var nested=new CPlugPrefab{Ents=[ents[body],triggerEnt]};ents[body]=new(){Model=nested,Rotation=Quat.Identity};
 }else{var index=ents.Count;ents.Add(triggerEnt);if(variant=="DrivenTrigger"){
  // Deliberate research candidate: a kinematic constraint aimed at a non-dynamic trigger entity.
  using var bytes=new MemoryStream();new Gbx<KC>(kc).Save(bytes);bytes.Position=0;var copy=Gbx.Parse<KC>(bytes).Node;
  ents.Add(new(){Model=copy,Rotation=Quat.Identity,Params=new NPlugDyna_SPrefabConstraintParams{Version=0,Ent1=-1,Ent2=index}});
 }}
 prefab.Ents=ents.ToArray();
 if(variant=="CommonRoot"){
  var dyna=(CPlugDynaObjectModel)ents[body].Model!;
  var common=new CGameCommonItemEntityModel{StaticObject=new CPlugStaticObjectModel{Version=3,Mesh=dyna.Mesh,Shape=dyna.StaticShape,IsMeshCollidable=false},TriggerShape=surface};common.CreateChunk<CGameCommonItemEntityModel.Chunk2E027000>().Version=6;item.EntityModel=common;
 }
 item.WaypointType=CGameItemModel.EWaypointType.Checkpoint;item.Name="TMIS_TriggerV3_"+variant;item.Ident=new(item.Name,26,item.Ident.Author);item.ArchetypeRef="";
 var file=Path.Combine(args[1],item.Name+".Item.Gbx");doc.Save(file);_=Gbx.Parse<CGameItemModel>(file);Console.WriteLine(file);
}
Console.WriteLine("Item type roundtrips and trigger research candidates written; no gameplay claim.");
