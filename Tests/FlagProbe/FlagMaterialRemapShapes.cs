using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.Game;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;

// Offline shape expansion of the accepted v6 material-remapping graph. Reuse
// complete accepted shape visuals, not a second deformation of the swallowtail.
internal static class FlagMaterialRemapShapes
{
    const string Placeholder = "TMIS_MatRemap_Placeholder.Material.Gbx";
    const string Skin = "TMIS_MatRemap.GameSkin.Gbx";
    const string Modifier = "TMIS_MatRemap.TerrainModifier.Gbx";
    const string Candidate = "TMIS_MatRemap_Candidate.Item.Gbx";
    static readonly (string Shape, string Source)[] Shapes = [
        ("Swallowtail", "TMIS_Step07_Swallowtail.Item.Gbx"),
        ("Long250", "TMIS_Step06_Long250.Item.Gbx"),
        ("Round", "TMIS_Step08_Round.Item.Gbx")
    ];
    static readonly GbxReadSettings MapSettings = new() { SkipChunkIds = [0x0304305B] };

    public static int NativeLastClone(string sourceMap, string output)
    {
        sourceMap = Path.GetFullPath(sourceMap);
        output = Path.GetFullPath(output);
        Require(!File.Exists(output) && !Directory.Exists(output) && !File.Exists(output + ".manifest.json"),
            "Preserve previous evidence; choose a new map output path");
        var sourceBytes = File.ReadAllBytes(sourceMap);
        var doc = Gbx.Parse<CGameCtnChallenge>(new MemoryStream(sourceBytes), MapSettings);
        var map = doc.Node;
        Require(map.AnchoredObjects is { Count: 4 } && map.EmbeddedZipData is { Length: > 0 },
            "Expected the native-first map with three custom flags");
        var ids = map.ExpectedEmbeddedItemModels?.Select(i => i.Id).ToArray()
            ?? throw new InvalidDataException("Expected three embedded item identities");
        Require(ids.Length == 3 && map.AnchoredObjects![0].ItemModel?.Id == "Flag16m"
            && map.AnchoredObjects.Skip(1).All(a => ids.Contains(a.ItemModel?.Id)),
            "Expected native Flag16m first and three custom anchors after it");
        var before = map.AnchoredObjects!.Select(a => new {
            a.ItemModel, a.AnchorTreeId, position = a.AbsolutePositionInMap,
            a.YawPitchRoll, a.PivotPosition, a.Scale
        }).ToArray();
        var zipBytes = map.EmbeddedZipData!.ToArray();
        var payloads = new List<object>();
        using (var zip = map.OpenReadEmbeddedZipData())
        {
            Require(zip.Entries.Count == 6, "Expected exactly six embedded graph payloads");
            foreach (var entry in zip.Entries)
            {
                using var stream = entry.Open();
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                var bytes = memory.ToArray();
                payloads.Add(new { zipPath = entry.FullName, bytes = bytes.Length, sha256 = Hash(bytes) });
            }
        }
        var native = map.AnchoredObjects![0];
        map.AnchoredObjects.RemoveAt(0);
        map.AnchoredObjects.Add(native);
        var token = Guid.NewGuid().ToString("N")[..20];
        map.MapName = "TMIS NATIVE LAST Three Flag Shapes " + token[..8];
        map.MapUid = "TMISLAST_" + token;
        using var outputMemory = new MemoryStream();
        doc.Save(outputMemory);
        var outputBytes = outputMemory.ToArray();
        var saved = Gbx.Parse<CGameCtnChallenge>(new MemoryStream(outputBytes), MapSettings).Node;
        Require(saved.AnchoredObjects?.Count == 4, "Expected exactly four anchors after reordering");
        for (var i = 0; i < 4; i++)
        {
            var actual = saved.AnchoredObjects![i];
            var expected = before[(i + 1) % 4];
            Require(actual.ItemModel == expected.ItemModel && actual.AnchorTreeId == expected.AnchorTreeId
                && actual.AbsolutePositionInMap == expected.position && actual.YawPitchRoll == expected.YawPitchRoll
                && actual.PivotPosition == expected.PivotPosition && actual.Scale == expected.Scale,
                "Reordering changed an anchor property or custom-anchor order");
        }
        Require(saved.AnchoredObjects![3].ItemModel == before[0].ItemModel,
            "Native primer must be the last anchor at index three");
        Require(saved.EmbeddedZipData?.AsSpan().SequenceEqual(zipBytes) == true,
            "Native-last clone must preserve complete embedded ZIP bytes");
        Require(saved.ExpectedEmbeddedItemModels?.Select(i => i.Id).SequenceEqual(ids) == true,
            "Native-last clone changed expected embedded identities");
        Require(Hash(File.ReadAllBytes(sourceMap)) == Hash(sourceBytes), "Source map changed");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllBytes(output, outputBytes);
        File.WriteAllText(output + ".manifest.json", JsonSerializer.Serialize(new {
            manifestVersion = 1, operation = "material-remap-three-shapes-native-last-control",
            scope = "Offline native-anchor-order control only. Serialized order changes from native-first to native-last; runtime build order and cold-start behavior need separate native observation.",
            sourceMap, sourceSha256 = Hash(sourceBytes), output, outputSha256 = Hash(outputBytes), map.MapName, map.MapUid,
            nativePrimer = new { anchorIndex = 3, itemId = before[0].ItemModel.Id,
                collection = before[0].ItemModel.Collection.ToString(), author = before[0].ItemModel.Author,
                embedded = false, propertiesUnchanged = true },
            anchors = saved.AnchoredObjects.Select((a, i) => new { anchorIndex = i, itemId = a.ItemModel?.Id,
                position = new[] { a.AbsolutePositionInMap.X, a.AbsolutePositionInMap.Y, a.AbsolutePositionInMap.Z },
                serializedYawPitchRoll = new[] { a.YawPitchRoll.X, a.YawPitchRoll.Y, a.YawPitchRoll.Z },
                pivot = new[] { a.PivotPosition.X, a.PivotPosition.Y, a.PivotPosition.Z }, a.Scale, a.AnchorTreeId }),
            expectedEmbeddedIds = ids, embeddedFilesExpected = 6, embeddedFiles = payloads, embeddedZipSha256 = Hash(zipBytes),
            checks = new { nativeFirstMovedToIndexThree = true, customAnchorOrderPreserved = true,
                allFourAnchorPropertiesPreserved = true, allThreeShapesRetained = true,
                wholeEmbeddedZipByteExact = true, sixEmbeddedPayloadsUnchanged = true,
                expectedEmbeddedIdentitiesUnchanged = true, sourceMapUnchanged = true }
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Native-last control: {output}; SHA256 {Hash(outputBytes)}; four unchanged anchors reordered, six-file ZIP byte-exact; runtime untested");
        return 0;
    }

    public static int CloseupFramingClones(string sourceMap, string outputFolder)
    {
        sourceMap = Path.GetFullPath(sourceMap);
        outputFolder = Path.GetFullPath(outputFolder);
        Require(!Directory.Exists(outputFolder) && !File.Exists(outputFolder), "Preserve previous evidence; choose a new output folder");
        var sourceBytes = File.ReadAllBytes(sourceMap);
        var source = Gbx.Parse<CGameCtnChallenge>(new MemoryStream(sourceBytes), MapSettings).Node;
        Require(source.AnchoredObjects is { Count: 4 } && source.EmbeddedZipData is { Length: > 0 },
            "Expected native primer and all three custom flags");
        var ids = source.ExpectedEmbeddedItemModels?.Select(i => i.Id).ToArray()
            ?? throw new InvalidDataException("Expected embedded custom identities");
        Require(ids.Length == 3 && source.AnchoredObjects![0].ItemModel?.Id == "Flag16m"
            && source.AnchoredObjects.Skip(1).All(a => ids.Contains(a.ItemModel?.Id)),
            "Expected native Flag16m first and three custom anchors after it");
        var originalZip = source.EmbeddedZipData!.ToArray();
        var before = source.AnchoredObjects!.Select(a => new {
            a.ItemModel, a.AnchorTreeId, position = a.AbsolutePositionInMap,
            a.YawPitchRoll, a.PivotPosition, a.Scale
        }).ToArray();
        var rows = new List<object>();
        Directory.CreateDirectory(outputFolder);
        foreach (var (shape, label, deltaX) in new[] { ("Swallowtail", "SWALLOW", 20), ("Round", "ROUND", -20) })
        {
            var doc = Gbx.Parse<CGameCtnChallenge>(new MemoryStream(sourceBytes), MapSettings);
            var map = doc.Node;
            foreach (var anchor in map.AnchoredObjects!.Skip(1))
            {
                var p = anchor.AbsolutePositionInMap;
                anchor.AbsolutePositionInMap = new Vec3(p.X + deltaX, p.Y, p.Z);
            }
            var token = Guid.NewGuid().ToString("N")[..20];
            map.MapName = "TMIS CLOSEUP " + label + " " + token[..8];
            map.MapUid = "TMISCLOSE_" + token;
            using var memory = new MemoryStream();
            doc.Save(memory);
            var bytes = memory.ToArray();
            var saved = Gbx.Parse<CGameCtnChallenge>(new MemoryStream(bytes), MapSettings).Node;
            Require(saved.AnchoredObjects?.Count == 4, "Framing clone changed anchor count");
            for (var i = 0; i < before.Length; i++)
            {
                var a = saved.AnchoredObjects![i];
                var b = before[i];
                var expected = new Vec3(b.position.X + (i == 0 ? 0 : deltaX), b.position.Y, b.position.Z);
                Require(a.ItemModel == b.ItemModel && a.AnchorTreeId == b.AnchorTreeId
                    && a.AbsolutePositionInMap == expected && a.YawPitchRoll == b.YawPitchRoll
                    && a.PivotPosition == b.PivotPosition && a.Scale == b.Scale,
                    "Framing clone changed an identity, native primer or unrequested transform");
            }
            Require(saved.EmbeddedZipData?.AsSpan().SequenceEqual(originalZip) == true,
                "Framing clone must preserve the complete embedded ZIP byte-exactly");
            Require(saved.ExpectedEmbeddedItemModels?.Select(i => i.Id).SequenceEqual(ids) == true,
                "Framing clone changed expected embedded identities");
            var centered = saved.AnchoredObjects!.Single(a => a.ItemModel!.Id.EndsWith("TMIS_MatRemap_" + shape + ".Item.Gbx"));
            Require(centered.AbsolutePositionInMap == new Vec3(768, 128, 450), "Requested shape was not centered at the default camera axis");
            var output = Path.Combine(outputFolder, "TMIS_Closeup_" + shape + "_NativePrimer.Map.Gbx");
            File.WriteAllBytes(output, bytes);
            var manifest = new {
                manifestVersion = 1, operation = "material-remap-three-shapes-closeup-framing",
                scope = "Offline framing clone only. All three custom flags and the native primer are retained; viewport framing and motion require native observation.",
                sourceMap, sourceSha256 = Hash(sourceBytes), output, outputSha256 = Hash(bytes), map.MapName, map.MapUid,
                centeredShape = shape, customAnchorDelta = new[] { deltaX, 0, 0 },
                centeredPosition = new[] { 768, 128, 450 },
                anchors = saved.AnchoredObjects!.Select((a, i) => new { anchorIndex = i, itemId = a.ItemModel?.Id,
                    position = new[] { a.AbsolutePositionInMap.X, a.AbsolutePositionInMap.Y, a.AbsolutePositionInMap.Z },
                    serializedYawPitchRoll = new[] { a.YawPitchRoll.X, a.YawPitchRoll.Y, a.YawPitchRoll.Z }, a.Scale }),
                embeddedZipSha256 = Hash(originalZip), expectedEmbeddedIds = ids,
                checks = new { nativePrimerUnchangedAndFirst = true, allThreeShapesRetained = true,
                    onlyCustomAnchorXTranslated = true, customYAndZUnchanged = true,
                    identitiesRotationsPivotsScalesUnchanged = true, wholeEmbeddedZipByteExact = true,
                    expectedEmbeddedIdentitiesUnchanged = true, sourceMapUnchanged = true }
            };
            File.WriteAllText(output + ".manifest.json", JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
            rows.Add(new { shape, output, sha256 = Hash(bytes), map.MapName, map.MapUid, deltaX });
        }
        Require(Hash(File.ReadAllBytes(sourceMap)) == Hash(sourceBytes), "Source map changed");
        File.WriteAllText(Path.Combine(outputFolder, "manifest.json"), JsonSerializer.Serialize(new {
            operation = "material-remap-closeup-framing-pair", sourceMap, sourceSha256 = Hash(sourceBytes),
            embeddedZipSha256 = Hash(originalZip), maps = rows,
            scope = "Offline generation only; no item payload changes, staging or game calls"
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    public static int NativePrimerClone(string sourceMap, string output)
    {
        sourceMap = Path.GetFullPath(sourceMap);
        output = Path.GetFullPath(output);
        var manifestPath = output + ".manifest.json";
        if (File.Exists(output) || Directory.Exists(output) || File.Exists(manifestPath))
            throw new IOException("Preserve previous evidence; choose a new map output path");
        var sourceBytes = File.ReadAllBytes(sourceMap);
        var doc = Gbx.Parse<CGameCtnChallenge>(new MemoryStream(sourceBytes), MapSettings);
        var map = doc.Node;
        Require(map.AnchoredObjects is { Count: 3 } && map.EmbeddedZipData is { Length: > 0 },
            "Expected the three-shape embedded source map");
        var originalZip = map.EmbeddedZipData!.ToArray();
        var originalIds = map.ExpectedEmbeddedItemModels?.Select(i => i.Id).ToArray()
            ?? throw new InvalidDataException("Expected three embedded model identities");
        Require(originalIds.Length == 3, "Expected three embedded model identities");
        var before = map.AnchoredObjects!.Select(a => new {
            a.ItemModel, a.AnchorTreeId, position = a.AbsolutePositionInMap,
            a.YawPitchRoll, a.PivotPosition, a.Scale
        }).ToArray();
        Require(before.All(a => originalIds.Contains(a.ItemModel?.Id)), "Source anchor is not one of the embedded custom flags");
        var payloads = new List<object>();
        using (var zip = map.OpenReadEmbeddedZipData())
        {
            Require(zip.Entries.Count == 6, "Expected exactly six embedded source payloads");
            foreach (var entry in zip.Entries)
            {
                using var stream = entry.Open();
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                var payload = memory.ToArray();
                payloads.Add(new { zipPath = entry.FullName, bytes = payload.Length, sha256 = Hash(payload) });
            }
        }
        const string nativeSource = @"C:\Users\PC\OpenplanetNext\Extract\GameData\Stadium\Items\Flag16m.Item.Gbx";
        var nativeBytes = File.ReadAllBytes(nativeSource);
        var nativeIdent = Gbx.ParseHeaderNode<CGameItemModel>(new MemoryStream(nativeBytes)).Ident;
        Require(nativeIdent is not null && !string.IsNullOrEmpty(nativeIdent.Id),
            "Genuine native item must provide its catalog identity");
        var primer = new CGameCtnAnchoredObject {
            ItemModel = nativeIdent!, AnchorTreeId = nativeIdent!.Id,
            AbsolutePositionInMap = new Vec3(16, 8, 16),
            YawPitchRoll = new Vec3(MathF.PI / 2, 0, 0),
            PivotPosition = new Vec3(0, 0, 0), Scale = 1
        };
        primer.CreateChunk<CGameCtnAnchoredObject.Chunk03101002>().Version = 8;
        primer.CreateChunk<CGameCtnAnchoredObject.Chunk03101004>().Version = 0;
        map.AnchoredObjects!.Insert(0, primer);
        var token = Guid.NewGuid().ToString("N")[..20];
        map.MapName = "TMIS Native Primer Three Flag Shapes " + token[..8];
        map.MapUid = "TMIS3PRIMER_" + token;
        using var outputMemory = new MemoryStream();
        doc.Save(outputMemory);
        var bytes = outputMemory.ToArray();
        var saved = Gbx.Parse<CGameCtnChallenge>(new MemoryStream(bytes), MapSettings).Node;
        Require(saved.AnchoredObjects?.Count == 4, "Expected native primer followed by three custom anchors");
        var first = saved.AnchoredObjects![0];
        Require(first.ItemModel == nativeIdent && first.AnchorTreeId == nativeIdent.Id
            && first.AbsolutePositionInMap == primer.AbsolutePositionInMap && first.YawPitchRoll == primer.YawPitchRoll
            && first.PivotPosition == primer.PivotPosition && first.Scale == primer.Scale,
            "Native primer identity, order or transform changed");
        for (var i = 0; i < before.Length; i++)
        {
            var actual = saved.AnchoredObjects[i + 1];
            var expected = before[i];
            Require(actual.ItemModel == expected.ItemModel && actual.AnchorTreeId == expected.AnchorTreeId
                && actual.AbsolutePositionInMap == expected.position && actual.YawPitchRoll == expected.YawPitchRoll
                && actual.PivotPosition == expected.PivotPosition && actual.Scale == expected.Scale,
                "Custom anchor identity, order or transform changed");
        }
        Require(saved.EmbeddedZipData?.AsSpan().SequenceEqual(originalZip) == true,
            "Native primer clone must preserve the complete embedded ZIP bytes");
        Require(saved.ExpectedEmbeddedItemModels?.Select(i => i.Id).SequenceEqual(originalIds) == true,
            "Native primer clone must preserve the three embedded model identities");
        Require(Hash(File.ReadAllBytes(sourceMap)) == Hash(sourceBytes), "Source map changed");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllBytes(output, bytes);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(new {
            manifestVersion = 1, operation = "material-remap-three-shapes-native-primer-clone",
            scope = "Offline packaging and round-trip only. Serialized native-first anchor order does not establish runtime build order. Cold-start loading, all three remaps, visible motion and operation without Openplanet require separate native verification.",
            sourceMap, sourceSha256 = Hash(sourceBytes), output, outputSha256 = Hash(bytes), map.MapName, map.MapUid,
            nativePrimer = new { sourceFile = nativeSource, sourceSha256 = Hash(nativeBytes),
                identityReadFromNativeHeader = true, itemId = nativeIdent.Id,
                collection = nativeIdent.Collection.ToString(), author = nativeIdent.Author,
                anchorIndex = 0, position = new[] { 16, 8, 16 },
                serializedYawPitchRoll = new[] { MathF.PI / 2, 0, 0 }, scale = 1, embedded = false },
            customAnchors = saved.AnchoredObjects.Skip(1).Select((a, i) => new { anchorIndex = i + 1,
                itemId = a.ItemModel?.Id, position = new[] { a.AbsolutePositionInMap.X, a.AbsolutePositionInMap.Y, a.AbsolutePositionInMap.Z },
                serializedYawPitchRoll = new[] { a.YawPitchRoll.X, a.YawPitchRoll.Y, a.YawPitchRoll.Z }, a.Scale }),
            embeddedZipSha256 = Hash(originalZip), embeddedModelIds = originalIds, embeddedFiles = payloads,
            checks = new { nativePrimerFirst = true, genuineNativeIdentity = true, nativePrimerNotEmbedded = true,
                threeCustomIdentitiesAndTransformsUnchanged = true, customAnchorOrderUnchanged = true,
                exactSixEmbeddedPayloadsPreserved = true, completeEmbeddedZipByteExact = true,
                expectedEmbeddedIdentitiesUnchanged = true, sourceMapUnchanged = true }
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Native-primer three-shape map: {output}; four anchors, complete six-file embedded ZIP byte-exact; runtime untested");
        return 0;
    }

    public static int CameraFriendlyClone(string sourceMap, string output)
    {
        sourceMap = Path.GetFullPath(sourceMap);
        output = Path.GetFullPath(output);
        var manifestPath = output + ".manifest.json";
        if (File.Exists(output) || Directory.Exists(output) || File.Exists(manifestPath))
            throw new IOException("Preserve previous evidence; choose a new map output path");
        var sourceBytes = File.ReadAllBytes(sourceMap);
        var doc = Gbx.Parse<CGameCtnChallenge>(new MemoryStream(sourceBytes), MapSettings);
        var map = doc.Node;
        Require(map.AnchoredObjects is { Count: > 0 } && map.EmbeddedZipData is { Length: > 0 },
            "Expected an accepted map with ordinary anchors and an embedded archive");
        var originalZip = map.EmbeddedZipData!.ToArray();
        var originalIds = map.ExpectedEmbeddedItemModels?.Select(i => i.Id).ToArray()
            ?? throw new InvalidDataException("Expected embedded model identities");
        var before = map.AnchoredObjects!.Select(a => new {
            a.ItemModel, a.AnchorTreeId, position = a.AbsolutePositionInMap,
            a.YawPitchRoll, a.PivotPosition, a.Scale
        }).ToArray();
        var delta = new Vec3(-712, 120, -910);
        foreach (var anchor in map.AnchoredObjects!)
        {
            var p = anchor.AbsolutePositionInMap;
            anchor.AbsolutePositionInMap = new Vec3(p.X + delta.X, p.Y + delta.Y, p.Z + delta.Z);
        }
        var token = Guid.NewGuid().ToString("N")[..20];
        map.MapName = "TMIS Embedded Flags Near Camera " + token[..8];
        map.MapUid = "TMISNEAR_" + token;
        using var memory = new MemoryStream();
        doc.Save(memory);
        var bytes = memory.ToArray();
        var saved = Gbx.Parse<CGameCtnChallenge>(new MemoryStream(bytes), MapSettings).Node;
        Require(saved.AnchoredObjects?.Count == before.Length, "Translated anchor count changed");
        Require(saved.EmbeddedZipData?.AsSpan().SequenceEqual(originalZip) == true,
            "Camera clone must preserve the exact embedded ZIP bytes");
        Require(saved.ExpectedEmbeddedItemModels?.Select(i => i.Id).SequenceEqual(originalIds) == true,
            "Camera clone must preserve embedded model identities");
        for (var i = 0; i < before.Length; i++)
        {
            var anchor = saved.AnchoredObjects![i];
            var expected = before[i];
            Require(anchor.ItemModel == expected.ItemModel && anchor.AnchorTreeId == expected.AnchorTreeId
                && anchor.AbsolutePositionInMap == new Vec3(expected.position.X + delta.X, expected.position.Y + delta.Y, expected.position.Z + delta.Z)
                && anchor.YawPitchRoll == expected.YawPitchRoll && anchor.PivotPosition == expected.PivotPosition
                && anchor.Scale == expected.Scale, "Camera clone altered an identity or unrequested transform");
        }
        Require(Hash(File.ReadAllBytes(sourceMap)) == Hash(sourceBytes), "Source map changed");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllBytes(output, bytes);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(new {
            operation = "material-remap-camera-friendly-clone", sourceMap, sourceSha256 = Hash(sourceBytes),
            output, outputSha256 = Hash(bytes), map.MapName, map.MapUid,
            scope = "Offline translation and byte-preservation checks only; default-camera visibility requires native verification",
            delta = new[] { -712, 120, -910 },
            targetDefaultEditorCamera = new[] { 768f, 161.073f, 398.448f },
            anchors = saved.AnchoredObjects!.Select(a => new { itemId = a.ItemModel?.Id,
                position = new[] { a.AbsolutePositionInMap.X, a.AbsolutePositionInMap.Y, a.AbsolutePositionInMap.Z },
                yawPitchRoll = new[] { a.YawPitchRoll.X, a.YawPitchRoll.Y, a.YawPitchRoll.Z }, a.Scale }),
            embeddedZipSha256 = Hash(originalZip), embeddedModelIds = originalIds,
            checks = new { exactEmbeddedZipBytesPreserved = true, identitiesPreserved = true,
                rotationsPivotsScalesPreserved = true, allAnchorsTranslatedByExactDelta = true, sourceUnchanged = true }
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Camera-friendly embedded map clone: {output}; {before.Length} anchors translated, embedded ZIP byte-exact; runtime untested");
        return 0;
    }

    public static int Create(string sourceMap, string v6Folder, string shapeFolder, string output)
    {
        output = Path.GetFullPath(output);
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Preserve previous evidence; output must not exist");
        sourceMap = Path.GetFullPath(sourceMap);
        v6Folder = Path.GetFullPath(v6Folder);
        shapeFolder = Path.GetFullPath(shapeFolder);
        using var seedManifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(v6Folder, "manifest.json")));
        Require(seedManifest.RootElement.GetProperty("fixtureVersion").GetInt32() == 6, "Expected the audited v6 fixture");
        var inputMapBytes = File.ReadAllBytes(sourceMap);
        var candidateBytes = File.ReadAllBytes(Path.Combine(v6Folder, Candidate));
        var dependencies = new[] { Placeholder, Skin, Modifier }.ToDictionary(n => n, n => File.ReadAllBytes(Path.Combine(v6Folder, n)));
        var files = new Dictionary<string, byte[]>(dependencies);
        var rows = new List<object>();
        var itemNames = new List<string>();
        var identities = new List<Ident>();
        foreach (var (shape, source) in Shapes)
        {
            var sourceBytes = File.ReadAllBytes(Path.Combine(shapeFolder, source));
            var sourceItem = Gbx.Parse<CGameItemModel>(new MemoryStream(sourceBytes)).Node;
            var sourceVisuals = Mesh(sourceItem).Visuals!;
            Require(sourceVisuals.Count == 5, "Expected five source shape visuals");
            var sourceVisualHashes = VisualHashes(Mesh(sourceItem));
            var doc = Gbx.Parse<CGameItemModel>(new MemoryStream(candidateBytes));
            var item = doc.Node;
            var name = "TMIS_MatRemap_" + shape + ".Item.Gbx";
            item.Name = "TMIS Embedded " + shape;
            Require(item.Ident?.Id == "" && item.ArchetypeRef == "", "Preserve v6 ordinary user identity");
            // Each visual includes every frame's positions/normals, frame table,
            // UVs, index buffer, bounds and stream metadata from the accepted
            // deformation. Material routing lives on Solid2 and stays v6.
            Mesh(item).Visuals = sourceVisuals;
            using var memory = new MemoryStream();
            doc.Save(memory);
            var bytes = memory.ToArray();
            var reopened = Gbx.Parse<CGameItemModel>(new MemoryStream(bytes));
            Require(VisualHashes(Mesh(reopened.Node)).SequenceEqual(sourceVisualHashes), "Serialized shape visuals changed");
            ValidateReferences(reopened);
            Require(reopened.Node.Ident == item.Ident && reopened.Node.ArchetypeRef == "", "Shape identity changed");
            var quality = Mesh(reopened.Node).Visuals!.Cast<CPlugVisualIndexedTriangles>()
                .Select((visual, lod) => Quality(visual, lod)).ToArray();
            files.Add(name, bytes);
            itemNames.Add(name);
            identities.Add(item.Ident!);
            rows.Add(new { shape, source = Path.Combine(shapeFolder, source), sourceSha256 = Hash(sourceBytes),
                itemFile = name, itemSha256 = Hash(bytes), fiveCompleteVisualsSha256 = sourceVisualHashes,
                allVisualBytesMatchAcceptedShape = true, quality });
        }
        var mapDoc = Gbx.Parse<CGameCtnChallenge>(new MemoryStream(inputMapBytes), MapSettings);
        var map = mapDoc.Node;
        Require(map.AnchoredObjects is { Count: > 0 } && (map.EmbeddedZipData?.Length ?? 0) > 0,
            "Expected a healthy embedded proof map");
        var token = Guid.NewGuid().ToString("N")[..20];
        var embeddedFolder = "TMIS_MatRemap_Shapes_" + token;
        map.MapName = "TMIS Embedded Flag Shapes " + token[..8];
        map.MapUid = "TMISMRS_" + token;
        map.AnchoredObjects!.Clear();
        for (var i = 0; i < itemNames.Count; i++)
        {
            var id = embeddedFolder + "\\" + itemNames[i];
            var anchor = new CGameCtnAnchoredObject {
                ItemModel = new Ident(id, identities[i].Collection, identities[i].Author),
                AbsolutePositionInMap = new Vec3(1460 + i * 20, 8, 1360),
                YawPitchRoll = new Vec3(MathF.PI / 2, 0, 0),
                PivotPosition = new Vec3(0, 0, 0), Scale = 1, AnchorTreeId = id
            };
            anchor.CreateChunk<CGameCtnAnchoredObject.Chunk03101002>().Version = 8;
            anchor.CreateChunk<CGameCtnAnchoredObject.Chunk03101004>().Version = 0;
            map.AnchoredObjects.Add(anchor);
        }
        map.EmbeddedZipData = null;
        map.UpdateEmbeddedZipData(zip => {
            foreach (var (name, bytes) in files)
            {
                using var stream = zip.CreateEntry("Items/" + embeddedFolder + "/" + name, CompressionLevel.Optimal).Open();
                stream.Write(bytes);
            }
        });
        using var mapMemory = new MemoryStream();
        mapDoc.Save(mapMemory);
        var mapBytes = mapMemory.ToArray();
        var saved = Gbx.Parse<CGameCtnChallenge>(new MemoryStream(mapBytes), MapSettings).Node;
        Require(saved.AnchoredObjects?.Count == 3, "Expected three ordinary anchors");
        for (var i = 0; i < 3; i++)
        {
            var anchor = saved.AnchoredObjects![i];
            Require(anchor.ItemModel?.Id == embeddedFolder + "\\" + itemNames[i]
                && anchor.AbsolutePositionInMap == new Vec3(1460 + i * 20, 8, 1360)
                && anchor.YawPitchRoll == new Vec3(MathF.PI / 2, 0, 0) && anchor.Scale == 1,
                "Round-trip changed an ordinary anchor identity/transform");
        }
        using (var zip = saved.OpenReadEmbeddedZipData())
        {
            Require(zip.Entries.Count == 6, "Expected three items and three dependencies");
            foreach (var (name, bytes) in files)
            {
                var entry = zip.GetEntry("Items/" + embeddedFolder + "/" + name)
                    ?? throw new InvalidDataException("Missing embedded file " + name);
                using var stream = entry.Open();
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                Require(memory.ToArray().AsSpan().SequenceEqual(bytes), "Embedded bytes changed: " + name);
            }
        }
        var expectedIds = itemNames.Select(n => embeddedFolder + "\\" + n).Order().ToArray();
        Require(saved.ExpectedEmbeddedItemModels?.Select(i => i.Id).Order().SequenceEqual(expectedIds) == true,
            "Expected embedded model identity list differs");
        Require(Hash(File.ReadAllBytes(sourceMap)) == Hash(inputMapBytes), "Source map changed");
        foreach (var (name, bytes) in dependencies)
            Require(File.ReadAllBytes(Path.Combine(v6Folder, name)).AsSpan().SequenceEqual(bytes), "v6 dependency changed");

        Directory.CreateDirectory(output);
        var fixtureOutput = Path.Combine(output, "files");
        Directory.CreateDirectory(fixtureOutput);
        foreach (var (name, bytes) in files) File.WriteAllBytes(Path.Combine(fixtureOutput, name), bytes);
        var mapOutput = Path.Combine(output, "TMIS_Embedded_Flag_Shapes_v6.Map.Gbx");
        File.WriteAllBytes(mapOutput, mapBytes);
        var manifest = new {
            manifestVersion = 1, operation = "material-remap-three-shapes-map",
            scope = "Offline generation/serialization only. Three-shape native loading, remapping, motion, restart and clean-client behavior require separate in-game verification.",
            sourceMap, sourceMapSha256 = Hash(inputMapBytes), v6Folder,
            v6CandidateSha256 = Hash(candidateBytes), shapeFolder, output = mapOutput, outputSha256 = Hash(mapBytes),
            embeddedFolder, map.MapName, map.MapUid,
            transformation = "Complete serialized visual nodes copied from accepted native-writer shape fixtures; all 86 frames in every visual are retained, no repeated deformation or topology rewrite",
            shapes = rows,
            anchors = itemNames.Select((n, i) => new { shape = Shapes[i].Shape, itemFile = n,
                position = new[] { 1460 + i * 20, 8, 1360 }, yawPitchRoll = new[] { MathF.PI / 2, 0, 0 }, scale = 1 }),
            dependencies = dependencies.Select(f => new { file = f.Key, sha256 = Hash(f.Value), byteForByteV6 = true }),
            embeddedFiles = files.Select(f => new { file = f.Key, bytes = f.Value.Length, sha256 = Hash(f.Value) }),
            checks = new { allFifteenVisualsByteExactToShapeSources = true, allFrameAndIndexTablesPreserved = true,
                threeOrdinaryAnchors = true, exactSixEmbeddedFiles = true, allEmbeddedBytesExact = true,
                sourceMapAndV6DependenciesUnchanged = true },
            qualityLimits = new[] {
                "Round is a square-to-disc deformation of an existing rectangular cloth grid, not a circular remesh; its boundary is a polygon at each LOD",
                "Round maps the old straight pole edge to the left circular arc; full-edge pole attachment is not preserved, and the exact touching point exists only when its UV midpoint is sampled",
                "Baked wave displacement is retained, so individual animated frames need not be geometrically exact circles",
                "Round normals were recalculated by the original shape generator; long/swallowtail retain the original fixture normals",
                "Per-LOD metrics below measure all baked frames and preserved topology; they do not establish runtime culling, lighting or visible LOD quality"
            }
        };
        File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Three-shape v6 offline fixture: {mapOutput}; three ordinary anchors, six byte-verified embedded files; runtime untested");
        return 0;
    }

    static void ValidateReferences(Gbx<CGameItemModel> doc)
    {
        Require(doc.RefTable?.AncestorLevel == 0 && doc.RefTable.Files.Count == 2 && doc.RefTable.Resources.Count == 0,
            "Expected the unchanged v6 sibling dependency graph");
        Require(Mesh(doc.Node).Materials is { Count: 1 } && Mesh(doc.Node).Materials!.Get(0).File?.FilePath == Placeholder
            && Mesh(doc.Node).Materials!.Get(0).File?.UseFile == false && doc.Node.References.MaterialModifier.File?.FilePath == Modifier
            && doc.Node.References.MaterialModifier.File?.UseFile == false, "v6 material/modifier references changed");
        Require((Mesh(doc.Node).CustomMaterials?.Length ?? 0) == 0 && (Mesh(doc.Node).MaterialInsts?.Count ?? 0) == 0,
            "Unexpected late material route");
    }
    static object Quality(CPlugVisualIndexedTriangles v, int lod)
    {
        Require(v.SubVisuals?.Length == 86 && v.Vertices.Length % 86 == 0, "Expected all 86 baked frames in every visual");
        var count = v.Vertices.Length / 86;
        var indices = v.IndexBuffer!.Indices;
        Require(indices.Length % 3 == 0 && indices.All(i => i >= 0 && i < count), "Indices outside a frame");
        var edges = new Dictionary<(int, int), int>();
        void Edge(int a, int b) { var key = a < b ? (a, b) : (b, a); edges[key] = edges.GetValueOrDefault(key) + 1; }
        for (var i = 0; i < indices.Length; i += 3) { Edge(indices[i], indices[i + 1]); Edge(indices[i + 1], indices[i + 2]); Edge(indices[i + 2], indices[i]); }
        var minArea = double.PositiveInfinity;
        var maxArea = 0d;
        var degenerate = 0;
        var zeroNormals = 0;
        for (var frame = 0; frame < 86; frame++)
        {
            for (var i = 0; i < indices.Length; i += 3)
            {
                var a = v.Vertices[frame * count + indices[i]].Position;
                var b = v.Vertices[frame * count + indices[i + 1]].Position;
                var c = v.Vertices[frame * count + indices[i + 2]].Position;
                var ux = (double)b.X - a.X; var uy = (double)b.Y - a.Y; var uz = (double)b.Z - a.Z;
                var vx = (double)c.X - a.X; var vy = (double)c.Y - a.Y; var vz = (double)c.Z - a.Z;
                var x = uy * vz - uz * vy; var y = uz * vx - ux * vz; var z = ux * vy - uy * vx;
                var area = Math.Sqrt(x * x + y * y + z * z) * .5;
                minArea = Math.Min(minArea, area); maxArea = Math.Max(maxArea, area);
                if (area <= 1e-10) degenerate++;
            }
        }
        foreach (var p in v.Vertices)
        {
            Require(float.IsFinite(p.Position.X) && float.IsFinite(p.Position.Y) && float.IsFinite(p.Position.Z), "Nonfinite shape position");
            if (p.Normal is {} n && n.X * n.X + n.Y * n.Y + n.Z * n.Z <= 1e-12) zeroNormals++;
        }
        return new { lod, frames = 86, verticesPerFrame = count, trianglesPerFrame = indices.Length / 3,
            totalBakedVertices = v.Vertices.Length, boundaryEdgeCount = edges.Values.Count(n => n == 1),
            nonManifoldEdgeCount = edges.Values.Count(n => n > 2), degenerateTriangleFrames = degenerate,
            degenerateAreaThreshold = 1e-10, minTriangleAreaAllFrames = minArea, maxTriangleAreaAllFrames = maxArea,
            zeroLengthNormals = zeroNormals };
    }
    static CPlugSolid2Model Mesh(CGameItemModel item) =>
        ((CPlugDynaObjectModel)((CPlugPrefab)((NPlugItem_SVariantList)item.EntityModel!).Variants![0].EntityModel!).Ents![1].Model!).Mesh!;
    static string[] VisualHashes(CPlugSolid2Model mesh) => mesh.Visuals!.Cast<CPlugVisualIndexedTriangles>()
        .Select(v => { using var stream = new MemoryStream(); new Gbx<CPlugVisualIndexedTriangles>(v).Save(stream); return Hash(stream.ToArray()); }).ToArray();
    static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
