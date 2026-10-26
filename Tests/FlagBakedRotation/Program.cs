using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;

Gbx.LZO = new GBX.NET.LZO.MiniLZO();
if (args.Length == 2 && args[0] == "--generate") { Fixture.Generate(args[1]); return; }
if (args.Length != 1) throw new ArgumentException("Use --generate <new-output-directory> or <item-path> for offline inspection");
var document = Gbx.Parse<CGameItemModel>(args[0]);
var prefab = (CPlugPrefab)((NPlugItem_SVariantList)document.Node.EntityModel!).Variants![0].EntityModel!;
Console.WriteLine(JsonSerializer.Serialize(new { document.Node.Name, entities = prefab.Ents!.Select(e => new {
    model = e.Model!.GetType().Name, e.Position, e.Rotation, parameters=e.Params,
    mesh = Mesh(e.Model) is {} m ? new {
        m.Materials, m.CustomMaterials, m.MaterialInsts, m.Boxes,
        visuals=m.Visuals!.Cast<CPlugVisualIndexedTriangles>().Select(v=>new {
            vertexCount=v.Vertices.Length, v.BoundingBox,
            tangents=v.Tangents?.Length, bitangents=v.BiTangents?.Length,
            tangentChunks=v.Chunks.OfType<CPlugVisual3D.Chunk0902C004>().Select(c=>new {c.Tangents1Count,c.Tangents2Count}),
            firstLastFrameMaxPositionDistance = v.SubVisuals is {Length:>1} ? FrameDistance(v) : (double?)null,
            firstLastFrameMaxNormalDistance = v.SubVisuals is {Length:>1} ? FrameNormalDistance(v) : (double?)null,
            subVisualCount=v.SubVisuals?.Length, sampleSubVisuals=v.SubVisuals?.Take(3),
            indexCount=v.IndexBuffer?.Indices.Length,
            streams=v.VertexStreams.Select(s=>new {positionCount=s.Positions?.Length,normalCount=s.Normals?.Length, uvCounts=s.UVs.Select(u=>new{slot=u.Key,count=u.Value.Length})}),
            min=v.Vertices.Length>0 ? new[]{v.Vertices.Min(x=>x.Position.X),v.Vertices.Min(x=>x.Position.Y),v.Vertices.Min(x=>x.Position.Z)} : null,
            max=v.Vertices.Length>0 ? new[]{v.Vertices.Max(x=>x.Position.X),v.Vertices.Max(x=>x.Position.Y),v.Vertices.Max(x=>x.Position.Z)} : null,
            samples=v.Vertices.Take(3)
        }) } : null
}) }, new JsonSerializerOptions {WriteIndented=true,IncludeFields=true}));
static CPlugSolid2Model? Mesh(object? model)=>model switch {CPlugStaticObjectModel s=>s.Mesh,CPlugDynaObjectModel d=>d.Mesh,_=>null};
static double FrameDistance(CPlugVisualIndexedTriangles v) { var n=v.Vertices.Length/v.SubVisuals!.Length;return Enumerable.Range(0,n).Max(i=>{var a=v.Vertices[i].Position;var b=v.Vertices[v.Vertices.Length-n+i].Position;return Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Y-b.Y,2)+Math.Pow(a.Z-b.Z,2));}); }
static double FrameNormalDistance(CPlugVisualIndexedTriangles v) { var n=v.Vertices.Length/v.SubVisuals!.Length;return Enumerable.Range(0,n).Max(i=>{var a=v.Vertices[i].Normal!.Value;var b=v.Vertices[v.Vertices.Length-n+i].Normal!.Value;return Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Y-b.Y,2)+Math.Pow(a.Z-b.Z,2));}); }
