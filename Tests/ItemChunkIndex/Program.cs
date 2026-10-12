using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Managers;
using GBX.NET.Serialization.Chunking;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Text;

// Usage: ItemChunkIndex <rootDir> <outDir> [--limit=N] [--jobs=N]
//
// Walks every *.Item.Gbx under rootDir, parses it with SafeSkippableChunks (so unknown
// chunks keep their raw payload), and aggregates per chunk-id:
//   - which class owns it, in which order it appears, chunk C# type, status
//     (parsed/ignored/skipped/unknown), versions, payload sizes, field value samples
//   - per-owner ordered chunk-id sequences (the on-disk read order)
//   - item feature correlations (item type, waypoint, entity model, icon, placement)
//   - raw payload analysis for unknown chunks (embedded strings, class-id candidates)

if (args.Length < 2)
{
    Console.WriteLine("usage: ItemChunkIndex <rootDir> <outDir> [--limit=N] [--jobs=N]");
    return 1;
}

var root = args[0];
var outDir = args[1];
int limit = int.MaxValue;
int jobs = Math.Max(1, Environment.ProcessorCount - 1);
var deepIds = new HashSet<uint>();
foreach (var a in args)
{
    if (a.StartsWith("--limit=")) limit = int.Parse(a["--limit=".Length..]);
    if (a.StartsWith("--jobs=")) jobs = Math.Max(1, int.Parse(a["--jobs=".Length..]));
    if (a.StartsWith("--deep="))
        foreach (var s in a["--deep=".Length..].Split(','))
            deepIds.Add(Convert.ToUInt32(s.Trim(), 16));
}

Gbx.LZO = new GBX.NET.LZO.MiniLZO();

var files = Directory.EnumerateFiles(root, "*.Item.Gbx", SearchOption.AllDirectories)
    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
    .Take(limit)
    .ToArray();
Console.WriteLine($"corpus: {files.Length} item files, jobs={jobs}");

Directory.CreateDirectory(outDir);
var samplesDir = Path.Combine(outDir, "samples");
Directory.CreateDirectory(samplesDir);
var deepCounts = new Dictionary<uint, int>();
var deepWriter = deepIds.Count > 0 ? new StreamWriter(Path.Combine(outDir, "deep.tsv")) : null;
deepWriter?.WriteLine("chunkId\tsize\tfile\thex");

