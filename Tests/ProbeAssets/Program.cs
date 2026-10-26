using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using GBX.NET;
using GBX.NET.Engines.Game;
using GBX.NET.Engines.GameData;
using TM_Item_Studio.Models;

// Native placement probe assets.
//
// Writes the three inputs the in-game probe plugin needs, and verifies its result:
//   1. the item, copied into the user Items folder so the inventory can resolve it,
//   2. a macroblock that references that item at known relative placements,
//   3. the request file the plugin picks up.
//
// Usage:
//   dotnet run --project Tests/ProbeAssets                      generate with defaults
//   dotnet run --project Tests/ProbeAssets -- --item <path>     generate from another item
//   dotnet run --project Tests/ProbeAssets -- --out <folder>    another user game folder
//   dotnet run --project Tests/ProbeAssets -- --verify [path]   verify the plugin's result
//   dotnet run --project Tests/ProbeAssets -- --inspect-map <path> --skip-lightmap-cache
//     inspect placements while explicitly excluding unsupported native lightmap cache 0304305B

Gbx.LZO = new GBX.NET.LZO.MiniLZO();

const string ProbeFolder = "TMItemStudio_Probe";
const string RequestFolder = "TMItemStudioPlacementProbe";
const string MacroblockName = "TMItemStudio_Probe";
const string MapNamePrefix = "TMIS_PROBE";
const float DegToRad = MathF.PI / 180f;

var outRoot = ArgValue("--out")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Trackmania");
var resultFile = Path.Combine(outRoot, RequestFolder, "placement-result.json");

var verifyArgument = ArgValue("--verify");
if (Array.IndexOf(args, "--verify") >= 0)
    return Verify(verifyArgument ?? resultFile);

// Read back the maps Trackmania wrote, so placement is proven from the saved file instead of
// from what the engine reported while the editor was open.
if (Array.IndexOf(args, "--inspect-map") >= 0)
    return InspectMaps(ArgValue("--inspect-map"), outRoot);

// Build a map that already contains the item as a real anchored object, so the engine's own map
// loader is what gets tested instead of an editor placement API that turned out to be a no-op.
if (Array.IndexOf(args, "--write-map") >= 0)
    return WriteProbeMap(ArgValue("--write-map"), outRoot);

var itemPath = ArgValue("--item") ?? Path.Combine("Test Exported items", "CustomItem_Static.Item.Gbx");

if (!File.Exists(itemPath))
{
    Console.WriteLine($"FAIL item not found: {itemPath}");
    return 1;
}

var item = Gbx.Parse<CGameItemModel>(itemPath, new GbxReadSettings { SafeSkippableChunks = true }).Node
    ?? throw new InvalidOperationException($"Could not parse {itemPath}.");
// Same normalization the studio runs before an export, so the copied file and the macroblock
// reference cannot disagree about the identity.
ItemExportValidator.EnsureGameReady(item, "TMIS_Probe_Item");
var ident = item.Ident?.Id ?? throw new InvalidOperationException("The item has no Ident.Id after normalization.");
var sourcePlacement = item.DefaultPlacement;
Console.WriteLine($"Item            : {itemPath} ({new FileInfo(itemPath).Length} bytes)");
Console.WriteLine($"Ident           : {ident}");
Console.WriteLine($"Source pivots   : {sourcePlacement?.PivotPositions?.Length ?? 0}");
// Magnet locations (chunk 2E020004) have no typed property in the bundled GBX.NET, so only their
// presence is reported here; the in-game probe reads the real values from the engine.
var magnetChunk = sourcePlacement?.Chunks.Get(0x2E020004);
Console.WriteLine($"Magnet chunk    : {(magnetChunk is null ? "absent" : "present")}");

// Explicit, distinct placements: differing positions, yaw and scale, so the read-back can tell
// one instance from another instead of accepting a single copy as proof.
var instances = ProbeInstances();

var export = ItemMacroblock.Export(item, instances, MacroblockName);

using var itemStream = new MemoryStream();
new Gbx<CGameItemModel>(item).Save(itemStream);

