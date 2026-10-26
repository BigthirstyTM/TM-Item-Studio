using System.Diagnostics;
using System.Text.Json;
using GBX.NET;

// Header-only corpus diagnostic. It never resolves refs or loads nodes in the game.
internal static class FlagResourceAudit
{
    public static int Run(string root, string output)
    {
        root = Path.GetFullPath(root);
        output = Path.GetFullPath(output);
        if (File.Exists(output)) throw new IOException("Refuse evidence overwrite: " + output);
        var watch = Stopwatch.StartNew();
        var resources = new List<object>();
        var failures = new List<object>();
        var classes = new Dictionary<string, int>();
        var extensions = new Dictionary<string, int>();
        var scanned = 0; var parsed = 0; var withFileRefs = 0; var fileRefs = 0; var resourceRefs = 0; var hookParsed = 0;
        var extractionFallbacks = new List<object>();
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(".gbx", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase))
        {
            scanned++;
            var relative = Path.GetRelativePath(root, path);
            try
            {
                Gbx gbx;
                try { gbx = ReadValidated(path, hook: false); }
                catch (Exception normalError)
                {
                    // Some Openplanet extracts retain a user-data length but omit
                    // those bytes. Never silently count a misaligned ref table.
                    try { gbx = ReadValidated(path, hook: true); }
                    catch (Exception hookError)
                    {
                        throw new InvalidDataException("Normal: " + normalError.Message + " | Hook: " + hookError.Message);
                    }
                    hookParsed++;
                    extractionFallbacks.Add(new { file = relative, normalError = normalError.Message });
                }
                parsed++;
                var classId = gbx.Header.ClassId.ToString("X8");
                classes[classId] = classes.GetValueOrDefault(classId) + 1;
                var suffix = Path.GetExtension(Path.GetFileNameWithoutExtension(path)).ToLowerInvariant();
                extensions[suffix] = extensions.GetValueOrDefault(suffix) + 1;
                var refs = gbx.RefTable;
                if (refs?.Files.Count > 0) withFileRefs++;
                fileRefs += refs?.Files.Count ?? 0;
                if (refs?.Resources.Count > 0)
                {
                    resourceRefs += refs.Resources.Count;
                    resources.Add(new { file = relative, classId, ancestorLevel = refs.AncestorLevel,
                        entries = refs.Resources.Select(r => new { r.Flags, r.UseFile, r.NodeIndex, r.ResourceIndex }).ToArray() });
                }
            }
            catch (Exception ex)
            {
                failures.Add(new { file = relative, type = ex.GetType().FullName, ex.Message });
            }
        }
        var result = new { purpose = "Read-only header scan; no game preload, no body parsing, no reference resolution",
            root, utc = DateTime.UtcNow, elapsedSeconds = watch.Elapsed.TotalSeconds,
            scanned, parsed, hookParsed, withFileRefs, fileRefs, resourceRefs, resourceFiles = resources.Count,
            classes = classes.OrderBy(p => p.Key).ToDictionary(),
            suffixes = extensions.OrderBy(p => p.Key).ToDictionary(), resources, extractionFallbacks, failures };
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(new { scanned, parsed, hookParsed, withFileRefs, fileRefs,
            resourceRefs, resourceFiles = resources.Count, failures = failures.Count, elapsedSeconds = watch.Elapsed.TotalSeconds, output }));
        return 0;
    }

    static Gbx ReadValidated(string path, bool hook)
    {
        var gbx = Gbx.ParseHeader(path, new GbxReadSettings { SkipUserData = true, OpenPlanetHookExtractMode = hook });
        var refs = gbx.RefTable;
        if (refs is null) return gbx;
        if (refs.AncestorLevel < 0 || refs.AncestorLevel > 64)
            throw new InvalidDataException("Implausible reference ancestor level: " + refs.AncestorLevel);
        foreach (var entry in refs.Files.Cast<GBX.NET.Components.UnlinkedGbxRefTableNode>().Concat(refs.Resources))
            if (entry.NodeIndex < 1 || entry.NodeIndex >= gbx.Header.NumNodes || entry.Flags < 0 || entry.Flags > 15)
                throw new InvalidDataException($"Implausible reference record: node={entry.NodeIndex}/{gbx.Header.NumNodes}, flags={entry.Flags}");
        if (refs.Resources.Any(r => r.ResourceIndex < 0))
            throw new InvalidDataException("Negative resource index");
        return gbx;
    }
}
