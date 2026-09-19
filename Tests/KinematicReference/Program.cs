// Kinematic reference analysis against the tracked exported pair. Dump mode prints the full
// kinematic structure of item archives; the default mode runs the regression checks below.
// Expectations are derived from the reference dump (docs/kinematic-conversion-analysis.md),
// not from the evaluator under test.
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.MwFoundations;
using GBX.NET.Engines.Plug;
using GBX.NET.LZO;
using TM_Item_Studio.Models;
using TmEssentials;
using KC = GBX.NET.Engines.Meta.NPlugDyna_SKinematicConstraint;

Gbx.LZO = new MiniLZO();
var repoRoot = FindRepoRoot();
if (args.Length > 0 && args[0] == "--dump")
{
    foreach (var path in args.Skip(1).Select(a => Path.IsPathRooted(a) ? a : Path.Combine(repoRoot, a)))
        DumpItem(path);
    return 0;
}

int passed = 0, failed = 0;
Console.WriteLine("Bundled parser SHA256: " + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Gbx).Assembly.Location))).ToLowerInvariant());
var kinematicPath = Path.Combine(repoRoot, "Test Exported items/CustomItem_Kinematic.Item.Gbx");
var staticPath = Path.Combine(repoRoot, "Test Exported items/CustomItem_Static.Item.Gbx");

Check("tracked fixture identity pins the analyzed archives", () =>
{
    Require(Sha256(kinematicPath) == "a51f35b4b589cf39fd2fd3fdf0f9152c1b01b26091b3c76c467d67aad84796f4", "kinematic fixture bytes changed");
    Require(Sha256(staticPath) == "afde25629cd7030844b1cbf2f1c400bed1280205f7c1e167b6d89048ddf4939c", "static fixture bytes changed");
    var kinematic = ParseItem(kinematicPath);
    var @static = ParseItem(staticPath);
    Require(kinematic.Ident.Author != @static.Ident.Author, "pair provenance assumption changed: same author");
});

Check("kinematic archive parses into the full mapped graph", () => VerifyKinematic(ParseItem(kinematicPath)));

Check("static archive parses into the common-item graph", () => VerifyStatic(ParseItem(staticPath)));

Check("parse, save and reparse preserve both tracked archives structurally", () =>
{
    foreach (var path in new[] { kinematicPath, staticPath })
    {
        var original = ParseItem(path);
        var first = Save(original);
        var reparsed = Reparse(first);
        if (path == kinematicPath) VerifyKinematic(reparsed); else VerifyStatic(reparsed);
        var second = Save(reparsed);
        Require(first.SequenceEqual(second), "second save of " + Path.GetFileName(path) + " is not byte-stable");
    }
});

Check("constraint and timeline fields round-trip through the typed adapter", () =>
{
    var item = ParseItem(kinematicPath);
    var constraint = KinematicConstraint(item);
    var fields = ItemMotion.Read(constraint).Fields;
    Require(fields.TranslationAxis == KC.EAxis.Y && fields.TranslationMin == 0 && fields.TranslationMax == 1, "translation scalars lost");
    Require(fields.RotationAxis == KC.EAxis.Y && fields.AngleMinDegrees == 0 && fields.AngleMaxDegrees == 0, "rotation scalars lost");
    Require(fields.Translation!.IsDuration, "duration flag lost");
    Require(fields.Translation.Keys.Count == 2
        && fields.Translation.Keys[0].Ease == KC.AnimEase.QuadInOut && !fields.Translation.Keys[0].Reverse && fields.Translation.Keys[0].DurationMilliseconds == 10000
        && fields.Translation.Keys[1].Ease == KC.AnimEase.QuadInOut && fields.Translation.Keys[1].Reverse && fields.Translation.Keys[1].DurationMilliseconds == 10000,
        "translation keys lost");
    Require(fields.Rotation!.Keys.Count == 1 && fields.Rotation.Keys[0].Ease == KC.AnimEase.Linear
        && fields.Rotation.Keys[0].DurationMilliseconds == 10000 && !fields.Rotation.Keys[0].Reverse, "rotation keys lost");
    var before = Save(item);
    Require(ItemMotion.Apply(constraint, fields).Success, "unchanged edit refused");
    Require(Save(item).SequenceEqual(before), "unchanged edit rewrote the archive");
    var edited = fields with { TranslationMax = 2, Translation = new(true, [new(KC.AnimEase.QuadOut, true, 2500)]) };
    Require(ItemMotion.Apply(constraint, edited).Success, "scalar/timeline edit refused");
    VerifyKinematic(Reparse(Save(item)), translationMax: 2, translationKeys: [(KC.AnimEase.QuadOut, true, 2500)]);
});