var lockObj = new object();
var chunkAgg = new Dictionary<uint, ChunkAgg>();
var seqAgg = new Dictionary<string, long>();
var featureFiles = new Dictionary<string, long>();
var chunkFeatureFiles = new Dictionary<(uint, string), long>();
var failures = new List<string>();
var itemRows = new List<string>();
var parsed = 0L;
long doneCount = 0;
var totalChunkOcc = 0L;
var sw = Stopwatch.StartNew();

Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = jobs }, path =>
{
    var rel = Path.GetRelativePath(root, path).Replace('\\', '/');
    List<Occ>? occs = null;
    List<(uint ownerClassId, string ownerType, uint[] ids)>? seqs = null;
    List<string>? features = null;
    string itemRow;
    try
    {
        var bytes = File.ReadAllBytes(path);
        Gbx<CGameItemModel> file;
        using (var ms = new MemoryStream(bytes))
        {
            file = Gbx.Parse<CGameItemModel>(ms, new GbxReadSettings { SafeSkippableChunks = true });
        }
        var node = file.Node ?? throw new Exception("null node");

        features = new List<string>
        {
            $"type={node.ItemType}",
            $"wp={node.WaypointType}",
            $"ent={node.EntityModel?.GetType().Name ?? "none"}",
            $"icon={(node.Icon != null ? "yes" : "no")}",
            $"place={(node.DefaultPlacement != null ? "yes" : "no")}",
            $"skin={(node.GameSkin != null ? "yes" : "no")}",
        };

        occs = new List<Occ>();
        seqs = new List<(uint, string, uint[])>();
        var seenObj = new HashSet<object>(ReferenceEqualityComparer.Instance);

        void Walk(object owner, int depth)
        {
            if (depth > 40) return;
            if (!seenObj.Add(owner)) return;
            var t = owner.GetType();
            var chunksProp = t.GetProperty("Chunks", BindingFlags.Public | BindingFlags.Instance);
            if (chunksProp?.GetValue(owner) is IEnumerable ce)
            {
                var list = new List<object>();
                foreach (var c in ce) if (c != null) list.Add(c);
                if (list.Count > 0)
                {
                    var ownerClassId = ClassManager.GetId(t) ?? 0u;
                    var ids = new uint[list.Count];
                    for (var i = 0; i < list.Count; i++)
                    {
                        var chunk = list[i];
                        var ct = chunk.GetType();
                        uint id = 0;
                        try { id = ((IChunk)chunk).Id; }
                        catch { }
                        ids[i] = id;
                        var status = ct == typeof(SkippableChunk) ? "unknown"
                            : chunk is ISkippableChunk sk && sk.Ignore ? "ignored"
                            : (chunk as ISkippableChunk)?.Data != null ? "readFailed"
                            : "parsed";
                        int? version = null;
                        if (chunk is IVersionable iv) version = iv.Version;
                        long? size = (chunk as ISkippableChunk)?.Data?.Length;
                        byte[]? data = (chunk as ISkippableChunk)?.Data;
                        Dictionary<string, string>? fields = null;
                        if (status == "parsed")
                        {
                            fields = new Dictionary<string, string>();
                            foreach (var f in ct.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                            {
                                if (f.IsStatic) continue;
                                object? v;
                                try { v = f.GetValue(chunk); } catch { continue; }
                                fields[f.Name] = FieldStr(v);
                                if (fields.Count >= 24) break;
                            }
                        }
                        occs.Add(new Occ(id, ClassManager.GetName(ownerClassId) ?? ownerType(t), ownerClassId, NiceName(ct), status, version, size, data, i, list.Count, fields));
                    }
                    seqs.Add((ownerClassId, t.Name, ids));
                }
            }

            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length > 0) continue;
                if (p.Name is "Chunks" or "Node" or "RefTable" or "Gbx" or "Logger") continue;
                object? v;
                try { v = p.GetValue(owner); } catch { continue; }
                Descend(v, depth, t.Name + "." + p.Name);
            }
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object? v;
                try { v = f.GetValue(owner); } catch { continue; }
                Descend(v, depth, t.Name + "." + f.Name);
            }
        }

        void Descend(object? v, int depth, string pathStr)
        {
            if (v == null || depth > 40) return;
            var ty = v.GetType();
            if (ty.Namespace is null || !ty.Namespace.StartsWith("GBX.NET")) return;
            if (ty.IsPrimitive || ty == typeof(string) || ty.IsEnum) return;

            if (v is Array arr)
            {
                var et = arr.GetType().GetElementType();
                if (et == null || et.IsValueType || et.Namespace is null || !et.Namespace.StartsWith("GBX.NET")) return;
                foreach (var el in arr)
                {
                    if (el == null) continue;
                    if (el is string) continue;
                    if (el.GetType().IsValueType) continue;
                    Walk(el, depth + 1);
                }
                return;
            }
            if (v is not string && v is IEnumerable en)
            {
                foreach (var el in en)
                {
                    if (el == null || el is string) continue;
                    if (el.GetType().IsValueType) continue;
                    Walk(el, depth + 1);
                }
                return;
            }
            if (ty.IsValueType) return;
            Walk(v, depth + 1);
        }

        Walk(node, 0);
        if (file.Header != null) Walk(file.Header, 0);

        var unk = occs.Count(o => o.status == "unknown");
        var placeSeq = string.Join(",", occs.Where(o => o.ownerClassId == 0x2E020000u).OrderBy(o => o.orderIdx).Select(o => o.id.ToString("X8")));
        itemRow = string.Join('\t', new[]
        {
            "ok", rel, Sanitize(node.Name ?? ""), node.ItemType.ToString(),
            node.WaypointType.ToString(), node.EntityModel?.GetType().Name ?? "none",
            (node.Icon != null ? "yes" : "no"), (node.DefaultPlacement != null ? "yes" : "no"),
            occs.Count.ToString(), unk.ToString(), placeSeq
        });
        Interlocked.Increment(ref parsed);
        Interlocked.Add(ref totalChunkOcc, occs.Count);
    }
    catch (Exception ex)
    {
        var msg = ex.Message.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
        if (msg.Length > 200) msg = msg[..200];
        itemRow = string.Join('\t', new[] { "FAIL", rel, msg, "", "", "", "", "", "0", "0", "" });
        lock (lockObj) failures.Add($"{rel}\t{msg}");
    }

    lock (lockObj)
    {
        itemRows.Add(itemRow);
        if (occs == null) return;
        var seenThisFile = new HashSet<uint>();
        foreach (var o in occs)
        {
            var agg = GetAgg(o.id);
            agg.Occ++;
            if (agg.SampleFiles.Count < 5) agg.SampleFiles.Add(rel);
            if (seenThisFile.Add(o.id))
            {
                agg.Files++;
                if (features != null)
                {
                    foreach (var f in features)
                        chunkFeatureFiles[(o.id, f)] = chunkFeatureFiles.TryGetValue((o.id, f), out var n) ? n + 1 : 1;
                }
            }
            agg.OwnerNames[ShortOwner(o.ownerClassId, o.ownerType)] = Inc(agg.OwnerNames, ShortOwner(o.ownerClassId, o.ownerType));
            agg.Statuses[o.status] = Inc(agg.Statuses, o.status);
            agg.ChunkTypes[o.chunkType] = Inc(agg.ChunkTypes, o.chunkType);
            if (o.version is int vv) agg.Versions[vv] = IncI(agg.Versions, vv);
            if (o.size is long sz)
            {
                if (agg.SizeMin is null || sz < agg.SizeMin) agg.SizeMin = sz;
                if (agg.SizeMax is null || sz > agg.SizeMax) agg.SizeMax = sz;
            }
            if (o.orderIdx < 40) agg.PosCount[o.orderIdx] = IncI(agg.PosCount, o.orderIdx);
            agg.OwnerLen[o.ownerLen] = IncI(agg.OwnerLen, o.ownerLen);
            if (o.fields != null)
            {
                foreach (var (k, v) in o.fields)
                {
                    if (!agg.Fields.TryGetValue(k, out var fa))
                        agg.Fields[k] = fa = new FieldAgg();
                    fa.Count++;
                    if (fa.Distinct.Count < 8) fa.Distinct.Add(v);
                    if (fa.Distinct.Count == 8) fa.Distinct.Add("...");
                }
            }
            // Sample one payload per distinct size (up to 4), so structurally different
            // variants of the same chunk are all captured, not just the first two files.
            if (o.status is "unknown" or "ignored" or "readFailed" && o.data != null
                && agg.SampledSizes.Count < 4 && agg.SampledSizes.Add(o.data.Length))
            {
                AnalyzePayload(o.data, agg, rel, samplesDir, o.id, agg.SampledSizes.Count);
            }
            if (deepIds.Contains(o.id) && o.data != null && deepWriter != null)
            {
                var n = deepCounts.TryGetValue(o.id, out var c) ? c : 0;
                if (n < 200000)
                {
                    deepCounts[o.id] = n + 1;
                    deepWriter.WriteLine($"{o.id:X8}\t{o.data.Length}\t{rel}\t{Convert.ToHexString(o.data)}");
                }
            }
        }
        if (seqs != null)
        {
            foreach (var (ownerClassId, ownerType, ids) in seqs)
            {
                var key = $"{ownerClassId:X8}|{ownerType}|{string.Join(",", ids.Select(x => x.ToString("X8")))}";
                seqAgg[key] = seqAgg.TryGetValue(key, out var n) ? n + 1 : 1;
            }
        }
        if (features != null)
        {
            foreach (var f in features)
                featureFiles[f] = featureFiles.TryGetValue(f, out var n) ? n + 1 : 1;
        }
    }

    var done = Interlocked.Increment(ref doneCount);
    if (done % 500 == 0)
        Console.WriteLine($"[{sw.Elapsed}] {done}/{files.Length} parsed={parsed} failures={failures.Count} chunks={chunkAgg.Count}");
});

