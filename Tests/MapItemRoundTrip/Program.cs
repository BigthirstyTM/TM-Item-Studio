using GBX.NET;
using GBX.NET.Engines.Game;
using GBX.NET.Serialization.Chunking;

// Map placement round-trip probe.
//
// Answers two questions about writing a map that holds a Studio item as a real anchored object:
//
//   1. Does a map the engine itself wrote still survive parse -> save with the bundled GBX.NET?
//      (The item chunk 0x03101005 is an "ignored" skippable chunk: the engine writes it, the
//      reader discards the body.)
//   2. Can this tooling write its own anchored object at all, and which chunks does the writer
//      actually need for the object to reach the file?
//
// Usage:
//   dotnet run --project Tests/MapItemRoundTrip                scan, round-trip, build
//   dotnet run --project Tests/MapItemRoundTrip -- --map <path> use one base map
//   dotnet run --project Tests/MapItemRoundTrip -- --scan       list maps and their item counts

Gbx.LZO = new GBX.NET.LZO.MiniLZO();

var failed = 0;
var passed = 0;

void Check(string name, Action body)
{
    try
    {
        body();
        passed++;
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"FAIL {name}: {ex.GetType().Name}: {ex.Message}");
    }
}

static void Require(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

var mapsRoot = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Trackmania");
var explicitMap = ArgValue("--map");

string? ArgValue(string name)
{
    var index = Array.IndexOf(args, name);
    if (index < 0) return null;
    return index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)
        ? args[index + 1]
        : null;
}

// ---------------------------------------------------------------------------------------------
// Scan: which saved maps actually carry anchored objects

var candidates = new List<string>();
if (explicitMap is not null)
{
    candidates.Add(explicitMap);
}
else
{
    foreach (var directory in new[]
    {
        Path.Combine(mapsRoot, "Maps", "Autosaves"),
        Path.Combine(mapsRoot, "Maps", "My Maps"),
    })
    {
        if (Directory.Exists(directory))
            candidates.AddRange(Directory.GetFiles(directory, "*.Map.Gbx", SearchOption.AllDirectories));
    }
    candidates = candidates.OrderByDescending(path => new FileInfo(path).LastWriteTimeUtc).ToList();
}

Console.WriteLine($"Maps root : {mapsRoot}");
Console.WriteLine($"Candidates: {candidates.Count}");
Console.WriteLine();

CGameCtnChallenge? load(string path)
{
    try
    {
        return Gbx.Parse<CGameCtnChallenge>(path, new GbxReadSettings { SafeSkippableChunks = true }).Node;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"    unreadable: {ex.GetType().Name}: {ex.Message}");
        return null;
    }
}

static byte[] save(CGameCtnChallenge map)
{
    using var stream = new MemoryStream();
    new Gbx<CGameCtnChallenge>(map).Save(stream);
    return stream.ToArray();
}

// Only the newest handful of maps is scanned: this is about finding one real map that holds
// items, not about auditing the whole library.
var scanLimit = Array.IndexOf(args, "--scan") >= 0 ? candidates.Count : candidates.Count;
var withItems = new List<(string Path, CGameCtnChallenge Map)>();
foreach (var path in candidates.Take(scanLimit))
{
    var map = load(path);
    if (map is null) continue;
    var anchored = map.AnchoredObjects?.Count ?? 0;
    Console.WriteLine($"{(anchored > 0 ? "items" : "     ")} {anchored,4} anchored  "
        + $"{map.MacroblockInstances?.Count ?? 0,3} macroblock inst  {Path.GetFileName(path)}");
    if (anchored > 0) withItems.Add((path, map));
}

Console.WriteLine();
Console.WriteLine($"Maps with anchored objects: {withItems.Count} of {Math.Min(scanLimit, candidates.Count)} scanned");
if (explicitMap is null && withItems.Count == 0)
{
    Console.WriteLine("No saved map holds an anchored object, so the round-trip check has nothing real to run on.");
    Console.WriteLine("Publish a map with a custom item from the editor and re-run.");
    Console.WriteLine();
    Console.WriteLine($"pass={passed} fail={failed}");
    return failed == 0 ? 0 : 1;
}

var baseMapPath = explicitMap ?? withItems.OrderByDescending(entry => new FileInfo(entry.Path).LastWriteTimeUtc).First().Path;
var baseMap = withItems.FirstOrDefault(entry => entry.Path == baseMapPath).Map ?? load(baseMapPath);
if (baseMap is null)
{
    Console.WriteLine($"FAIL could not read the base map {baseMapPath}");
    return 1;
}

