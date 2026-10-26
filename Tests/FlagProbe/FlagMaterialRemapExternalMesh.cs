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

// One-variable v6 experiment: only the cloth Mesh becomes an external archive.
// The VariantList, Prefab and Dyna remain inline; no native primer is included.
internal static class FlagMaterialRemapExternalMesh
{
    const string Candidate = "TMIS_MatRemap_Candidate.Item.Gbx";
    const string Placeholder = "TMIS_MatRemap_Placeholder.Material.Gbx";
    const string Skin = "TMIS_MatRemap.GameSkin.Gbx";
    const string Modifier = "TMIS_MatRemap.TerrainModifier.Gbx";
    const string OutputItem = "TMIS_MatRemap_ExternalMesh.Item.Gbx";
    const string OutputMesh = "TMIS_MatRemap_ExternalMesh.Mesh.Gbx";
    static readonly GbxReadSettings MapSettings = new() { SkipChunkIds = [0x0304305B] };
    sealed class RefDoc<T>(T node, GbxRefTable table) : Gbx<T>(node) where T : CMwNod, new()
    { public void SetTable() => RefTable = table; }

    public static int Create(string v6Folder, string sourceMap, string output)
    {
        v6Folder = Path.GetFullPath(v6Folder);
        sourceMap = Path.GetFullPath(sourceMap);
        output = Path.GetFullPath(output);
        if (Directory.Exists(output) || File.Exists(output))
            throw new IOException("Preserve previous evidence; choose a new output folder");
        using var seedManifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(v6Folder, "manifest.json")));
        Require(seedManifest.RootElement.GetProperty("fixtureVersion").GetInt32() == 6, "Expected audited v6 fixtures");
        var candidateBytes = File.ReadAllBytes(Path.Combine(v6Folder, Candidate));
        var sourceMapBytes = File.ReadAllBytes(sourceMap);
        var sourceDoc = Gbx.Parse<CGameItemModel>(new MemoryStream(candidateBytes));
        var item = sourceDoc.Node;
        var sourceName = item.Name;
        var sourceIdent = item.Ident ?? throw new InvalidDataException("Candidate item has no identity");
        Require(sourceIdent.Id == "" && item.ArchetypeRef == "", "Preserve ordinary user identity");
        var variant = ((NPlugItem_SVariantList)item.EntityModel!).Variants!.Single();
        var prefab = (CPlugPrefab)variant.EntityModel!;
        var dyna = (CPlugDynaObjectModel)prefab.Ents![1].Model!;
        var mesh = dyna.Mesh ?? throw new InvalidDataException("Expected inline source cloth mesh");
        Require(dyna.MeshFile is null && variant.EntityModelFile is null && prefab.Ents[1].ModelFile is null,
            "Expected inline source VariantList/Prefab/Dyna graph");
        Require(mesh.Materials is { Length: 1 } && mesh.Materials[0].File?.FilePath == Placeholder
            && mesh.Materials[0].File?.UseFile == false && item.MaterialModifierFile?.FilePath == Modifier
            && item.MaterialModifierFile.UseFile == false, "Expected accepted early-placeholder material route");
        Require((mesh.CustomMaterials?.Length ?? 0) == 0 && (mesh.MaterialInsts?.Length ?? 0) == 0,
            "Unexpected late material route");
        var sourceVisualHashes = VisualHashes(mesh);
        Require(sourceVisualHashes.Length == 5, "Expected five source visuals");
        var files = new Dictionary<string, byte[]>();
        foreach (var name in new[] { Placeholder, Skin, Modifier })
            files.Add(name, File.ReadAllBytes(Path.Combine(v6Folder, name)));

        var meshRefs = new GbxRefTable { AncestorLevel = 0 };
        mesh.Materials = [new External<CPlugMaterial>(null, new(meshRefs, 1, false, Placeholder))];
        var meshBytes = Save(mesh, meshRefs);
        var meshDoc = Gbx.Parse<CPlugSolid2Model>(new MemoryStream(meshBytes));
        Require(meshDoc.RefTable?.AncestorLevel == 0 && meshDoc.RefTable.Files.Count == 1
            && meshDoc.RefTable.Resources.Count == 0 && meshDoc.Node.Materials!.Single().File?.FilePath == Placeholder
            && meshDoc.Node.Materials![0].File?.UseFile == false, "External mesh must have only its sibling placeholder dependency");
        Require(VisualHashes(meshDoc.Node).SequenceEqual(sourceVisualHashes), "Complete visual serialization changed");
        Require(Save(meshDoc.Node, meshDoc.RefTable!).AsSpan().SequenceEqual(meshBytes), "Full mesh archive did not round-trip byte-exactly");
        files.Add(OutputMesh, meshBytes);

        var itemRefs = new GbxRefTable { AncestorLevel = 0 };
        dyna.Mesh = null;
        dyna.MeshFile = new(itemRefs, 1, false, OutputMesh);
        item.MaterialModifierFile = new(itemRefs, 1, false, Modifier);
        var itemBytes = Save(item, itemRefs);
        var reopened = Gbx.Parse<CGameItemModel>(new MemoryStream(itemBytes));
        var actualVariant = ((NPlugItem_SVariantList)reopened.Node.EntityModel!).Variants!.Single();
        var actualPrefab = (CPlugPrefab)actualVariant.EntityModel!;
        var actualDyna = (CPlugDynaObjectModel)actualPrefab.Ents![1].Model!;
        Require(reopened.Node.Name == sourceName && reopened.Node.Ident == sourceIdent && reopened.Node.ArchetypeRef == "",
            "Unrequested item name or identity change");
        Require(actualVariant.EntityModelFile is null && actualPrefab.Ents[1].ModelFile is null
            && actualDyna.Mesh is null && actualDyna.MeshFile?.FilePath == OutputMesh
            && actualDyna.MeshFile.UseFile == false, "Only Dyna.Mesh must become external");
        Require(reopened.RefTable?.AncestorLevel == 0 && reopened.RefTable.Files.Count == 2
            && reopened.RefTable.Resources.Count == 0 && reopened.Node.MaterialModifierFile?.FilePath == Modifier
            && reopened.Node.MaterialModifierFile.UseFile == false, "Item must retain its exact modifier route");
        Require(reopened.RefTable!.Files.Select(f => f.FilePath).Order().SequenceEqual(new[] { OutputMesh, Modifier }.Order()),
            "Unexpected item dependency");
        files.Add(OutputItem, itemBytes);

        var token = Guid.NewGuid().ToString("N")[..20];
        var embeddedFolder = "TMIS_MatRemap_ExternalMesh_" + token;
        var itemId = embeddedFolder + "\\" + OutputItem;
        var mapRows = new List<object>();
        Directory.CreateDirectory(output);
        Directory.CreateDirectory(Path.Combine(output, "files"));
        foreach (var (name, bytes) in files) File.WriteAllBytes(Path.Combine(output, "files", name), bytes);
        foreach (var empty in new[] { true, false })
        {
            var mapDoc = Gbx.Parse<CGameCtnChallenge>(new MemoryStream(sourceMapBytes), MapSettings);
            var map = mapDoc.Node;
            Require(map.AnchoredObjects is not null, "Source map has no ordinary anchor list");
            map.AnchoredObjects!.Clear();
            if (!empty)
            {
                var anchor = new CGameCtnAnchoredObject {
                    ItemModel = new Ident(itemId, sourceIdent.Collection, sourceIdent.Author), AnchorTreeId = itemId,
                    AbsolutePositionInMap = new Vec3(768, 128, 450), YawPitchRoll = new Vec3(MathF.PI / 2, 0, 0),
                    PivotPosition = new Vec3(0, 0, 0), Scale = 1
                };
                anchor.CreateChunk<CGameCtnAnchoredObject.Chunk03101002>().Version = 8;
                anchor.CreateChunk<CGameCtnAnchoredObject.Chunk03101004>().Version = 0;
                map.AnchoredObjects.Add(anchor);
            }
            map.MapName = (empty ? "TMIS External Mesh Preload " : "TMIS External Mesh Single Near ") + token[..8];
            map.MapUid = "TMISXM_" + token + (empty ? "E" : "S");
            map.EmbeddedZipData = null;
            map.UpdateEmbeddedZipData(zip => {
                foreach (var (name, bytes) in files)
                {
                    using var stream = zip.CreateEntry("Items/" + embeddedFolder + "/" + name, CompressionLevel.Optimal).Open();
                    stream.Write(bytes);
                }
            });
            using var memory = new MemoryStream();
            mapDoc.Save(memory);
            var mapBytes = memory.ToArray();
            var saved = Gbx.Parse<CGameCtnChallenge>(new MemoryStream(mapBytes), MapSettings).Node;
            Require(saved.AnchoredObjects?.Count == (empty ? 0 : 1), "Unexpected map anchor count");
            if (!empty)
            {
                var actual = saved.AnchoredObjects!.Single();
                Require(actual.ItemModel?.Id == itemId && actual.AbsolutePositionInMap == new Vec3(768, 128, 450)
                    && actual.YawPitchRoll == new Vec3(MathF.PI / 2, 0, 0) && actual.PivotPosition == new Vec3(0, 0, 0)
                    && actual.Scale == 1 && actual.AnchorTreeId == itemId, "Map changed the custom anchor");
            }
            Require(saved.ExpectedEmbeddedItemModels?.Select(i => i.Id).SequenceEqual(new[] { itemId }) == true,
                "Expected exactly one embedded item identity");
            using (var zip = saved.OpenReadEmbeddedZipData())
            {
                Require(zip.Entries.Count == 5, "Expected five embedded graph files");
                foreach (var (name, bytes) in files)
                {
                    var entry = zip.GetEntry("Items/" + embeddedFolder + "/" + name)
                        ?? throw new InvalidDataException("Missing embedded graph dependency " + name);
                    using var stream = entry.Open();
                    using var embedded = new MemoryStream();
                    stream.CopyTo(embedded);
                    Require(embedded.ToArray().AsSpan().SequenceEqual(bytes), "Embedded graph bytes changed");
                }
            }
            var path = Path.Combine(output, empty ? "TMIS_MatRemap_ExternalMesh_Empty.Map.Gbx" : "TMIS_MatRemap_ExternalMesh_Single_Near.Map.Gbx");
            File.WriteAllBytes(path, mapBytes);
            mapRows.Add(new { path, bytes = mapBytes.Length, sha256 = Hash(mapBytes), map.MapName, map.MapUid,
                anchorCount = saved.AnchoredObjects!.Count, nativePrimerIncluded = false,
                customPosition = empty ? null : new[] { 768, 128, 450 },
                expectedEmbeddedItemId = itemId, embeddedZipSha256 = Hash(saved.EmbeddedZipData!) });
        }
        Require(Hash(File.ReadAllBytes(sourceMap)) == Hash(sourceMapBytes), "Source map changed");
        Require(Hash(File.ReadAllBytes(Path.Combine(v6Folder, Candidate))) == Hash(candidateBytes), "Source candidate changed");
        foreach (var name in new[] { Placeholder, Skin, Modifier })
            Require(File.ReadAllBytes(Path.Combine(v6Folder, name)).AsSpan().SequenceEqual(files[name]), "Source dependency changed");
        File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(new {
            manifestVersion = 1, operation = "material-remap-v6-external-mesh-only",
            scope = "Offline external-mesh archive boundary experiment only. No native flag anchor, companion, preload patch or runtime initialization is included. Native parse, effective material/structured-buffer state, cold-start visibility and motion remain untested.",
            sourceCandidate = Path.Combine(v6Folder, Candidate), sourceCandidateSha256 = Hash(candidateBytes),
            sourceMap, sourceMapSha256 = Hash(sourceMapBytes), embeddedFolder,
            itemNameUnchanged = sourceName, sourceVisualSerializationSha256 = sourceVisualHashes,
            graph = new { inline = new[] { "VariantList", "Prefab", "Dyna" }, external = "Dyna.Mesh only",
                itemMeshUseFile = false, meshPlaceholderUseFile = false, itemModifierUseFile = false,
                gameSkinSourceUseFile = true, ancestorLevel = 0,
                exactNativeReplacement = @"Stadium\Media\Material\ItemFlag.Material.Gbx" },
            files = files.Select(f => new { name = f.Key, bytes = f.Value.Length, sha256 = Hash(f.Value),
                exactV6Dependency = f.Key == Placeholder || f.Key == Skin || f.Key == Modifier }),
            maps = mapRows,
            checks = new { onlyMeshBoundaryExternalized = true, fiveCompleteVisualSerializationsExact = true,
                completeMeshArchiveByteExactAfterRoundTrip = true, itemNameAndIdentityUnchanged = true,
                earlyPlaceholderAndModifierReferenceModesPreserved = true, exactThreeV6DependencyPayloads = true,
                noNativeAnchor = true, allFiveFilesEmbeddedByteExact = true, noLooseFileRelianceInMap = true,
                sourceMapAndFixtureUnchanged = true }
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"External-mesh-only v6 fixture: {output}; five graph files, empty preload and single near-camera maps; runtime untested");
        return 0;
    }

    static byte[] Save<T>(T node, GbxRefTable refs) where T : CMwNod, new()
    {
        var doc = new RefDoc<T>(node, refs); doc.SetTable();
        using var memory = new MemoryStream(); doc.Save(memory); return memory.ToArray();
    }
    static string[] VisualHashes(CPlugSolid2Model mesh) => mesh.Visuals!.Cast<CPlugVisualIndexedTriangles>()
        .Select(v => { using var memory = new MemoryStream(); new Gbx<CPlugVisualIndexedTriangles>(v).Save(memory); return Hash(memory.ToArray()); }).ToArray();
    static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    static void Require(bool ok, string message) { if (!ok) throw new InvalidDataException(message); }
}
