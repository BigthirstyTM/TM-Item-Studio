using GBX.NET;
using GBX.NET.Engines;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

if (args.Length == 0)
{
    Console.WriteLine("usage: PathFollowerDiag <file.Item.Gbx> [--chunks] [--depth=N]");
    return 1;
}

var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);

Gbx.LZO = new GBX.NET.LZO.MiniLZO();

var path = args[0];
var showChunks = args.Contains("--chunks");
var maxDepth = 24;
foreach (var a in args.Where(a => a.StartsWith("--depth=")))
    maxDepth = int.Parse(a["--depth=".Length..]);

var bytes = File.ReadAllBytes(path);
Console.WriteLine($"file: {path} ({bytes.Length} bytes)");
Console.WriteLine($"sha256: {Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant()}");

if (args.Contains("--raw"))
{
    using var rawMs = new MemoryStream();
    Gbx.Decompress(path, rawMs);
    var b = rawMs.ToArray();
    Console.WriteLine($"decompressed body: {b.Length} bytes");
    var ids = new (string Name, byte[] Id)[]
    {
        ("2E001010", new byte[] { 0x10, 0x10, 0x00, 0x2E }),
        ("2E002001", new byte[] { 0x01, 0x20, 0x00, 0x2E }),
        ("2E020000", new byte[] { 0x00, 0x00, 0x02, 0x2E }),
        ("2E020001", new byte[] { 0x01, 0x00, 0x02, 0x2E }),
        ("2E020003", new byte[] { 0x03, 0x00, 0x02, 0x2E }),
        ("2E020004", new byte[] { 0x04, 0x00, 0x02, 0x2E }),
        ("2E020005", new byte[] { 0x05, 0x00, 0x02, 0x2E }),
        ("2E001019", new byte[] { 0x19, 0x10, 0x00, 0x2E }),
        ("class-CPlugSurface", new byte[] { 0x00, 0xC0, 0x00, 0x09 }),
        ("chunk-0900C003", new byte[] { 0x03, 0xC0, 0x00, 0x09 }),
    };
    foreach (var (name, id) in ids)
    {
        for (var i = 0; i + 4 <= b.Length; i++)
        {
            if (b[i] == id[0] && b[i + 1] == id[1] && b[i + 2] == id[2] && b[i + 3] == id[3])
            {
                var hex = string.Join(" ", b.Skip(i).Take(64).Select(x => x.ToString("X2")));
                Console.WriteLine($"  {name} at 0x{i:X}: {hex}");
            }
        }
    }
    var dumpArg = args.FirstOrDefault(a => a.StartsWith("--dump="));
    if (dumpArg != null)
    {
        var parts = dumpArg["--dump=".Length..].Split(':');
        var cid = Convert.ToUInt32(parts[0], 16);
        var len = parts.Length > 1 ? int.Parse(parts[1]) : 256;
        var le = BitConverter.GetBytes(cid);
        for (var i = 0; i + 4 <= b.Length; i++)
        {
            if (b[i] == le[0] && b[i + 1] == le[1] && b[i + 2] == le[2] && b[i + 3] == le[3])
            {
                Console.WriteLine($"  match {cid:X8} at 0x{i:X}:");
                var take = Math.Min(len, b.Length - i);
                for (var row = 0; row < take; row += 16)
                {
                    var hex = string.Join(" ", b.Skip(i + row).Take(16).Select(x => x.ToString("X2")));
                    var asc = new string(b.Skip(i + row).Take(16).Select(x => x is >= 32 and < 127 ? (char)x : '.').ToArray());
                    Console.WriteLine($"    +{row:X4}: {hex,-47} {asc}");
                }
            }
        }
        return 0;
    }
    var hexChunkArg = args.FirstOrDefault(a => a.StartsWith("--hexchunk="));
    if (hexChunkArg is not null)
    {
        var cid = Convert.ToUInt32(hexChunkArg["--hexchunk=".Length..], 16);
        var le = BitConverter.GetBytes(cid);
        for (var i = 0; i + 8 <= b.Length; i++)
        {
            if (b[i] == le[0] && b[i + 1] == le[1] && b[i + 2] == le[2] && b[i + 3] == le[3])
            {
                var size = BitConverter.ToInt32(b, i + 4);
                Console.WriteLine($"  chunk {cid:X8} at 0x{i:X} size={size}");
                var take = Math.Min(size + 8, b.Length - i);
                for (var row = 0; row < take; row += 32)
                {
                    var line = string.Join(" ", b.Skip(i + row).Take(32).Select(x => x.ToString("X2")));
                    Console.WriteLine($"    +{row:X4}: {line}");
                }
            }
        }
    }
    return 0;
}

