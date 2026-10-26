using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;
Gbx.LZO = new GBX.NET.LZO.MiniLZO();
if (args.Length != 1) throw new ArgumentException("Use <original-plane-item> for offline structural inspection");
var doc = Gbx.Parse<CGameItemModel>(args[0]);
var prefab = doc.Node.EntityModel as CPlugPrefab ?? (CPlugPrefab)((NPlugItem_SVariantList)doc.Node.EntityModel!).Variants![0].EntityModel!;
var result = new { file=Path.GetFullPath(args[0]), doc.Node.Name, sourceItemRootModelType=doc.Node.EntityModel!.GetType().Name,
    references=doc.RefTable?.Files.Select(f=>new { f.FilePath,f.NodeIndex,f.Flags,f.UseFile }),
    entities=prefab.Ents!.Select((e,i)=>new { index=i, model=e.Model!.GetType().Name,e.Position,e.Rotation,parameters=e.Params,
        constraint=e.Model is NPlugDyna_SKinematicConstraint k ? k : null,
        mesh=Mesh(e.Model) is {} m ? new { m.Materials,m.CustomMaterials,m.MaterialInsts,m.Boxes,m.ShadedGeoms,
            visuals=m.Visuals!.Cast<CPlugVisualIndexedTriangles>().Select((v,j)=>new {index=j,inlineVertices=v.Vertices.Length,subVisuals=v.SubVisuals?.Length,
                v.BoundingBox,indexCount=v.IndexBuffer?.Indices.Length,
                streams=v.VertexStreams.Select(s=>new {positionCount=s.Positions?.Length,normalCount=s.Normals?.Length,
                  min=s.Positions is {Length:>0} ? new[]{s.Positions.Min(p=>p.X),s.Positions.Min(p=>p.Y),s.Positions.Min(p=>p.Z)}:null,
                  max=s.Positions is {Length:>0} ? new[]{s.Positions.Max(p=>p.X),s.Positions.Max(p=>p.Y),s.Positions.Max(p=>p.Z)}:null}) }) }:null
    }) };
Console.WriteLine(JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
static CPlugSolid2Model? Mesh(object? model)=>model switch {CPlugStaticObjectModel s=>s.Mesh,CPlugDynaObjectModel d=>d.Mesh,_=>null};
