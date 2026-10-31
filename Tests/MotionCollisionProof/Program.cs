using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Plug;
using GBX.NET.Engines.Meta;
using TM_Item_Studio.Models;
using TmEssentials;
using KC=GBX.NET.Engines.Meta.NPlugDyna_SKinematicConstraint;
Gbx.LZO=new GBX.NET.LZO.MiniLZO();
if(args.Length != 2) throw new ArgumentException("Usage: MotionCollisionProof <source Cube.Item.Gbx> <output directory>");
Directory.CreateDirectory(args[1]);
foreach(var name in new[]{"Orbit","Lift","DualAxis"}) {
 var source=Gbx.Parse<CGameItemModel>(args[0]).Node;
 var result=ItemKinematicEntityTemplate.ConvertStaticToKinematic(source,ItemKinematicEntityTemplate.GetDefaultMovingTemplate());
 if(!result.Success)throw new Exception(result.Reason);
 var item=result.Value!;var prefab=(CPlugPrefab)item.EntityModel!;
 var body=prefab.Ents.Single(e=>e.Model is CPlugDynaObjectModel);
 var dyna=(CPlugDynaObjectModel)body.Model!;
 var driver=prefab.Ents.Single(e=>e.Model is KC);var kc=(KC)driver.Model!;
 var ip=(NPlugDynaObjectModel_SInstanceParams)body.Params!;ip.Phase01=0;ip.Phase01Max=-1;ip.PeriodSc=1;ip.PeriodScMax=-1;
 kc.TransAxis=KC.EAxis.X;kc.TransMin=kc.TransMax=0;kc.TransAnimFunc=Func(Key(KC.AnimEase.Constant,12000));
 kc.RotAxis=KC.EAxis.Y;kc.AngleMinDeg=kc.AngleMaxDeg=0;kc.RotAnimFunc=Func(Key(KC.AnimEase.Constant,12000));
 if(name=="Orbit") {
  var solid=(CPlugSolid2Model)dyna.Mesh!;
  foreach(var stream in (solid.Visuals??[]).SelectMany(v=>v.VertexStreams??[]).Distinct())
   if(stream.Positions is {} positions)for(int i=0;i<positions.Length;i++)positions[i]=new(positions[i].X+12,positions[i].Y,positions[i].Z);
  foreach(var shape in new[]{dyna.StaticShape,dyna.DynaShape}.Distinct())
   if(shape?.Surf is CPlugSurface.Mesh mesh)for(int i=0;i<mesh.Vertices.Length;i++)mesh.Vertices[i]=new(mesh.Vertices[i].X+12,mesh.Vertices[i].Y,mesh.Vertices[i].Z);
  kc.AngleMaxDeg=360;kc.RotAnimFunc=Func(Key(KC.AnimEase.Linear,12000));
 } else if(name=="Lift") {
  kc.TransAxis=KC.EAxis.Y;kc.TransMax=12;kc.TransAnimFunc=Func(Key(KC.AnimEase.Constant,2000),Key(KC.AnimEase.Linear,4000),Key(KC.AnimEase.Constant,2000,true),Key(KC.AnimEase.Linear,4000,true));
 } else {
  kc.TransMax=16;kc.TransAnimFunc=Func(Key(KC.AnimEase.Linear,3000),Key(KC.AnimEase.Constant,3000,true),Key(KC.AnimEase.Linear,3000,true),Key(KC.AnimEase.Constant,3000));
  var second=new KC{Version=0,SubVersion=3,TransAxis=KC.EAxis.Z,TransMin=0,TransMax=16,RotAxis=KC.EAxis.Y,AngleMinDeg=0,AngleMaxDeg=0,RotAnimFunc=Func(Key(KC.AnimEase.Constant,12000)),TransAnimFunc=Func(Key(KC.AnimEase.Constant,3000),Key(KC.AnimEase.Linear,3000),Key(KC.AnimEase.Constant,3000,true),Key(KC.AnimEase.Linear,3000,true))};
  prefab.Ents=[..prefab.Ents,new(){Model=second,Rotation=Quat.Identity,Params=new NPlugDyna_SPrefabConstraintParams{Version=0,Ent1=-1,Ent2=0}}];
 }
 item.Name="TMIS_Contact_"+name;item.Ident=new(item.Name,26,item.Ident.Author);item.ArchetypeRef="";
 var path=Path.Combine(args[1],item.Name+".Item.Gbx");
 new Gbx<CGameItemModel>(item).Save(path);Console.WriteLine(path);
}
KC.SubAnimFunc Key(KC.AnimEase ease,int ms,bool reverse=false)=>new(){Ease=ease,Duration=new TimeInt32(ms),Reverse=reverse};
KC.AnimFunc Func(params KC.SubAnimFunc[] keys)=>new(){IsDuration=true,SubFuncs=keys};