// Raw scan for non-finite IEEE-754 floats anywhere in the file body.
var nanOffsets = new List<int>();
for (var i = 0; i + 4 <= bytes.Length; i++)
{
    var bits = BitConverter.ToSingle(bytes, i);
    if (float.IsNaN(bits) || float.IsInfinity(bits))
    {
        // skip plausible false positives: only report aligned-to-4 runs later; keep count
        nanOffsets.Add(i);
    }
}
Console.WriteLine($"raw non-finite float bit-patterns (any alignment): {nanOffsets.Count}");

Gbx<CGameItemModel> file;
try
{
    using var ms = new MemoryStream(bytes);
    file = Gbx.Parse<CGameItemModel>(ms, new GbxReadSettings { SafeSkippableChunks = true });
}
catch (Exception ex)
{
    Console.WriteLine($"PARSE FAILED: {ex}");
    return 1;
}

if (args.Contains("--hdr"))
{
    Dump(file.Header, "header", 0);
    return 0;
}

if (args.Contains("--c201F"))
{
    var chunks = file.Node.Chunks;
    var c = chunks.Get(0x2E00201F);
    if (c is null) { Console.WriteLine("chunk 2E00201F not present"); return 0; }
    Console.WriteLine($"chunk: {c.GetType().Name}");
    foreach (var f in c.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
    {
        object? v;
        try { v = f.GetValue(c); } catch (Exception ex) { Console.WriteLine($"  {f.Name} = <{ex.GetType().Name}>"); continue; }
        var desc = v is null ? "null" : (v is string s ? "string(len=" + s.Length + ")" : v.GetType().FullName);
        Console.WriteLine($"  {f.Name} = {desc}");
        if (v is not null and not string)
            Dump(v, $"    .{f.Name}", 1);
    }
    return 0;
}

if (args.Contains("--summary"))
{
    var node0 = file.Node;
    Console.WriteLine($"name={node0.Name} id={node0.Ident?.Id} type={node0.ItemType} entity={node0.EntityModel?.GetType().Name} placement={(node0.DefaultPlacement is null ? "null" : string.Join(",", node0.DefaultPlacement.Chunks?.Select(c => $"{c.Id:X8}") ?? Array.Empty<string>()))} placementClass={(node0.DefaultPlacement?.PlacementClass is null ? "null" : "v" + node0.DefaultPlacement.PlacementClass.Version)} icon={(node0.Icon is null ? "null" : $"{node0.Icon.GetLength(0)}x{node0.Icon.GetLength(1)}")}");
    if (node0.EntityModel is CPlugPrefab pf)
    {
        var bodySlot = new Dictionary<int, int>();
        var slot = 0;
        for (var i = 0; i < (pf.Ents?.Length ?? 0); i++)
        {
            if (pf.Ents[i].Model is CPlugDynaObjectModel && pf.Ents[i].Params is GBX.NET.Engines.Meta.NPlugDynaObjectModel_SInstanceParams { IsKinematic: true })
                bodySlot[i] = slot++;
        }
        Console.WriteLine($"prefab v{pf.Version} ents={pf.Ents?.Length} kinematicSlots={slot}");
        for (var i = 0; i < (pf.Ents?.Length ?? 0); i++)
        {
            var e = pf.Ents[i];
            if (e.Model is CPlugDynaObjectModel d)
            {
                var vis = d.Mesh?.Visuals?.Count ?? -1;
                var mats = d.Mesh?.CustomMaterials?.Length ?? -1;
                var sg = d.Mesh?.ShadedGeoms?.Length ?? -1;
                var mver = d.Mesh is null ? "null" : (d.Mesh.Chunks.Get(0x090BB000)?.GetType().GetProperty("Version")?.GetValue(d.Mesh.Chunks.Get(0x090BB000))?.ToString() ?? "?");
                var bbox = d.Mesh?.Visuals?.FirstOrDefault()?.BoundingBox;
                var verts = (d.Mesh?.Visuals?.FirstOrDefault() as GBX.NET.Engines.Plug.CPlugVisualIndexedTriangles)?.VertexStreams?.FirstOrDefault()?.Positions?.Length ?? -1;
                var idx = (d.Mesh?.Visuals?.FirstOrDefault() as GBX.NET.Engines.Plug.CPlugVisualIndexedTriangles)?.IndexBuffer?.Indices;
                var maxIdx = idx is { Length: > 0 } ? idx.Max() : -1;
                var sv = d.DynaShape?.Surf is GBX.NET.Engines.Plug.CPlugSurface.Mesh sm ? $"{sm.Vertices?.Length ?? 0}v/{sm.Triangles?.Length ?? 0}t/surfV{sm.Version}" : "none";
                Console.WriteLine($"  ent{i} BODY slot={(bodySlot.TryGetValue(i, out var s) ? s.ToString() : "-")} pos={e.Position} meshV={mver} vis={vis} mats={mats} shaded={sg} verts={verts} maxIdx={maxIdx} bbox={bbox} dynaShape={sv} shapeChunkV={(d.DynaShape?.Chunks.Get(0x0900C003)?.GetType().GetProperty("Version")?.GetValue(d.DynaShape.Chunks.Get(0x0900C003)) ?? "?")} staticDup={(ReferenceEquals(d.StaticShape, d.DynaShape) ? "yes" : "no")} kin={((NPlugDynaObjectModel_SInstanceParams?)e.Params)?.IsKinematic}");
            }
            else if (e.Model is GBX.NET.Engines.Meta.NPlugDyna_SKinematicConstraint kc)
            {
                var p = e.Params as GBX.NET.Engines.Meta.NPlugDyna_SPrefabConstraintParams;
                var tf = kc.TransAnimFunc; var rf = kc.RotAnimFunc;
                Console.WriteLine($"  ent{i} CONS parent={p?.Ent1} child={p?.Ent2} pos1={p?.Pos1} pos2={p?.Pos2} axis={kc.TransAxis} trans=[{kc.TransMin},{kc.TransMax}] rotAxis={kc.RotAxis} angle=[{kc.AngleMinDeg},{kc.AngleMaxDeg}] v{kc.Version}/sv{kc.SubVersion} transKeys={(tf?.SubFuncs?.Length ?? 0)}[isDur={tf?.IsDuration} ease={string.Join("+", tf?.SubFuncs?.Select(k => $"{k.Ease}/{(k.Reverse ? "R" : "F")}/{k.Duration.TotalMilliseconds}") ?? Array.Empty<string>())}] rotKeys={(rf?.SubFuncs?.Length ?? 0)}[isDur={rf?.IsDuration} ease={string.Join("+", rf?.SubFuncs?.Select(k => $"{k.Ease}/{(k.Reverse ? "R" : "F")}/{k.Duration.TotalMilliseconds}") ?? Array.Empty<string>())}]");
            }
            else
            {
                Console.WriteLine($"  ent{i} OTHER {e.Model?.GetType().Name ?? (e.References.Model.File != null ? "external" : "null")}");
            }
        }
    }
    else if (node0.EntityModel is CGameCommonItemEntityModel common)
    {
        Console.WriteLine($"common staticObject={(common.StaticObject?.GetType().Name ?? "null")} phyModel={(common.PhyModel?.GetType().Name ?? "null")}");
    }
    return 0;
}

var node = file.Node;
Console.WriteLine($"root class: {node.GetType().FullName}");
Console.WriteLine($"name: {node.Name}");
Console.WriteLine($"ident: {node.Ident} (id='{node.Ident?.Id}', collection={node.Ident?.Collection}, author={node.Ident?.Author})");
Console.WriteLine($"itemType: {node.ItemType} / {node.ItemTypeE}");
Console.WriteLine($"waypoint: {node.WaypointType}");
Console.WriteLine($"archetypeRef: {node.ArchetypeRef}");
Console.WriteLine($"pageName: {node.PageName} catalogPos: {node.CatalogPosition}");
Console.WriteLine($"entityModel: {(node.EntityModel?.GetType().Name ?? "<null>")}");

// Reference table (external files the item depends on)
try
{
    var refTable = file.RefTable;
    Console.WriteLine($"reftable: {(refTable is null ? "<null>" : refTable.GetType().Name)}");
    foreach (var prop in refTable?.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance) ?? Array.Empty<PropertyInfo>())
    {
        object? val;
        try { val = prop.GetValue(refTable); } catch { continue; }
        if (val is IEnumerable en)
        {
            foreach (var f in en)
                Console.WriteLine($"  ref.{prop.Name}: {f}");
        }
        else if (val is string or int or uint or bool)
        {
            Console.WriteLine($"  ref.{prop.Name}: {val}");
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"reftable error: {ex.Message}");
}

// Header chunks
DumpChunks(file.Header, "header", 0, showChunks);
try
{
    var hc = file.Header?.GetType().GetProperty("Chunks")?.GetValue(file.Header);
    if (hc is IEnumerable hce)
    {
        foreach (var c in hce)
            Console.WriteLine($"  headerChunk: {c?.GetType().Name} id={(c?.GetType().GetProperty("Id")?.GetValue(c))}");
    }
}
catch { }

Console.WriteLine("--- object graph ---");
Dump(node, "root", 0);
return 0;

void Dump(object? obj, string pathStr, int depth)
{
    if (depth > maxDepth) { Indent(depth); Console.WriteLine($"{pathStr} = <depth limit>"); return; }
    if (obj is null) return;

    var t = obj.GetType();
    if (obj is string s) { Indent(depth); Console.WriteLine($"{pathStr} = \"{s}\" ({s.Length})"); return; }

    if (IsPrimitiveLike(t))
    {
        Indent(depth);
        var extra = obj is float f && !float.IsFinite(f) ? "  <== NON-FINITE" : "";
        Console.WriteLine($"{pathStr} = {Format(obj)}{extra}");
        return;
    }

    if (obj is Array arr)
    {
        Indent(depth);
        Console.WriteLine($"{pathStr} = {t.Name}[{arr.Length}] rank={arr.Rank}");
        if (arr.Rank > 1)
        {
            var idx = new int[arr.Rank];
            var n = 0;
            foreach (var el in arr)
            {
                if (n++ >= 64) { Indent(depth + 1); Console.WriteLine($"... {arr.Length - 64} more"); break; }
                Dump(el, $"{pathStr}[{string.Join(",", idx)}]", depth + 1);
                for (var d = arr.Rank - 1; d >= 0; d--)
                {
                    idx[d]++;
                    if (idx[d] < arr.GetLength(d)) break;
                    idx[d] = 0;
                }
            }
            return;
        }
        var limit = Math.Min(arr.Length, 64);
        for (var i = 0; i < limit; i++)
            Dump(arr.GetValue(i), $"{pathStr}:{i}", depth + 1);
        if (arr.Length > limit)
        {
            Indent(depth + 1);
            Console.WriteLine($"... {arr.Length - limit} more");
        }
        return;
    }

    if (obj is IEnumerable list && t != typeof(string))
    {
        var items = new List<object?>();
        foreach (var item in list) items.Add(item);
        Indent(depth);
        Console.WriteLine($"{pathStr} = {t.Name} ({items.Count})");
        var limit = Math.Min(items.Count, 64);
        for (var i = 0; i < limit; i++)
            Dump(items[i], $"{pathStr}:{i}", depth + 1);
        if (items.Count > limit)
        {
            Indent(depth + 1);
            Console.WriteLine($"... {items.Count - limit} more");
        }
        return;
    }

    if (seen.Contains(obj))
    {
        Indent(depth);
        Console.WriteLine($"{pathStr} = <dup {t.Name}#{RuntimeHelpers.GetHashCode(obj)}>");
        return;
    }
    seen.Add(obj);

    Indent(depth);
    Console.WriteLine($"{pathStr} : {t.Name}");
    DumpChunks(obj, pathStr, depth, showChunks);

    foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
    {
        if (p.GetIndexParameters().Length > 0) continue;
        if (p.Name is "Chunks" or "Node" or "RefTable") continue;
        object? val;
        try { val = p.GetValue(obj); }
        catch (Exception ex)
        {
            Indent(depth + 1);
            Console.WriteLine($"{pathStr}.{p.Name} = <{ex.GetType().Name}: {ex.Message}>");
            continue;
        }
        Dump(val, $"{pathStr}.{p.Name}", depth + 1);
    }
    foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
    {
        object? val;
        try { val = f.GetValue(obj); }
        catch { continue; }
        Dump(val, $"{pathStr}.{f.Name}", depth + 1);
    }
}

void DumpChunks(object? owner, string pathStr, int depth, bool detailed)
{
    if (owner is null) return;
    var chunksProp = owner.GetType().GetProperty("Chunks", BindingFlags.Public | BindingFlags.Instance);
    if (chunksProp is null) return;
    object? chunks;
    try { chunks = chunksProp.GetValue(owner); } catch { return; }
    if (chunks is null) return;

    var ids = new List<string>();
    if (chunks is IEnumerable ce)
    {
        foreach (var chunk in ce)
        {
            if (chunk is null) { ids.Add("<null>"); continue; }
            var ct = chunk.GetType();
            uint? id = null;
            try
            {
                var idVal = ct.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy)?.GetValue(chunk);
                if (idVal is uint u) id = u;
                else if (idVal is int ii) id = (uint)ii;
            }
            catch { }
            var idStr = id.HasValue ? $"0x{id:X8}" : "?";
            object? version = null;
            try { version = ct.GetProperty("Version", BindingFlags.Public | BindingFlags.Instance)?.GetValue(chunk); } catch { }
            var desc = $"{idStr} {ct.Name}" + (version is not null ? $" v{version}" : "");
            if (detailed)
            {
                var sb = new StringBuilder();
                foreach (var fp in ct.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (fp.GetIndexParameters().Length > 0) continue;
                    object? fv;
                    try { fv = fp.GetValue(chunk); } catch { continue; }
                    if (fv is string or bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal || fv is Enum)
                        sb.Append($" {fp.Name}={fv}");
                    else if (fv is Array a) sb.Append($" {fp.Name}=arr[{a.Length}]");
                    else if (fv is null) sb.Append($" {fp.Name}=null");
                    else sb.Append($" {fp.Name}={fv.GetType().Name}");
                }
                desc += sb.ToString();
            }
            ids.Add(desc);
        }
    }
    Indent(depth + 1);
    Console.WriteLine($"{pathStr}.chunks ({ids.Count}): {string.Join(", ", ids)}");
}

static bool IsPrimitiveLike(Type t) =>
    t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime);

static string Format(object o) => o switch
{
    float f => f.ToString("R"),
    double d => d.ToString("R"),
    _ => o.ToString() ?? "?"
};

static void Indent(int depth) => Console.Write(new string(' ', depth * 2));
