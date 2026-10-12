using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.MetaNotPersistent;
using GBX.NET.Engines.MwFoundations;
using GBX.NET.Engines.Plug;
using TM_Item_Studio.Models;
using KC = GBX.NET.Engines.Meta.NPlugDyna_SKinematicConstraint;

// Writes the in-game test items for the placement editor and the group motion at the engine's
// straight through the studio's own export path, so Trackmania loads exactly what the app
// would hand it. Files land in the "02. Freebuff RE Items" Items subfolder, which the game
// shows in its own item browser without an import step.
//
// Usage: GameTestItems <outputDir> <placementSource.Item.Gbx> <kinematicSource.Item.Gbx> [probeDir]
//
// The engine only loads kinematic timelines of up to four segments (see docs/item-gbx-chunks.md,
// section 16): five segments are refused by the native loader and eight or more terminate the
// process. Nothing above four segments is ever written into the Items folder, because the game's
// own item browser would load it. The optional probe directory exists outside Items/ for the
// deliberate limit probe.

if (args.Length is < 3 or > 4)
    throw new ArgumentException(
        "Usage: GameTestItems <outputDir> <placementSource.Item.Gbx> <kinematicSource.Item.Gbx> [probeDir]");

const int EngineSegmentLimit = 4;

Gbx.LZO = new GBX.NET.LZO.MiniLZO();

var outputDirectory = args[0];
var placementSourcePath = args[1];
var kinematicSourcePath = args[2];
var probeDirectory = args.Length == 4 ? args[3] : null;
Directory.CreateDirectory(outputDirectory);

var written = new List<string>();

Gbx<CGameItemModel> Load(string path) => Gbx.Parse<CGameItemModel>(path);

/// <summary>Save through the same path the app uses, then prove the file reloads with the edit intact.</summary>
void Export(string name, Gbx<CGameItemModel> document, Action<CGameItemModel> verify, string? directory = null)
{
    ItemExportValidator.EnsureGameReady(document.Node, name);
    var source = ItemVariantSource.FromFile(name, document)[0];
    var destination = Path.Combine(directory ?? outputDirectory, name + ".Item.Gbx");
    using (var stream = File.Create(destination))
        source.Save(stream);

    var reloaded = Load(destination);
    verify(reloaded.Node);
    written.Add(destination);
    Console.WriteLine($"WROTE {Path.GetFileName(destination)}");
}

/// <summary>Depth-first walk of the entity tree; kinematic constraints sit in prefab entries.</summary>
List<KC> FindConstraints(CMwNod? root)
{
    var found = new List<KC>();
    var seen = new HashSet<CMwNod>(ReferenceEqualityComparer.Instance);
    var queue = new Queue<CMwNod>();
    if (root is not null)
        queue.Enqueue(root);

    while (queue.Count > 0)
    {
        var node = queue.Dequeue();
        if (node is null || !seen.Add(node))
            continue;

        switch (node)
        {
            case KC constraint:
                found.Add(constraint);
                break;
            case CPlugPrefab prefab when prefab.Ents is not null:
                foreach (var entry in prefab.Ents)
                    if (entry?.Model is not null)
                        queue.Enqueue(entry.Model);
                break;
            case NPlugItem_SVariantList list when list.Variants is not null:
                foreach (var variant in list.Variants)
                    if (variant?.EntityModel is not null)
                        queue.Enqueue(variant.EntityModel);
                break;
            case NPlugItem_SVariant variant when variant.EntityModel is not null:
                queue.Enqueue(variant.EntityModel);
                break;
        }
    }

    return found;
}

// ---- 1. control: the untouched source through the same save path ----
var placementSource = Load(placementSourcePath);
Export("FBRE_Control", placementSource, _ => { });

// ---- 2. ten pivots, placement flags and two magnet locations ----
var placement = Load(placementSourcePath);
var placementParam = placement.Node.DefaultPlacement
    ?? throw new InvalidOperationException("The placement source has no default placement parameter.");

placementParam.CreateChunk<CGameItemPlacementParam.Chunk2E020001>();
while (ItemEdits.ReadPivots(placementParam).Length < 10)
{
    var index = ItemEdits.ReadPivots(placementParam).Length;
    ItemEdits.AddPivot(placementParam, new ItemEdits.Pivot(
        new Vec3(index * 4, 0, 0),
        new Quat(0, 0, 0, 1)));
}

