using System.Text.Json;
using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;

// One-variable file-reference probes. Complete native parent; shader is a FID.
internal static class FlagShaderPaths
{
    const string Data = "C:/Users/PC/OpenplanetNext/Extract/GameData/";
    const string Native = "GameData/Techno3/Media/Shader/Tech3_Warp_TDiffSpec_VertexTween.Shader.Gbx";
    const string GameRoot = "C:/Program Files (x86)/Ubisoft/Ubisoft Game Launcher/games/Trackmania/";
    public static int Create(string seed, string folder)
    {
        Directory.CreateDirectory(folder);
        var manifest = new List<object>();
        foreach (var (mode, flags, path) in new[] {
            ("Relative0",0,Native), ("Absolute0",0,GameRoot+Native),
            ("Absolute1",1,GameRoot+Native), ("GameAlias0",0,":game:/"+Native),
            ("GameDataAlias0",0,":GameData:/Techno3/Media/Shader/Tech3_Warp_TDiffSpec_VertexTween.Shader.Gbx") })
        {
            var item=Gbx.Parse<CGameItemModel>(seed).Node;
            var prefab=(CPlugPrefab)((NPlugItem_SVariantList)item.EntityModel!).Variants![0].EntityModel!;
            var mesh=((CPlugDynaObjectModel)prefab.Ents![1].Model!).Mesh!;
            var table=new GbxRefTable { AncestorLevel=0 };
            var mat=Gbx.Parse<CPlugMaterial>(Data+"Stadium/Media/Material/ItemFlag.Material.Gbx").Node;
            foreach(var c in mat.Chunks.Where(c=>c.Ignore).ToArray()) mat.Chunks.Remove(c);
            mat.CustomMaterial=null;
            var chunk=mat.Chunks.OfType<CPlugMaterial.Chunk09079015>().Single();chunk.U03=[];chunk.U04=null;
            var parent=Gbx.Parse<CPlugMaterial>(Data+"Techno3/Media/Material/Tech3_Warp_TDiffSpec_VertexTween.Material.gbx").Node;
            foreach(var c in parent.Chunks.Where(c=>c.Ignore).ToArray()) parent.Chunks.Remove(c);
            var reference=new GbxRefTableFile(table,flags,true,path.Replace('/','\\'));
            foreach(var dev in parent.DeviceMaterials!) dev.References.Shader1.Descriptor=dev.References.Shader2.Descriptor=dev.References.Shader3.Descriptor=reference;
            mat.Shader=parent;if (mat.References.Shader.Descriptor is not null) mat.References.Shader.Clear();
            mesh.Materials=new External<CPlugMaterial>[] { new External<CPlugMaterial>(mat,null) };
            mesh.CustomMaterials=null;mesh.MaterialInsts=null;mesh.MaterialIds=[];
            var file=Path.Combine(folder,"TMIS_Path_"+mode+".Item.Gbx");
            new ReferenceTableDocument(item,table).Save(file);
            Gbx.Parse<CGameItemModel>(file);
            manifest.Add(new { mode, flags, path, file });
            Console.WriteLine("PARSED "+file);
        }
        File.WriteAllText(Path.Combine(folder,"path-manifest.json"),JsonSerializer.Serialize(manifest,new JsonSerializerOptions{WriteIndented=true}));
        return 0;
    }
}
