using System.Security.Cryptography;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Function;
using GBX.NET.Engines.Plug;

// Serialization-only probes. No candidate is written, staged or passed to game.
internal static class LegacyGraphAudit
{
    public static void Run(string sourcePath,string output)
    {
        var bytes=File.ReadAllBytes(sourcePath);
        Require(Hash(bytes)=="7EEDD88D3417A59F08724F6755EE665ADC35CAF7CE2E46AEFC554C5443EE2FB6","Unexpected original source Item");
        Require(!Directory.Exists(output)&&!File.Exists(output),"Preserve earlier evidence");
        var doc=Gbx.Parse<CGameItemModel>(new MemoryStream(bytes));
        var prefab=(CPlugPrefab)doc.Node.EntityModel!;
        var mesh=((CPlugDynaObjectModel)prefab.Ents![0].Model!).Mesh!;
        var itemChunk=doc.Node.Chunks.OfType<CGameItemModel.Chunk2E002019>().Single();
        var rotation=new CFuncTreeRotate{Axis=new(0,1,0),AngleMin=0,AngleMax=360,Period=20,Phase=0,AutoCreateMotion=true,RandomizePhase=false};
        rotation.CreateChunk<CFuncPlug.Chunk0500B004>();rotation.CreateChunk<CFuncTreeRotate.Chunk0501E001>();
        var tree=new CPlugTree{Visual=mesh.Visuals![0],FuncTree=rotation};tree.CreateChunk<CPlugTree.Chunk0904F006>();tree.CreateChunk<CPlugTree.Chunk0904F016>();tree.CreateChunk<CPlugTree.Chunk0904F011>();
        var legacy=new CPlugSolid{Tree=tree};legacy.CreateChunk<CPlugSolid.Chunk09005011>();
        var vis=new CGameObjectVisModel{Mesh="",SolidRef="",Solid=legacy,MeshShaded=mesh};
        vis.CreateChunk<CGameObjectVisModel.Chunk2E007001>().Version=14;
        var standaloneBytes=Save(vis);
        var standalone=Gbx.Parse<CGameObjectVisModel>(new MemoryStream(standaloneBytes)).Node;
        Require(standalone.Solid?.Tree is CPlugTree&&standalone.MeshShaded is not null,"Independent dual host failed offline serialization");
        doc.Node.VisModelCustom=vis;
        var modified=Save(doc.Node);
        var round=Gbx.Parse<CGameItemModel>(new MemoryStream(modified)).Node;
        var sourceCanonical=Save(Gbx.Parse<CGameItemModel>(new MemoryStream(bytes)).Node);
        var manifest=new {
            scope="In-memory serialization only. No GBX candidate emitted or game operation performed.",
            sourcePath=Path.GetFullPath(sourcePath),sourceSha256=Hash(bytes),
            sourceItemType=doc.Node.ItemType.ToString(),sourceItemChunkVersion=itemChunk.Version,
            sourceEntityModel=prefab.GetType().Name,sourceVisualCount=mesh.Visuals!.Length,
            sourceMaterialCount=mesh.CustomMaterials!.Length,
            remainingRigidVisualCount=mesh.ShadedGeoms!.Count(g=>g.VisualIndex!=9),
            rigidMaterialIndices=mesh.ShadedGeoms!.Where(g=>g.VisualIndex!=9).Select(g=>g.MaterialIndex).Distinct().ToArray(),
            materials=mesh.CustomMaterials.Select((m,i)=>new {index=i,m.MaterialUserInst!.MaterialName,m.MaterialUserInst.Link,m.MaterialUserInst.IsUsingGameMaterial,
                constants=m.MaterialUserInst.Csts?.Length,colors=m.MaterialUserInst.Color?.Length,uvAnimations=m.MaterialUserInst.UvAnims?.Length,userTextures=m.MaterialUserInst.UserTextures?.Length,
                materialNodeSha256=Hash(Save(m.MaterialUserInst))}).ToArray(),
            independentVisModelRoundTrip=new {hasSolid=standalone.Solid is not null,hasMeshShaded=standalone.MeshShaded is not null,legacyTreeType=standalone.Solid?.Tree?.GetType().Name,
                sameVisualNodeIdentity=ReferenceEquals(((CPlugTree)standalone.Solid!.Tree!).Visual,standalone.MeshShaded!.Visuals![0]),
                functionType=((CPlugTree)standalone.Solid.Tree).FuncTree?.GetType().Name,sha256=Hash(standaloneBytes)},
            modernItemAuxiliaryHostRoundTrip=new {hasRequestedVisModel=round.VisModelCustom is not null,hasSourceEntityModel=round.EntityModel is CPlugPrefab,
                exactlyEqualToCanonicalUnchangedItem=modified.AsSpan().SequenceEqual(sourceCanonical),sha256=Hash(modified)},
            sourceUnchanged=File.ReadAllBytes(sourcePath).AsSpan().SequenceEqual(bytes),
            conclusions=new[]{"Standalone CGameObjectVisModel retains both typed sibling fields offline.","A sibling legacy Solid has no established transform or clock parenting of a modern EntityModel.","Do not put CPlugMaterialUserInst in legacy Tree.Shader.","No native playback or shared-clock verdict follows from these parser observations."}
        };
        Directory.CreateDirectory(output);
        var json=JsonSerializer.Serialize(manifest,new JsonSerializerOptions{WriteIndented=true});
        File.WriteAllText(Path.Combine(output,"serializer-audit.json"),json+"\n");
        Console.WriteLine(json);
    }
    static byte[] Save<T>(T node)where T:GBX.NET.Engines.MwFoundations.CMwNod {using var output=new MemoryStream();new Gbx<T>(node).Save(output);return output.ToArray();}
    static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    static void Require(bool condition,string message){if(!condition)throw new InvalidDataException(message);}
}