Console.WriteLine($"[{sw.Elapsed}] scan done: parsed={parsed}, failures={failures.Count}, chunk ids={chunkAgg.Count}, occurrences={totalChunkOcc}");

// ---- reports ----
static long Inc(Dictionary<string, long> d, string k)
    => d.TryGetValue(k, out var n) ? n + 1 : (d[k] = 1);

static long IncI(Dictionary<int, long> d, int k)
    => d.TryGetValue(k, out var n) ? n + 1 : (d[k] = 1);

static string ShortOwner(uint ownerClassId, string ownerType)
    => ownerClassId != 0 ? $"{ownerClassId:X8} {ownerType}" : ownerType;

static string ownerType(Type t) => t.Name;

static string NiceName(Type t)
{
    var n = t.Name;
    var tick = n.IndexOf('`');
    if (tick >= 0) n = n[..tick];
    if (t.IsGenericType)
        n += "<" + string.Join(",", t.GetGenericArguments().Select(a => a.Name)) + ">";
    return n;
}

static string FieldStr(object? v) => v switch
{
    null => "null",
    string s => "\"" + (s.Length > 40 ? s[..40] + "…" : s) + "\"",
    bool b => b ? "true" : "false",
    float f => f.ToString("R"),
    double d => d.ToString("R"),
    Enum e => e.ToString(),
    Array a => $"[{a.Length}]",
    IClass => "<node>",
    _ => v.ToString() ?? "?"
};

