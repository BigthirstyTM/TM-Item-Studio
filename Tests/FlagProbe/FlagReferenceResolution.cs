using System.Text.Json;
using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;

// Native loader diagnostics. Local controls are shader-free and must not be placed.
internal static class FlagReferenceResolution
{
    const string Data = "C:/Users/PC/OpenplanetNext/Extract/GameData/";
    const string Game = "C:/Program Files (x86)/Ubisoft/Ubisoft Game Launcher/games/Trackmania/";
    const string Parent = "GameData/Techno3/Media/Material/Tech3_Warp_TDiffSpec_VertexTween.Material.gbx";
    const string Shader = "GameData/Techno3/Media/Shader/Tech3_Warp_TDiffSpec_VertexTween.Shader.Gbx";
    public static int Create(string seed, string folder)
    {
        Directory.CreateDirectory(folder);
        var manifest = new List<object>();
        // LocalParent loaded; LocalParentUseFile and NativeParentUseFile crashed
        // on preload. Never regenerate those rejected UseFile=true material refs.
        // Native-tested: the two absolute material references return null.
        // Both shader-file candidates load, but leave all five structured bits
        // clear. Loading is NOT proof of rendering or standalone compatibility.
        foreach (var mode in new[] { "NativeParentAbsolute", "NativeShaderUseFile", "NativeShaderAbsolute", "NativeMaterialAbsolute" })
        {
            var item = Gbx.Parse<CGameItemModel>(seed).Node;
            var prefab = (CPlugPrefab)((NPlugItem_SVariantList)item.EntityModel!).Variants![0].EntityModel!;
            var mesh = ((CPlugDynaObjectModel)prefab.Ents![1].Model!).Mesh!;
            var table = new GbxRefTable { AncestorLevel = 0 };
            GbxRefTableFile Ref(string path, bool use) => new(table, 1, use, path.Replace('/', '\\'));
            var mat = Gbx.Parse<CPlugMaterial>(Data + "Stadium/Media/Material/ItemFlag.Material.Gbx").Node;
            foreach (var c in mat.Chunks.Where(c => c.Ignore).ToArray()) mat.Chunks.Remove(c);
            mat.CustomMaterial = null;
            var chunk = mat.Chunks.OfType<CPlugMaterial.Chunk09079015>().Single();
            chunk.U03 = []; chunk.U04 = null;
            mat.Shader = null;
            mat.ShaderFile = Ref((mode == "NativeParentAbsolute" ? Game : "") + Parent, false);
            if (mode.StartsWith("NativeShader"))
            {
                var parent = Gbx.Parse<CPlugMaterial>(Data + Parent[9..]).Node;
                foreach (var c in parent.Chunks.Where(c => c.Ignore).ToArray()) parent.Chunks.Remove(c);
                foreach (var dev in parent.DeviceMaterials!)
                    dev.Shader1File = dev.Shader2File = dev.Shader3File = Ref((mode.EndsWith("Absolute") ? Game : "") + Shader, true);
                mat.Shader = parent; mat.ShaderFile = null;
            }
            mesh.Materials = mode == "NativeMaterialAbsolute" ? [new External<CPlugMaterial>(null, Ref(Game + "GameData/Stadium/Media/Material/ItemFlag.Material.Gbx", false))]
                : [new External<CPlugMaterial>(mat, null)];
            mesh.CustomMaterials = null; mesh.MaterialInsts = null; mesh.MaterialIds = [];
            var path = Path.Combine(folder, "TMIS_Refs_" + mode + ".Item.Gbx");
            new ReferenceTableDocument(item, table).Save(path);
            var read = Gbx.Parse<CGameItemModel>(path);
            manifest.Add(new { mode, path, bytes = new FileInfo(path).Length,
                references = read.RefTable?.Files.Select(f => new { f.FilePath, f.Flags, f.UseFile }).ToArray(),
                use = "Native preload only; shader-free controls must not be placed" });
            Console.WriteLine("PARSED " + path);
        }
        File.WriteAllText(Path.Combine(folder, "reference-resolution.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}