Check("binding resolver classifies the tracked constraint as world-relative and supported", () =>
{
    var item = ParseItem(kinematicPath);
    var prefab = (CPlugPrefab)item.EntityModel!;
    var constraint = KinematicConstraint(item);
    var parameters = (NPlugDyna_SPrefabConstraintParams)prefab.Ents[1].Params!;
    var binding = ItemMotionBindings.Resolve(constraint, prefab, parameters, ItemMotionBindings.RootPath(0, null));
    Require(binding.Status == ItemMotionStatus.Supported, binding.Reason ?? "world binding failed");
    Require(binding.Parent.IsWorld && binding.Parent.RawSlot == -1 && binding.Parent.OriginalArrayIndex is null
        && binding.Parent.Path == "doc:0/variant:none/root", "world parent misclassified");
    Require(binding.Child.RawSlot == 0 && binding.Child.OriginalArrayIndex == 0
        && binding.Child.Path == "doc:0/variant:none/root/ent:0", "child misclassified");
    Require(binding.Slots.Count == 1 && binding.Slots[0].OriginalArrayIndex == 0, "constraint entity leaked into the slot table");
});

Check("typed segment schema exposes no per-key waypoints", () =>
{
    var keyParameters = typeof(ItemMotionKey).GetConstructors().Single().GetParameters();
    Require(keyParameters.Select(p => p.Name).SequenceEqual(new[] { "Ease", "Reverse", "DurationMilliseconds" }),
        "timeline keys gained positional data");
    var editProperties = typeof(ItemMotionEdit).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToArray();
    Require(editProperties.SequenceEqual(new[] { "TranslationAxis", "TranslationMin", "TranslationMax",
        "RotationAxis", "AngleMinDegrees", "AngleMaxDegrees", "Translation", "Rotation" }), "channel fields changed shape");
    var constraint = KinematicConstraint(ParseItem(kinematicPath));
    var timeline = constraint.TransAnimFunc!;
    Require(timeline.SubFuncs!.Length == 2 && constraint.TransMin == 0 && constraint.TransMax == 1
        && timeline.SubFuncs.All(k => k.Duration.TotalMilliseconds is 10000), "keys do not share one scalar channel range");
});

Check("exported kinematic template carries explicit collision surface meshes", () =>
{
    var body = (CPlugDynaObjectModel)((CPlugPrefab)ParseItem(kinematicPath).EntityModel!).Ents[0].Model!;
    Require(body.StaticShape is CPlugSurface && body.DynaShape is CPlugSurface, "shape classes changed");
    Require(!ReferenceEquals(body.StaticShape, body.DynaShape), "shapes collapsed into one shared node");
    foreach (var shape in new[] { body.StaticShape, body.DynaShape })
    {
        var surface = (CPlugSurface)shape!;
        Require(surface.Surf is CPlugSurface.Mesh && surface.Geom is null && surface.Materials.Length == 0,
            "exported collision surfaces changed");
    }
});

Console.WriteLine($"{passed} passed, {failed} failed.");
return failed == 0 ? 0 : 1;

