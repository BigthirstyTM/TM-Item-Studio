using System.Security.Cryptography;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;

// An installation-context control, not a standalone user item. It deliberately
// uses only ordinary relative FIDs, resolved from a new game-drive Items folder.
internal static class FlagInstalledFiles
{
    public static int Create(string seeds, string output, bool adjacent = false)
    {
        if (Directory.Exists(output)) throw new IOException("Preserve previous fixture");
        Directory.CreateDirectory(output);
        var rows = new List<object>();
        foreach (var (shape, source) in new[] {
            ("Long", "TMIS_Step06_Long250.Item.Gbx"),
            ("Swallowtail", "TMIS_Step07_Swallowtail.Item.Gbx"),
            ("Round", "TMIS_Step08_Round.Item.Gbx") })
        {
            var item = Gbx.Parse<CGameItemModel>(Path.Combine(seeds, source)).Node;
            var name = (adjacent ? "TMIS_Adjacent_" : "TMIS_Installed_") + shape;
            item.Name = name;
            item.Ident = item.Ident! with { Id = name };
            var prefab = (CPlugPrefab)((NPlugItem_SVariantList)item.EntityModel!).Variants![0].EntityModel!;
            var mesh = ((CPlugDynaObjectModel)prefab.Ents![1].Model!).Mesh!;
            var table = new GbxRefTable { AncestorLevel = adjacent?1:4 };
            var reference = new GbxRefTableFile(table, 1, false,
                adjacent ? "Media\\Material\\ItemFlag.Material.Gbx" : "GameData\\Stadium\\Media\\Material\\ItemFlag.Material.Gbx");
            mesh.Materials = [new External<CPlugMaterial>(null, reference)];
            mesh.MaterialIds = []; mesh.MaterialInsts = null; mesh.CustomMaterials = null;
            var path = Path.Combine(output, name + ".Item.Gbx");
            new ReferenceTableDocument(item, table).Save(path);
            var parsed = Gbx.Parse<CGameItemModel>(path);
            if (parsed.RefTable!.AncestorLevel != (adjacent?1:4) || parsed.RefTable.Files.Count != 1)
                throw new Exception("Unexpected reference table");
            rows.Add(new {shape,path,sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
                scope="Game-drive installation control; not a portable user item",
                relative=adjacent ? "GameData/Stadium/Items/"+name+".Item.Gbx" : name+".Item.Gbx",
                requiredFolder=adjacent ? "GameData/Stadium/Items" : "GameData/Stadium/Items/TMIS_FlagFiles_20261022"});
        }
        File.WriteAllText(Path.Combine(output,"manifest.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions {WriteIndented=true}));
        return 0;
    }
}
