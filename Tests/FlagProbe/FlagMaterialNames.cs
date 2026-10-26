using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;

// Preload-only probes of the legacy MaterialIds/MaterialsFolderName fields.
// These differ from CustomMaterials.MaterialName; never render an unresolved file.
internal static class FlagMaterialNames
{
    public static int Create(string seed, string folder)
    {
        if (Directory.Exists(folder)) throw new IOException("Preserve previous fixtures");
        Directory.CreateDirectory(folder);
        var rows = new List<object>();
        foreach (var (mode, version, id, materialFolder) in new[] {
            ("IdsBare28", 28, "ItemFlag", ""),
            ("IdsFolder28", 28, "ItemFlag", "Stadium\\Media\\Material\\"),
            ("IdsGameFolder28", 28, "ItemFlag", "GameData\\Stadium\\Media\\Material\\"),
            ("IdsFull28", 28, "GameData\\Stadium\\Media\\Material\\ItemFlag.Material.Gbx", ""),
            ("IdsBare15", 15, "ItemFlag", ""),
            ("IdsGameFolder15", 15, "ItemFlag", "GameData\\Stadium\\Media\\Material\\") })
        {
            var item = Gbx.Parse<CGameItemModel>(seed).Node;
            var prefab = (CPlugPrefab)((NPlugItem_SVariantList)item.EntityModel!).Variants![0].EntityModel!;
            var mesh = ((CPlugDynaObjectModel)prefab.Ents![1].Model!).Mesh!;
            mesh.Chunks.OfType<CPlugSolid2Model.Chunk090BB000>().Single().Version = version;
            mesh.Materials = [];
            mesh.CustomMaterials = null;
            mesh.MaterialInsts = null;
            mesh.MaterialIds = [id];
            mesh.MaterialsFolderName = materialFolder;
            var path = Path.Combine(folder, "TMIS_Names_" + mode + ".Item.Gbx");
            new Gbx<CGameItemModel>(item).Save(path);
            Gbx.Parse<CGameItemModel>(path);
            rows.Add(new { mode, path, version, id, materialFolder, scope = "preload-only" });
        }
        File.WriteAllText(Path.Combine(folder, "manifest.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}
