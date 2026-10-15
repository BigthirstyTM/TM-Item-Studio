using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.MetaNotPersistent;
using GBX.NET.Engines.MwFoundations;
using GBX.NET.Engines.Plug;
using KC = GBX.NET.Engines.Meta.NPlugDyna_SKinematicConstraint;

// Counts the kinematic timeline segments in every item of a corpus, so the engine's real
// segment limit can be read off authored content instead of guessed.
//
// Usage: MotionSegmentScan <corpusDirectory>

if (args.Length != 1)
    throw new ArgumentException("Usage: MotionSegmentScan <corpusDirectory>");

Gbx.LZO = new GBX.NET.LZO.MiniLZO();

var corpus = args[0];
var histogram = new SortedDictionary<int, int>();
var exporters = new SortedDictionary<int, string>();
var failures = 0;
var scanned = 0;

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

foreach (var path in Directory.EnumerateFiles(corpus, "*.Item.Gbx", SearchOption.AllDirectories))
{
    Gbx<CGameItemModel> document;
    try
    {
        document = Gbx.Parse<CGameItemModel>(path);
    }
    catch
    {
        failures++;
        continue;
    }

    scanned++;
    foreach (var constraint in FindConstraints(document.Node.EntityModel))
    {
        foreach (var timeline in new[] { constraint.TransAnimFunc, constraint.RotAnimFunc })
        {
            var count = timeline?.SubFuncs?.Length ?? 0;
            if (count == 0)
                continue;
            histogram[count] = histogram.GetValueOrDefault(count) + 1;
            if (!exporters.ContainsKey(count))
                exporters[count] = path;
        }
    }
}

Console.WriteLine($"scanned={scanned} unparsable={failures}");
Console.WriteLine("segments  timelines  example");
foreach (var (count, occurrences) in histogram)
    Console.WriteLine($"{count,8}  {occurrences,9}  {exporters[count]}");
Console.WriteLine($"max={histogram.Keys.DefaultIfEmpty(0).Max()}");
