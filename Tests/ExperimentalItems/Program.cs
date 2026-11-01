using GBX.NET;
using GBX.NET.Engines.Game;
using System.IO.Compression;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Plug;
using GBX.NET.Engines.Meta;
using KC=GBX.NET.Engines.Meta.NPlugDyna_SKinematicConstraint;
Gbx.LZO=new GBX.NET.LZO.MiniLZO();
if(args.Length==3&&args[0]=="--common"){
 Directory.CreateDirectory(args[2]);
 foreach(var mode in new[]{"OrnamentControl","PickUp","AudioPermanent","AudioSpawn"}){
 var doc=Gbx.Parse<CGameItemModel>(args[1]);var item=doc.Node;var prefab=(CPlugPrefab)item.EntityModel!;
 var common=new CGameCommonItemEntityModel{StaticObject=(CPlugStaticObjectModel)prefab.Ents.First(e=>e.Model is CPlugStaticObjectModel).Model!,TriggerShape=((NPlugTrigger_SWaypoint)prefab.Ents.First(e=>e.Model is NPlugTrigger_SWaypoint).Model!).TriggerShape};
 common.CreateChunk<CGameCommonItemEntityModel.Chunk2E027000>().Version=6;
 if(mode=="AudioPermanent")common.SoundRefPermanent=@"Stadium\Media\Audio\WhooshDecoItems.Sound.Gbx";
 if(mode=="AudioSpawn")common.SoundRefSpawn=@"ShootMania\Media\Audio\Sound\UI\UISound_Bonus.Sound.Gbx";
 item.EntityModel=common;item.ItemType=mode=="PickUp"?CGameItemModel.EItemType.PickUp:CGameItemModel.EItemType.Ornament;item.WaypointType=CGameItemModel.EWaypointType.None;
 item.Name="TMIS_ExpD_"+mode;item.Ident=new("TMIS_Experimental\\"+item.Name+".Item.Gbx",26,"TM_Item_Studio");item.ArchetypeRef="";
 var output=Path.Combine(args[2],item.Name+".Item.Gbx");doc.Save(output);_=Gbx.Parse<CGameItemModel>(output);Console.WriteLine(output);
 }return;
}
if(args.Length==3&&args[0]=="--catalog"){
 var rows=new List<object>();foreach(var file in Directory.EnumerateFiles(args[1],"*.Item.Gbx",SearchOption.AllDirectories)){
 try{var doc=Gbx.Parse<CGameItemModel>(file);var item=doc.Node;var common=item.EntityModel as CGameCommonItemEntityModel;
 rows.Add(new{file,type=item.ItemType.ToString(),waypoint=item.WaypointType.ToString(),model=item.EntityModel?.GetType().Name,sound=common?.SoundRefPermanent,spawn=common?.SoundRefSpawn,grab=common?.SoundRefGrab,trigger=common?.TriggerShape?.GetType().Name,actions=common?.Actions?.Count});}
 catch(Exception e){rows.Add(new{file,error=e.GetType().Name});}}
 File.WriteAllText(args[2],System.Text.Json.JsonSerializer.Serialize(rows,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(rows.Count);return;
}
if(args.Length==2&&args[0]=="--particle"){
 var d=Gbx.Parse<CPlugParticleEmitterModel>(args[1]);var visited=new HashSet<object>(ReferenceEqualityComparer.Instance);
 void Walk(object? n,string path,int depth){if(n is null||depth>9)return;if(n is string||n.GetType().IsPrimitive||n.GetType().IsEnum||n.GetType().IsValueType){Console.WriteLine(path+"="+n);return;}if(!visited.Add(n))return;if(n is System.Collections.IEnumerable seq){int i=0;foreach(var v in seq)Walk(v,path+"/"+i++,depth+1);return;}Console.WriteLine(path+":"+n.GetType().Name);foreach(var prop in n.GetType().GetProperties())if(prop.GetIndexParameters().Length==0&&prop.Name is not("Chunks" or "References")){try{Walk(prop.GetValue(n),path+"/"+prop.Name,depth+1);}catch{}}}
 Walk(d.Node,"root",0);return;
}
if(args.Length is 4 or 5&&args[0]=="--map"){
 var map=Gbx.Parse<CGameCtnChallenge>(args[1],new GbxReadSettings{SkipChunkIds=[0x0304305B]});var item=Gbx.Parse<CGameItemModel>(args[2]).Node;map.Node.AnchoredObjects!.Clear();
 var id=item.Ident.Id;var anchor=new CGameCtnAnchoredObject{ItemModel=item.Ident,AnchorTreeId=id,AbsolutePositionInMap=new(args.Length==5?float.Parse(args[4],System.Globalization.CultureInfo.InvariantCulture):320,8,336),YawPitchRoll=default,PivotPosition=default,Scale=1};anchor.CreateChunk<CGameCtnAnchoredObject.Chunk03101002>().Version=8;anchor.CreateChunk<CGameCtnAnchoredObject.Chunk03101004>().Version=0;map.Node.AnchoredObjects.Add(anchor);
 map.Node.MapName="TMIS Experiment "+item.Name;map.Node.MapUid="TMIS_EXP_"+Guid.NewGuid().ToString("N")[..18];map.Node.EmbeddedZipData=null;map.Node.UpdateEmbeddedZipData(zip=>{using var dest=zip.CreateEntry("Items/"+id.Replace('\\','/'),CompressionLevel.Optimal).Open();dest.Write(File.ReadAllBytes(args[2]));});map.Save(args[3]);
 var shape=(item.EntityModel as CPlugPrefab)?.Ents.Select(e=>e.Model).OfType<NPlugTrigger_SWaypoint>().FirstOrDefault()?.TriggerShape??(item.EntityModel as CGameCommonItemEntityModel)?.TriggerShape as CPlugSurface;if(shape?.Surf is CPlugSurface.Mesh mesh)Console.WriteLine("Trigger bounds "+string.Join(";",new[]{mesh.Vertices.Min(v=>v.X),mesh.Vertices.Max(v=>v.X),mesh.Vertices.Min(v=>v.Y),mesh.Vertices.Max(v=>v.Y),mesh.Vertices.Min(v=>v.Z),mesh.Vertices.Max(v=>v.Z)}));Console.WriteLine(args[3]);return;
}
if(args.Length!=3)throw new ArgumentException("ExperimentalItems <known-checkpoint.Item.Gbx> <kinematic-source.Item.Gbx> <output-directory>");
Directory.CreateDirectory(args[2]);
foreach(var mode in new[]{"CheckpointControl","VisualOnly","DrivenTrigger"}){
 var doc=Gbx.Parse<CGameItemModel>(args[0]);var item=doc.Node;var root=(CPlugPrefab)item.EntityModel!;var ents=root.Ents.ToList();var triggerIndex=ents.FindIndex(e=>e.Model is NPlugTrigger_SWaypoint);var trigger=(NPlugTrigger_SWaypoint)ents[triggerIndex].Model!;trigger.NoRespawn=false;
 if(mode!="CheckpointControl"){
  var cart=(CPlugPrefab)Gbx.Parse<CGameItemModel>(args[1]).Node.EntityModel!;var dynamic=cart.Ents.First(e=>e.Model is CPlugDynaObjectModel);var model=(CPlugDynaObjectModel)dynamic.Model!;var bodyIndex=ents.FindIndex(e=>e.Model is CPlugStaticObjectModel);var source=(CPlugStaticObjectModel)ents[bodyIndex].Model!;
  model.Mesh=source.Mesh;model.StaticShape=model.DynaShape=source.Shape??model.StaticShape;foreach(var collision in new[]{model.StaticShape,model.DynaShape}.Distinct())if(collision?.Surf is CPlugSurface.Mesh cm)cm.Vertices=cm.Vertices.Select(v=>new Vec3(v.X*.00001f,v.Y*.00001f-100,v.Z*.00001f)).ToArray();dynamic.Position=ents[bodyIndex].Position;dynamic.Rotation=ents[bodyIndex].Rotation;ents[bodyIndex]=dynamic;
  CPlugPrefab.EntRef Driver(int parent,int child,bool moving){return new(){Rotation=Quat.Identity,Model=new KC{Version=0,SubVersion=3,TransAxis=KC.EAxis.X,TransMin=0,TransMax=moving?32:0,RotAxis=KC.EAxis.Y,AngleMinDeg=0,AngleMaxDeg=0,TransAnimFunc=new(){IsDuration=true,SubFuncs=[new(){Ease=KC.AnimEase.Linear,Duration=new(4000)},new(){Ease=KC.AnimEase.Linear,Duration=new(4000),Reverse=true}]},RotAnimFunc=new(){IsDuration=true,SubFuncs=[new(){Ease=KC.AnimEase.Constant,Duration=new(8000)}]}},Params=new NPlugDyna_SPrefabConstraintParams{Version=0,Ent1=parent,Ent2=child}};}
  ents.Add(Driver(-1,bodyIndex,true));
  if(mode=="DrivenTrigger")ents.Add(Driver(-1,triggerIndex,true));
 }
 root.Ents=ents.ToArray();item.Name="TMIS_ExpC_"+mode;item.Ident=new("TMIS_Experimental\\"+item.Name+".Item.Gbx",26,"TM_Item_Studio");item.ArchetypeRef="";
 var output=Path.Combine(args[2],item.Name+".Item.Gbx");doc.Save(output);var read=Gbx.Parse<CGameItemModel>(output);Console.WriteLine($"{output}: {trigger.Version}, {trigger.Type}, {new FileInfo(output).Length} bytes");
}
