using System.Numerics;
using System.Reflection;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Plug;
using TM_Item_Studio.Models;
using KC=GBX.NET.Engines.Meta.NPlugDyna_SKinematicConstraint;

// Individually editable, procedural demonstration models. No external model assets.
static class TemplateGallery {
 static T Clone<T>(T v) where T:GBX.NET.Engines.MwFoundations.CMwNod,new(){using var s=new MemoryStream();new Gbx<T>(v).Save(s);s.Position=0;return Gbx.Parse<T>(s).Node;}
 sealed class Mesh {
  public List<Vec3> P=[],N=[];public List<int> I=[];
  public void Box(float x,float y,float z,float a,float b,float c){
   var v=new Vector3[]{new(-a,-b,-c),new(a,-b,-c),new(a,b,-c),new(-a,b,-c),new(-a,-b,c),new(a,-b,c),new(a,b,c),new(-a,b,c)};
   foreach(var f in new[]{new[]{0,3,2,1},new[]{4,5,6,7},new[]{0,4,7,3},new[]{1,2,6,5},new[]{0,1,5,4},new[]{3,7,6,2}}){var n=Vector3.Normalize(Vector3.Cross(v[f[1]]-v[f[0]],v[f[2]]-v[f[0]]));int offset=P.Count;foreach(int i in f){var p=v[i]+new Vector3(x,y,z);P.Add(new(p.X,p.Y,p.Z));N.Add(new(n.X,n.Y,n.Z));}I.AddRange([offset,offset+1,offset+2,offset,offset+2,offset+3]);}
  }
  public void Wheel(float x,float y,float z,float r){for(int i=0;i<12;i++){float angle=i*MathF.PI/6;Box(x+r*.7f*MathF.Cos(angle),y+r*.7f*MathF.Sin(angle),z,r*.24f,r*.24f,.18f);}}
 }
 public static void Run(string sourceFile,string output){
  Directory.CreateDirectory(output);Directory.CreateDirectory(Path.Combine(output,"Sources"));Directory.CreateDirectory(Path.Combine(output,"Templates"));
  var original=Gbx.Parse<CGameItemModel>(sourceFile).Node;var entries=new List<object>();
  foreach(int index in Enumerable.Range(0,14)){
   var kind=(MotionBehaviorKind)Math.Min(index,12);string label=index==13?"Custom":kind.ToString();
   var item=Clone(original);var source=(CPlugPrefab)item.EntityModel!;var body=(CPlugDynaObjectModel)source.Ents.First(e=>e.Model is CPlugDynaObjectModel).Model!;var solid=(CPlugSolid2Model)body.Mesh!;
   var meshes=new List<Mesh>{new()};var parts=new List<MotionPathPart>();Mesh root=meshes[0];
   void Box(float x,float y,float z,float a,float b,float c)=>root.Box(x,y,z,a,b,c);
   Mesh Part(Vector3 pivot,KC.EAxis axis,MotionPartKind mode=MotionPartKind.Wing,int period=800,bool reverse=false,float min=-25,float max=25){var m=new Mesh();parts.Add(new(meshes.Count,mode,pivot,axis,min,max,period,.65f,reverse));meshes.Add(m);return m;}
   switch(kind){
    case MotionBehaviorKind.BirdFlight:case MotionBehaviorKind.Butterfly:case MotionBehaviorKind.Fish:
     Box(0,0,0,1.5f,.45f,.45f);Box(1.6f,.1f,0,.45f,.4f,.4f);
     if(kind==MotionBehaviorKind.Fish){Part(new(-1.5f,0,0),KC.EAxis.Y,period:600).Box(-2.1f,0,0,.6f,.8f,.12f);Box(0,.7f,0,.4f,.4f,.1f);}
     else {int period=kind==MotionBehaviorKind.Butterfly?240:850;foreach(int sign in new[]{-1,1})Part(new(0,0,.3f*sign),KC.EAxis.X,period:period,reverse:sign<0,min:-15,max:55).Box(-.2f,0,sign*1.7f,kind==MotionBehaviorKind.Butterfly?1.4f:1,.12f,1.4f);}
     break;
    case MotionBehaviorKind.Drone:
     Box(0,0,0,1,.5f,1);foreach(int x in new[]{-1,1})foreach(int z in new[]{-1,1}){Box(x,0,z,1.2f,.12f,.12f);Part(new(x*2,.4f,z*2),KC.EAxis.Y,MotionPartKind.Propeller,period:350).Box(x*2,.4f,z*2,1.1f,.08f,.18f);}break;
    case MotionBehaviorKind.Robot:
     Box(0,2,0,.65f,1,.8f);Box(0,3.4f,0,.55f,.45f,.55f);foreach(int sign in new[]{-1,1}){Part(new(0,1,sign*.5f),KC.EAxis.Z,period:900,reverse:sign<0).Box(0,0,sign*.5f,.25f,1,.25f);Part(new(0,2.7f,sign),KC.EAxis.Z,period:900,reverse:sign>0).Box(0,1.8f,sign,.2f,.9f,.2f);}break;
    case MotionBehaviorKind.LiftPlatform:
     Box(0,0,0,3,.25f,3);foreach(int z in new[]{-3,3}){Box(0,1.5f,z,3,.12f,.12f);foreach(int x in new[]{-3,3})Box(x,.8f,z,.1f,.8f,.1f);}break;
    case MotionBehaviorKind.CableCar:
     Box(0,0,0,2,.2f,1.5f);Box(0,2.5f,0,2,.2f,1.5f);foreach(int x in new[]{-2,2})foreach(int z in new[]{-1,1})Box(x,1.25f,z*1.5f,.1f,1.25f,.1f);Box(0,3.5f,0,.15f,1,.15f);break;
    case MotionBehaviorKind.Boat:
     Box(0,0,0,3,.4f,1.4f);Box(1,.6f,0,1.2f,.3f,1.2f);Box(-1,1.3f,0,.8f,.9f,.9f);break;
    case MotionBehaviorKind.Ride:
     Box(0,0,0,2,.4f,2);Box(0,1.5f,0,.2f,1.5f,.2f);Box(0,3,0,3,.2f,3);foreach(int z in new[]{-1,1})Box(0,.8f,z*1.5f,1.3f,.25f,.4f);break;
    case MotionBehaviorKind.Conveyor:
     Box(0,1,0,1.3f,1,1.3f);Box(0,2.05f,0,.15f,.08f,1.35f);break;
    default:
     Box(0,1,0,2.6f,.4f,1.4f);Box(-.4f,2,0,1.2f,.65f,1.1f);if(kind==MotionBehaviorKind.Train){Box(1.7f,2,0,.6f,.8f,1.2f);Box(1.5f,3,0,.2f,.5f,.2f);}
     foreach(int x in new[]{-1,1})foreach(int z in new[]{-1,1})Part(new(x*1.6f,.65f,z*1.5f),KC.EAxis.Z,MotionPartKind.Wheel).Wheel(x*1.6f,.65f,z*1.5f,.65f);break;
   }
   var template=(CPlugVisualIndexedTriangles)solid.Visuals![0];var materials=solid.CustomMaterials!;
   solid.Visuals=new(meshes.Select(m=>{
    var v=Clone(template);var st=Clone(template.VertexStreams[0]);foreach(var d in st.DataDecls)d.Data=null;
    st.Positions=m.P.ToArray();st.Normals=m.N.ToArray();st.VertexCount=m.P.Count;foreach(var key in st.UVs.Keys.ToArray())st.UVs[key]=m.P.Select(p=>new Vec2(p.X,p.Z)).ToArray();
    st.TangentUs=m.N.Select(n=>{var a=new Vector3(n.X,n.Y,n.Z);var t=Vector3.Normalize(Vector3.Cross(a,Math.Abs(a.Y)<.9f?Vector3.UnitY:Vector3.UnitZ));return new Vec3(t.X,t.Y,t.Z);}).ToArray();
    st.TangentVs=m.N.Select((n,i)=>{var u=st.TangentUs[i];var a=Vector3.Cross(new(n.X,n.Y,n.Z),new(u.X,u.Y,u.Z));return new Vec3(a.X,a.Y,a.Z);}).ToArray();
    v.VertexStreams=[st];v.IndexBuffer=new(){Indices=m.I.ToArray()};v.IndexBuffer.CreateChunk<CPlugIndexBuffer.Chunk09057000>();v.BoundingBox=new(0,1,0,6,6,6);typeof(CPlugVisual).GetProperty("Count",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(v,st.VertexCount);return (CPlugVisual)v;
   }));
   solid.ShadedGeoms=meshes.Select((m,i)=>new CPlugSolid2Model.ShadedGeom{VisualIndex=i,MaterialIndex=i==0?index%materials.Length:Math.Min(2,materials.Length-1),LodMask=1}).ToArray();
   var carrier=ItemKinematicEntityTemplate.CreateCarrierBody();body.DynaShape=carrier.DynaShape;body.StaticShape=carrier.StaticShape;
   void Save(string name,string folder){item.Name=name;item.Ident=new(name,26,item.Ident.Author);item.ArchetypeRef="";new Gbx<CGameItemModel>(item).Save(Path.Combine(folder,name+".Item.Gbx"));}
   Save("TMIS_Demo_"+label+"_Source",Path.Combine(output,"Sources"));
   Vector3[] points=Enumerable.Range(0,32).Select(i=>new Vector3(11*MathF.Cos(i*MathF.PI/16),6+2*MathF.Sin(i*MathF.PI/8),11*MathF.Sin(i*MathF.PI/16))).ToArray();
   if(kind is MotionBehaviorKind.LiftPlatform)points=[new(0,1,0),new(0,16,0)];
   if(kind is MotionBehaviorKind.Drone)points=[new(-8,5,-8),new(8,12,-8),new(8,7,8),new(-8,15,8)];
   if(kind is MotionBehaviorKind.Conveyor)points=[new(-8,1,-6),new(8,1,-6),new(8,1,6),new(-8,1,6)];
   var p=ItemMotionPath.Plan(points,MotionPathUse.Scenery,MotionPathRepeat.Closed,4,KC.AnimEase.Linear).Value!;
   var result=ItemMotionBehavior.Apply(p,ItemMotionBehavior.Defaults(kind));if(!result.Success)throw new Exception(label+": "+result.Reason);
   p=result.Value! with{Parts=parts.ToArray()};
   if(index==13){var custom=new MotionCustomTemplate("Showcase custom","Slow spinning workpiece","Showcase",p with{Rotations=p.Rotations!.Select((r,i)=>new MotionPathRotation(360f*i/p.Legs.Count,0,0)).ToArray()},IncludeRoute:true);p=ItemMotionCustomTemplate.Apply(custom,p).Value!;File.WriteAllText(Path.Combine(output,"Templates","Custom.motion-template.json"),JsonSerializer.Serialize(custom,ItemMotionCustomTemplate.Json));}
   var built=ItemMotionPath.Build(source,p);if(!built.Success)throw new Exception(label+": "+built.Reason);item.EntityModel=built.Value;
   string id="TMIS_Demo_"+label;Save(id,output);var read=Gbx.Parse<CGameItemModel>(Path.Combine(output,id+".Item.Gbx")).Node;FlightScene.Verify(Path.Combine(output,id+".Item.Gbx"));
   File.WriteAllText(Path.Combine(output,"Templates",label+".motion-path.json"),JsonSerializer.Serialize(p,ItemMotionCustomTemplate.Json));
   entries.Add(new{label,item=id+".Item.Gbx",x=240+index%5*60,y=8,z=240+index/5*60,durationMs=ItemMotionPathProfile.Compile(p).Value!.DurationMs,parts=parts.Count});
  }
  File.WriteAllText(Path.Combine(output,"gallery.json"),JsonSerializer.Serialize(entries,new JsonSerializerOptions{WriteIndented=true}));
 }
}