static string Sanitize(string s)
{
    var sb = new StringBuilder(s.Length);
    foreach (var c in s)
        sb.Append(c is '\t' or '\n' or '\r' ? ' ' : c);
    return sb.ToString();
}

// payload analysis for unknown chunks
static void AnalyzePayload(byte[] d, ChunkAgg agg, string rel, string samplesDir, uint id, int n)
{
    var scanLen = Math.Min(d.Length, 262144);
    agg.PayloadBytes += scanLen;
    agg.SampleFilesPayload ??= rel;

    var zero = 0;
    for (var i = 0; i < scanLen; i++) if (d[i] == 0) zero++;
    agg.ZeroRatio = (double)zero / scanLen;

    // hex of the first bytes
    if (agg.FirstHex is null)
        agg.FirstHex = string.Join(" ", d.Take(Math.Min(96, d.Length)).Select(b => b.ToString("X2")));

    // int32-length-prefixed printable strings (GBX binary string format)
    var i2 = 0;
    while (i2 + 5 <= scanLen && agg.Strings.Count < 14)
    {
        var L = BitConverter.ToInt32(d, i2);
        if (L >= 2 && L <= 300 && i2 + 4 + L <= d.Length)
        {
            var ok = true;
            for (var j = 0; j < L; j++)
            {
                var b = d[i2 + 4 + j];
                if (!(b == 9 || b == 10 || b == 13 || (b >= 32 && b < 127))) { ok = false; break; }
            }
            if (ok)
            {
                var s = Encoding.ASCII.GetString(d, i2 + 4, L).Trim();
                if (s.Length >= 2 && !agg.Strings.Contains(s)) agg.Strings.Add(s);
                i2 += 4 + L;
                continue;
            }
        }
        i2++;
    }

    // UTF-16LE variant
    i2 = 0;
    while (i2 + 5 <= scanLen && agg.Strings16.Count < 8)
    {
        var L = BitConverter.ToInt32(d, i2);
        if (L >= 3 && L <= 200 && i2 + 4 + L * 2 <= d.Length)
        {
            var ok = true;
            for (var j = 0; j < L; j++)
            {
                var b = d[i2 + 4 + j * 2];
                var hi = d[i2 + 4 + j * 2 + 1];
                if (hi != 0 || !(b == 9 || b == 10 || b == 13 || (b >= 32 && b < 127))) { ok = false; break; }
            }
            if (ok)
            {
                var s = Encoding.Unicode.GetString(d, i2 + 4, L * 2).Trim();
                if (s.Length >= 3 && !agg.Strings16.Contains(s)) agg.Strings16.Add(s);
                i2 += 4 + L * 2;
                continue;
            }
        }
        i2++;
    }

    // class-id candidates (class ids are 4k-aligned and known to ClassManager)
    for (var i = 0; i + 8 <= scanLen && agg.ClassCands.Count < 12; i += 4)
    {
        var v = BitConverter.ToUInt32(d, i);
        if ((v & 0xFFF) != 0 || v < 0x01000000 || v > 0x3F000000) continue;
        var name = ClassManager.GetName(v);
        if (name == null) continue;
        agg.ClassCands.Add($"+0x{i:X} {v:X8} {name}");
    }

    // nested chunk candidates: [chunkId][size] where masked id is a known class
    for (var i = 0; i + 8 <= scanLen && agg.ChunkCands.Count < 12; i += 4)
    {
        var v = BitConverter.ToUInt32(d, i);
        if ((v & 0xFFF) == 0 || v < 0x01000000 || v > 0x3F000000) continue;
        if (ClassManager.GetName(v & 0xFFFFF000u) == null) continue;
        var sz = BitConverter.ToInt32(d, i + 4);
        if (sz < 0 || sz > 10_000_000 || i + 8 + sz > d.Length) continue;
        agg.ChunkCands.Add($"+0x{i:X} {v:X8} (class {v & 0xFFFFF000u:X8}) size={sz}");
    }

    // save a raw sample once
    try
    {
        var samplePath = Path.Combine(samplesDir, $"{id:X8}_{n}.bin");
        File.WriteAllBytes(samplePath, d.Take(Math.Min(d.Length, 65536)).ToArray());
        agg.SampleBin = Path.GetFileName(samplePath);
    }
    catch { }
}

