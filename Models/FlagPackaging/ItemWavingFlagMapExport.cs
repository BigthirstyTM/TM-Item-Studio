using System.Text.Json;
using GBX.NET;

namespace TM_Item_Studio.Models;

using FlagPackaging;

public sealed record ItemWavingFlagMapExportResult(byte[] Bytes, string FileName, string ReportJson, IReadOnlyList<string> Warnings);

/// <summary>Restores one exported waving flag's embedded sibling references after a native map save.</summary>
/// <remarks>This bounded, in-memory operation does not add a native flag, validate a track or prove rendering.</remarks>
public static class ItemWavingFlagMapExport
{
    static readonly Dictionary<string, string> DependencyHashes = new(StringComparer.Ordinal) {
        ["TMIS_MatRemap_Placeholder.Material.Gbx"] = "CB3B703C2B5BED9F359919C59B24A08730EE833FC5E422C40062DA061CDA588A",
        ["TMIS_MatRemap.GameSkin.Gbx"] = "DC6118300D7C4FDBA9DAA737130B45249A5E46F1AA56AC17A6D7F658CFA6859E",
        ["TMIS_MatRemap.TerrainModifier.Gbx"] = "D276FA3E545347EC01533401B9A44A8EC2254BCDE9C2C7BFA4FAFC7CE20D942B"
    };

