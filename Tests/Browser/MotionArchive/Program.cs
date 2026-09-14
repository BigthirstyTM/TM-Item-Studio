using System.Text.Json;
using System.Globalization;
using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.MetaNotPersistent;
using GBX.NET.Engines.MwFoundations;
using GBX.NET.Engines.Plug;
using GBX.NET.Serialization;

Gbx.LZO = new GBX.NET.LZO.MiniLZO();
if (args.Length % 2 != 1) throw new ArgumentException("Usage: MotionArchive <item> [translation-max <metres>] [translation-duration <ms>] [translation-count|rotation-count <count>]");
var item = Gbx.Parse<CGameItemModel>(args[0]).Node;
var constraints = new List<object>();
Visit(item, "item");
Console.WriteLine(JsonSerializer.Serialize(constraints));

void Visit(CMwNod? node, string path)
{
    switch (node)
    {
        case CGameItemModel model:
            Visit(model.EntityModel, path + "/entity");
            break;
        case NPlugItem_SVariantList list:
            var variants = list.Variants ?? Array.Empty<NPlugItem_SVariant>();
            for (var i = 0; i < variants.Length; i++)
                Visit(variants[i].EntityModel, path + "/variant:" + i);
            break;
        case CPlugPrefab prefab:
            for (var i = 0; i < prefab.Ents.Length; i++)
            {
                Visit(prefab.Ents[i].Model, path + "/ent:" + i);
            }
            break;
        case NPlugDyna_SKinematicConstraint constraint:
            // Independent expected explicit edits to the first authored constraint.
            // Shared references naturally retain the same native sharing behavior.
            if (constraints.Count == 0)
                for (var i = 1; i < args.Length; i += 2)
                    switch (args[i])
                    {
                        case "translation-max": constraint.TransMax = float.Parse(args[i + 1], CultureInfo.InvariantCulture); break;
                        case "translation-duration": constraint.TransAnimFunc!.SubFuncs![0].Duration = new TmEssentials.TimeInt32(int.Parse(args[i + 1], CultureInfo.InvariantCulture)); break;
                        case "translation-count":
                        case "rotation-count":
                            var timeline = args[i] == "translation-count" ? constraint.TransAnimFunc! : constraint.RotAnimFunc!;
                            var count = int.Parse(args[i + 1], CultureInfo.InvariantCulture);
                            var retained = timeline.SubFuncs!.Take(count).ToList();
                            while (retained.Count < count)
                                retained.Add(new() { Ease = NPlugDyna_SKinematicConstraint.AnimEase.Linear, Reverse = false,
                                    Duration = new TmEssentials.TimeInt32(timeline.IsDuration || retained.Count == 0 ? 1000 : retained[^1].Duration.TotalMilliseconds + 1000) });
                            timeline.SubFuncs = retained.ToArray();
                            break;
                        default: throw new ArgumentException("Unknown expected edit");
                    }
            // Independent archive oracle: all serialized fields, including shader
            // functions and unknown data, not the Studio motion adapter's projection.
            using (var stream = new MemoryStream())
            {
                using var writer = new GbxWriter(stream);
                using var rw = new GbxReaderWriter(writer);
                constraint.ReadWrite(rw);
                constraints.Add(new { path, bytes = Convert.ToBase64String(stream.ToArray()),
                    constraint.TransMin, constraint.TransMax, constraint.TransAxis,
                    constraint.AngleMinDeg, constraint.AngleMaxDeg, constraint.RotAxis,
                    translationIsDuration = constraint.TransAnimFunc?.IsDuration,
                    rotationIsDuration = constraint.RotAnimFunc?.IsDuration,
                    translation = constraint.TransAnimFunc?.SubFuncs?.Select(k => new {
                        ease = k.Ease.ToString(), k.Reverse, milliseconds = k.Duration.TotalMilliseconds }),
                    rotation = constraint.RotAnimFunc?.SubFuncs?.Select(k => new {
                        ease = k.Ease.ToString(), k.Reverse, milliseconds = k.Duration.TotalMilliseconds }) });
            }
            break;
    }
}
