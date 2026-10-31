using System.Numerics;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Plug;
using TM_Item_Studio.Models;
using KC=GBX.NET.Engines.Meta.NPlugDyna_SKinematicConstraint;
Gbx.LZO=new GBX.NET.LZO.MiniLZO();
if(args.Length==1) {
 var item=Gbx.Parse<CGameItemModel>(args[0]).Node;
 var prefab=(CPlugPrefab)item.EntityModel!;
 var bodies=prefab.Ents.Count(e=>e.Model is CPlugDynaObjectModel);
 var drivers=prefab.Ents.Where(e=>e.Model is KC).ToArray();
 if(bodies==0 || drivers.Length!=bodies)throw new Exception("Missing relay bodies/drivers");
 foreach(var entry in drivers) {
  if(entry.Params is not GBX.NET.Engines.Meta.NPlugDyna_SPrefabConstraintParams b || b.Ent1!=-1 || b.Ent2<0 || b.Ent2>=bodies)throw new Exception("Invalid binding");
  var c=(KC)entry.Model!;
  if(c.TransAnimFunc!.SubFuncs!.Length>4 || c.RotAnimFunc!.SubFuncs!.Length>4)throw new Exception("Native key limit exceeded");
 }
 Console.WriteLine($"PASS browser export reparsed: {bodies} collision bodies and {drivers.Length} world-bound drivers");return;
}
if(args.Length!=3) throw new ArgumentException("Usage: source.Item.Gbx route.json output-directory");
var points=JsonSerializer.Deserialize<float[][]>(File.ReadAllText(args[1]))!.Select(p=>new Vector3(p[0],p[1],p[2])).ToArray();
var origin=points[0]; points=points.Select(p=>p-origin).ToArray();
Directory.CreateDirectory(args[2]);
foreach(var ease in new[]{KC.AnimEase.Linear,KC.AnimEase.QuadInOut}) {
 var source=Gbx.Parse<CGameItemModel>(args[0]).Node;
 var conversion=ItemKinematicEntityTemplate.ConvertStaticToKinematic(source,ItemKinematicEntityTemplate.GetDefaultMovingTemplate());
 if(!conversion.Success)throw new Exception(conversion.Reason);
 var item=conversion.Value!;
 var plan=ItemMotionPath.Plan(points,MotionPathUse.CollisionRelay,MotionPathRepeat.Closed,5,ease,0,8000);
 if(!plan.Success)throw new Exception(plan.Reason);
 var result=ItemMotionPath.Build((CPlugPrefab)item.EntityModel!,plan.Value!);
 if(!result.Success)throw new Exception(result.Reason);
 item.EntityModel=result.Value; item.Name="TMIS_CE_"+ease;item.Ident=new(item.Name,26,item.Ident.Author);item.ArchetypeRef="";
 var file=Path.Combine(args[2],item.Name+".Item.Gbx");new Gbx<CGameItemModel>(item).Save(file);
 var roundtrip=Gbx.Parse<CGameItemModel>(file).Node;
 if(((CPlugPrefab)roundtrip.EntityModel!).Ents.Length != points.Length*2)throw new Exception("Entity count changed after export");
 Console.WriteLine($"PASS {file}: {plan.Value!.Legs.Count} segments, {plan.Value.DurationMs} ms");
}