    /// <exception cref="InvalidDataException">Inputs do not match the bounded flag format or cannot be matched unambiguously.</exception>
    public static ItemWavingFlagMapExportResult Repair(byte[] savedMap, byte[] flagPackageZip, string? sourceName = null)
    {
        ArgumentNullException.ThrowIfNull(savedMap); ArgumentNullException.ThrowIfNull(flagPackageZip);
        try { return RepairCore(savedMap, flagPackageZip, sourceName); }
        catch (InvalidDataException) { throw; }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or OperationCanceledException))
        {
            throw new InvalidDataException("Waving flag map packaging refused malformed or unsupported input: " + error.Message, error);
        }
    }

    static ItemWavingFlagMapExportResult RepairCore(byte[] savedMap, byte[] packageZip, string? sourceName)
    {
        PackagingGuard.That(savedMap.Length is > 0 and <= PackagingGuard.MaxMapBytes && packageZip.Length is > 0 and <= PackagingGuard.MaxFileBytes,
            "Map or source package exceeds the supported size limit");
        Gbx.LZO ??= new GBX.NET.LZO.MiniLZO();
        var originalMapHash = PackagingGuard.Hash(savedMap); var originalPackageHash = PackagingGuard.Hash(packageZip);
        var package = PackagingGuard.ReadZip(packageZip);
        var itemEntries = package.Where(p => p.Key.EndsWith(".Item.Gbx", StringComparison.OrdinalIgnoreCase)).ToArray();
        PackagingGuard.That(itemEntries.Length == 1, "Source package must contain exactly one flag Item");
        var sourceItem = itemEntries.Single(); var sourcePath = PackagingGuard.CanonicalPath(sourceItem.Key).Split('/');
        PackagingGuard.That(sourcePath.Length == 2 || sourcePath.Length == 3 && sourcePath[0] == "Items",
            "Source flag Item must be inside one package folder");
        var itemName = sourcePath[^1]; var sourceFolder = sourceItem.Key[..sourceItem.Key.LastIndexOfAny(['/', '\\'])];
        var dependencies = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var dependency in DependencyHashes)
        {
            var path = sourceFolder + "/" + dependency.Key;
            var matches = package.Where(p => PackagingGuard.CanonicalPath(p.Key) == PackagingGuard.CanonicalPath(path)).ToArray();
            PackagingGuard.That(matches.Length == 1 && PackagingGuard.Hash(matches[0].Value) == dependency.Value,
                "Source package dependency fails the pinned flag hash: " + dependency.Key);
            dependencies.Add(dependency.Key, matches[0].Value);
        }
        var expectedGbxPaths = new[] { PackagingGuard.CanonicalPath(sourceItem.Key) }
            .Concat(DependencyHashes.Keys.Select(name => PackagingGuard.CanonicalPath(sourceFolder + "/" + name))).ToHashSet(StringComparer.Ordinal);
        PackagingGuard.That(package.Keys.Where(n => n.EndsWith(".Gbx", StringComparison.OrdinalIgnoreCase))
            .All(n => expectedGbxPaths.Contains(PackagingGuard.CanonicalPath(n))), "Source package contains unexpected GBX dependencies");
        var sourceDocument = FlagItemArchive.ValidatePackageItem(sourceItem.Value);
        var sourceGraph = FlagItemGraph.Inspect(sourceDocument);

        var document = new MapArchive(savedMap); var map = document.Map;
        var nativeCount = map.AnchoredObjects?.Count(a => a.ItemModel?.Id == "Flag16m" && a.ItemModel.Author == "Nadeo"
            && (a.ItemModel.Collection.Number == 26 || a.ItemModel.Collection.String == "Stadium2020")) ?? 0;
        PackagingGuard.That(nativeCount > 0, "Map must already contain a genuine native Flag16m by Nadeo; this operation never adds one");
        PackagingGuard.That(map.EmbeddedZipData is { Length: > 0 }, "Map has no embedded item ZIP");
        var files = PackagingGuard.ReadZip(map.EmbeddedZipData!); var rawZip = new RawZip(map.EmbeddedZipData!);
        var candidates = files.Where(p => p.Key.Replace('\\', '/').Split('/').Last().Equals(itemName, StringComparison.OrdinalIgnoreCase)).ToArray();
        PackagingGuard.That(candidates.Length == 1, candidates.Length == 0
            ? "Map contains no embedded Item matching the source package basename"
            : "Map contains ambiguous duplicate Item basenames; select a uniquely named flag package");
        var selected = candidates.Single();
        var repaired = FlagItemArchive.Normalize(selected.Key, selected.Value);
        PackagingGuard.That(repaired.Document.Node.Ident?.Author == sourceDocument.Node.Ident?.Author,
            "Embedded flag author does not match the source package");
        sourceGraph.RequireMatch(FlagItemGraph.Inspect(repaired.Document));
        var selectedPath = PackagingGuard.CanonicalPath(selected.Key); var expectedId = selectedPath["Items/".Length..].Replace('/', '\\');
        PackagingGuard.That(map.AnchoredObjects!.Any(a => a.ItemModel?.Id == expectedId
            && a.ItemModel.Author == sourceDocument.Node.Ident!.Author), "Matching embedded flag has no corresponding placed map anchor");
        PackagingGuard.That(map.ExpectedEmbeddedItemModels?.Any(i => i.Id == expectedId && i.Author == sourceDocument.Node.Ident!.Author) == true,
            "Matching flag is absent from the map's embedded identity list");
        var folder = selected.Key[..selected.Key.LastIndexOfAny(['/', '\\'])];
        var changes = new Dictionary<string, byte[]>(); var additions = new Dictionary<string, byte[]>();
        if (!selected.Value.AsSpan().SequenceEqual(repaired.Bytes)) changes.Add(selected.Key, repaired.Bytes);
        foreach (var dependency in dependencies)
        {
            var path = folder + "/" + dependency.Key;
            var existing = files.Where(p => PackagingGuard.CanonicalPath(p.Key).Equals(PackagingGuard.CanonicalPath(path), StringComparison.OrdinalIgnoreCase)).ToArray();
            if (existing.Length == 0) additions.Add(path, dependency.Value);
            else PackagingGuard.That(existing.Length == 1 && existing[0].Value.AsSpan().SequenceEqual(dependency.Value),
                "Existing embedded dependency conflicts with the exact source package: " + dependency.Key);
        }
        var outputZip = changes.Count == 0 && additions.Count == 0 ? map.EmbeddedZipData! : rawZip.Replace(changes, additions);
        var outputFiles = PackagingGuard.ReadZip(outputZip);
        PackagingGuard.That(outputFiles.Count == files.Count + additions.Count, "Unexpected embedded entry count after repair");
        foreach (var file in files) PackagingGuard.That(outputFiles[file.Key].AsSpan().SequenceEqual(changes.GetValueOrDefault(file.Key, file.Value)),
            "An unrelated embedded payload changed");
        foreach (var addition in additions) PackagingGuard.That(outputFiles[addition.Key].AsSpan().SequenceEqual(addition.Value), "Restored dependency bytes changed");
        byte[] output; object mapEvidence;
        if (ReferenceEquals(outputZip, map.EmbeddedZipData))
        {
            output = savedMap.ToArray(); mapEvidence = new { alreadyNormalized = true, entireMapByteExact = true, mapNodeSerializationPerformed = false };
        }
        else
        {
            var splice = document.ReplaceZip(outputZip); output = splice.Output; mapEvidence = splice.Evidence;
        }
        PackagingGuard.That(PackagingGuard.Hash(savedMap) == originalMapHash && PackagingGuard.Hash(packageZip) == originalPackageHash,
            "An input buffer changed during packaging");
        var warnings = new[] {
            "Keep at least one genuine native Flag16m by Nadeo in the map; custom-only rendering is not established.",
            "Only the selected flag is repaired. Other embedded items remain unchanged.",
            "Native validation data and unknown map bytes are preserved. Play and cold-reload this exact output to verify loading, validation acceptance and waving.",
            "A later native map save may rewrite these references or omit dependencies. Repair the newly saved map again before sharing."
        };
        var fileName = OutputName(sourceName);
        var report = JsonSerializer.Serialize(new {
            formatVersion = 1, operation = "waving-flag-native-map-export-repair", sourceName, fileName,
            sourceMapSha256 = originalMapHash, sourcePackageSha256 = originalPackageHash,
            outputSha256 = PackagingGuard.Hash(output), outputBytes = output.Length,
            map.MapName, map.MapUid, nativeFlag16mCount = nativeCount, nativeFlagAdded = false,
            selectedEmbeddedItem = selected.Key, sourcePackageItem = sourceItem.Key,
            sourceGraph.StructureHash, sourceGraph.VisualHashes, sourceGraph.VerticesPerFrame, sourceGraph.TrianglesPerFrame,
            exactDecodedGeometryAndGraphMatched = true, repairedItemCount = changes.Count, restoredDependencyCount = additions.Count,
            dependencyHashes = DependencyHashes, itemArchiveSplice = repaired.Evidence, mapArchiveSplice = mapEvidence,
            embeddedFiles = outputFiles.Select(p => new { zipPath = p.Key, bytes = p.Value.Length, sha256 = PackagingGuard.Hash(p.Value) }),
            preservation = new { itemHeaderAndRawBodyByteExact = true, validationAndUnknownMapBytesPreserved = true,
                unrelatedZipRecordsPreserved = true, inputBuffersUnchanged = true, runtimeWritesPerformed = false,
                nativeValidationAcceptanceTested = false, nativeRenderingTested = false }, warnings
        }, new JsonSerializerOptions { WriteIndented = true });
        return new(output, fileName, report, warnings);
    }

    static string OutputName(string? sourceName)
    {
        var name = (sourceName ?? "CustomFlags").Replace('\\', '/').Split('/').Last();
        if (name.EndsWith(".Map.Gbx", StringComparison.OrdinalIgnoreCase)) name = name[..^8];
        else if (name.EndsWith(".Gbx", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        name = new string(name.Where(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or ' ').Take(100).ToArray()).Trim();
        if (name.Length == 0) name = "CustomFlags";
        return name + "_EmbeddedFlags.Map.Gbx";
    }
}





