using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.GameData;
using TM_Item_Studio.Models.FlagPackaging;
using TM_Item_Studio.Models.FxPackaging;

namespace TM_Item_Studio.Models;

public sealed record FxMapPackagingTarget(string ItemRelativePath, string Author, int Placements);
public sealed record FxMapPackagingResult(byte[] Bytes, string FileName, string ReportJson, IReadOnlyList<string> Warnings);

/// <summary>Packages exact supported FX-item bytes at an existing map anchor's explicit path.</summary>
/// <remarks>Repeat after every native editor save. This operation never adds anchors or proves native rendering.</remarks>
public static class FxMapPackaging
{
    static readonly string[] SparklerTextures = [@"Stadium\Media\Texture\Sparkler_D.Texture.gbx", @"Stadium\Media\Texture\SparklerEnd_D.Texture.gbx"];
    static readonly string[] FoggerTextures = [@"Stadium\Media\Texture\FoggerSmoke.Texture.gbx"];

    public static IReadOnlyList<FxMapPackagingTarget> FindTargets(byte[] savedMap, byte[] originalFxItem, string originalItemFileName)
        => Guard(() => {
            ArgumentNullException.ThrowIfNull(originalItemFileName);
            Initialize(); var source = InspectSource(originalFxItem); var archive = new FxMapArchive(savedMap);
            var name = originalItemFileName.Replace('\\', '/').Split('/').Last();
            PackagingGuard.That(name.Length > 9 && name.EndsWith(".Item.Gbx", StringComparison.OrdinalIgnoreCase)
                && PackagingGuard.CanonicalPath(name) == name, "Source filename must name an Item.Gbx");
            var matches = Targets(archive, source).Where(target => target.ItemRelativePath.Split('/').Last() == name).ToArray();
            return (IReadOnlyList<FxMapPackagingTarget>)Array.AsReadOnly(matches);
        });

    public static FxMapPackagingResult Repair(byte[] savedMap, byte[] originalFxItem, string itemRelativePath, string? sourceName = null)
        => Guard(() => RepairCore(savedMap, originalFxItem, itemRelativePath, sourceName));

    static FxMapPackagingResult RepairCore(byte[] savedMap, byte[] originalFxItem, string itemRelativePath, string? sourceName)
    {
        Initialize(); var source = InspectSource(originalFxItem); var archive = new FxMapArchive(savedMap);
        var sourceMapHash = PackagingGuard.Hash(savedMap); var sourceItemHash = PackagingGuard.Hash(originalFxItem);
        var relative = ItemPath(itemRelativePath);
        PackagingGuard.That(source.Id is null || ItemPath(source.Id) == relative, "Source item's stored full identity differs from the selected anchor path");
        var matches = Targets(archive, source).Where(target => target.ItemRelativePath == relative).ToArray();
        PackagingGuard.That(matches.Length == 1, "Map has no exact matching placed FX anchor with the source author and collection");
        var target = matches.Single(); var zipPath = "Items/" + relative;
        var files = archive.Embedded.Zip.Length == 0 ? new Dictionary<string, byte[]>(StringComparer.Ordinal) : PackagingGuard.ReadZip(archive.Embedded.Zip);
        var aliases = files.Where(file => PackagingGuard.CanonicalPath(file.Key).Equals(zipPath, StringComparison.OrdinalIgnoreCase)).ToArray();
        PackagingGuard.That(aliases.Length <= 1 && aliases.All(file => PackagingGuard.CanonicalPath(file.Key) == zipPath
            && !file.Key.EndsWith('/') && !file.Key.EndsWith('\\')), "Selected embedded ZIP path has a case collision or names a directory");
        var previous = aliases.SingleOrDefault();
        if (previous.Key is not null) RequireExistingIdentity(previous.Value, relative, source.Author);
        var identities = archive.Embedded.Identities.Where(identity => FxEmbeddedFiles.CanonicalIdentity(identity.Id).Equals(relative, StringComparison.OrdinalIgnoreCase)).ToArray();
        PackagingGuard.That(identities.Length <= 1 && identities.All(identity => FxEmbeddedFiles.CanonicalIdentity(identity.Id) == relative
            && identity.Author == source.Author && identity.CollectionNumber == 26 && identity.CollectionString is null),
            "Selected embedded metadata conflicts in path, author or collection");
        var addedMetadata = identities.Length == 0;
        var changedItem = previous.Key is null || !previous.Value.AsSpan().SequenceEqual(originalFxItem);
        byte[] outputZip;
        if (!changedItem) outputZip = archive.Embedded.Zip;
        else if (archive.Embedded.Zip.Length == 0) outputZip = NewZip(zipPath, originalFxItem);
        else
        {
            var replacements = new Dictionary<string, byte[]>(); var additions = new Dictionary<string, byte[]>();
            if (previous.Key is null) additions.Add(zipPath, originalFxItem); else replacements.Add(previous.Key, originalFxItem);
            outputZip = new RawZip(archive.Embedded.Zip).Replace(replacements, additions);
        }
        var outputFiles = PackagingGuard.ReadZip(outputZip);
        PackagingGuard.That(outputFiles.Count == files.Count + (previous.Key is null ? 1 : 0), "Unexpected embedded-file count after FX packaging");
        foreach (var file in files.Where(file => file.Key != previous.Key))
            PackagingGuard.That(outputFiles[file.Key].AsSpan().SequenceEqual(file.Value), "Unrelated embedded payload changed");
        var outputEntry = outputFiles.Single(file => PackagingGuard.CanonicalPath(file.Key) == zipPath);
        PackagingGuard.That(outputEntry.Value.AsSpan().SequenceEqual(originalFxItem), "Packaged FX item is not byte exact");
        byte[] output; object evidence;
        if (!changedItem && !addedMetadata)
        { output = savedMap.ToArray(); evidence = new { alreadyPackaged = true, entireMapByteExact = true, mapNodeSerializationPerformed = false }; }
        else
        {
            var addition = addedMetadata ? new FxEmbeddedFiles.Identity(relative.Replace('/', '\\'), 26, null, source.Author) : null;
            var payload = archive.Embedded.Replace(outputZip, addition);
            (output, evidence) = archive.Replace(payload);
        }
        PackagingGuard.That(PackagingGuard.Hash(savedMap) == sourceMapHash && PackagingGuard.Hash(originalFxItem) == sourceItemHash, "Input buffer changed during FX packaging");
        var warnings = new[] {
            "Package again after every native editor save; the native editor may omit the embedded FX item.",
            "Only the selected item is restored. This operation does not add anchors, alter track validation or prove native loading or rendered particles."
        };
        var name = OutputName(sourceName);
        var report = JsonSerializer.Serialize(new {
            formatVersion = 1, operation = "exact-fx-item-map-packaging", sourceName, fileName = name,
            sourceMapSha256 = sourceMapHash, sourceItemSha256 = sourceItemHash, outputSha256 = PackagingGuard.Hash(output), outputBytes = output.Length,
            target, source.EmitterCount, source.TexturePaths, sourceItemStoredId = source.Id,
            previousEmbeddedItemSha256 = previous.Key is null ? null : PackagingGuard.Hash(previous.Value),
            addedItemCount = previous.Key is null ? 1 : 0, restoredItemCount = previous.Key is not null && changedItem ? 1 : 0,
            addedMetadataCount = addedMetadata ? 1 : 0, mapArchiveSplice = evidence,
            preservation = new { exactOriginalFxItemBytesEmbedded = true, unrelatedZipRecordsPreserved = true,
                inputBuffersUnchanged = true, nativeValidationAcceptanceTested = false, nativeRenderingTested = false, runtimeWritesPerformed = false }, warnings
        }, new JsonSerializerOptions { WriteIndented = true });
        return new(output, name, report, warnings);
    }

