using System.Numerics;
using System.Reflection;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Plug;
using TM_Item_Studio.Models;
using KC=GBX.NET.Engines.Meta.NPlugDyna_SKinematicConstraint;
static class CartBuilder {
 public static void Run(string cube,string meshFile,string frameFile,string output) {
  Directory.CreateDirectory(output);
  var converted=ItemKinematicEntityTemplate.ConvertStaticToKinematic(Gbx.Parse<CGameItemModel>(cube).Node,ItemKinematicEntityTemplate.GetDefaultMovingTemplate());
  if(!converted.Success)throw new Exception(converted.Reason);
  var item=converted.Value!;var source=(CPlugPrefab)item.EntityModel!;
  var body=(CPlugDynaObjectModel)source.Ents.First(e=>e.Model is CPlugDynaObjectModel).Model!;
  var solid=(CPlugSolid2Model)body.Mesh!;
  var shadedTemplate=solid.ShadedGeoms![0];
  Console.WriteLine($"Shaded template: {shadedTemplate.U01}, {shadedTemplate.LodMask}, {shadedTemplate.U02}");
  var templateVisual=(CPlugVisualIndexedTriangles)solid.Visuals![0];
  Console.WriteLine("Template visual chunks: "+string.Join(",",templateVisual.Chunks.Select(c=>c.Id.ToString("X8"))));
  foreach(var s in templateVisual.VertexStreams)Console.WriteLine("Declarations: "+string.Join(";",s.DataDecls.Select(d=>$"{d.Semantic} {d.Type} {d.Space} {d.Stride}")));
  var visuals=new List<CPlugVisual>();var materials=new List<CPlugSolid2Model.Material>();
  var groups=JsonDocument.Parse(File.ReadAllText(meshFile)).RootElement;
  const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
  foreach(var group in groups.EnumerateArray()) {
   float[] Floats(string key)=>group.GetProperty(key).EnumerateArray().Select(x=>x.GetSingle()).ToArray();
   Vec3[] Vectors(string key)=>Floats(key).Chunk(3).Select(x=>new Vec3(x[0],x[1],x[2])).ToArray();
   using var streamBytes=new MemoryStream();new Gbx<CPlugVertexStream>(templateVisual.VertexStreams[0]).Save(streamBytes);streamBytes.Position=0;
   var stream=Gbx.Parse<CPlugVertexStream>(streamBytes).Node;
   foreach(var declaration in stream.DataDecls)declaration.Data=null;
   stream.Positions=Vectors("positions");stream.Normals=Vectors("normals");stream.VertexCount=stream.Positions.Length;
   var uv=Floats("uvs").Chunk(2).Select(x=>new Vec2(x[0],x[1])).ToArray();
   foreach(var key in stream.UVs.Keys.ToArray())stream.UVs[key]=uv.ToArray();
   stream.TangentUs=stream.Normals.Select(n=>{var v=new Vector3(n.X,n.Y,n.Z);var t=Vector3.Normalize(Vector3.Cross(v,Math.Abs(v.Y)<.9f?Vector3.UnitY:Vector3.UnitZ));return new Vec3(t.X,t.Y,t.Z);}).ToArray();
   stream.TangentVs=stream.Normals.Select((n,i)=>{var u=stream.TangentUs[i];var v=Vector3.Cross(new(n.X,n.Y,n.Z),new(u.X,u.Y,u.Z));return new Vec3(v.X,v.Y,v.Z);}).ToArray();
   var ib=new CPlugIndexBuffer{Indices=group.GetProperty("indices").EnumerateArray().Select(x=>x.GetInt32()).ToArray()};ib.CreateChunk<CPlugIndexBuffer.Chunk09057000>();
   using var visualBytes=new MemoryStream();new Gbx<CPlugVisualIndexedTriangles>(templateVisual).Save(visualBytes);visualBytes.Position=0;
   var visual=Gbx.Parse<CPlugVisualIndexedTriangles>(visualBytes).Node;
   visual.VertexStreams=[stream];visual.IndexBuffer=ib;visual.BoundingBox=new(0,1.5f,0,3.5f,1.6f,4.3f);
   typeof(CPlugVisual).GetProperty("Count",flags)!.SetValue(visual,stream.VertexCount);
   visual.CreateChunk<CPlugVisual.Chunk0900600F>().Version=6;visual.CreateChunk<CPlugVisualIndexed.Chunk0906A001>();visuals.Add(visual);
   using var materialBytes=new MemoryStream();new Gbx<CPlugMaterialUserInst>(solid.CustomMaterials![0].MaterialUserInst!).Save(materialBytes);materialBytes.Position=0;
   var material=Gbx.Parse<CPlugMaterialUserInst>(materialBytes).Node;
   material.Link="Stadium\\Media\\Material\\"+(materials.Count==0?"RoadBump":materials.Count==1?"PlatformTech":"RoadTech");
   materials.Add(new(){MaterialUserInst=material});
  }
  solid.Visuals=new(visuals);solid.CustomMaterials=materials.ToArray();solid.ShadedGeoms=Enumerable.Range(0,visuals.Count).Select(i=>new CPlugSolid2Model.ShadedGeom {VisualIndex=i,MaterialIndex=i,LodMask=shadedTemplate.LodMask,U01=shadedTemplate.U01,U02=shadedTemplate.U02}).ToArray();
  // A simple chassis collider, deliberately separate from the visual seats/wheels.
  foreach(var sh in new[]{body.StaticShape,body.DynaShape}.Distinct()) {
   var m=(CPlugSurface.Mesh)sh!.Surf!;
   for(int i=0;i<m.Vertices.Length;i++){var v=m.Vertices[i];m.Vertices[i]=new(v.X/8*6.5f,v.Y/8*.5f+.8f,v.Z/8*7.4f);}
  }
  void Save(string name){item.Name=name;item.Ident=new(name,26,item.Ident.Author);item.ArchetypeRef="";new Gbx<CGameItemModel>(item).Save(Path.Combine(output,name+".Item.Gbx"));}
  foreach(var c in source.Ents.Select(e=>e.Model).OfType<KC>()){c.TransMin=c.TransMax=0;c.AngleMinDeg=c.AngleMaxDeg=0;}
  Save("TMIS_Cart_Source");
  var frames=JsonDocument.Parse(File.ReadAllText(frameFile)).RootElement.GetProperty("frames").EnumerateArray().ToArray();
  Vector3 Vec(JsonElement e,string key){var a=e.GetProperty(key).EnumerateArray().Select(v=>v.GetSingle()).ToArray();return new(a[0],a[1],a[2]);}
  var points=frames.Select(f=>Vec(f,"point")).ToArray();var origin=points[0];points=points.Select(p=>p-origin-new Vector3(0,4.2f,0)).ToArray();
  var plan=ItemMotionPath.Plan(points,MotionPathUse.CollisionRelay,MotionPathRepeat.Closed,5,KC.AnimEase.Linear,0,8000).Value!;
  var result=ItemMotionPath.Build(source,plan);if(!result.Success)throw new Exception(result.Reason);
  var prefab=result.Value!;var bodies=prefab.Ents.Where(e=>e.Model is CPlugDynaObjectModel).ToArray();
  for(int i=0;i<bodies.Length;i++) {
   var ent=bodies[i];var q=new Quaternion(ent.Rotation.X,ent.Rotation.Y,ent.Rotation.Z,ent.Rotation.W);var inverse=Quaternion.Inverse(q);
   var f=Vec(frames[i],"forward");var u=Vec(frames[i],"up");var z=Vec(frames[i],"side");
   var target=Quaternion.CreateFromRotationMatrix(new Matrix4x4(f.X,f.Y,f.Z,0,u.X,u.Y,u.Z,0,z.X,z.Y,z.Z,0,0,0,0,1));
   var model=(CPlugDynaObjectModel)ent.Model!;var seen=new HashSet<Vec3[]>();
   void Transform(Vec3[]? a,bool position){if(a==null||!seen.Add(a))return;for(int j=0;j<a.Length;j++){var p=new Vector3(a[j].X,a[j].Y,a[j].Z);if(position)p-=new Vector3(0,64,0);p=Vector3.Transform(Vector3.Transform(Vector3.Transform(p,q),target),inverse);if(position)p+=new Vector3(0,64,0);a[j]=new(p.X,p.Y,p.Z);}}
   foreach(var v in ((CPlugSolid2Model)model.Mesh!).Visuals!) {
    foreach(var s in v.VertexStreams){Transform(s.Positions,true);Transform(s.Normals,false);}
    var ps=v.VertexStreams.SelectMany(s=>s.Positions??[]).Select(p=>new Vector3(p.X,p.Y,p.Z)).ToArray();var min=ps.Aggregate(Vector3.Min);var max=ps.Aggregate(Vector3.Max);var c=(min+max)/2;var e=(max-min)/2+new Vector3(.001f);v.BoundingBox=new(c.X,c.Y,c.Z,e.X,e.Y,e.Z);
   }
   foreach(var sh in new[]{model.StaticShape,model.DynaShape}.Distinct())Transform(((CPlugSurface.Mesh)sh!.Surf!).Vertices,true);
  }
  item.EntityModel=prefab;Save("TMIS_Cart_Banked");
  var read=Gbx.Parse<CGameItemModel>(Path.Combine(output,"TMIS_Cart_Banked.Item.Gbx")).Node;
  if(((CPlugPrefab)read.EntityModel!).Ents.Length!=256)throw new Exception("Cart graph changed");
  Console.WriteLine("PASS custom four-seat cart, 128 pitch/bank-aligned collision segments; native test required");
 }
}
