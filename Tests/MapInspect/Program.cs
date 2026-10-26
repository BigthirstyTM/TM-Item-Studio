using System.IO.Compression;
using System.Security.Cryptography;
using GBX.NET;
using GBX.NET.Engines.Game;

Gbx.LZO = new GBX.NET.LZO.MiniLZO();
if (args.Length == 0) throw new ArgumentException("Usage: MapInspect <map> [output-dir]");
var path = args[0];
if (args.Length >= 4 && args[1].Equals("--merge-dir", StringComparison.OrdinalIgnoreCase))
{
    var mergeRoot = args[2];
    var mergedOutput = args[3];
    var mergeDocument = Gbx.Parse<CGameCtnChallenge>(path, new GbxReadSettings { SafeSkippableChunks = true });
    var mergeMap = mergeDocument.Node ?? throw new InvalidOperationException("Map has no node.");
    mergeMap.UpdateEmbeddedZipData(zip =>
    {
        foreach (var source in Directory.GetFiles(mergeRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(mergeRoot, source).Replace(Path.DirectorySeparatorChar, '/');
            if (relative.EndsWith(".Item.Gbx", StringComparison.OrdinalIgnoreCase)) continue;
            using var entry = zip.CreateEntry(relative, CompressionLevel.Optimal).Open();
            using var input = File.OpenRead(source);
            input.CopyTo(entry);
        }
    });
    mergeDocument.Save(mergedOutput);
    Console.WriteLine($"merged={mergedOutput} embeddedBytes={mergeMap.EmbeddedZipData?.Length ?? 0}");
    return;
}
if (args.Length >= 4 && args[1].Equals("--raw-merge-dir", StringComparison.OrdinalIgnoreCase))
{
    var mergeRoot = args[2];
    var mergedOutput = args[3];
    var sourceBytes = File.ReadAllBytes(path);
    var rawArchive = new MapArchive(sourceBytes);
    var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
    using (var oldZip = new ZipArchive(new MemoryStream(rawArchive.Map.EmbeddedZipData!, writable: false), ZipArchiveMode.Read))
    {
        foreach (var entry in oldZip.Entries)
        {
            using var input = entry.Open();
            using var memory = new MemoryStream();
            input.CopyTo(memory);
            files[entry.FullName] = memory.ToArray();
        }
    }
    foreach (var source in Directory.GetFiles(mergeRoot, "*", SearchOption.AllDirectories))
    {
        var relative = Path.GetRelativePath(mergeRoot, source).Replace(Path.DirectorySeparatorChar, '/');
        files[relative] = File.ReadAllBytes(source);
    }
    using var zipMemory = new MemoryStream();
    using (var zip = new ZipArchive(zipMemory, ZipArchiveMode.Create, true))
    {
        foreach (var file in files.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            using var output = zip.CreateEntry(file.Key, CompressionLevel.Optimal).Open();
            output.Write(file.Value);
        }
    }
    var result = rawArchive.ReplaceZip(zipMemory.ToArray()).Output;
    File.WriteAllBytes(mergedOutput, result);
    Console.WriteLine($"rawMerged={mergedOutput} bytes={result.Length} embeddedBytes={zipMemory.Length}");
    return;
}
var outputDir = args.Length > 1 ? args[1] : Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + ".embedded");
var document = Gbx.Parse<CGameCtnChallenge>(path, new GbxReadSettings { SafeSkippableChunks = true });
var map = document.Node ?? throw new InvalidOperationException("Map has no node.");
Console.WriteLine($"map={map.MapName} anchors={map.AnchoredObjects?.Count ?? 0} embeddedBytes={map.EmbeddedZipData?.Length ?? 0}");
if (map.AnchoredObjects is not null)
    foreach (var anchor in map.AnchoredObjects)
        Console.WriteLine($"anchor id={anchor.ItemModel?.Id} collection={anchor.ItemModel?.Collection} author={anchor.ItemModel?.Author} pos={anchor.AbsolutePositionInMap}");
if (map.EmbeddedZipData is not { Length: > 0 } zipBytes) return;
Directory.CreateDirectory(outputDir);
using var archive = new ZipArchive(new MemoryStream(zipBytes, writable: false), ZipArchiveMode.Read);
foreach (var entry in archive.Entries)
{
    // Native map ZIPs may use absolute game-folder names. Never let an archive
    // entry escape the inspection output directory or overwrite installed items.
    var segments = entry.FullName.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
    if (segments.Any(segment => segment is "." or ".."))
        throw new InvalidDataException("Unsafe embedded path: " + entry.FullName);
    var relative = Path.Combine(segments.Select(segment => segment.Replace(':', '_')).ToArray());
    var root = Path.GetFullPath(outputDir) + Path.DirectorySeparatorChar;
    var destination = Path.GetFullPath(Path.Combine(root, relative));
    if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException("Embedded path escapes the output directory: " + entry.FullName);
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    using var input = entry.Open();
    using var bytes = new MemoryStream();
    input.CopyTo(bytes);
    var data = bytes.ToArray();
    File.WriteAllBytes(destination, data);
    Console.WriteLine($"file={entry.FullName} bytes={data.Length} sha256={Convert.ToHexString(SHA256.HashData(data))}");
}