ChunkAgg GetAgg(uint id)
{
    if (!chunkAgg.TryGetValue(id, out var a))
        chunkAgg[id] = a = new ChunkAgg();
    return a;
}

// ---- write reports ----
using (var w = new StreamWriter(Path.Combine(outDir, "summary.tsv")))
{
    w.WriteLine("chunkId\tclassId\tstatus\tfiles\tocc\towners\tchunkTypes\tversions\tsizeMin\tsizeMax\ttopPositions\townerLens\tfields");
    foreach (var (id, a) in chunkAgg.OrderByDescending(x => x.Value.Files))
    {
        var owners = string.Join("; ", a.OwnerNames.OrderByDescending(x => x.Value).Take(4).Select(x => $"{x.Key}({x.Value})"));
        var types = string.Join("; ", a.ChunkTypes.OrderByDescending(x => x.Value).Take(3).Select(x => $"{x.Key}({x.Value})"));
        var vers = string.Join(",", a.Versions.OrderByDescending(x => x.Value).Select(x => $"{x.Key}({x.Value})"));
        var pos = string.Join(",", a.PosCount.OrderByDescending(x => x.Value).Take(6).OrderBy(x => x.Key).Select(x => $"{x.Key}:{x.Value}"));
        var lens = string.Join(",", a.OwnerLen.OrderByDescending(x => x.Value).Take(5).OrderBy(x => x.Key).Select(x => $"{x.Key}:{x.Value}"));
        var fields = string.Join("; ", a.Fields.Take(24).Select(kv => $"{kv.Key}={string.Join("|", kv.Value.Distinct.Take(5))}"));
        w.WriteLine($"{id:X8}\t{id & 0xFFFFF000u:X8}\t{string.Join(",", a.Statuses.Keys)}\t{a.Files}\t{a.Occ}\t{owners}\t{types}\t{vers}\t{a.SizeMin}\t{a.SizeMax}\t{pos}\t{lens}\t{fields}");
    }
}

using (var w = new StreamWriter(Path.Combine(outDir, "payload.tsv")))
{
    w.WriteLine("chunkId\tfiles\tsizeMin\tsizeMax\tzeroRatio\tsampleBin\tfirstHex\tstrings\tstringsUtf16\tclassCands\tchunkCands");
    foreach (var (id, a) in chunkAgg.OrderByDescending(x => x.Value.Files))
    {
        if (!a.Statuses.ContainsKey("unknown") && !a.Statuses.ContainsKey("ignored") && !a.Statuses.ContainsKey("readFailed")) continue;
        w.WriteLine(string.Join('\t', new[]
        {
            $"{id:X8}", a.Files.ToString(), a.SizeMin?.ToString() ?? "", a.SizeMax?.ToString() ?? "",
            a.ZeroRatio?.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) ?? "", a.SampleBin ?? "", a.FirstHex ?? "",
            string.Join(" | ", a.Strings.Select(Sanitize)),
            string.Join(" | ", a.Strings16.Select(Sanitize)),
            string.Join(" ; ", a.ClassCands),
            string.Join(" ; ", a.ChunkCands)
        }));
    }
}