// Relative to the user Items folder, which is how the plugin resolves it through a FID.
var itemRelativePath = $"{ProbeFolder}/{ident}.Item.Gbx";
var itemFile = ToDiskPath(outRoot, $"Items/{itemRelativePath}");
Directory.CreateDirectory(Path.GetDirectoryName(itemFile)!);
File.WriteAllBytes(itemFile, itemStream.ToArray());

var macroblockRelativePath = $"Macroblocks/{export.FileName}";
var macroblockFile = ToDiskPath(outRoot, macroblockRelativePath);
Directory.CreateDirectory(Path.GetDirectoryName(macroblockFile)!);
File.WriteAllBytes(macroblockFile, export.Bytes);

var requestFile = Path.Combine(outRoot, RequestFolder, "placement-request.json");
Directory.CreateDirectory(Path.GetDirectoryName(requestFile)!);
var request = new JsonObject
{
    ["version"] = 1,
    ["runId"] = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture),
    // Inert by default: the plugin refuses to place anything until the request is armed, so a
    // request that is simply left on disk cannot act on whatever editor session opens next.
    ["armed"] = false,
    ["itemRelativePath"] = itemRelativePath,
    ["macroblockRelativePath"] = macroblockRelativePath,
    ["itemIdent"] = ident,
    ["mapNamePrefix"] = MapNamePrefix,
    // A new map still counts as thousands of blocks because Trackmania builds its own base
    // platform, so "no blocks" is not a usable emptiness test.
    ["acceptUnnamedMap"] = true,
    // One operation per request, and nothing runs until the editor has settled, so a run can
    // never touch a map while the editor is still loading it.
    ["op"] = "status",
    ["settleTicks"] = 240,
    ["targetCoord"] = new JsonObject { ["x"] = 0, ["y"] = 16, ["z"] = 0 },
    ["direction"] = "North",
    ["expected"] = new JsonArray(instances.Select(instance => (JsonNode)new JsonObject
    {
        ["label"] = instance.Label,
        ["x"] = instance.PosX,
        ["y"] = instance.PosY,
        ["z"] = instance.PosZ,
        ["yawDeg"] = instance.YawDeg,
        ["scale"] = instance.Scale,
    }).ToArray()),
};
File.WriteAllText(requestFile, request.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

Console.WriteLine($"Item written    : {itemFile}");
Console.WriteLine($"Macroblock      : {macroblockFile} ({export.Bytes.Length} bytes, {export.InstanceCount} instances)");
Console.WriteLine($"Request         : {requestFile}  (armed = false)");
Console.WriteLine();
Console.WriteLine($"Open a map whose name starts with '{MapNamePrefix}' in the map editor, set");
Console.WriteLine("\"armed\": true in the request, and let the probe run. It writes placement-result.json");
Console.WriteLine("next to the request file.");
return 0;

// ---------------------------------------------------------------------------------------------
// Verify

int Verify(string resultPath)
{
    if (!File.Exists(resultPath))
    {
        Console.WriteLine($"FAIL result not found: {resultPath}");
        return 1;
    }

    var result = JsonNode.Parse(File.ReadAllText(resultPath))?.AsObject()
        ?? throw new InvalidOperationException("The result file is not a JSON object.");
    var expected = JsonNode.Parse(File.ReadAllText(
        Path.Combine(Path.GetDirectoryName(resultPath)!, "placement-request.json")))!["expected"]!.AsArray();

    var failed = 0;
    var passed = 0;
    void Check(string name, Action body)
    {
        try { body(); passed++; Console.WriteLine($"PASS {name}"); }
        catch (Exception ex) { failed++; Console.WriteLine($"FAIL {name}: {ex.Message}"); }
    }
    static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    var runId = result["runId"]?.GetValue<string>() ?? "(none)";
    // A placed macroblock keeps its items inside a macroblock instance, where they are computed on
    // request; only the destructive expansion path appends plain anchored objects. Both are
    // evidence of placement, so either is accepted and the report says which one was used.
    var instanceItems = result["instanceItems"]?.AsArray() ?? new JsonArray();
    var anchoredObjects = result["objects"]?.AsArray() ?? new JsonArray();
    var useInstancePath = instanceItems.Count > 0;
    var evidence = useInstancePath ? instanceItems : anchoredObjects;
    var evidencePath = useInstancePath ? "macroblock-instance" : "anchored-object";

    Console.WriteLine($"Result      : {resultPath}");
    Console.WriteLine($"Run         : {runId}");
    Console.WriteLine($"Map         : {result["mapName"]?.GetValue<string>() ?? "(none)"}");
    Console.WriteLine($"Accepted by : {result["acceptedBy"]?.GetValue<string>() ?? "(n/a)"}");
    Console.WriteLine($"Placed      : {result["placed"]?.GetValue<bool>()}");
    Console.WriteLine($"Evidence    : {evidence.Count} of {expected.Count} item(s) via {evidencePath}");
    Console.WriteLine($"Also present: {anchoredObjects.Count} plain anchored object(s), "
        + $"{result["instanceItemCount"]?.GetValue<int>() ?? 0} instance item(s) reported");
    if (result["notes"] is JsonValue note) Console.WriteLine($"Notes       : {note}");
    Console.WriteLine();

    Check("plugin reports a completed run", () => Require(
        result["status"]?.GetValue<string>() == "completed", $"status was '{result["status"]}'"));
    Check("the engine placed the macroblock", () => Require(
        result["placed"]?.GetValue<bool>() == true, "PlaceMacroblock returned false"));
    Check("every expected copy was materialised by the engine", () => Require(
        evidence.Count == expected.Count, $"{evidence.Count} materialised instead of {expected.Count}"));

    Check("each materialised copy resolves the probe item by its engine identifier", () =>
    {
        var sourceIdName = result["source"]?["idName"]?.GetValue<string>() ?? "";
        Require(sourceIdName.Length > 0, "the plugin did not report the source item's identifier");
        for (var i = 0; i < evidence.Count; i++)
        {
            Require(evidence[i]!["itemModelNull"]?.GetValue<bool>() == false,
                $"copy {i} has no item model");
            Require(evidence[i]!["itemModelIdName"]?.GetValue<string>() == sourceIdName,
                $"copy {i} resolved to '{evidence[i]!["itemModelIdName"]}' instead of '{sourceIdName}'");
            if (evidence[i]!["isProbeItem"] is JsonValue flag)
                Require(flag.GetValue<bool>(), $"copy {i} is not reported as the probe item");
        }
    });

    Check("placement parameters survive the round trip", () =>
    {
        var sourcePivots = result["source"]?["pivotCount"]?.GetValue<int>() ?? -1;
        var sourceMagnets = result["source"]?["magnetCount"]?.GetValue<int>() ?? -1;
        Require(sourcePivots >= 0, "the plugin did not report the source pivot count");
        for (var i = 0; i < evidence.Count; i++)
        {
            Require(evidence[i]!["pivotCount"]?.GetValue<int>() == sourcePivots,
                $"copy {i} has {evidence[i]!["pivotCount"]} pivots, source has {sourcePivots}");
            Require(evidence[i]!["magnetCount"]?.GetValue<int>() == sourceMagnets,
                $"copy {i} has {evidence[i]!["magnetCount"]} magnets, source has {sourceMagnets}");
        }
    });

    // The placement origin is chosen by the engine in block units, so only the offsets between
    // copies are predictable — exactly the part that proves they landed on the exported grid.
    Check("copies keep their relative offsets", () =>
    {
        var expectedX = expected.Select(e => e!["x"]!.GetValue<float>()).Order().ToArray();
        var expectedGaps = expectedX.Zip(expectedX.Skip(1), (a, b) => b - a).ToArray();
        var actualX = evidence.Select(o => o!["x"]?.GetValue<float>() ?? float.NaN).Order().ToArray();
        var actualGaps = actualX.Zip(actualX.Skip(1), (a, b) => b - a).ToArray();
        Require(actualGaps.Length == expectedGaps.Length,
            $"{actualGaps.Length} gaps instead of {expectedGaps.Length}");
        for (var i = 0; i < actualGaps.Length; i++)
            Require(MathF.Abs(actualGaps[i] - expectedGaps[i]) < 0.01f,
                $"gap {i} is {actualGaps[i]} m instead of {expectedGaps[i]} m");
    });

    // Scale and yaw are only exposed on plain anchored objects. Say so instead of quietly passing
    // or failing when the instance path is the one that produced the evidence.
    if (useInstancePath)
    {
        Console.WriteLine("SKIP scale set is preserved: not exposed on macroblock-instance items");
        Console.WriteLine("SKIP yaw set is preserved: not exposed on macroblock-instance items");
    }
    else
    {
        Check("scale set is preserved", () =>
        {
            var expectedScales = expected.Select(e => e!["scale"]!.GetValue<float>()).Order().ToArray();
            var actualScales = evidence.Select(o => o!["scale"]?.GetValue<float>() ?? float.NaN).Order().ToArray();
            Require(expectedScales.Length == actualScales.Length, "count mismatch");
            for (var i = 0; i < expectedScales.Length; i++)
                Require(MathF.Abs(expectedScales[i] - actualScales[i]) < 0.01f,
                    $"scale {actualScales[i]} != {expectedScales[i]}");
        });

        // Yaw is reported twice by the plugin because the binding's unit is not documented; accept
        // a match in either reading and say which one it was, instead of asserting a guess.
        Check("yaw set is preserved", () =>
        {
            var expectedYaw = expected.Select(e => e!["yawDeg"]!.GetValue<float>()).Order().ToArray();
            foreach (var (key, label) in new[] { ("yawDeg", "degrees"), ("yawRad", "radians") })
            {
                var actual = evidence
                    .Select(o => key == "yawDeg" ? o!["yawDeg"]?.GetValue<float>() ?? float.NaN
                        : o!["yawRad"]?.GetValue<float>() ?? float.NaN)
                    .Select(v => key == "yawDeg" ? v : v * 180f / MathF.PI)
                    .Order().ToArray();
                if (actual.Length != expectedYaw.Length) continue;
                var matches = !expectedYaw.Where((t, i) => MathF.Abs(((t - actual[i] + 180f) % 360f + 360f) % 360f - 180f) > 0.5f).Any();
                if (matches) { Console.WriteLine($"     yaw read as {label}"); return; }
            }
            throw new Exception("no yaw reading matched the exported yaw values");
        });
    }

    // -----------------------------------------------------------------------------------------
    // The saved map on disk. The engine's own report says what it intended; the map file says
    // what it kept, including the yaw and scale the editor session cannot show us.

    var root = Directory.GetParent(Path.GetDirectoryName(resultPath)!)!.FullName;
    var probeIdent = result["source"]?["idName"]?.GetValue<string>() ?? "";
    var mapPath = ArgValue("--map") ?? newestMapMentioning(probeIdent, root) ?? NewestMap(root);
    CGameCtnChallenge? savedMap = null;

    Console.WriteLine();
    if (mapPath is null)
    {
        Console.WriteLine("No .Map.Gbx found under Maps/Autosaves or Maps/My Maps.");
        failed++;
        Console.WriteLine("FAIL a saved map exists to verify against: none found");
    }
    else
    {
        var info = new FileInfo(mapPath);
        Console.WriteLine($"Saved map   : {mapPath}");
        Console.WriteLine($"Written     : {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
        try
        {
            savedMap = Gbx.Parse<CGameCtnChallenge>(mapPath).Node;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Map parse failed: {ex.GetType().Name}: {ex.Message}");
        }

        var stored = savedMap?.AnchoredObjects?.Where(i => IdentMatches(i.ItemModel, probeIdent)).ToList() ?? [];
        var expectedItems = savedMap?.ExpectedEmbeddedItemModels ?? [];
        Console.WriteLine($"Map name    : {savedMap?.MapName ?? "(unparsed)"}");
        Console.WriteLine($"Stored items: {stored.Count} with this ident, {savedMap?.AnchoredObjects?.Count ?? 0} in total");
        Console.WriteLine($"Mob instances: {savedMap?.MacroblockInstances?.Count ?? 0}");
        Console.WriteLine($"Expected    : {expectedItems.Count}"
            + (expectedItems.Count > 0 ? $" [{string.Join(", ", expectedItems.Select(e => e.Id))}]" : ""));
        foreach (var item in stored)
            Console.WriteLine($"   {item.ItemModel?.Id} pos {item.AbsolutePositionInMap} "
                + $"yaw {item.YawPitchRoll.Y * 180f / MathF.PI:F2} deg scale {item.Scale}");
        Console.WriteLine();

        Check("the saved map references the probe item", () => Require(
            Mentions(savedMap, probeIdent),
            $"'{probeIdent}' appears in neither AnchoredObjects nor ExpectedEmbeddedItemModels"));

        Check("the saved map stores every expected copy with the exported transform", () =>
        {
            Require(stored.Count == expected.Count,
                $"{stored.Count} stored instead of {expected.Count}");

            var expectedX = expected.Select(e => e!["x"]!.GetValue<float>()).Order().ToArray();
            var expectedGaps = expectedX.Zip(expectedX.Skip(1), (a, b) => b - a).ToArray();
            var actualX = stored.Select(i => i.AbsolutePositionInMap.X).Order().ToArray();
            var actualGaps = actualX.Zip(actualX.Skip(1), (a, b) => b - a).ToArray();
            Require(actualGaps.Length == expectedGaps.Length,
                $"{actualGaps.Length} gaps instead of {expectedGaps.Length}");
            for (var i = 0; i < actualGaps.Length; i++)
                Require(MathF.Abs(actualGaps[i] - expectedGaps[i]) < 0.01f,
                    $"gap {i} is {actualGaps[i]} m instead of {expectedGaps[i]} m");

            var expectedYaw = expected.Select(e => e!["yawDeg"]!.GetValue<float>()).Order().ToArray();
            var actualYaw = stored.Select(i => i.YawPitchRoll.Y * 180f / MathF.PI).Order().ToArray();
            for (var i = 0; i < expectedYaw.Length; i++)
                Require(MathF.Abs(((expectedYaw[i] - actualYaw[i] + 180f) % 360f + 360f) % 360f - 180f) < 0.5f,
                    $"yaw {actualYaw[i]:F2} != {expectedYaw[i]:F2}");

            var expectedScale = expected.Select(e => e!["scale"]!.GetValue<float>()).Order().ToArray();
            var actualScale = stored.Select(i => i.Scale).Order().ToArray();
            for (var i = 0; i < expectedScale.Length; i++)
                Require(MathF.Abs(expectedScale[i] - actualScale[i]) < 0.01f,
                    $"scale {actualScale[i]} != {expectedScale[i]}");
        });
    }

    Console.WriteLine();
    Console.WriteLine($"pass={passed} fail={failed}");
    return failed == 0 ? 0 : 1;
}

string? NewestMap(string root)
{
    var directories = new[]
    {
        Path.Combine(root, "Maps", "Autosaves"),
        Path.Combine(root, "Maps", "My Maps"),
    };
    return directories
        .Where(Directory.Exists)
        .SelectMany(dir => Directory.GetFiles(dir, "*.Map.Gbx", SearchOption.AllDirectories))
        .Select(path => new FileInfo(path))
        .OrderByDescending(info => info.LastWriteTimeUtc)
        .Select(info => info.FullName)
        .FirstOrDefault();
}

static bool Mentions(CGameCtnChallenge? map, string ident) =>
    !string.IsNullOrEmpty(ident) && map is not null
    && ((map.AnchoredObjects?.Any(item => IdentMatches(item.ItemModel, ident)) ?? false)
        || (map.ExpectedEmbeddedItemModels?.Any(item => IdentMatches(item, ident)) ?? false));

// ---------------------------------------------------------------------------------------------
// Building a map that already holds the item

int WriteProbeMap(string? requestedBase, string root)
{
    var basePath = requestedBase
        ?? new[] { Path.Combine(root, "Maps", "My Maps", "TMIS_PROBE.Map.Gbx") }.FirstOrDefault(File.Exists)
        ?? NewestMap(root);
    if (basePath is null || !File.Exists(basePath))
    {
        Console.WriteLine("FAIL no base map found; pass --write-map <path to a .Map.Gbx>");
        return 1;
    }

    var itemPath = ArgValue("--item") ?? Path.Combine("Test Exported items", "CustomItem_Static.Item.Gbx");
    var item = Gbx.Parse<CGameItemModel>(itemPath, new GbxReadSettings { SafeSkippableChunks = true }).Node
        ?? throw new InvalidOperationException($"Could not parse {itemPath}.");
    ItemExportValidator.EnsureGameReady(item, "TMIS_Probe_Item");
    var itemIdent = item.Ident ?? throw new InvalidOperationException("The item has no Ident.");

    var map = Gbx.Parse<CGameCtnChallenge>(basePath, new GbxReadSettings { SafeSkippableChunks = true }).Node
        ?? throw new InvalidOperationException($"Could not parse {basePath}.");

    // A map reference to an item is a path-like ident, as the engine itself writes it: a real
    // object in AutoSave11 carries 'BF2_ASSETS\CollectablesOptimized\Starter_Dip_Optimized.Item.Gbx'.
    var referenceId = JsonStringOrDefault(ArgValue("--item-relative"))
        ?? $"{ProbeFolder}\\{itemIdent.Id}.Item.Gbx";

    // The anchored object references the item by path, so that file has to exist where the game
    // resolves it from, otherwise the map points at nothing.
    using (var itemStream = new MemoryStream())
    {
        new Gbx<CGameItemModel>(item).Save(itemStream);
        var itemFile = ToDiskPath(root, $"Items/{ProbeFolder}/{itemIdent.Id}.Item.Gbx");
        Directory.CreateDirectory(Path.GetDirectoryName(itemFile)!);
        File.WriteAllBytes(itemFile, itemStream.ToArray());
        Console.WriteLine($"Item copied: {itemFile}");
    }

    var instances = ProbeInstances();
    var anchored = map.AnchoredObjects ?? [];
    // Objects the base map already held. Everything from this index on is what this run appends,
    // which is what the checks below may judge — a base map may well hold the same item already.
    var baseAnchoredCount = anchored.Count;
    foreach (var instance in instances)
    {
        var placed = new CGameCtnAnchoredObject
        {
            ItemModel = new Ident(referenceId, itemIdent.Collection, itemIdent.Author),
            AbsolutePositionInMap = new Vec3(instance.PosX, instance.PosY, instance.PosZ),
            YawPitchRoll = new Vec3(instance.PitchDeg * DegToRad, instance.YawDeg * DegToRad, instance.RollDeg * DegToRad),
            PivotPosition = new Vec3(instance.PosX, instance.PosY, instance.PosZ),
            Scale = instance.Scale,
            AnchorTreeId = referenceId,
        };

        // A freshly constructed GBX.NET node has no chunks, so without these the object serializes
        // to nothing at all. The versions mirror an object the engine itself wrote (AutoSave11).
        // Chunk 0x03101005 must NOT be created: it is an ignored chunk GBX.NET only knows how to
        // read back, so adding it makes the whole map fail to write. Tests/MapItemRoundTrip covers
        // both that failure and the working shape.
        placed.CreateChunk<CGameCtnAnchoredObject.Chunk03101002>().Version = 8;
        placed.CreateChunk<CGameCtnAnchoredObject.Chunk03101004>().Version = 0;

        anchored.Add(placed);
    }
    map.AnchoredObjects = anchored;

    // ExpectedEmbeddedItemModels is read-only in the bundled GBX.NET, so the list the game
    // maintains itself cannot be written here; the anchored object identity has to carry it.
    using var stream = new MemoryStream();
    new Gbx<CGameCtnChallenge>(map).Save(stream);

    var suffix = JsonStringOrDefault(ArgValue("--out-name")) ?? "ItemProbe";
    var outFile = Path.Combine(root, "Maps", "My Maps", Path.GetFileNameWithoutExtension(basePath) + $"_{suffix}.Map.Gbx");
    Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);
    File.WriteAllBytes(outFile, stream.ToArray());

    var reread = Gbx.Parse<CGameCtnChallenge>(new MemoryStream(stream.ToArray())).Node!;
    var stored = (reread.AnchoredObjects ?? []).Skip(baseAnchoredCount).ToList();
    var referencing = stored.Count(a => IdentMatches(a.ItemModel, referenceId));
    Console.WriteLine($"Base map   : {basePath}");
    Console.WriteLine($"Item       : {itemIdent.Id}  referenced as '{referenceId}'");
    Console.WriteLine($"Written    : {outFile} ({stream.Length} bytes)");
    Console.WriteLine($"Re-read    : {referencing} of {instances.Count} added copies reference this item "
        + $"({reread.AnchoredObjects?.Count ?? 0} anchored object(s) in total, {baseAnchoredCount} from the base map)");
    Console.WriteLine($"Expected   : {reread.ExpectedEmbeddedItemModels?.Count ?? 0} embedded item model(s) (read-only in GBX.NET)");
    if (referencing != instances.Count)
    {
        Console.WriteLine("FAIL the written map does not re-read with every copy");
        return 1;
    }

    // What the file kept, not what this tool intended: the transform is the part a wrong chunk
    // version silently drops, and the offsets are what the game then has to reproduce.
    var mismatched = 0;
    for (var i = 0; i < instances.Count; i++)
    {
        var expectedInstance = instances[i];
        var actual = stored[i];
        Console.WriteLine($"  copy {i + 1} '{expectedInstance.Label}' "
            + $"pos {actual.AbsolutePositionInMap} yaw {actual.YawPitchRoll.Y * 180f / MathF.PI:F2} deg scale {actual.Scale}");
        if (Math.Abs(actual.AbsolutePositionInMap.X - expectedInstance.PosX) > 1e-3f
            || Math.Abs(actual.AbsolutePositionInMap.Z - expectedInstance.PosZ) > 1e-3f
            || Math.Abs(actual.Scale - expectedInstance.Scale) > 1e-4f
            || Math.Abs(((actual.YawPitchRoll.Y * 180f / MathF.PI - expectedInstance.YawDeg + 180f) % 360f + 360f) % 360f - 180f) > 0.5f)
        {
            Console.WriteLine($"   FAIL expected pos ({expectedInstance.PosX}, {expectedInstance.PosZ}) "
                + $"yaw {expectedInstance.YawDeg} scale {expectedInstance.Scale}");
            mismatched++;
        }
    }
    if (mismatched > 0)
    {
        Console.WriteLine($"FAIL {mismatched} of {instances.Count} copies lost their transform");
        return 1;
    }
    Console.WriteLine("OK every copy kept its position, yaw and scale in the written file");
    Console.WriteLine();
    Console.WriteLine("This has not been loaded by Trackmania. A malformed anchored object can crash the");
    Console.WriteLine("game, so open this copy deliberately, with the original map left untouched.");
    return 0;
}

static string? JsonStringOrDefault(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

static List<ItemMacroblockInstance> ProbeInstances() =>
[
    new() { Label = "Probe A", PosX = 8f, PosY = 0f, PosZ = -16f, YawDeg = 0f, Scale = 1f },
    new() { Label = "Probe B", PosX = 24f, PosY = 0f, PosZ = -16f, YawDeg = 90f, Scale = 1f },
    new() { Label = "Probe C", PosX = 40f, PosY = 0f, PosZ = -16f, YawDeg = 180f, Scale = 2f },
];

// ---------------------------------------------------------------------------------------------
// Saved maps

string? newestMapMentioning(string ident, string root)
{
    var directories = new[]
    {
        Path.Combine(root, "Maps", "Autosaves"),
        Path.Combine(root, "Maps", "My Maps"),
    };
    var candidates = directories
        .Where(Directory.Exists)
        .SelectMany(dir => Directory.GetFiles(dir, "*.Map.Gbx", SearchOption.AllDirectories))
        .Select(path => new FileInfo(path))
        .OrderByDescending(info => info.LastWriteTimeUtc)
        .ToList();

    foreach (var candidate in candidates)
    {
        if (MapMentions(candidate.FullName, ident))
            return candidate.FullName;
    }
    return null;
}

bool MapMentions(string path, string ident)
{
    try
    {
        return Mentions(Gbx.Parse<CGameCtnChallenge>(path).Node, ident);
    }
    catch
    {
        return false;
    }
}

static bool IdentMatches(Ident? ident, string id) =>
    !string.IsNullOrEmpty(id)
    && string.Equals(ident?.Id, id, StringComparison.OrdinalIgnoreCase);

int InspectMaps(string? requested, string root)
{
    var failures = 0;
    var paths = new List<string>();
    if (!string.IsNullOrWhiteSpace(requested))
    {
        paths.Add(requested);
    }
    else
    {
        foreach (var directory in new[] { Path.Combine(root, "Maps", "Autosaves"), Path.Combine(root, "Maps", "My Maps") })
            if (Directory.Exists(directory))
                paths.AddRange(Directory.GetFiles(directory, "*.Map.Gbx", SearchOption.AllDirectories));
        paths = paths.OrderByDescending(path => new FileInfo(path).LastWriteTimeUtc).Take(6).ToList();
    }

    var requestPath = Path.Combine(root, RequestFolder, "placement-request.json");
    var probeIdent = File.Exists(requestPath)
        ? JsonNode.Parse(File.ReadAllText(requestPath))?["itemIdent"]?.GetValue<string>() ?? ""
        : "";
    Console.WriteLine($"Probe item ident: {probeIdent}");
    Console.WriteLine();

    foreach (var path in paths)
    {
        var info = new FileInfo(path);
        Console.WriteLine($"=== {path}");
        Console.WriteLine($"    written {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}  {info.Length} bytes");
        try
        {
            var skipLightmap = Array.IndexOf(args, "--skip-lightmap-cache") >= 0;
            if (skipLightmap)
                Console.WriteLine("    excluded      : 0304305B lightmap cache; this is placement evidence only");
            var map = Gbx.Parse<CGameCtnChallenge>(path, new GbxReadSettings
            {
                SkipChunkIds = skipLightmap ? [0x0304305B] : null
            }).Node
                ?? throw new InvalidOperationException("no readable node");
            var anchored = map.AnchoredObjects ?? [];
            var mine = anchored.Where(item => IdentMatches(item.ItemModel, probeIdent)).ToList();

            Console.WriteLine($"    map name      : {map.MapName}");
            Console.WriteLine($"    blocks        : {map.Blocks?.Count ?? 0}");
            Console.WriteLine($"    anchored      : {anchored.Count} (of which ours: {mine.Count})");
            Console.WriteLine($"    macroblock ref: {anchored.Count(item => item.MacroblockReference is not null)}");
            Console.WriteLine($"    macroblock inst: {map.MacroblockInstances?.Count ?? 0}");
            var expected = map.ExpectedEmbeddedItemModels ?? [];
            Console.WriteLine($"    expected items: {expected.Count}"
                + (expected.Count > 0 ? $" [{string.Join(", ", expected.Select(e => e.Id))}]" : ""));
            Console.WriteLine($"    mentions ours : {MapMentions(path, probeIdent)}");

            // Always show what is actually stored, not only the copies that matched by id, so a
            // written file that does not resolve shows what the engine/parser kept instead.
            foreach (var item in anchored.Take(4))
            {
                Console.WriteLine($"      stored ident: id='{item.ItemModel?.Id}'"
                    + $" collection='{item.ItemModel?.Collection}' author='{item.ItemModel?.Author}'"
                    + $" pos {item.AbsolutePositionInMap} scale {item.Scale}");
            }

            // Chunk layout of a real, engine-written object: a freshly constructed node carries no
            // chunks, so a hand-built object serializes to nothing unless the chunks are created.
            if (anchored.Count > 0)
            {
                var sample = anchored[0];
                Console.WriteLine($"      first object chunks: {sample.Chunks.Count()}");
                foreach (var chunk in sample.Chunks)
                    Console.WriteLine($"        {chunk.GetType().Name} id=0x{chunk.Id:X8}"
                        + (chunk is IVersionable versioned ? $" v{versioned.Version}" : ""));
            }

            foreach (var item in mine)
            {
                Console.WriteLine($"      item {item.ItemModel?.Id}");
                Console.WriteLine($"        pos     {item.AbsolutePositionInMap}");
                Console.WriteLine($"        ypr     {item.YawPitchRoll}  (radians)");
                Console.WriteLine($"        scale   {item.Scale}  unitCoord {item.BlockUnitCoord}");
                Console.WriteLine($"        macroblockRef {(item.MacroblockReference is null ? "none" : "present")}");
            }
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"    FAILED to parse: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
        Console.WriteLine();
    }
    return failures == 0 ? 0 : 1;
}

string? ArgValue(string name)
{
    var index = Array.IndexOf(args, name);
    if (index < 0) return null;
    return index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)
        ? args[index + 1]
        : null;
}

static string ToDiskPath(string root, string relative) =>
    Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
