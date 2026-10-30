using System.Text.Json;
using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;

// Version 15/28 can store early Materials and late UserInsts together.
// Test whether the engine's material refresh revisits custom-buffer preparation.
internal static class FlagMaterialBootstrap
{
    public static int Create(string seed, string folder)
    {
        if (Directory.Exists(folder)) throw new IOException("Preserve previous fixtures");
        Directory.CreateDirectory(folder);
        var rows = new List<object>();
        foreach (var mode in new[] { "Custom28", "Inst15", "InstAndCustom15" })
        {
            var item = Gbx.Parse<CGameItemModel>(seed).Node;
            var prefab = (CPlugPrefab)((NPlugItem_SVariantList)item.EntityModel!).Variants![0].EntityModel!;
            var mesh = ((CPlugDynaObjectModel)prefab.Ents![1].Model!).Mesh!;
            var instances = mesh.CustomMaterials!.Select(m => m.MaterialUserInst!).ToArray();
            if (instances.Length != 1) throw new Exception("Expected one all-animated material");
            var mat = Gbx.Parse<CPlugMaterial>("C:/Users/PC/OpenplanetNext/Extract/GameData/Stadium/Media/Material/ItemFlag.Material.Gbx").Node;
            foreach (var c in mat.Chunks.Where(c => c.Ignore).ToArray()) mat.Chunks.Remove(c);
            mat.CustomMaterial = null;
            mat.Shader = null;
            if (mat.References.Shader.Descriptor is not null) mat.References.Shader.Clear();
            mat.DeviceMaterials = [];
            var materialChunk = mat.Chunks.OfType<CPlugMaterial.Chunk09079015>().Single();
            materialChunk.U03 = []; materialChunk.U04 = null;
            mesh.Materials = new External<CPlugMaterial>[] { new External<CPlugMaterial>(mat, null) };
            mesh.MaterialIds = [];
            mesh.Chunks.OfType<CPlugSolid2Model.Chunk090BB000>().Single().Version = mode == "Custom28" ? 28 : 15;
            mesh.MaterialInsts = mode == "Custom28" ? null : instances;
            if (mode == "Inst15") mesh.CustomMaterials = null;
            var path = Path.Combine(folder, "TMIS_Bootstrap_" + mode + ".Item.Gbx");
            new Gbx<CGameItemModel>(item).Save(path);
            Gbx.Parse<CGameItemModel>(path);
            rows.Add(new { mode, path, scope = "preload-only until effective shader and initialization are proven" });
        }
        File.WriteAllText(Path.Combine(folder, "manifest.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}
