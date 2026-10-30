using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.Game;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;

// Safe context isolation: zero anchored objects, valid current-format models.
// Do not place any embedded candidate until native preload proves initialization.
internal static class FlagEmbeddedContext
{
    public static int Create(string mapPath, string seedPath, string output)
    {
        if(Directory.Exists(output))throw new IOException("Preserve previous fixture");
        Directory.CreateDirectory(output);
        var map=Gbx.Parse<CGameCtnChallenge>(mapPath,new GbxReadSettings {SkipChunkIds=[0x0304305B]});
        map.Node.AnchoredObjects!.Clear();
        map.Node.MapName="TMIS Embedded Context Empty";
        map.Node.MapUid="TMIS_Embedded_Context_20261022";
        map.Node.EmbeddedZipData=null;
        var rows=new List<object>();
        var files=new List<(string Path,byte[] Bytes)>();
        foreach(var (mode,ancestor,path) in new[] {
            ("RootGameData",0,@"GameData\Stadium\Media\Material\ItemFlag.Material.Gbx"),
            ("ParentGameData",1,@"GameData\Stadium\Media\Material\ItemFlag.Material.Gbx"),
            ("ParentMedia",1,@"Media\Material\ItemFlag.Material.Gbx")})
        {
            var item=Gbx.Parse<CGameItemModel>(seedPath).Node;
            var name="TMIS_EmbeddedContext_"+mode+".Item.Gbx";
            item.Name="TMIS Embedded Context "+mode;
            item.Ident=item.Ident! with {Id=name};
            var mesh=((CPlugDynaObjectModel)((CPlugPrefab)((NPlugItem_SVariantList)item.EntityModel!).Variants![0].EntityModel!).Ents![1].Model!).Mesh!;
            var refs=new GbxRefTable {AncestorLevel=ancestor};
            mesh.Materials=new External<CPlugMaterial>[] { new External<CPlugMaterial>(null,new GbxRefTableFile(refs,1,false,path)) };
            using var stream=new MemoryStream();
            new ReferenceTableDocument(item,refs).Save(stream);
            var bytes=stream.ToArray();
            Gbx.Parse<CGameItemModel>(new MemoryStream(bytes));
            var target=Path.Combine(output,name);File.WriteAllBytes(target,bytes);
            files.Add(("Items/"+name,bytes));
            rows.Add(new {mode,path,ancestor,file=target,zipPath="Items/"+name,sha256=Convert.ToHexString(SHA256.HashData(bytes))});
        }
        map.Node.UpdateEmbeddedZipData(zip=> {
            foreach(var file in files) {
                using var dest=zip.CreateEntry(file.Path,CompressionLevel.Optimal).Open();
                dest.Write(file.Bytes);
            }
        });
        var targetMap=Path.Combine(output,"TMIS_EmbeddedContext_Empty.Map.Gbx");
        map.Save(targetMap);
        var parsed=Gbx.Parse<CGameCtnChallenge>(targetMap,new GbxReadSettings {SkipChunkIds=[0x0304305B]});
        if(parsed.Node.AnchoredObjects!.Count!=0)throw new Exception("Preload test map must be empty");
        File.WriteAllText(Path.Combine(output,"manifest.json"),JsonSerializer.Serialize(new {
            map=targetMap,sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(targetMap))),
            anchors=0,embeddedBytes=parsed.Node.EmbeddedZipData!.Length,items=rows,
            scope="Preload-only empty-map embedded-context test; do not place until structured initialization passes"
        },new JsonSerializerOptions {WriteIndented=true}));
        return 0;
    }
}