using (var w = new StreamWriter(Path.Combine(outDir, "sequences.tsv")))
{
    w.WriteLine("ownerClassId\townerType\tchunkIds\tcount");
    foreach (var (key, count) in seqAgg.OrderByDescending(x => x.Value))
    {
        var parts = key.Split('|');
        w.WriteLine($"{parts[0]}\t{parts[1]}\t{parts[2]}\t{count}");
    }
}

var totalFiles = Math.Max(1, parsed);
using (var w = new StreamWriter(Path.Combine(outDir, "features.tsv")))
{
    w.WriteLine("chunkId\tfeature\tfilesBoth\tfilesChunk\tfilesFeature\tlift");
    var rows = new List<string>();
    foreach (var ((cid, feat), both) in chunkFeatureFiles)
    {
        if (!chunkAgg.TryGetValue(cid, out var a)) continue;
        if (a.Files < 3) continue;
        var ff = featureFiles.TryGetValue(feat, out var f) ? f : 0;
        var lift = a.Files > 0 && ff > 0 ? (double)both / a.Files / ((double)ff / totalFiles) : 0;
        rows.Add($"{cid:X8}\t{feat}\t{both}\t{a.Files}\t{ff}\t{lift.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}");
    }
    foreach (var r in rows.OrderBy(r => r.Split('\t')[0]).ThenByDescending(r => double.Parse(r.Split('\t')[5], System.Globalization.CultureInfo.InvariantCulture)))
        w.WriteLine(r);
}

using (var w = new StreamWriter(Path.Combine(outDir, "items.tsv")))
{
    w.WriteLine("status\tfile\tname\titemType\twaypoint\tentity\ticon\tplacement\tchunks\tunknownChunks\tplacementChunks");
    foreach (var row in itemRows.OrderBy(r => r))
        w.WriteLine(row);
}

File.WriteAllLines(Path.Combine(outDir, "failures.txt"), failures.OrderBy(x => x));

File.WriteAllLines(Path.Combine(outDir, "stats.txt"), new[]
{
    $"files={files.Length}",
    $"parsed={parsed}",
    $"failures={failures.Count}",
    $"distinctChunkIds={chunkAgg.Count}",
    $"totalChunkOccurrences={totalChunkOcc}",
    $"unknownChunkIds={chunkAgg.Count(kv => kv.Value.Statuses.ContainsKey("unknown"))}",
    $"elapsed={sw.Elapsed}",
});

Console.WriteLine($"reports written to {outDir}");
deepWriter?.Flush();
deepWriter?.Dispose();
return 0;

class ChunkAgg
{
    public long Files;
    public long Occ;
    public Dictionary<string, long> OwnerNames = new();
    public Dictionary<string, long> Statuses = new();
    public Dictionary<string, long> ChunkTypes = new();
    public Dictionary<int, long> Versions = new();
    public Dictionary<int, long> PosCount = new();
    public Dictionary<int, long> OwnerLen = new();
    public Dictionary<string, FieldAgg> Fields = new();
    public List<string> SampleFiles = new();
    public long? SizeMin;
    public long? SizeMax;
    public HashSet<int> SampledSizes = new();
    public long PayloadBytes;
    public double? ZeroRatio;
    public string? FirstHex;
    public string? SampleBin;
    public string? SampleFilesPayload;
    public List<string> Strings = new();
    public List<string> Strings16 = new();
    public List<string> ClassCands = new();
    public List<string> ChunkCands = new();
}

class FieldAgg
{
    public long Count;
    public HashSet<string> Distinct = new();
}

readonly record struct Occ(uint id, string ownerType, uint ownerClassId, string chunkType, string status,
    int? version, long? size, byte[]? data, int orderIdx, int ownerLen, Dictionary<string, string>? fields);