placementParam.SwitchPivotManually = true;
placementParam.NotOnObject = true;
placementParam.YawOnly = false;
placementParam.AutoRotation = false;
ItemPlacementAlternatives.Write(placementParam,
    [new(new Vec3(8, 8, 0), new Vec3(90, 0, 0)), new(new Vec3(-8, 8, 0), new Vec3(-90, 0, 0))]);

Export("FBRE_Placement10", placement, node =>
{
    var reloadedParam = node.DefaultPlacement
        ?? throw new InvalidOperationException("The reloaded placement item lost its placement parameter.");
    var pivots = ItemEdits.ReadPivots(reloadedParam);
    if (pivots.Length != 10)
        throw new InvalidOperationException($"Reloaded pivot count is {pivots.Length}, expected 10.");
    if (!reloadedParam.SwitchPivotManually || !reloadedParam.NotOnObject || reloadedParam.YawOnly || reloadedParam.AutoRotation)
        throw new InvalidOperationException("Reloaded placement flags do not match the edit.");
    var magnets = ItemPlacementAlternatives.Read(reloadedParam);
    if (magnets.Length != 2 || magnets[0].Position != new Vec3(8, 8, 0))
        throw new InvalidOperationException("Reloaded magnet locations do not match the edit.");
});

// ---- 3. and 4. group motion at four and at ten segments ----
foreach (var segments in new[] { 4, 5, 8, 10 })
{
    if (segments > EngineSegmentLimit && probeDirectory is null)
    {
        Console.WriteLine($"SKIP {segments} segments: above the engine's {EngineSegmentLimit}-segment limit " +
            "and no probe directory was given.");
        continue;
    }

    var target = segments > EngineSegmentLimit ? probeDirectory! : outputDirectory;
    if (segments > EngineSegmentLimit)
        Directory.CreateDirectory(target);

    var kinematic = Load(kinematicSourcePath);
    var constraints = FindConstraints(kinematic.Node.EntityModel);
    if (constraints.Count == 0)
        throw new InvalidOperationException("The kinematic source exposes no kinematic constraint.");

    var writtenTimelines = 0;
    foreach (var constraint in constraints)
    {
        foreach (var timeline in new[] { constraint.TransAnimFunc, constraint.RotAnimFunc })
        {
            if (timeline is null)
                continue;
            var before = timeline.SubFuncs?.Length ?? 0;
            if (segments > EngineSegmentLimit)
            {
                // The studio refuses this on purpose, so the probe writes the keys itself: this is a
                // measurement of the engine, not a path the app is allowed to take.
                timeline.SubFuncs = Enumerable.Range(0, segments).Select(_ => new KC.SubAnimFunc
                {
                    Ease = KC.AnimEase.Linear,
                    Reverse = false,
                    Duration = new TmEssentials.TimeInt32(1000)
                }).ToArray();
            }
            else
            {
                var result = ItemMotion.ResizeTimeline(timeline, segments);
                // A timeline that refuses the count would make the game test compare the wrong thing.
                if (result.Status != ItemMotionStatus.Supported)
                    throw new InvalidOperationException($"Could not set {segments} segments: {result.Reason}");
            }

            writtenTimelines += timeline.SubFuncs?.Length ?? 0;
            Console.WriteLine($"  {segments} segments: isDuration={timeline.IsDuration} keys {before} -> {timeline.SubFuncs?.Length ?? 0}");
        }
    }

    if (writtenTimelines == 0)
        throw new InvalidOperationException("No timeline was resized; the game test would compare nothing.");

    Export($"FBRE_Kinematic{segments:00}", kinematic, node =>
    {
        var reloaded = FindConstraints(node.EntityModel);
        if (reloaded.Count != constraints.Count)
            throw new InvalidOperationException($"Reloaded constraint count is {reloaded.Count}, expected {constraints.Count}.");
        var total = 0;
        foreach (var constraint in reloaded)
        {
            foreach (var timeline in new[] { constraint.TransAnimFunc, constraint.RotAnimFunc })
            {
                if (timeline is null)
                    continue;
                if (timeline.SubFuncs is not { } keys || keys.Length != segments)
                    throw new InvalidOperationException(
                        $"Reloaded segment count is {timeline.SubFuncs?.Length ?? -1}, expected {segments}.");
                total += keys.Length;
            }
        }

        if (total != writtenTimelines)
            throw new InvalidOperationException($"Reloaded {total} segments in total, expected {writtenTimelines}.");
    }, target);
}

Console.WriteLine($"PASS: {written.Count} test items written");
foreach (var path in written)
    Console.WriteLine("  " + path);
