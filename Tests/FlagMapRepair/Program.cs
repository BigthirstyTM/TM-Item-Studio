using System.IO.Compression;
using System.IO.Hashing;
using System.Security.Cryptography;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.Game;

try
{
    if (args.Length is not (3 or 5) || args[0] != "--repair" || (args.Length == 5 && args[3] != "--dependencies"))
        throw new ArgumentException("Usage: dotnet run --project Tests/FlagMapRepair -- --repair <native-saved-map> <new-output-map> [--dependencies <original-v6-package-folder>]");
    Gbx.LZO = new GBX.NET.LZO.MiniLZO();
    var source = Path.GetFullPath(args[1]); var output = Path.GetFullPath(args[2]);
    Check.That(!source.Equals(output, StringComparison.OrdinalIgnoreCase), "Output must differ from source");
    Check.That(!File.Exists(output) && !Directory.Exists(output) && !File.Exists(output + ".manifest.json") && !Directory.Exists(output + ".manifest.json"), "Refusing to overwrite output or adjacent manifest");
    var package = Path.GetFullPath(args.Length == 5 ? args[4] : Path.Combine(FindRoot(), "artifacts/flag-selfcontained-20261022/material-remap-v6-audited"));
    var hashes = new Dictionary<string, string> {
        ["TMIS_MatRemap_Placeholder.Material.Gbx"] = "CB3B703C2B5BED9F359919C59B24A08730EE833FC5E422C40062DA061CDA588A",
        ["TMIS_MatRemap.GameSkin.Gbx"] = "DC6118300D7C4FDBA9DAA737130B45249A5E46F1AA56AC17A6D7F658CFA6859E",
        ["TMIS_MatRemap.TerrainModifier.Gbx"] = "D276FA3E545347EC01533401B9A44A8EC2254BCDE9C2C7BFA4FAFC7CE20D942B"
    };
    var dependencies = hashes.ToDictionary(p => p.Key, p => File.ReadAllBytes(Path.Combine(package, p.Key)));
    foreach (var p in dependencies) Check.That(Check.Hash(p.Value) == hashes[p.Key], "Dependency package fails pinned v6 hash: " + p.Key);
    var sourceBytes = File.ReadAllBytes(source); var document = new MapArchive(sourceBytes); var map = document.Map;
    var primerCount = map.AnchoredObjects!.Count(a => a.ItemModel?.Id == "Flag16m" && a.ItemModel.Author == "Nadeo"
        && (a.ItemModel.Collection.Number == 26 || a.ItemModel.Collection.String == "Stadium2020"));
    Check.That(primerCount > 0, "Map must already contain a genuine native Flag16m/Nadeo anchor; this tool never inserts a primer");
    Check.That(map.EmbeddedZipData is { Length: > 0 }, "Map has no embedded ZIP");
    var rawZip = new RawZip(map.EmbeddedZipData!); var files = ReadZip(map.EmbeddedZipData!);
    var flags = files.Where(p => ItemArchive.IsKnownName(BaseName(p.Key))).ToArray();
    Check.That(flags.Length is >= 1 and <= 3 && flags.Select(p => BaseName(p.Key)).Distinct(StringComparer.OrdinalIgnoreCase).Count() == flags.Length, "Expected a subset of the three known shape Items, without duplicate basenames");
    var folders = flags.Select(p => p.Key.Replace('\\', '/')[..p.Key.Replace('\\', '/').LastIndexOf('/')]).Distinct().ToArray();
    Check.That(folders.Length == 1 && folders[0].StartsWith("Items/", StringComparison.Ordinal), "Known flags must share one Items ZIP folder");
    var changes = new Dictionary<string, byte[]>(); var added = new Dictionary<string, byte[]>(); var itemEvidence = new List<object>();
    foreach (var flag in flags)
    {
        var repaired = ItemArchive.Repair(flag.Key, flag.Value); itemEvidence.Add(repaired.Evidence);
        if (!repaired.Bytes.AsSpan().SequenceEqual(flag.Value)) changes.Add(flag.Key, repaired.Bytes);
    }
    foreach (var dependency in dependencies)
    {
        var zipPath = folders[0] + "/" + dependency.Key;
        if (files.TryGetValue(zipPath, out var existing)) Check.That(existing.AsSpan().SequenceEqual(dependency.Value), "Existing dependency is not the exact accepted v6 file: " + zipPath);
        else added.Add(zipPath, dependency.Value);
    }
    var outputZip = changes.Count == 0 && added.Count == 0 ? map.EmbeddedZipData! : rawZip.Replace(changes, added);
    var zipFiles = ReadZip(outputZip); Check.That(zipFiles.Count == files.Count + added.Count, "Embedded entry count changed unexpectedly");
    foreach (var p in files) Check.That(zipFiles.TryGetValue(p.Key, out var value) && value.AsSpan().SequenceEqual(changes.GetValueOrDefault(p.Key, p.Value)), "Original embedded payload changed unexpectedly");
    foreach (var p in added) Check.That(zipFiles[p.Key].AsSpan().SequenceEqual(p.Value), "Restored dependency payload mismatch");
    var result = document.ReplaceZip(outputZip); var reparsed = new MapArchive(result.Output);
    var beforeState = State(map); var afterState = State(reparsed.Map);
    var stateJson = new JsonSerializerOptions { IncludeFields = true };
    Check.That(JsonSerializer.Serialize(beforeState, stateJson) == JsonSerializer.Serialize(afterState, stateJson), "Parsed map fields, validation metadata, blocks or anchors changed");
    Check.That(File.ReadAllBytes(source).AsSpan().SequenceEqual(sourceBytes), "Source map changed during repair");
    foreach (var p in dependencies) Check.That(File.ReadAllBytes(Path.Combine(package, p.Key)).AsSpan().SequenceEqual(p.Value), "Dependency package changed during repair");
    var manifest = new {
        manifestVersion = 1, operation = "bounded-known-custom-flag-native-export-repair",
        scope = "Supports only the three pinned TMIS material-remap shape assets. Preserves serialized validation data; native validation acceptance, loading, gameplay, animation and independent-client behavior require game tests for this output.",
        sourceMap = source, sourceSha256 = Check.Hash(sourceBytes), outputMap = output, outputBytes = result.Output.Length, outputSha256 = Check.Hash(result.Output),
        map.MapName, map.MapUid, embeddedFolder = folders[0]["Items/".Length..], anchoredObjectCount = map.AnchoredObjects!.Count,
        nativePrimerPresent = true, nativePrimerCount = primerCount, nativePrimerAdded = false,
        knownFlagPayloads = flags.Length, repairedItemCount = changes.Count, restoredDependencyCount = added.Count,
        expectedEmbeddedItemIds = map.ExpectedEmbeddedItemModels?.Select(i => i.Id),
        embeddedFiles = zipFiles.Select(p => new { zipPath = p.Key, bytes = p.Value.Length, sha256 = Check.Hash(p.Value) }),
        sourceEmbeddedZipSha256 = Check.Hash(map.EmbeddedZipData!), outputEmbeddedZipSha256 = Check.Hash(outputZip),
        originalDependencyPackage = package, dependencyHashes = hashes, itemArchiveSplices = itemEvidence,
        mapArchiveSplice = result.Evidence, sourceAndOutputParsedState = beforeState,
        preservation = new { entireOriginalMapHeaderAndRefTableByteExact = true, originalNonZipUncompressedMapBodyExactExceptThreeLengthWords = true,
            serializedValidationBlocksAnchorsAndUnknownChunksPreserved = true, expectedEmbeddedIdentityListRawBytesPreserved = true,
            everyOtherZipLocalRecordCompressedPayloadExtraFieldsAndCommentExact = true,
            everyOtherZipCentralRecordExactExceptRelocatedOffset = true, sourceMapAndDependencyPackageUnchanged = true,
            itemHeaderAndRawBodiesExact = true, allDependencyFilesPinnedByteExact = true, mapOrItemOrDependencySerializationPerformed = false,
            nativePrimerFabricated = false, gameOrStagingOperationsPerformed = false },
        knownLimits = new[] { "Only the three exact native-written flag raw bodies and known ancestor3 or sibling reference layouts are accepted.",
            "A genuine native Flag16m/Nadeo anchor must already exist; no shader initialization helper is added.",
            "ZIP64, split, ambiguous or unsupported ZIP layouts are refused.",
            "Raw file preservation does not establish native validation acceptance after changing embedded content. Open, play and cold-reload this exact output in the game." }
    };
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    using (var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write)) stream.Write(result.Output);
    using (var stream = new FileStream(output + ".manifest.json", FileMode.CreateNew, FileAccess.Write)) JsonSerializer.Serialize(stream, manifest, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true });
    Check.That(Check.Hash(File.ReadAllBytes(output)) == Check.Hash(result.Output) && File.ReadAllBytes(source).AsSpan().SequenceEqual(sourceBytes), "Final source/output hash check failed");
    Console.WriteLine(JsonSerializer.Serialize(new { outputMap = output, outputSha256 = Check.Hash(result.Output), outputBytes = result.Output.Length,
        repairedItemCount = changes.Count, restoredDependencyCount = added.Count, nativePrimerCount = primerCount, serializedValidationPreserved = true, nativeValidationAcceptanceTested = false }));
}
catch (Exception error) { Console.Error.WriteLine("Repair refused: " + error.Message); Environment.ExitCode = 2; }