    sealed record Source(string? Id, string Author, int EmitterCount, IReadOnlyList<string> TexturePaths);
    static Source InspectSource(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        PackagingGuard.That(bytes.Length is > 0 and <= PackagingGuard.MaxFileBytes, "FX source item exceeds the supported size limit");
        var archive = new FxEmissionPoint.Archive(bytes);
        var bodyIdentity = FxIdentity.Read(bytes);
        var document = Gbx.ParseHeader<CGameItemModel>(new MemoryStream(bytes, writable: false));
        var header = document.Node.Ident;
        PackagingGuard.That(header is not null && header.Author == bodyIdentity.Author && header.Collection.Number == 26
            && bodyIdentity.Collection == 26 && (document.RefTable?.Files.Count ?? 0) == 0 && (document.RefTable?.Resources.Count ?? 0) == 0,
            "FX source requires matching Stadium header/body author and no external GBX references");
        PackagingGuard.That((string.IsNullOrEmpty(header!.Id) && bodyIdentity.Id is null)
            || (!string.IsNullOrEmpty(header.Id) && bodyIdentity.Id == header.Id), "FX source header and body identities differ");
        if (bodyIdentity.Id is not null) ItemPath(bodyIdentity.Id);
        var owner = FxEmitterTransform.Read(bytes); var native = owner.Native;
        var expected = native.Emitters.Count switch { 2 => SparklerTextures, 1 => FoggerTextures, _ => throw new InvalidDataException("Only supported one/two-emitter FX carriers can be packaged") };
        var paths = new List<string>();
        var graphEnd = native.Emitters[^1].LengthOffset;
        ReadOnlySpan<byte> signature = [0x02, 0x60, 0x0C, 0x09];
        for (var scan = native.FxGraphOffset; scan <= graphEnd - 16;)
        {
            var relative = archive.Body.AsSpan(scan, graphEnd - scan).IndexOf(signature); if (relative < 0) break;
            var at = scan + relative; scan = at + 1;
            PackagingGuard.That(UInt(archive.Body, at + 4) == 22 && UInt(archive.Body, at + 12) == 1,
                "FX packaging supports only validated version22 GPU models with builtin texture FID strings");
            var length = UInt(archive.Body, at + 16);
            PackagingGuard.That(length is > 0 and <= 256 && (long)at + 20 + length <= graphEnd, "Invalid builtin FX texture FID");
            var path = new UTF8Encoding(false, true).GetString(archive.Body, at + 20, checked((int)length));
            PackagingGuard.That(paths.Count < expected.Length && path == expected[paths.Count], "FX texture is outside the proven builtin Sparkler/Fogger whitelist");
            foreach (var slot in new[] { 84, 124, 164 }) PackagingGuard.That(UInt(archive.Body, checked(at + slot + (int)length)) == uint.MaxValue,
                "FX source has an unsupported additional bitmap dependency");
            paths.Add(path);
        }
        PackagingGuard.That(paths.Count == expected.Length, "FX GPU texture record count does not match its supported carrier");
        return new(bodyIdentity.Id, bodyIdentity.Author, native.Emitters.Count, paths.AsReadOnly());
    }

