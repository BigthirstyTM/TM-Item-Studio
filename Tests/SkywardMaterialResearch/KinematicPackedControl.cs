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
using TmEssentials;
using KC = GBX.NET.Engines.Meta.NPlugDyna_SKinematicConstraint;

// Isolated file-only pair. The native rendering decision is a later game test.
internal static class KinematicPackedControl
{
    public static void InspectMapAnchor(string path)
    {
        var map = ReadMap(File.ReadAllBytes(path)).Node;
        var json = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true };
        Console.WriteLine(JsonSerializer.Serialize(new {
            mapName = map.MapName, anchors = map.AnchoredObjects!.Select(a => new {
                a.ItemModel, a.AnchorTreeId, a.AbsolutePositionInMap, a.YawPitchRoll,
                a.PivotPosition, a.Scale, a.Flags
            }), expected = map.ExpectedEmbeddedItemModels?.Select(x => x.Id),
            embedded = map.OpenReadEmbeddedZipData().Entries.Select(x => x.FullName)
        }, json));
    }
    const string SourceFolder = "artifacts/skyward-banner-20261023/material-candidates-native-warpflag-oneuv-v1";
    const string SourceMapName = "TMIS_Skyward_NativeWarpFlag.Map.Gbx";
    const string SourceItemName = "TMIS_Skyward_NativeWarpFlag.Item.Gbx";
    const string SourceMapHash = "02703C4CE9A8A691D9F321D846A4A9F360C50CDBED4402E268FB2D2CBD174BB3";
    const string SourceItemHash = "1C8B5B625F7AF6CD10F1E0B6E6C1A90FE1C1666300A50EBB9A260E590B6AE060";
    const string VisualHash = "BEA705E379D37AB073F4C0178807496787FACDFA4B3964304E8AA5B8D960AE42";
    static readonly string[] Dependencies = ["TMIS_MatRemap_Placeholder.Material.Gbx", "TMIS_MatRemap.GameSkin.Gbx", "TMIS_MatRemap.TerrainModifier.Gbx"];
    static readonly GbxReadSettings MapSettings = new() { SkipChunkIds = [0x0304305B] };
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, IncludeFields = true };

    public static void Generate(string output)
    {
        output = Path.GetFullPath(output);
        Require(!Directory.Exists(output) && !File.Exists(output), "Choose a new evidence directory");
        var sourceMapPath = Path.GetFullPath(Path.Combine(SourceFolder, SourceMapName));
        var sourceItemPath = Path.GetFullPath(Path.Combine(SourceFolder, "files", SourceItemName));
        var sourceMapBytes = File.ReadAllBytes(sourceMapPath);
        var sourceItemBytes = File.ReadAllBytes(sourceItemPath);
        Require(Hash(sourceMapBytes) == SourceMapHash && Hash(sourceItemBytes) == SourceItemHash, "Pinned source changed");
        var sourceItem = ReadItem(sourceItemBytes);
        var sourcePrefab = Prefab(sourceItem.Node);
        Require(sourcePrefab.Ents!.Length == 2 && sourcePrefab.Ents[0].Model is CPlugStaticObjectModel, "Expected unchanged static plane followed by cloth");
        var sourceCloth = (CPlugDynaObjectModel)sourcePrefab.Ents[1].Model!;
        var sourceParams = (NPlugDynaObjectModel_SInstanceParams)sourcePrefab.Ents[1].Params!;
        Require(!sourceParams.IsKinematic && sourceCloth.StaticShape is null && sourceCloth.DynaShape is null, "Expected ordinary unshaped cloth baseline");
        var meshHash = NodeHash(sourceCloth.Mesh!);
        var planeEntryHash = EntryHash(sourcePrefab.Ents[0]);
        var visualHashes = sourceCloth.Mesh!.Visuals!.Select(NodeHash).ToArray();
        Require(visualHashes.Length == 5 && visualHashes.All(h => h == VisualHash), "Expected complete accepted five-LOD cloth");
        Require(sourceCloth.Mesh.Visuals!.Cast<CPlugVisualIndexedTriangles>().All(v => v.SubVisuals!.Length == 86 && v.TexCoords.Length == 1), "Expected 86 frames and one UV channel");
        var canonicalSource = Save(new RefDoc<CGameItemModel>(sourceItem.Node, sourceItem.RefTable!));
        var referenceSignature = ReferenceSignature(sourceItem.RefTable!);
        var sourceMap = ReadMap(sourceMapBytes);
        Require(sourceMap.Node.AnchoredObjects!.Count == 2 && sourceMap.Node.AnchoredObjects[0].ItemModel!.Id == "Flag16m", "Expected native Flag16m first and one custom anchor");
        Require(sourceMap.Node.AnchoredObjects[1].AbsolutePositionInMap == new Vec3(903, 128, 450), "Stationary source anchor changed");
        var sourceAnchors = sourceMap.Node.AnchoredObjects.Select(a => NodeHash(a)).ToArray();
        var dependencies = new Dictionary<string, byte[]>();
        using (var zip = sourceMap.Node.OpenReadEmbeddedZipData())
        {
            Require(zip.Entries.Count == 4, "Expected source Item and exactly three dependencies");
            foreach (var name in Dependencies)
            {
                var entry = zip.Entries.Single(e => e.Name == name);
                using var s = entry.Open(); using var m = new MemoryStream(); s.CopyTo(m);
                var bytes = m.ToArray();
                Require(bytes.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(SourceFolder, "files", name))), "Source dependency differs from embedded dependency");
                dependencies[name] = bytes;
            }
            var embeddedItem = zip.Entries.Single(e => e.Name == SourceItemName);
            using var itemStream = embeddedItem.Open(); using var itemMemory = new MemoryStream(); itemStream.CopyTo(itemMemory);
            Require(itemMemory.ToArray().AsSpan().SequenceEqual(sourceItemBytes), "Source embedded Item differs");
        }
        var normalizedPairHashes = new List<string>();
        var rows = new List<object>();
        Directory.CreateDirectory(output);
        foreach (var isKinematic in new[] { false, true })
        {
            var mode = isKinematic ? "Kinematic" : "Ordinary";
            var doc = ReadItem(sourceItemBytes);
            var prefab = Prefab(doc.Node);
            var clothEntry = prefab.Ents![1];
            var cloth = (CPlugDynaObjectModel)clothEntry.Model!;
            var timing = (NPlugDynaObjectModel_SInstanceParams)clothEntry.Params!;
            var shape = new CPlugSurface { Surf = new CPlugSurface.Mesh {
                Version = 6, Vertices = [new(0,0,0), new(.02f,0,0), new(0,.02f,0)],
                Triangles = [new(new Int3(0,1,2), 0, 0, 0)] } };
            shape.CreateChunk<CPlugSurface.Chunk0900C003>().Version = 2;
            cloth.StaticShape = shape; cloth.DynaShape = shape;
            timing.IsKinematic = isKinematic;
            prefab.Ents = [prefab.Ents[0], clothEntry, new CPlugPrefab.EntRef {
                Rotation = new(0,0,0,1),
                Params = new NPlugDyna_SPrefabConstraintParams { Version = 0, Ent1 = -1, Ent2 = 1 },
                Model = new KC { Version = 0, SubVersion = 3,
                    TransAxis = KC.EAxis.Y, TransMin = 0, TransMax = 0,
                    RotAxis = KC.EAxis.Y, AngleMinDeg = 0, AngleMaxDeg = 0,
                    TransAnimFunc = Timeline(), RotAnimFunc = Timeline() } }];
            var title = "TMIS Skyward Packed Control " + mode;
            var itemStem = "TMIS_Skyward_PackedControl_" + mode;
            var itemName = itemStem + ".Item.Gbx";
            var mapName = itemStem + ".Map.Gbx";
            doc.Node.Name = title;
            doc.Node.Ident = doc.Node.Ident! with { Id = itemStem };
            var itemBytes = Save(new RefDoc<CGameItemModel>(doc.Node, doc.RefTable!));
            var parsed = ReadItem(itemBytes);
            var parsedPrefab = Prefab(parsed.Node);
            var parsedCloth = (CPlugDynaObjectModel)parsedPrefab.Ents![1].Model!;
            var parsedTiming = (NPlugDynaObjectModel_SInstanceParams)parsedPrefab.Ents[1].Params!;
            Require(parsedPrefab.Ents.Length == 3 && parsedTiming.IsKinematic == isKinematic, "Pair graph failed reparse");
            Require(EntryHash(parsedPrefab.Ents[0]) == planeEntryHash, "Complete static plane entry changed");
            Require(NodeHash(parsedCloth.Mesh!) == meshHash && parsedCloth.Mesh!.Visuals!.Select(NodeHash).SequenceEqual(visualHashes), "Complete cloth mesh changed");
            Require(ReferenceSignature(parsed.RefTable!) == referenceSignature, "Item reference paths, flags or UseFile changed: " + ReferenceSignature(parsed.RefTable!) + " != " + referenceSignature);
            Require(ReferenceEquals(parsedCloth.StaticShape, parsedCloth.DynaShape), "Expected shared diagnostic collision shape");
            var shapeHash = NodeHash(parsedCloth.StaticShape!);
            var constraintHash = EntryHash(parsedPrefab.Ents[2]);
            // Whole-Item normalization makes accidental differences fail loudly.
            parsed.Node.Name = sourceItem.Node.Name;
            parsed.Node.Ident = sourceItem.Node.Ident;
            parsedTiming.IsKinematic = sourceParams.IsKinematic;
            var normalizedPair = Save(new RefDoc<CGameItemModel>(parsed.Node, parsed.RefTable!));
            normalizedPairHashes.Add(Hash(normalizedPair));
            // Undo the common probe additions as well and recover the exact source graph.
            parsedCloth.StaticShape = sourceCloth.StaticShape; parsedCloth.DynaShape = sourceCloth.DynaShape;
            parsedPrefab.Ents = parsedPrefab.Ents.Take(2).ToArray();
            Require(Save(new RefDoc<CGameItemModel>(parsed.Node, parsed.RefTable!)).AsSpan().SequenceEqual(canonicalSource), "Fields outside names, IsKinematic, shape and constraint changed");

            var files = new Dictionary<string, byte[]>(dependencies) { [itemName] = itemBytes };
            var mapDoc = ReadMap(sourceMapBytes);
            var map = mapDoc.Node;
            var folder = "TMIS_SkywardPacked_" + mode + "_" + Guid.NewGuid().ToString("N")[..10];
            var identity = folder + "\\" + itemName;
            var anchor = map.AnchoredObjects![1];
            // The map anchor must carry the generated Item's collection/author as
            // well as its path. Changing only Id leaves a stale collector identity;
            // Trackmania then silently drops the custom anchor on load.
            anchor.ItemModel = new Ident(identity, sourceItem.Node.Ident!.Collection, sourceItem.Node.Ident.Author); anchor.AnchorTreeId = identity;
            map.MapName = title; map.MapUid = "TMISSKYPK_" + Guid.NewGuid().ToString("N")[..17]; map.EmbeddedZipData = null;
            map.UpdateEmbeddedZipData(zip => { foreach (var f in files) { using var s = zip.CreateEntry("Items/" + folder + "/" + f.Key, CompressionLevel.Optimal).Open(); s.Write(f.Value); } });
            var mapBytes = Save(mapDoc);
            var mapCheck = ReadMap(mapBytes).Node;
            Require(mapCheck.AnchoredObjects!.Count == 2 && mapCheck.AnchoredObjects[0].ItemModel!.Id == "Flag16m", "Native primer/order changed");
            Require(mapCheck.AnchoredObjects[1].ItemModel!.Id == identity && mapCheck.ExpectedEmbeddedItemModels!.Single().Id == identity, "Embedded Item identity changed");
            using (var zip = mapCheck.OpenReadEmbeddedZipData())
            {
                Require(zip.Entries.Count == 4, "Embedded file count changed");
                foreach (var f in files) { using var s = zip.GetEntry("Items/" + folder + "/" + f.Key)!.Open(); using var m = new MemoryStream(); s.CopyTo(m); Require(m.ToArray().AsSpan().SequenceEqual(f.Value), "Embedded bytes changed"); }
            }
            mapCheck.AnchoredObjects[1].ItemModel = sourceMap.Node.AnchoredObjects[1].ItemModel;
            mapCheck.AnchoredObjects[1].AnchorTreeId = sourceMap.Node.AnchoredObjects[1].AnchorTreeId;
            Require(mapCheck.AnchoredObjects.Select(a => NodeHash(a)).SequenceEqual(sourceAnchors), "Anchor fields/transforms changed");
            var destination = Path.Combine(output, mode); Directory.CreateDirectory(destination); Directory.CreateDirectory(Path.Combine(destination, "files"));
            foreach (var f in files) File.WriteAllBytes(Path.Combine(destination, "files", f.Key), f.Value);
            File.WriteAllBytes(Path.Combine(destination, mapName), mapBytes);
            var row = new { mode, isKinematic, outputMap = Path.Combine(destination, mapName), mapSha256 = Hash(mapBytes), outputItem = Path.Combine(destination,"files",itemName), itemSha256 = Hash(itemBytes),
                embeddedFolder = folder, expectedEmbeddedItemId = identity, nativePrimerIndex = 0, anchorCount = 2, anchorPosition = anchor.AbsolutePositionInMap,
                completeStaticPlaneEntrySha256 = planeEntryHash, completeClothMeshSha256 = meshHash, completeAnimatedVisualHashes = visualHashes,
                commonCollisionSha256 = shapeHash, commonConstraintEntrySha256 = constraintHash,
                timing.Version, timing.PeriodSc, timing.PeriodScMax, timing.Phase01, timing.Phase01Max, timing.TextureId,
                normalizedWholeItemSha256 = Hash(normalizedPair), sourceRestoredExactly = true, anchorsRestoredExactly = true,
                embeddedFiles = files.Select(f => new { name = f.Key, sha256 = Hash(f.Value), bytes = f.Value.Length }) };
            File.WriteAllText(Path.Combine(destination,"manifest.json"), JsonSerializer.Serialize(row,Json) + "\n"); rows.Add(row);
        }
        Require(normalizedPairHashes.Distinct().Count() == 1, "Whole pair differs beyond names, identity and IsKinematic");
        Require(Hash(File.ReadAllBytes(sourceMapPath)) == SourceMapHash && Hash(File.ReadAllBytes(sourceItemPath)) == SourceItemHash, "Source was modified");
        var manifest = new { operation = "offline-skyward-kinematic-packed-control", runtimeTested = false,
            scope = "Two isolated embedded editor fixtures. Native Flag16m is first in each. Ordinary and kinematic candidates differ only in IsKinematic, display names and file identities. No native render/packed-buffer verdict.",
            sourceMapPath, sourceMapSha256 = SourceMapHash, sourceItemPath, sourceItemSha256 = SourceItemHash,
            normalizedWholeItemSha256 = normalizedPairHashes[0], pairNormalizationExact = true, fullSourceRestorationExact = true,
            commonChanges = "Cloth-only shared tiny diagnostic collision and world-to-cloth zero translation/rotation constraint (12,000 ms linear functions); plane and all five complete animated cloth visuals unchanged.", rows };
        File.WriteAllText(Path.Combine(output,"manifest.json"), JsonSerializer.Serialize(manifest,Json) + "\n");
        File.WriteAllText(Path.Combine(output,"README.md"), Report);
        Console.WriteLine(JsonSerializer.Serialize(manifest,Json));
    }
    static KC.AnimFunc Timeline() => new() { IsDuration = true, SubFuncs = [new() { Ease = KC.AnimEase.Linear, Reverse = false, Duration = new TimeInt32(12000) }] };
    static Gbx<CGameItemModel> ReadItem(byte[] bytes) => Gbx.Parse<CGameItemModel>(new MemoryStream(bytes));
    static Gbx<CGameCtnChallenge> ReadMap(byte[] bytes) => Gbx.Parse<CGameCtnChallenge>(new MemoryStream(bytes), MapSettings);
    static CPlugPrefab Prefab(CGameItemModel item) => (CPlugPrefab)((NPlugItem_SVariantList)item.EntityModel!).Variants![0].EntityModel!;
    static string EntryHash(CPlugPrefab.EntRef entry) => NodeHash(new CPlugPrefab { Version = 11, Ents = [entry] });
    static string ReferenceSignature(GbxRefTable table) => JsonSerializer.Serialize(new { table.AncestorLevel, files = table.Files.Select(f => new { f.Flags, f.UseFile, f.FilePath }) });
    static byte[] Save(Gbx doc) { using var m = new MemoryStream(); doc.Save(m); return m.ToArray(); }
    static string NodeHash<T>(T node) where T : CMwNod => Hash(Save(new Gbx<T>(node)));
    static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
    static void Require(bool success, string message) { if (!success) throw new InvalidDataException(message); }
    sealed class RefDoc<T> : Gbx<T> where T : CMwNod, new() { public RefDoc(T node, GbxRefTable table) : base(node) { RefTable = table; } }
    const string Report = """
    # Skyward ordinary/kinematic packed-timing control

    This is an offline-generated pair, not a runtime result. Each map retains native Flag16m as its first anchor and the accepted full blue/white one-UV native-WarpFlag Skyward banner at the original (903,128,450) anchor. Each embeds exactly its Item and the three byte-identical source dependencies. No loose files have been installed.

    Both candidates add the same tiny triangular collision surface to the cloth body's StaticShape and DynaShape, plus an identical world-to-cloth constraint (Ent1=-1, Ent2=1). Translation and rotation ranges are zero. Both linear functions last 12,000 ms. The diagnostic shape avoids the previously tested null-shape path; it is not production collision. The plane remains Static. All five complete 86-frame cloth visuals, their mesh/material graph and original timing values are unchanged.

    The only difference between candidates is cloth instance IsKinematic, display name and Item/map packaging identity. Whole-Item canonical serialization becomes byte-identical after restoring display name, Item ident and IsKinematic. Removing the common shape/constraint additions then recovers the exact canonical source Item bytes. Both map anchors recover identical serialized hashes after restoring the custom anchor's file identity. ZIP contents were reopened and verified byte-for-byte.

    The targeted game test must compare cloth movement and the cloth render instance's packed timing word in both fixtures, using exact source-model identity. An instanced kinematic object can have a moving transform while period code 2047 disables its vertex animation; freezing alone does not prove a noninstanced draw. These fixtures isolate that possibility without changing geometry, material, UV count, timing ranges, plane class or collision between the pair.
    """;
}
