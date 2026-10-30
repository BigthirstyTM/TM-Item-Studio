using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;

// Targeted follow-up to the proven IA/SRV mismatch. Real material nodes are
// available during Solid2 post-load, before its structured-buffer flag check.
internal static class FlagGpuMaterials
{
    const string Data = "C:/Users/PC/OpenplanetNext/Extract/GameData/";
    public static int Create(string seed, string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var mode in new[] { "InlineMaterial", "InlineParent", "InlineShader", "MaterialIds" })
        {
            var item = Gbx.Parse<CGameItemModel>(seed).Node;
            var prefab = (CPlugPrefab)((NPlugItem_SVariantList)item.EntityModel!).Variants![0].EntityModel!;
            var mesh = ((CPlugDynaObjectModel)prefab.Ents![1].Model!).Mesh!;
            var table = new GbxRefTable { AncestorLevel = 0 };
            GbxRefTableFile Ref(string path, bool use = false) => new(table, 1, use, path.Replace('/', '\\'));
            var mat = Gbx.Parse<CPlugMaterial>(Data + "Stadium/Media/Material/ItemFlag.Material.Gbx").Node;
            mat.References.Shader.Descriptor = Ref("GameData/Techno3/Media/Material/Tech3_Warp_TDiffSpec_VertexTween.Material.gbx");
            foreach (var tex in mat.CustomMaterial!.Textures!)
                tex.References.Texture.Descriptor = Ref("GameData/" + tex.References.Texture.File!.FilePath, true);
            if (mode != "InlineMaterial")
            {
                var parent = Gbx.Parse<CPlugMaterial>(Data + "Techno3/Media/Material/Tech3_Warp_TDiffSpec_VertexTween.Material.gbx").Node;
                foreach (var dev in parent.DeviceMaterials!)
                {
                    dev.References.Shader1.Descriptor = Ref("GameData/Techno3/Media/Shader/Tech3_Warp_TDiffSpec_VertexTween.Shader.Gbx");
                    dev.References.Shader2.Descriptor = dev.References.Shader1.File; dev.References.Shader3.Descriptor = dev.References.Shader1.File;
                    if (mode == "InlineShader")
                    {
                        var shader = Gbx.Parse<CPlugShaderApply>(Data + "Techno3/Media/Shader/Tech3_Warp_TDiffSpec_VertexTween.Shader.Gbx").Node;
                        dev.Shader1 = shader; dev.Shader2 = shader; dev.Shader3 = shader;
                        if (dev.References.Shader1.Descriptor is not null) dev.References.Shader1.Clear(); if (dev.References.Shader2.Descriptor is not null) dev.References.Shader2.Clear(); if (dev.References.Shader3.Descriptor is not null) dev.References.Shader3.Clear();
                    }
                }
                mat.Shader = parent; if (mat.References.Shader.Descriptor is not null) mat.References.Shader.Clear();
            }
            mesh.Materials = new External<CPlugMaterial>[] { new External<CPlugMaterial>(mat, null) };
            mesh.CustomMaterials = null; mesh.MaterialInsts = null; mesh.MaterialIds = [];
            if (mode == "MaterialIds")
            {
                mesh.Materials = []; mesh.MaterialIds = ["ItemFlag"];
                mesh.MaterialsFolderName = "Stadium\\Media\\Material\\";
            }
            var path = Path.Combine(folder, "TMIS_Gpu_" + mode + ".Item.Gbx");
            new ReferenceTableDocument(item, table).Save(path);
            Gbx.Parse<CGameItemModel>(path);
            Console.WriteLine("PARSED " + path);
        }
        return 0;
    }
}
