using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;

// Bounded backward-compatibility probe. No placeholder early material: test
// whether old material-instance/ID readers resolve game links before post-load.
internal static class FlagLegacyMaterials
{
    public static int Create(string seed,string output)
    {
        if(Directory.Exists(output))throw new IOException("Preserve prior fixture");
        Directory.CreateDirectory(output);
        var rows=new List<object>();
        foreach(var mode in new[]{"Inst8","Inst14","Inst15","Ids7Bare","Ids7Folder","Ids9Folder"})
        {
            var item=Gbx.Parse<CGameItemModel>(seed).Node;
            var mesh=((CPlugDynaObjectModel)((CPlugPrefab)((NPlugItem_SVariantList)item.EntityModel!).Variants![0].EntityModel!).Ents![1].Model!).Mesh!;
            var inst=mesh.CustomMaterials!.Select(m=>m.MaterialUserInst!).ToArray();
            mesh.Materials=[];mesh.CustomMaterials=null;mesh.MaterialIds=[];mesh.MaterialInsts=null;
            var version=mode.StartsWith("Inst")?int.Parse(mode[4..]):mode.StartsWith("Ids7")?7:9;
            mesh.Chunks.OfType<CPlugSolid2Model.Chunk090BB000>().Single().Version=version;
            if(mode.StartsWith("Inst"))mesh.MaterialInsts=inst;
            else
            {
                mesh.MaterialIds=["ItemFlag"];
                mesh.MaterialsFolderName=mode.EndsWith("Folder")?"Stadium\\Media\\Material\\":"";
            }
            var path=Path.Combine(output,"TMIS_Legacy_"+mode+".Item.Gbx");
            new Gbx<CGameItemModel>(item).Save(path);Gbx.Parse<CGameItemModel>(path);
            rows.Add(new{mode,path,version,scope="preload-only user-context control"});
        }
        File.WriteAllText(Path.Combine(output,"manifest.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
        return 0;
    }
}
