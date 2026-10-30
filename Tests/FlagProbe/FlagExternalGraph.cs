using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.Game;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.MwFoundations;
using GBX.NET.Engines.Plug;

// Tests whether finalizing an external Mesh archive before its dyna/prefab
// changes the late UserInst initialization seen with an inline mesh.
// Empty embedded map only: never place until the structured-buffer gate passes.
internal static class FlagExternalGraph
{
    sealed class RefDoc<T>(T node, GbxRefTable table) : Gbx<T>(node) where T : CMwNod, new()
    { public void SetTable() => RefTable = table; }

    public static int Create(string seed, string mapSource, string output)
    {
        if (Directory.Exists(output)) throw new IOException("Preserve previous fixture");
        Directory.CreateDirectory(output);
        var name = "TMIS_ExternalGraph";
        var item = Gbx.Parse<CGameItemModel>(seed).Node;
        item.Name = name; item.Ident = item.Ident! with { Id = "" }; item.ArchetypeRef = "";
        var variant = ((NPlugItem_SVariantList)item.EntityModel!).Variants![0];
        var prefab = (CPlugPrefab)variant.EntityModel!;
        var dyna = (CPlugDynaObjectModel)prefab.Ents![1].Model!;
        var mesh = dyna.Mesh!;
        if (mesh.CustomMaterials?.Single().MaterialUserInst?.Link?.EndsWith("ItemFlag") != true)
            throw new Exception("Expected proven late native material link");
        var files = new List<(string Name, byte[] Bytes)>();
        void Save<T>(T node, GbxRefTable refs, string suffix) where T : CMwNod, new() {
            var doc = new RefDoc<T>(node, refs); doc.SetTable();
            using var stream = new MemoryStream(); doc.Save(stream);
            var bytes = stream.ToArray(); Gbx.Parse<T>(new MemoryStream(bytes));
            files.Add((name + suffix, bytes));
        }
        Save(mesh, new(), ".Mesh.Gbx");
        var dynaRefs = new GbxRefTable { AncestorLevel = 0 };
        dyna.Mesh = null; dyna.References.Mesh.Descriptor = new GBX.NET.Components.GbxRefTableFile(dynaRefs, 1, false, name + ".Mesh.Gbx");
        Save(dyna, dynaRefs, ".DynaObject.Gbx");
        var prefabRefs = new GbxRefTable { AncestorLevel = 0 };
        prefab.Ents[1].Model = null; prefab.Ents[1].References.Model.Descriptor = new GBX.NET.Components.GbxRefTableFile(prefabRefs, 1, false, name + ".DynaObject.Gbx");
        Save(prefab, prefabRefs, ".Prefab.Gbx");
        var itemRefs = new GbxRefTable { AncestorLevel = 0 };
        variant.EntityModel = null; variant.References.EntityModel.Descriptor = new GBX.NET.Components.GbxRefTableFile(itemRefs, 1, false, name + ".Prefab.Gbx");
        Save(item, itemRefs, ".Item.Gbx");
        foreach (var (file, bytes) in files) File.WriteAllBytes(Path.Combine(output, file), bytes);
        var map = Gbx.Parse<CGameCtnChallenge>(mapSource, new GbxReadSettings { SkipChunkIds = [0x0304305B] });
        map.Node.AnchoredObjects!.Clear(); map.Node.EmbeddedZipData = null;
        map.Node.MapName = "TMIS Embedded External Graph Preload";
        map.Node.MapUid = "TMISExternalGraph20261022";
        map.Node.UpdateEmbeddedZipData(zip => {
            foreach (var (file, bytes) in files) {
                using var stream = zip.CreateEntry("Items/TMIS_ExternalGraph/" + file, CompressionLevel.Optimal).Open();
                stream.Write(bytes);
            }
        });
        var target = Path.Combine(output, "TMIS_ExternalGraph_Empty.Map.Gbx"); map.Save(target);
        File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(new {
            map = target, anchors = 0, files = files.Select(f => new { file = f.Name,
                bytes = f.Bytes.Length, sha256 = Convert.ToHexString(SHA256.HashData(f.Bytes)) }),
            scope = "Preload only; no automatic animation or placement claim" }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}
