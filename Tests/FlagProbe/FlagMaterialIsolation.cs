using System.Text.Json;
using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;

// Loader diagnostics only. Shader-free candidates must never be placed.
internal static class FlagMaterialIsolation
{
    const string Data = "C:/Users/PC/OpenplanetNext/Extract/GameData/";
    public static int Create(string seed, string folder)
    {
        Directory.CreateDirectory(folder);
        var manifest = new List<object>();
        // REJECTED: removing a native shader's passes crashes during preload.
        // The retained failure artifact must never be regenerated or staged.
        foreach (var mode in new[] { "Bare", "NativeNullShader", "ParentNullShaders", "CurrentNativeMaterial", "CurrentNativeParent", "CurrentNativeShader" })
        {
            var item = Gbx.Parse<CGameItemModel>(seed).Node;
            var prefab = (CPlugPrefab)((NPlugItem_SVariantList)item.EntityModel!).Variants![0].EntityModel!;
            var mesh = ((CPlugDynaObjectModel)prefab.Ents![1].Model!).Mesh!;
            var table = new GbxRefTable { AncestorLevel = 0 };
            GbxRefTableFile Ref(string path, bool use = false) => new(table, 1, use, path.Replace('/', '\\'));
            var mat = mode == "Bare" ? new CPlugMaterial() : Gbx.Parse<CPlugMaterial>(Data + "Stadium/Media/Material/ItemFlag.Material.Gbx").Node;
            foreach (var c in mat.Chunks.Where(c => c.Ignore).ToArray()) mat.Chunks.Remove(c);
            mat.CustomMaterial = null;
            // Remove optional color-target file references, independently of shader.
            var matChunk = mat.Chunks.OfType<CPlugMaterial.Chunk09079015>().SingleOrDefault();
            if (matChunk is not null) { matChunk.U03 = []; matChunk.U04 = null; }
            if (mode == "NativeNullShader")
            { mat.ShaderFile = null; mat.Shader = null; mat.DeviceMaterials = []; }
            if (mode is "ParentNullShaders" or "CurrentNativeParent" or "CurrentNativeShader")
            {
                var parent = Gbx.Parse<CPlugMaterial>(Data + "Techno3/Media/Material/Tech3_Warp_TDiffSpec_VertexTween.Material.gbx").Node;
                foreach (var c in parent.Chunks.Where(c => c.Ignore).ToArray()) parent.Chunks.Remove(c);
                parent.CustomMaterial = null;
                foreach (var dev in parent.DeviceMaterials!)
                {
                    dev.Shader1File = null; dev.Shader2File = null; dev.Shader3File = null;
                    if (mode != "ParentNullShaders")
                    {
                        var shader = Gbx.Parse<CPlugShaderApply>(Data + "Techno3/Media/Shader/Tech3_Warp_TDiffSpec_VertexTween.Shader.Gbx").Node;
                        if (mode == "CurrentNativeParent")
                            dev.Shader1File = dev.Shader2File = dev.Shader3File = Ref("GameData/Techno3/Media/Shader/Tech3_Warp_TDiffSpec_VertexTween.Shader.Gbx");
                        else dev.Shader1 = dev.Shader2 = dev.Shader3 = shader;
                    }
                }
                mat.Shader = parent; mat.ShaderFile = null;
            }
            if (mode == "CurrentNativeMaterial")
                mat.ShaderFile = Ref("GameData/Techno3/Media/Material/Tech3_Warp_TDiffSpec_VertexTween.Material.gbx");
            mesh.Materials = [new External<CPlugMaterial>(mat, null)];
            mesh.CustomMaterials = null; mesh.MaterialInsts = null; mesh.MaterialIds = [];
            var path = Path.Combine(folder, "TMIS_Isolate_" + mode + ".Item.Gbx");
            new ReferenceTableDocument(item, table).Save(path);
            var read = Gbx.Parse<CGameItemModel>(path);
            manifest.Add(new { mode, path, bytes = new FileInfo(path).Length, references = read.RefTable?.Files.Select(f => f.FilePath).ToArray(),
                use = "Native loading only; do not place shader-free fixtures" });
            Console.WriteLine("PARSED " + path);
        }
        File.WriteAllText(Path.Combine(folder, "material-isolation.json"), JsonSerializer.Serialize(manifest,new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}