    static IReadOnlyList<FxMapPackagingTarget> Targets(FxMapArchive archive, Source source)
    {
        var anchors = archive.Map.AnchoredObjects?.Where(anchor => anchor.ItemModel?.Author == source.Author
            && anchor.ItemModel.Collection.Number == 26).ToArray() ?? [];
        var rows = new List<string>(); var casePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var anchor in anchors)
        {
            var id = anchor.ItemModel!.Id;
            if (!id.EndsWith(".Item.Gbx", StringComparison.OrdinalIgnoreCase)) continue;
            string path;
            try { path = ItemPath(id); }
            catch (InvalidDataException) { continue; } // Unrelated club/unsupported identifiers are never local package targets.
            if (source.Id is not null && ItemPath(source.Id) != path) continue;
            PackagingGuard.That(!casePaths.TryGetValue(path, out var old) || old == path, "Map has case-colliding FX anchor paths");
            casePaths[path] = path; rows.Add(path);
        }
        foreach (var anchor in archive.Map.AnchoredObjects ?? [])
        {
            var identity = anchor.ItemModel;
            if (identity is null || !identity.Id.EndsWith(".Item.Gbx", StringComparison.OrdinalIgnoreCase)) continue;
            var path = identity.Id.Replace('\\', '/');
            if (!casePaths.TryGetValue(path, out var selected)) continue;
            PackagingGuard.That(path == selected && identity.Author == source.Author && identity.Collection.Number == 26,
                "A selected FX anchor path collides with another author, collection or path case");
        }
        return rows.GroupBy(path => path, StringComparer.Ordinal).Select(group => new FxMapPackagingTarget(group.Key, source.Author, group.Count())).OrderBy(target => target.ItemRelativePath, StringComparer.Ordinal).ToArray();
    }

    static void RequireExistingIdentity(byte[] bytes, string relative, string author)
    {
        var body = FxIdentity.Read(bytes); var header = Gbx.ParseHeader<CGameItemModel>(new MemoryStream(bytes, writable: false)).Node.Ident;
        PackagingGuard.That(header is not null && header.Author == author && body.Author == author && header.Collection.Number == 26 && body.Collection == 26
            && (string.IsNullOrEmpty(header.Id) && body.Id is null || header.Id == body.Id && body.Id is not null && ItemPath(body.Id) == relative),
            "Existing embedded item identity or author conflicts with the original FX source");
    }
    static string ItemPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path); var canonical = PackagingGuard.CanonicalPath(path);
        PackagingGuard.That(!path.EndsWith('/') && !path.EndsWith('\\')
            && !canonical.StartsWith("Items/", StringComparison.OrdinalIgnoreCase) && canonical.EndsWith(".Item.Gbx", StringComparison.OrdinalIgnoreCase),
            "Select an Items-relative path ending in .Item.Gbx (without an Items/ prefix)");
        return canonical;
    }
    static byte[] NewZip(string name, byte[] bytes)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        { var entry = zip.CreateEntry(name, CompressionLevel.Optimal); entry.LastWriteTime = new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.Zero); using var stream = entry.Open(); stream.Write(bytes); }
        return memory.ToArray();
    }
    static uint UInt(byte[] bytes, int at) { PackagingGuard.That(at >= 0 && at <= bytes.Length - 4, "Truncated FX GPU model"); return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at)); }
    static void Initialize() => Gbx.LZO ??= new GBX.NET.LZO.MiniLZO();
    static T Guard<T>(Func<T> action)
    {
        try { return action(); }
        catch (InvalidDataException) { throw; }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or OperationCanceledException))
        { throw new InvalidDataException("FX map packaging refused malformed or unsupported input: " + error.Message, error); }
    }
    static string OutputName(string? sourceName)
    {
        var name = (sourceName ?? "CustomFX").Replace('\\', '/').Split('/').Last();
        if (name.EndsWith(".Map.Gbx", StringComparison.OrdinalIgnoreCase)) name = name[..^8];
        else if (name.EndsWith(".Gbx", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        name = new string(name.Where(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or ' ').Take(100).ToArray()).Trim();
        return (name.Length == 0 ? "CustomFX" : name) + "_EmbeddedFX.Map.Gbx";
    }
}