Console.WriteLine();
Console.WriteLine($"Base map  : {baseMapPath}");
Console.WriteLine($"            {baseMap.AnchoredObjects?.Count ?? 0} anchored, "
    + $"{baseMap.ExpectedEmbeddedItemModels?.Count ?? 0} expected item model(s)");
Console.WriteLine();

// ---------------------------------------------------------------------------------------------
// 1. Does the engine's own map survive a parse -> save round trip?

var preserved = 0;
Check("a map with engine-written anchored objects survives parse -> save", () =>
{
    var bytes = save(baseMap);
    Require(bytes.Length > 0, "the writer produced no bytes");

    var reparsed = Gbx.Parse<CGameCtnChallenge>(new MemoryStream(bytes), new GbxReadSettings { SafeSkippableChunks = true }).Node;
    Require(reparsed is not null, "the written map did not parse again");

    var before = baseMap.AnchoredObjects ?? [];
    var after = reparsed!.AnchoredObjects ?? [];
    Require(after.Count == before.Count, $"{after.Count} anchored object(s) instead of {before.Count}");

    for (var i = 0; i < before.Count; i++)
    {
        Require(string.Equals(after[i].ItemModel?.Id, before[i].ItemModel?.Id, StringComparison.Ordinal),
            $"object {i} came back as '{after[i].ItemModel?.Id}' instead of '{before[i].ItemModel?.Id}'");
        Require(Math.Abs(after[i].AbsolutePositionInMap.X - before[i].AbsolutePositionInMap.X) < 1e-3f,
            $"object {i} moved from X {before[i].AbsolutePositionInMap.X} to {after[i].AbsolutePositionInMap.X}");
        Require(Math.Abs(after[i].Scale - before[i].Scale) < 1e-4f,
            $"object {i} changed scale from {before[i].Scale} to {after[i].Scale}");
    }
    preserved = after.Count;
});

// ---------------------------------------------------------------------------------------------
// 2. Can an anchored object we build ourselves reach the file?

var syntheticChunks = new[]
{
    (Label: "item chunk only (0x03101002 v8)", IncludeIgnored: false),
    (Label: "with the ignored chunk 0x03101005", IncludeIgnored: true),
};

foreach (var variant in syntheticChunks)
{
    Console.WriteLine();
    Console.WriteLine($"--- synthetic anchored object, {variant.Label}");
    var outcome = "not attempted";
    Check($"a hand-built anchored object writes and reads back ({variant.Label})", () =>
    {
        var map = load(baseMapPath) ?? throw new Exception("the base map was not readable");

        // A freshly constructed node owns no chunks; without them the object serializes to
        // nothing at all, which is what an earlier attempt produced (three empty objects).
        var placed = new CGameCtnAnchoredObject
        {
            ItemModel = new Ident(@"TMItemStudio_Probe\RoundTrip.Item.Gbx", "Stadium", "Nadeo"),
            AbsolutePositionInMap = new Vec3(16f, 0f, -16f),
            YawPitchRoll = new Vec3(0f, 0f, 0f),
            PivotPosition = new Vec3(16f, 0f, -16f),
            Scale = 1.5f,
            AnchorTreeId = "TMItemStudio_Probe",
        };
        placed.CreateChunk<CGameCtnAnchoredObject.Chunk03101002>().Version = 8;
        if (variant.IncludeIgnored) placed.CreateChunk<CGameCtnAnchoredObject.Chunk03101005>();

        var anchored = map.AnchoredObjects ?? [];
        anchored.Add(placed);
        map.AnchoredObjects = anchored;

        var bytes = save(map);
        var reparsed = Gbx.Parse<CGameCtnChallenge>(new MemoryStream(bytes), new GbxReadSettings { SafeSkippableChunks = true }).Node
            ?? throw new Exception("the written map did not parse again");

        var mine = (reparsed.AnchoredObjects ?? [])
            .Where(item => item.ItemModel?.Id == placed.ItemModel.Id && item.AbsolutePositionInMap.X == 16f)
            .ToList();
        outcome = $"{bytes.Length} bytes, {mine.Count} object(s) matched";
        Require(mine.Count == 1, $"the reparse found {mine.Count} matching object(s)");
        Require(Math.Abs(mine[0].Scale - 1.5f) < 1e-4f, $"scale came back as {mine[0].Scale}");
    });
    Console.WriteLine($"    outcome: {outcome}");
}

Console.WriteLine();
Console.WriteLine($"preserved anchored objects: {preserved}");
Console.WriteLine($"pass={passed} fail={failed}");
return failed == 0 ? 0 : 1;
