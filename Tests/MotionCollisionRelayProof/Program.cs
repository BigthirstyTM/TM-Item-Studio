using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Plug;
using GBX.NET.Engines.Meta;
using TM_Item_Studio.Models;
using TmEssentials;
using KC=GBX.NET.Engines.Meta.NPlugDyna_SKinematicConstraint;
Gbx.LZO=new GBX.NET.LZO.MiniLZO();
if(args.Length != 2) throw new ArgumentException("Usage: MotionCollisionRelayProof <8m Cube.Item.Gbx> <output directory>");
var output=args[1];
Directory.CreateDirectory(output);
foreach(var name in new[]{"OrbitPlatform","CompoundPlatform","HelixPlatform","RelaySquare","RelaySquareCage","RelaySquareLowPivot"}) {
 // Fixture-specific parking offset, not an arbitrary-map collision guarantee.
 var parkHeight=name.EndsWith("LowPivot")?8f:48f;
 var source=Gbx.Parse<CGameItemModel>(args[0]).Node;
 var result=ItemKinematicEntityTemplate.ConvertStaticToKinematic(source,ItemKinematicEntityTemplate.GetDefaultMovingTemplate());
 if(!result.Success)throw new Exception(result.Reason);
 var item=result.Value!;var prefab=(CPlugPrefab)item.EntityModel!;
 var body=prefab.Ents.Single(e=>e.Model is CPlugDynaObjectModel);
 var dyna=(CPlugDynaObjectModel)body.Model!;
 var kc=(KC)prefab.Ents.Single(e=>e.Model is KC).Model!;
 var ip=(NPlugDynaObjectModel_SInstanceParams)body.Params!;ip.Phase01=0;ip.Phase01Max=-1;ip.PeriodSc=1;ip.PeriodScMax=-1;
 Vec3 Transform(Vec3 p)=>name.StartsWith("RelaySquare")?new(p.X*2,p.Y*.25f+parkHeight,p.Z*2):new(p.X*2+12,p.Y*.25f,p.Z*2);
 var solid=(CPlugSolid2Model)dyna.Mesh!;
 foreach(var stream in (solid.Visuals??[]).SelectMany(v=>v.VertexStreams??[]).Distinct())
  if(stream.Positions is {} positions)for(int i=0;i<positions.Length;i++)positions[i]=Transform(positions[i]);
 foreach(var shape in new[]{dyna.StaticShape,dyna.DynaShape}.Distinct())
  if(shape?.Surf is CPlugSurface.Mesh mesh)for(int i=0;i<mesh.Vertices.Length;i++)mesh.Vertices[i]=Transform(mesh.Vertices[i]);
 kc.TransAxis=name=="HelixPlatform"?KC.EAxis.Y:KC.EAxis.X;
 kc.TransMin=0;kc.TransMax=name=="OrbitPlatform"?0:16;
 kc.TransAnimFunc=Func(Key(KC.AnimEase.QuadInOut,12000),Key(KC.AnimEase.QuadInOut,12000,true));
 kc.RotAxis=KC.EAxis.Y;kc.AngleMinDeg=0;kc.AngleMaxDeg=360;
 kc.RotAnimFunc=Func(Key(KC.AnimEase.Linear,24000));
 if(name.StartsWith("RelaySquare")) {
  System.Numerics.Vector3[] points=[new(0,0,0),new(16,0,0),new(16,0,16),new(0,0,16),new(0,0,0)];
  var entries=new List<CPlugPrefab.EntRef>();
  var models=new List<CPlugDynaObjectModel>{dyna};
  if(name.EndsWith("Cage")) for(int wall=0;wall<4;wall++) {
   var wallItem=ItemKinematicEntityTemplate.ConvertStaticToKinematic(Gbx.Parse<CGameItemModel>(args[0]).Node,ItemKinematicEntityTemplate.GetDefaultMovingTemplate()).Value!;
   var wm=(CPlugDynaObjectModel)((CPlugPrefab)wallItem.EntityModel!).Ents.Single(e=>e.Model is CPlugDynaObjectModel).Model!;
   Vec3 Wall(Vec3 p)=>wall<2?new(p.X*.125f+(wall==0?-8:8),p.Y*.5f+50,p.Z*2):new(p.X*2,p.Y*.5f+50,p.Z*.125f+(wall==2?-8:8));
   foreach(var vs in (((CPlugSolid2Model)wm.Mesh!).Visuals??[]).SelectMany(v=>v.VertexStreams??[]).Distinct())if(vs.Positions is {} ps)for(int j=0;j<ps.Length;j++)ps[j]=Wall(ps[j]);
   foreach(var sh in new[]{wm.StaticShape,wm.DynaShape}.Distinct())if(sh?.Surf is CPlugSurface.Mesh sm)for(int j=0;j<sm.Vertices.Length;j++)sm.Vertices[j]=Wall(sm.Vertices[j]);
   models.Add(wm);
  }
  for(int i=0;i<4;i++)foreach(var m in models)entries.Add(new(){Model=m,Position=new(points[i].X,-parkHeight,points[i].Z),Rotation=ItemMotionPath.AlignX(points[i+1]-points[i]),Params=ip});
  for(int i=0;i<4;i++) {
   // Each world-bound body covers exactly one leg. Constant rotation keys
   // park the inactive body below the fixture terrain. The discontinuous
   // handoff is intentionally under test: it can launch a contacting car.
   var trans=new List<KC.SubAnimFunc>();var rot=new List<KC.SubAnimFunc>();
   if(i>0){trans.Add(Key(KC.AnimEase.Constant,i*8000));rot.Add(Key(KC.AnimEase.Constant,i*8000,true));}
   trans.Add(Key(KC.AnimEase.Linear,8000));rot.Add(Key(KC.AnimEase.Constant,8000));
   if(i<3){trans.Add(Key(KC.AnimEase.Constant,(3-i)*8000,true));rot.Add(Key(KC.AnimEase.Constant,(3-i)*8000,true));}
   for(int j=0;j<models.Count;j++)entries.Add(new(){Model=new KC{Version=0,SubVersion=3,TransAxis=KC.EAxis.X,TransMin=0,TransMax=16,RotAxis=KC.EAxis.X,AngleMinDeg=0,AngleMaxDeg=180,TransAnimFunc=Func(trans.ToArray()),RotAnimFunc=Func(rot.ToArray())},Rotation=Quat.Identity,Params=new NPlugDyna_SPrefabConstraintParams{Version=0,Ent1=-1,Ent2=i*models.Count+j}});
  }
  prefab.Ents=entries.ToArray();
 }
 item.Name="TMIS_CF2_"+name;item.Ident=new(item.Name,26,item.Ident.Author);item.ArchetypeRef="";
 var path=Path.Combine(output,item.Name+".Item.Gbx");
 new Gbx<CGameItemModel>(item).Save(path);
 var parsed=Gbx.Parse<CGameItemModel>(path).Node;
 var read=((CPlugPrefab)parsed.EntityModel!).Ents.Where(e=>e.Model is KC).Select(e=>(KC)e.Model!).ToArray();
 if(read.Length!=(name=="RelaySquareCage"?20:name.StartsWith("RelaySquare")?4:1)||read.Any(c=>c.AngleMaxDeg!=(name.StartsWith("RelaySquare")?180:360)))throw new Exception("Roundtrip mismatch");
 var ents=((CPlugPrefab)parsed.EntityModel!).Ents;
 foreach(var ent in ents.Where(e=>e.Model is KC)) {
  if(ent.Params is not NPlugDyna_SPrefabConstraintParams binding || binding.Ent1!=-1 || binding.Ent2<0 || binding.Ent2>=ents.Length || ents[binding.Ent2].Model is not CPlugDynaObjectModel)
   throw new Exception("Invalid world-bound body reference after save");
 }
 foreach(var constraint in read) {
  var expectedMs=name.StartsWith("RelaySquare")?32000:24000;
  foreach(var channel in new[]{constraint.TransAnimFunc,constraint.RotAnimFunc})
   if(channel?.SubFuncs is not {} keys || keys.Length>4 || keys.Sum(k=>k.Duration.TotalMilliseconds)!=expectedMs)
    throw new Exception("Invalid native channel budget after save");
 }
 Console.WriteLine(path);
}
KC.SubAnimFunc Key(KC.AnimEase ease,int ms,bool reverse=false)=>new(){Ease=ease,Duration=new TimeInt32(ms),Reverse=reverse};
KC.AnimFunc Func(params KC.SubAnimFunc[] keys)=>new(){IsDuration=true,SubFuncs=keys};