void Check(string name, Action action) { try { action(); passed++; Console.WriteLine("PASS: " + name); } catch (Exception e) { failed++; Console.Error.WriteLine("FAIL: " + name + ": " + e); } }
static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TM-Item-Studio.csproj"))) dir = dir.Parent;
    return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
}
static string Sha256(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
static CGameItemModel ParseItem(string path) => Gbx.Parse<CGameItemModel>(
    new MemoryStream(File.ReadAllBytes(path)), new GbxReadSettings { SafeSkippableChunks = true }).Node;
static byte[] Save(CGameItemModel node)
{
    using var stream = new MemoryStream();
    new Gbx<CGameItemModel>(node).Save(stream);
    return stream.ToArray();
}
static CGameItemModel Reparse(byte[] bytes) => Gbx.Parse<CGameItemModel>(
    new MemoryStream(bytes), new GbxReadSettings { SafeSkippableChunks = true }).Node;
static KC KinematicConstraint(CGameItemModel item) => (KC)((CPlugPrefab)item.EntityModel!).Ents[1].Model!;

static void VerifyKinematic(CGameItemModel item, float translationMax = 1,
    (KC.AnimEase Ease, bool Reverse, int Milliseconds)[]? translationKeys = null)
{
    Require(item is not null && item.ItemType == CGameItemModel.EItemType.Ornament && item.ItemTypeE == CGameItemModel.EItemType.Ornament,
        "item type changed");
    Require(item.DefaultPlacement is not null, "default placement lost");
    var prefab = (CPlugPrefab)(item.EntityModel ?? throw new Exception("kinematic archive lost its prefab entity model"));
    Require(prefab.Version == 11 && prefab.Ents!.Length == 2, "prefab envelope changed");
    var body = prefab.Ents[0];
    Require(body.ModelFile is null && body.Model is CPlugDynaObjectModel, "body entry changed");
    var instance = (NPlugDynaObjectModel_SInstanceParams)body.Params!;
    Require(instance.Version == 2 && instance.PeriodSc == 1 && instance.PeriodScMax == -1 && instance.Phase01 == -1
        && instance.Phase01Max == -1 && instance.TextureId == 0 && instance.IsKinematic && instance.CastStaticShadow,
        "instance params changed");
    var dyna = (CPlugDynaObjectModel)body.Model!;
    Require(dyna.Version == 13 && !dyna.IsStatic && !dyna.DynamizeOnSpawn && dyna.Mass == 100 && dyna.BreakSpeedKmh == 100,
        "dyna object fields changed");
    Require(dyna.Mesh is CPlugSolid2Model && dyna.StaticShape is CPlugSurface && dyna.DynaShape is CPlugSurface && dyna.LocAnim is null,
        "dyna subgraphs changed");
    var constraintEntry = prefab.Ents[1];
    var constraint = (KC)constraintEntry.Model!;
    Require(constraint.Version == 0 && constraint.SubVersion == 3, "constraint versions changed");
    Require(constraint.TransAxis == KC.EAxis.Y && constraint.TransMin == 0 && Near0(constraint.TransMax - translationMax), "translation channel changed");
    Require(constraint.RotAxis == KC.EAxis.Y && constraint.AngleMinDeg == 0 && constraint.AngleMaxDeg == 0, "rotation channel changed");
    Require(constraint.ShaderTcType == KC.EShaderTcType.None && constraint.ShaderTcAnimFunc is null or { Length: 0 }, "shader channel changed");
    var timeline = constraint.TransAnimFunc!;
    Require(timeline.IsDuration, "duration semantics changed");
    var expected = translationKeys ?? new[] { (KC.AnimEase.QuadInOut, false, 10000), (KC.AnimEase.QuadInOut, true, 10000) };
    Require(timeline.SubFuncs!.Length == expected.Length, "translation key count changed");
    for (int i = 0; i < expected.Length; i++)
        Require(timeline.SubFuncs[i].Ease == expected[i].Item1 && timeline.SubFuncs[i].Reverse == expected[i].Item2
            && timeline.SubFuncs[i].Duration.TotalMilliseconds == expected[i].Item3, $"translation key {i} changed");
    var rotation = constraint.RotAnimFunc!;
    Require(rotation.IsDuration && rotation.SubFuncs!.Length == 1 && rotation.SubFuncs[0].Ease == KC.AnimEase.Linear
        && !rotation.SubFuncs[0].Reverse && rotation.SubFuncs[0].Duration.TotalMilliseconds == 10000, "rotation timeline changed");
    var parameters = (NPlugDyna_SPrefabConstraintParams)constraintEntry.Params!;
    Require(parameters.Version == 0 && parameters.Ent1 == -1 && parameters.Ent2 == 0
        && parameters.Pos1 == default && parameters.Pos2 == default, "constraint binding changed");
    static bool Near0(double d) => Math.Abs(d) < .0001;
}

static void VerifyStatic(CGameItemModel item)
{
    Require(item.ItemType == CGameItemModel.EItemType.Ornament && item.ItemTypeE == CGameItemModel.EItemType.Ornament, "item type changed");
    Require(item.DefaultPlacement is not null, "default placement lost");
    var entity = (CGameCommonItemEntityModel)item.EntityModel!;
    Require(entity.VisModel is null && entity.PhyModel is null && entity.TriggerShape is null, "unexpected extra entity models");
    var body = (CPlugStaticObjectModel)entity.StaticObject!;
    Require(body.Version == 3 && body.Mesh is CPlugSolid2Model && body.Shape is null && body.IsMeshCollidable, "static body changed");
    Require(item.EntityModel is not CPlugPrefab, "static archive gained a prefab");
}

static void DumpItem(string path)
{
    var bytes = File.ReadAllBytes(path);
    Console.WriteLine($"# {Path.GetFileName(path)}");
    Console.WriteLine($"  sha256={Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()} bytes={bytes.Length}");
    using var decompressed = new MemoryStream();
    Gbx.Decompress(path, decompressed);
    Console.WriteLine($"  decompressed-bytes={decompressed.Length}");
    var file = Gbx.Parse<CGameItemModel>(new MemoryStream(bytes), new GbxReadSettings { SafeSkippableChunks = true });
    Console.WriteLine($"  root={file.Node.GetType().Name} ident={file.Node.Ident} itemtype={file.Node.ItemType} itemtypee={file.Node.ItemTypeE}");
    Console.WriteLine($"  defaultplacement={(file.Node.DefaultPlacement is null ? "null" : "present")}");
    var seen = new HashSet<CMwNod>();
    DumpNode(file.Node.EntityModel, "  entity-model", seen, 0);
    Console.WriteLine();
}

static void DumpNode(CMwNod? node, string label, HashSet<CMwNod> seen, int depth)
{
    var indent = new string(' ', 2 + depth * 2);
    if (node is null) { Console.WriteLine($"{indent}{label}: null"); return; }
    if (!seen.Add(node)) { Console.WriteLine($"{indent}{label}: {TypeName(node)} <shared-ref-already-dumped>"); return; }
    switch (node)
    {
        case CPlugPrefab prefab:
            Console.WriteLine($"{indent}{label}: CPlugPrefab version={prefab.Version} url={prefab.Url ?? "null"} u01={prefab.U01} u02={prefab.U02} ents={prefab.Ents?.Length ?? -1}");
            for (int i = 0; i < (prefab.Ents?.Length ?? 0); i++)
            {
                var e = prefab.Ents![i];
                Console.WriteLine($"{indent}  ent[{i}]: model={TypeName(e.Model)} external={(e.ModelFile is null ? "null" : e.ModelFile.FilePath)} pos={e.Position} rot={e.Rotation} u01=\"{e.U01 ?? ""}\"");
                DumpParams(e.Params, indent + "    ", seen);
                DumpNode(e.Model, "model", seen, depth + 2);
            }
            return;
        case CGameCommonItemEntityModel common:
            Console.WriteLine($"{indent}{label}: CGameCommonItemEntityModel");
            Console.WriteLine($"{indent}  staticobject={TypeName(common.StaticObject)} vismodel={TypeName(common.VisModel)} phymodel={TypeName(common.PhyModel)} triggertype={TypeName(common.TriggerShape)}");
            DumpNode(common.StaticObject, "staticObject", seen, depth + 1);
            DumpNode(common.VisModel, "visModel", seen, depth + 1);
            DumpNode(common.PhyModel, "phyModel", seen, depth + 1);
            return;
        case KC constraint:
            Console.WriteLine($"{indent}{label}: NPlugDyna_SKinematicConstraint version={constraint.Version} subversion={constraint.SubVersion}");
            Console.WriteLine($"{indent}  trans: axis={constraint.TransAxis} min={constraint.TransMin} max={constraint.TransMax}");
            DumpTimeline("trans", constraint.TransAnimFunc, indent + "  ");
            Console.WriteLine($"{indent}  rot: axis={constraint.RotAxis} minDeg={constraint.AngleMinDeg} maxDeg={constraint.AngleMaxDeg}");
            DumpTimeline("rot", constraint.RotAnimFunc, indent + "  ");
            Console.WriteLine($"{indent}  shadertc: type={constraint.ShaderTcType} version={constraint.ShaderTcVersion} keys={constraint.ShaderTcAnimFunc?.Length.ToString() ?? "null"}");
            return;
        case CPlugDynaObjectModel dyna:
            Console.WriteLine($"{indent}{label}: CPlugDynaObjectModel version={dyna.Version} isstatic={dyna.IsStatic} dynamizeonspawn={dyna.DynamizeOnSpawn} mass={dyna.Mass} breakspeedkmh={dyna.BreakSpeedKmh}");
            Console.WriteLine($"{indent}  mesh={TypeName(dyna.Mesh)} staticshape={TypeName(dyna.StaticShape)} dynashape={TypeName(dyna.DynaShape)} locanim={TypeName(dyna.LocAnim)}");
            DumpNode(dyna.Mesh, "mesh", seen, depth + 2);
            DumpNode(dyna.StaticShape, "staticShape", seen, depth + 2);
            DumpNode(dyna.DynaShape, "dynaShape", seen, depth + 2);
            return;
        case CPlugSurface surface:
            Console.WriteLine($"{indent}{label}: CPlugSurface surf={surface.Surf?.GetType().Name ?? "null"} geom={TypeName(surface.Geom)} materials={surface.Materials?.Length.ToString() ?? "null"}");
            return;
        case CPlugStaticObjectModel staticModel:
            Console.WriteLine($"{indent}{label}: CPlugStaticObjectModel version={staticModel.Version} mesh={TypeName(staticModel.Mesh)} shape={TypeName(staticModel.Shape)} meshcollidable={staticModel.IsMeshCollidable}");
            return;
        default:
            Console.WriteLine($"{indent}{label}: {TypeName(node)}");
            return;
    }
}

static void DumpParams(object? parameters, string indent, HashSet<CMwNod> seen)
{
    switch (parameters)
    {
        case null: Console.WriteLine($"{indent}params: null"); return;
        case NPlugDyna_SPrefabConstraintParams constraint:
            Console.WriteLine($"{indent}params: NPlugDyna_SPrefabConstraintParams version={constraint.Version} ent1={constraint.Ent1} ent2={constraint.Ent2} pos1={constraint.Pos1} pos2={constraint.Pos2}"); return;
        case NPlugDynaObjectModel_SInstanceParams instance:
            Console.WriteLine($"{indent}params: NPlugDynaObjectModel_SInstanceParams version={instance.Version} periodsc={instance.PeriodSc} periodscmax={instance.PeriodScMax} phase01={instance.Phase01} phase01max={instance.Phase01Max} textureid={instance.TextureId} iskinematic={instance.IsKinematic} caststaticshadow={instance.CastStaticShadow}"); return;
        default:
            Console.WriteLine($"{indent}params: {parameters.GetType().Name}"); return;
    }
}

static void DumpTimeline(string name, KC.AnimFunc? timeline, string indent)
{
    if (timeline is null) { Console.WriteLine($"{indent}{name}animfunc: null"); return; }
    Console.WriteLine($"{indent}{name}animfunc: isduration={timeline.IsDuration} keys={timeline.SubFuncs?.Length.ToString() ?? "null"}");
    for (int i = 0; i < (timeline.SubFuncs?.Length ?? 0); i++)
    {
        var k = timeline.SubFuncs![i];
        Console.WriteLine($"{indent}  key[{i}]: ease={k.Ease} reverse={k.Reverse} duration={k.Duration.TotalMilliseconds}ms");
    }
}

static string TypeName(CMwNod? node) => node?.GetType().Name ?? "null";
