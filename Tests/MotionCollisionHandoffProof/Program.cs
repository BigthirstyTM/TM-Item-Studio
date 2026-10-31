using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Plug;
using GBX.NET.Engines.Meta;
using TM_Item_Studio.Models;
using TmEssentials;
using KC=GBX.NET.Engines.Meta.NPlugDyna_SKinematicConstraint;
Gbx.LZO=new GBX.NET.LZO.MiniLZO();
if(args.Length!=2)throw new ArgumentException("Usage: source-cube output-folder");
Directory.CreateDirectory(args[1]);
foreach(var name in new[]{"StraightHard","CornerHard","CornerOverlap","CornerNoFold","CornerClearance","SquareOverlap"}) {
 var conversion=ItemKinematicEntityTemplate.ConvertStaticToKinematic(Gbx.Parse<CGameItemModel>(args[0]).Node,ItemKinematicEntityTemplate.GetDefaultMovingTemplate());
 if(!conversion.Success)throw new Exception(conversion.Reason);
 var item=conversion.Value!;var prefab=(CPlugPrefab)item.EntityModel!;
 var body=prefab.Ents.Single(e=>e.Model is CPlugDynaObjectModel);var model=(CPlugDynaObjectModel)body.Model!;
 Vec3 Transform(Vec3 p)=>new(p.X*2,p.Y*.25f+48,p.Z*2);
 foreach(var stream in (((CPlugSolid2Model)model.Mesh!).Visuals??[]).SelectMany(v=>v.VertexStreams??[]).Distinct())
  if(stream.Positions is {} ps)for(int i=0;i<ps.Length;i++)ps[i]=Transform(ps[i]);
 foreach(var shape in new[]{model.StaticShape,model.DynaShape}.Distinct())
  if(shape?.Surf is CPlugSurface.Mesh mesh)for(int i=0;i<mesh.Vertices.Length;i++)mesh.Vertices[i]=Transform(mesh.Vertices[i]);
 var ip=(NPlugDynaObjectModel_SInstanceParams)body.Params!;ip.Phase01=0;ip.Phase01Max=-1;ip.PeriodSc=1;ip.PeriodScMax=-1;
 var noFold=name=="CornerNoFold";var overlap=name is "CornerOverlap" or "CornerClearance";
 var secondStart=overlap?12000:10000;
 // Overlap: incoming deck is already present, and both rest at the corner
 // for 2 seconds. Outgoing deck folds only 2 seconds after departure.
 var firstHide=name=="CornerClearance"?19000:overlap?14000:10000;
 var ents=new List<CPlugPrefab.EntRef>{
  new(){Model=model,Position=new(0,-48,0),Rotation=Quat.Identity,Params=ip},
  new(){Model=model,Position=new(16,-48,0),Rotation=name=="StraightHard"?Quat.Identity:ItemMotionPath.AlignX(new(0,0,16)),Params=ip}
 };
 for(int i=0;i<2;i++) {
  var start=i==0?2000:secondStart;
  KC.AnimFunc rot=noFold || (overlap&&i==1)?Func(Key(KC.AnimEase.Constant,120000)):
   i==0?Func(Key(KC.AnimEase.Constant,firstHide),Key(KC.AnimEase.Constant,120000-firstHide,true)):
   Func(Key(KC.AnimEase.Constant,secondStart,true),Key(KC.AnimEase.Constant,120000-secondStart));
  var c=new KC{Version=0,SubVersion=3,TransAxis=KC.EAxis.X,TransMin=0,TransMax=16,
   TransAnimFunc=Func(Key(KC.AnimEase.Constant,start),Key(KC.AnimEase.Linear,8000),Key(KC.AnimEase.Constant,120000-start-8000,true)),
   RotAxis=KC.EAxis.X,AngleMinDeg=0,AngleMaxDeg=180,RotAnimFunc=rot};
  ents.Add(new(){Model=c,Rotation=Quat.Identity,Params=new NPlugDyna_SPrefabConstraintParams{Version=0,Ent1=-1,Ent2=i}});
 }
 if(name=="SquareOverlap") {
  System.Numerics.Vector3[] points=[new(0,0,0),new(16,0,0),new(16,0,16),new(0,0,16),new(0,0,0)];
  ents.Clear();
  for(int i=0;i<4;i++)ents.Add(new(){Model=model,Position=new(points[i].X,-48,points[i].Z),Rotation=ItemMotionPath.AlignX(points[i+1]-points[i]),Params=ip});
  for(int i=0;i<4;i++) {
   var start=i*10000;var trans=new List<KC.SubAnimFunc>();
   if(i==3)trans.AddRange([Key(KC.AnimEase.Constant,2000,true),Key(KC.AnimEase.Constant,28000),Key(KC.AnimEase.Linear,8000),Key(KC.AnimEase.Constant,2000,true)]);
   else {
    if(start>0)trans.Add(Key(KC.AnimEase.Constant,start));
    trans.Add(Key(KC.AnimEase.Linear,8000));trans.Add(Key(KC.AnimEase.Constant,4000,true));
    trans.Add(Key(KC.AnimEase.Constant,40000-start-12000));
   }
   // Visible eight seconds before travel until four seconds after travel.
   // Reset translation only while hidden; split wrapped intervals explicitly.
   var begin=(start-8000+40000)%40000;var end=(start+12000)%40000;
   var cuts=new[]{0,begin,end,40000}.Distinct().Order().ToArray();var rot=new List<KC.SubAnimFunc>();
   for(int j=0;j<cuts.Length-1;j++) {
    var mid=(cuts[j]+cuts[j+1])/2;
    var visible=begin<end?mid>=begin&&mid<end:mid>=begin||mid<end;
    rot.Add(Key(KC.AnimEase.Constant,cuts[j+1]-cuts[j],!visible));
   }
   ents.Add(new(){Model=new KC{Version=0,SubVersion=3,TransAxis=KC.EAxis.X,TransMin=0,TransMax=16,TransAnimFunc=Func(trans.ToArray()),RotAxis=KC.EAxis.X,AngleMinDeg=0,AngleMaxDeg=180,RotAnimFunc=Func(rot.ToArray())},Rotation=Quat.Identity,Params=new NPlugDyna_SPrefabConstraintParams{Version=0,Ent1=-1,Ent2=i}});
  }
 }
 prefab.Ents=ents.ToArray();item.Name="TMIS_CH_"+name;item.Ident=new(item.Name,26,item.Ident.Author);item.ArchetypeRef="";
 var path=Path.Combine(args[1],item.Name+".Item.Gbx");new Gbx<CGameItemModel>(item).Save(path);
 var parsed=(CPlugPrefab)Gbx.Parse<CGameItemModel>(path).Node.EntityModel!;
 var count=name=="SquareOverlap"?4:2;var cycle=name=="SquareOverlap"?40000:120000;
 if(parsed.Ents.Length!=count*2)throw new Exception("Entity count mismatch");
 foreach(var e in parsed.Ents.Where(e=>e.Model is KC)) {
  if(e.Params is not NPlugDyna_SPrefabConstraintParams b||b.Ent1!=-1||b.Ent2<0||b.Ent2>=count)throw new Exception("Binding mismatch");
  var c=(KC)e.Model!;
  foreach(var f in new[]{c.TransAnimFunc,c.RotAnimFunc})
   if(f?.SubFuncs is not {} keys||keys.Length>4||keys.Sum(k=>k.Duration.TotalMilliseconds)!=cycle)throw new Exception("Timeline mismatch");
 }
 Console.WriteLine(path);
}
KC.SubAnimFunc Key(KC.AnimEase ease,int ms,bool reverse=false)=>new(){Ease=ease,Duration=new TimeInt32(ms),Reverse=reverse};
KC.AnimFunc Func(params KC.SubAnimFunc[] keys)=>new(){IsDuration=true,SubFuncs=keys};
