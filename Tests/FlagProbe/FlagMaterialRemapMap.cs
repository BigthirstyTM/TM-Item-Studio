using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.Game;
using GBX.NET.Engines.GameData;

// Offline-only map packaging. External GBX dependencies are copied byte-for-byte.
internal static class FlagMaterialRemapMap
{
    static readonly string[] FixtureNames =
    [
        "TMIS_MatRemap_Placeholder.Material.Gbx",
        "TMIS_MatRemap.GameSkin.Gbx",
        "TMIS_MatRemap.TerrainModifier.Gbx",
        "TMIS_MatRemap_Control.Item.Gbx",
        "TMIS_MatRemap_Candidate.Item.Gbx"
    ];

    static readonly GbxReadSettings MapSettings = new() { SkipChunkIds = [0x0304305B] };
    const string NativePrimerSource = @"C:\Users\PC\OpenplanetNext\Extract\GameData\Stadium\Items\Flag16m.Item.Gbx";
    static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    public static int Create(string sourceMap, string fixtureFolder, string output, bool empty = false, bool nativePrimer = false)
    {
        if (empty && nativePrimer) throw new ArgumentException("A native-primer map must include the candidate anchor.");
        sourceMap = Path.GetFullPath(sourceMap);
        fixtureFolder = Path.GetFullPath(fixtureFolder);
        output = Path.GetFullPath(output);
        var manifestPath = output + ".manifest.json";
        if (File.Exists(output) || File.Exists(manifestPath))
            throw new IOException("Preserve previous evidence; choose a new output path.");

        var sourceHash = Hash(File.ReadAllBytes(sourceMap));
        var files = FixtureNames.Select(name => (Name: name,
            Bytes: File.ReadAllBytes(Path.Combine(fixtureFolder, name)))).ToArray();
        var candidate = Gbx.ParseHeaderNode<CGameItemModel>(
            new MemoryStream(files.Single(f => f.Name == FixtureNames[4]).Bytes));
        var ident = candidate.Ident ?? throw new InvalidDataException("Candidate has no item identity.");
        var primerBytes = nativePrimer ? File.ReadAllBytes(NativePrimerSource) : null;
        var primerIdent = primerBytes is null ? null : Gbx.ParseHeaderNode<CGameItemModel>(new MemoryStream(primerBytes)).Ident;
        if (nativePrimer && (primerIdent is null || string.IsNullOrEmpty(primerIdent.Id)))
            throw new InvalidDataException("The genuine native primer file must provide its stored catalog identity.");
        var doc = Gbx.Parse<CGameCtnChallenge>(sourceMap, MapSettings);
        var map = doc.Node;
        if (map.AnchoredObjects is null || map.AnchoredObjects.Count == 0 ||
            (map.EmbeddedZipData?.Length ?? 0) == 0)
            throw new InvalidDataException("Expected a healthy previously embedded proof map.");

        var token = Guid.NewGuid().ToString("N")[..20];
        var folder = "TMIS_MatRemap_Embedded_" + token;
        var candidateId = folder + "\\" + FixtureNames[4];
        map.MapName = (nativePrimer ? "TMIS Native Primer Material Remap " : empty ? "TMIS Material Remap Preload " : "TMIS Embedded Material Remap ") + token[..8];
        map.MapUid = "TMISMR_" + token;
        map.AnchoredObjects.Clear();
        CGameCtnAnchoredObject? primer = null;
        if (nativePrimer)
        {
            // Use the exact native catalog identity read from the extracted item,
            // not a custom-file path or an invented ID. List order is intentional:
            // native first, candidate second. Native build order remains a runtime
            // observation; serialized order alone does not establish that order.
            primer = new CGameCtnAnchoredObject
            {
                ItemModel = primerIdent!,
                AbsolutePositionInMap = new Vec3(16, 8, 16),
                YawPitchRoll = new Vec3(MathF.PI / 2, 0, 0),
                PivotPosition = new Vec3(0, 0, 0), Scale = 1,
                AnchorTreeId = primerIdent!.Id
            };
            primer.CreateChunk<CGameCtnAnchoredObject.Chunk03101002>().Version = 8;
            primer.CreateChunk<CGameCtnAnchoredObject.Chunk03101004>().Version = 0;
            map.AnchoredObjects.Add(primer);
        }
        var anchor = new CGameCtnAnchoredObject
        {
            ItemModel = new Ident(candidateId, ident.Collection, ident.Author),
            AbsolutePositionInMap = new Vec3(1460, 8, 1360),
            // GBX stores yaw, pitch, roll in XYZ order. E++ runtime Rot uses
            // pitch, yaw, roll; the native proof map stores (PI/2, 0, 0).
            YawPitchRoll = new Vec3(MathF.PI / 2, 0, 0),
            PivotPosition = new Vec3(0, 0, 0),
            Scale = 1,
            AnchorTreeId = candidateId
        };
        anchor.CreateChunk<CGameCtnAnchoredObject.Chunk03101002>().Version = 8;
        anchor.CreateChunk<CGameCtnAnchoredObject.Chunk03101004>().Version = 0;
        if (!empty) map.AnchoredObjects.Add(anchor);
        map.EmbeddedZipData = null;
        map.UpdateEmbeddedZipData(zip =>
        {
            foreach (var file in files)
            {
                using var stream = zip.CreateEntry("Items/" + folder + "/" + file.Name,
                    CompressionLevel.Optimal).Open();
                stream.Write(file.Bytes);
            }
        });
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        doc.Save(output);

        var saved = Gbx.Parse<CGameCtnChallenge>(output, MapSettings).Node;
        var expectedAnchorCount = empty ? 0 : nativePrimer ? 2 : 1;
        if (saved.AnchoredObjects?.Count != expectedAnchorCount)
            throw new InvalidDataException("Round-trip anchor count changed.");
        if (nativePrimer)
        {
            var first = saved.AnchoredObjects![0];
            if (first.ItemModel != primerIdent || first.AbsolutePositionInMap != primer!.AbsolutePositionInMap
                || first.YawPitchRoll != primer.YawPitchRoll || first.Scale != 1
                || first.AnchorTreeId != primerIdent!.Id)
                throw new InvalidDataException("Native primer identity, transform or first-anchor order changed.");
        }
        if (!empty)
        {
            var actual = saved.AnchoredObjects![nativePrimer ? 1 : 0];
            if (actual.ItemModel?.Id != candidateId ||
                actual.AbsolutePositionInMap != anchor.AbsolutePositionInMap ||
                actual.YawPitchRoll != anchor.YawPitchRoll || actual.Scale != 1)
                throw new InvalidDataException("Round-trip anchor identity or transform changed.");
        }
        using (var zip = saved.OpenReadEmbeddedZipData())
        {
            if (zip.Entries.Count != files.Length)
                throw new InvalidDataException("Round-trip must contain exactly five embedded files.");
            foreach (var file in files)
            {
                var entry = zip.GetEntry("Items/" + folder + "/" + file.Name)
                    ?? throw new InvalidDataException("Missing embedded dependency: " + file.Name);
                using var stream = entry.Open();
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                if (!memory.ToArray().AsSpan().SequenceEqual(file.Bytes))
                    throw new InvalidDataException("Embedded bytes changed: " + file.Name);
            }
        }
        var expectedIds = new[] { folder + "\\" + FixtureNames[3], candidateId };
        var actualIds = saved.ExpectedEmbeddedItemModels?.Select(i => i.Id).ToArray();
        if (actualIds is null || actualIds.Length != 2 ||
            !actualIds.Order().SequenceEqual(expectedIds.Order()))
            throw new InvalidDataException("Expected embedded item identity list was not rebuilt correctly.");
        if (Hash(File.ReadAllBytes(sourceMap)) != sourceHash)
            throw new InvalidDataException("Source map unexpectedly changed.");

        var manifest = new
        {
            manifestVersion = nativePrimer ? 4 : 3,
            operation = nativePrimer ? "material-remap-native-primer-map" : empty ? "material-remap-empty-map" : "material-remap-map",
            scope = empty
                ? "Offline preload-only packaging and GBX round-trip verified. No anchored objects; native shader loading must be accepted before any placement, drawing or model-build test."
                : nativePrimer ? "Offline native-primer comparison map only. Genuine native Flag16m is serialized first, the unmodified embedded v6 candidate second. This does not prove native build order, remapping, motion, cold-start success or standalone behavior."
                : "Offline packaging and GBX round-trip verified. Native loading, material remapping, rendering and clean-client behavior require separate in-game verification.",
            sourceMap, sourceSha256 = sourceHash, fixtureFolder, output,
            outputSha256 = Hash(File.ReadAllBytes(output)),
            map.MapName, map.MapUid,
            embeddedFolder = folder,
            anchoredObjectCount = saved.AnchoredObjects!.Count,
            nativePrimer = primer is null ? null : new {
                sourceFile = NativePrimerSource, sourceSha256 = Hash(primerBytes!),
                identityReadFromNativeHeader = true, itemId = primerIdent!.Id,
                collection = primerIdent.Collection.ToString(), author = primerIdent.Author,
                anchorIndex = 0, position = new[] { 16, 8, 16 },
                serializedYawPitchRoll = new[] { MathF.PI / 2, 0, 0 }, scale = 1,
                embedded = false,
                purpose = "Test whether loading a genuine native flag in the same map primes the v6 material-remapping route"
            },
            anchor = empty ? null : new { itemId = candidateId, collection = ident.Collection.ToString(),
                author = ident.Author, position = new[] { 1460, 8, 1360 },
                anchorIndex = nativePrimer ? 1 : 0,
                yawRadians = MathF.PI / 2, pitchRadians = 0, rollRadians = 0,
                serializedYawPitchRoll = new[] { MathF.PI / 2, 0, 0 }, scale = 1 },
            expectedEmbeddedItemIds = actualIds,
            nativeReplacement = @"Stadium\Media\Material\ItemFlag.Material.Gbx",
            nativeReplacementEmbedded = false,
            embeddedFiles = files.Select(file => new { file.Name, bytes = file.Bytes.Length,
                sha256 = Hash(file.Bytes), zipPath = "Items/" + folder + "/" + file.Name }),
            validation = new { sourceUnchanged = true, expectedAnchorCount,
                anchorCountVerified = true,
                nativePrimerIdentityFromExtractedHeader = nativePrimer,
                nativePrimerFirstCandidateSecond = nativePrimer,
                nativePrimerNotEmbedded = nativePrimer,
                exactFiveEmbeddedFiles = true, allEmbeddedBytesUnchanged = true,
                twoExpectedItemIdentities = true, isolatedFolder = true }
        };
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(manifestPath, json);
        Console.WriteLine(json);
        return 0;
    }
}