static string FindRoot()
{
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "TM-Item-Studio.csproj"))) return dir.FullName;
    throw new DirectoryNotFoundException("Cannot locate repository; pass --dependencies with the original v6 package folder");
}
static string BaseName(string path) => path.Replace('\\', '/').Split('/').Last();
static Dictionary<string, byte[]> ReadZip(byte[] bytes)
{
    using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read); var result = new Dictionary<string, byte[]>(); long total = 0;
    foreach (var entry in zip.Entries)
    {
        Check.That(entry.Length is >= 0 and <= 268435456 && (total += entry.Length) <= 536870912, "Embedded payload size exceeds bounded repair");
        using var input = entry.Open(); using var memory = new MemoryStream(); var buffer = new byte[65536];
        for (var read = input.Read(buffer); read != 0; read = input.Read(buffer))
        {
            Check.That(memory.Length + read <= entry.Length, "Embedded payload expands beyond its declared length"); memory.Write(buffer, 0, read);
        }
        var data = memory.ToArray();
        Check.That(data.Length == entry.Length && Crc32.HashToUInt32(data) == entry.Crc32, "Embedded payload length/CRC mismatch"); result.Add(entry.FullName, data);
    }
    return result;
}
static object State(CGameCtnChallenge m) => new { m.MapName, m.MapUid, xmlSha256 = Check.Hash(System.Text.Encoding.UTF8.GetBytes(m.Xml ?? "")),
    authorTime = m.AuthorTime?.TotalMilliseconds, goldTime = m.GoldTime?.TotalMilliseconds, silverTime = m.SilverTime?.TotalMilliseconds, bronzeTime = m.BronzeTime?.TotalMilliseconds,
    m.NbCheckpoints, m.IsLapRace, m.NbLaps, scriptValidated = m.ChallengeParameters?.IsValidatedForScriptModes, hasValidationGhost = m.ChallengeParameters?.RaceValidateGhost is not null,
    expected = m.ExpectedEmbeddedItemModels, blocks = m.Blocks?.Select(b => new { b.Name, b.Coord, b.Direction, b.Flags, b.Variant, b.SubVariant }),
    baked = m.BakedBlocks?.Select(b => new { b.Name, b.Coord, b.Direction, b.Flags, b.Variant, b.SubVariant }),
    anchors = m.AnchoredObjects?.Select(a => new { a.ItemModel, a.AnchorTreeId, a.AbsolutePositionInMap, a.YawPitchRoll, a.PivotPosition, a.Scale, a.Flags,
        a.Color, a.AnimPhaseOffset, a.LightmapQuality, a.SnappedOnGroup }) };

internal static class Check
{
    public static void That(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
