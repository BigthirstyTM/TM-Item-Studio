using System.Security.Cryptography;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;

// Address-free native-loader probes. Never place a candidate until the native
// effective material and all five structured visuals have been validated.
internal static class FlagStandaloneRoutes
{
    static CPlugSolid2Model Mesh(CGameItemModel item) => ((CPlugDynaObjectModel)
        ((CPlugPrefab)((NPlugItem_SVariantList)item.EntityModel!).Variants![0].EntityModel!).Ents![1].Model!).Mesh!;

    public static int Create(string seed, string relativeShaderSeed, string folder)
    {
        Directory.CreateDirectory(folder);
        var manifest=new List<object>();
        foreach(var (mode,name) in new[] {
            ("NamedBare", "ItemFlag"),
            ("NamedStadium", "Stadium\\Media\\Material\\ItemFlag"),
            ("NamedGameData", "GameData\\Stadium\\Media\\Material\\ItemFlag.Material.Gbx") })
        {
            var item=Gbx.Parse<CGameItemModel>(seed).Node;
            var mesh=Mesh(item);
            mesh.Materials=[];mesh.MaterialIds=[];mesh.MaterialInsts=null;
            mesh.CustomMaterials=[new CPlugSolid2Model.Material {MaterialName=name}];
            var path=Path.Combine(folder,"TMIS_Route_"+mode+".Item.Gbx");
            if(File.Exists(path))throw new IOException("Preserve previous fixture: "+path);
            new Gbx<CGameItemModel>(item).Save(path);
            Gbx.Parse<CGameItemModel>(path);
            manifest.Add(new {mode,path,materialName=name,scope="preload-only until native initialization passes"});
        }
        foreach(var ancestor in new[] {1,2,3,4})
        {
            var doc=Gbx.Parse<CGameItemModel>(relativeShaderSeed);
            doc.RefTable!.AncestorLevel=ancestor;
            var path=Path.Combine(folder,"TMIS_Route_ShaderAncestor"+ancestor+".Item.Gbx");
            if(File.Exists(path))throw new IOException("Preserve previous fixture: "+path);
            new ReferenceTableDocument(doc.Node,doc.RefTable).Save(path);
            Gbx.Parse<CGameItemModel>(path);
            manifest.Add(new {mode="ShaderAncestor"+ancestor,path,ancestor,scope="preload-only until native initialization passes"});
        }
        File.WriteAllText(Path.Combine(folder,"manifest.json"),JsonSerializer.Serialize(manifest,new JsonSerializerOptions {WriteIndented=true}));
        Console.WriteLine(JsonSerializer.Serialize(manifest));
        return 0;
    }

    public static int Stats(string[] paths)
    {
        var rows=paths.Select(path=> {
            var mesh=Mesh(Gbx.Parse<CGameItemModel>(path).Node);
            var visuals=mesh.Visuals!.OfType<CPlugVisualIndexedTriangles>().Select((v,lod)=> {
                var positions=v.Vertices.Select(x=>x.Position).Concat(v.VertexStreams.SelectMany(s=>s.Positions??[])).ToArray();
                using var memory=new MemoryStream();using var writer=new BinaryWriter(memory);
                foreach(var p in positions) {writer.Write(p.X);writer.Write(p.Y);writer.Write(p.Z);}
                return new {lod,positions=positions.Length,frames=v.SubVisuals?.Length,
                    minX=positions.Min(x=>x.X),maxX=positions.Max(x=>x.X),minY=positions.Min(x=>x.Y),maxY=positions.Max(x=>x.Y),
                    positionSha256=Convert.ToHexString(SHA256.HashData(memory.ToArray()))};
            }).ToArray();
            return new {path,visuals};
        }).ToArray();
        Console.WriteLine(JsonSerializer.Serialize(rows,new JsonSerializerOptions {WriteIndented=true}));
        return 0;
    }
}
