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

Check("synthetic two-object/two-constraint item builds from scratch and reparses", () =>
{
    foreach (var interleaved in new[] { false, true })
    {
        var item = ExperimentItem(interleaved);
        using var bytes = new MemoryStream();
        new Gbx<CGameItemModel>(item) { BodyCompression = GbxCompression.Uncompressed }.Save(bytes);
        bytes.Position = 0;
        var reopened = Gbx.Parse<CGameItemModel>(bytes, new GbxReadSettings { SafeSkippableChunks = true }).Node;
        VerifyExperiment(reopened, interleaved);
    }
});

Check("binding resolver classifies world->A and A->B constraints as supported flat bindings", () =>
{
    foreach (var interleaved in new[] { false, true })
    {
        var prefab = (CPlugPrefab)ExperimentItem(interleaved).EntityModel!;
        var (aIx, bIx) = interleaved ? (0, 2) : (0, 1);
        var world = (KC)prefab.Ents[interleaved ? 1 : 2].Model!;
        var chained = (KC)prefab.Ents[3].Model!;
        var worldParams = (NPlugDyna_SPrefabConstraintParams)prefab.Ents[interleaved ? 1 : 2].Params!;
        var chainedParams = (NPlugDyna_SPrefabConstraintParams)prefab.Ents[3].Params!;
        var root = ItemMotionBindings.RootPath(0, null);
        var worldBinding = ItemMotionBindings.Resolve(world, prefab, worldParams, root);
        Require(worldBinding.Status == ItemMotionStatus.Supported, worldBinding.Reason ?? "world constraint failed");
        Require(worldBinding.Parent.IsWorld && worldBinding.Parent.RawSlot == -1 && worldBinding.Child.RawSlot == 0
            && worldBinding.Child.OriginalArrayIndex == aIx, "world constraint targets wrong slot");
        var chainedBinding = ItemMotionBindings.Resolve(chained, prefab, chainedParams, root);
        Require(chainedBinding.Status == ItemMotionStatus.Supported, chainedBinding.Reason ?? "chained constraint failed");
        Require(!chainedBinding.Parent.IsWorld && chainedBinding.Parent.RawSlot == 0
            && chainedBinding.Parent.OriginalArrayIndex == aIx && chainedBinding.Parent.Path == $"{root}/ent:{aIx}",
            "chained parent is not object A");
        Require(chainedBinding.Child.RawSlot == 1 && chainedBinding.Child.OriginalArrayIndex == bIx
            && chainedBinding.Child.Path == $"{root}/ent:{bIx}", "chained child is not object B");
        Require(chainedBinding.Slots.Count == 2 && chainedBinding.Slots[0].OriginalArrayIndex == aIx
            && chainedBinding.Slots[1].OriginalArrayIndex == bIx, "filtered slot table order changed with layout");
    }
});

Check("experiment graph keeps every binding-resolver guard active", () =>
{
    var prefab = (CPlugPrefab)ExperimentItem(false).EntityModel!;
    var chained = (KC)prefab.Ents[3].Model!;
    var parameters = (NPlugDyna_SPrefabConstraintParams)prefab.Ents[3].Params!;
    var root = ItemMotionBindings.RootPath(0, null);
    Require(ItemMotionBindings.Resolve(chained, prefab, parameters, $"{root}/ent:0").Status == ItemMotionStatus.Unsupported,
        "nested occurrence guard inactive");
    Require(ItemMotionBindings.Resolve(chained, prefab, parameters, "owned-edge", isNestedPrefabOccurrence: true).Status == ItemMotionStatus.Unsupported,
        "explicit nested guard inactive");
    parameters.Version = 1;
    Require(ItemMotionBindings.Resolve(chained, prefab, parameters, root).Status == ItemMotionStatus.Unsupported, "params version guard inactive");
    parameters.Version = 0; parameters.Pos1 = new(1, 0, 0);
    Require(ItemMotionBindings.Resolve(chained, prefab, parameters, root).Status == ItemMotionStatus.Unsupported, "anchor guard inactive");
    parameters.Pos1 = default; parameters.Ent1 = 1;
    Require(ItemMotionBindings.Resolve(chained, prefab, parameters, root).Status == ItemMotionStatus.Unsupported, "self-parent guard inactive");
    parameters.Ent1 = 0;
});

Check("chained constraint edits rebind and round-trip through save and reparse", () =>
{
    var item = ExperimentItem(false);
    var prefab = (CPlugPrefab)item.EntityModel!;
    var chained = (KC)prefab.Ents[3].Model!;
    var parameters = (NPlugDyna_SPrefabConstraintParams)prefab.Ents[3].Params!;
    Require(!ItemMotionBindings.ApplyTargets(chained, prefab, parameters, "root", 0, 9).Success
        && parameters.Ent1 == 0 && parameters.Ent2 == 1, "invalid rebind mutated targets");
    var fields = ItemMotion.Read(chained).Fields;
    Require(ItemMotion.Apply(chained, fields with { TranslationMax = 3,
        Translation = new(true, [new(KC.AnimEase.QuadInOut, false, 3000)]) }).Success, "chained edit refused");
    Require(ItemMotionBindings.ApplyTargets(chained, prefab, parameters, "root", -1, 1).Success, "decoupling rebind refused");
    using var bytes = new MemoryStream();
    new Gbx<CGameItemModel>(item) { BodyCompression = GbxCompression.Uncompressed }.Save(bytes);
    bytes.Position = 0;
    VerifyExperiment(Gbx.Parse<CGameItemModel>(bytes, new GbxReadSettings { SafeSkippableChunks = true }).Node, false,
        chainedTranslationMax: 3, chainedTranslationKeys: [(KC.AnimEase.QuadInOut, false, 3000)], chainedEnt1: -1);
});

Check("composed preview evaluates both constraints of the chain independently", () =>
{
    var prefab = (CPlugPrefab)ExperimentItem(false).EntityModel!;
    var world = (KC)prefab.Ents[2].Model!;
    var chained = (KC)prefab.Ents[3].Model!;
    // Both timelines total 3000 ms; at 0.75 s A is three quarters through its first
    // sweep and B is mid-key of its first: A at +3 m on X, B at +1 m on Z, B at -45 deg.
    var aLive = Value(ItemMotion.Evaluate(world, 0.75));
    var bSignal = Value(ItemMotion.Evaluate(chained, 0.75));
    Vector(aLive.Translation, new(3, 0, 0));
    Vector(bSignal.Translation, new(0, 0, 1));
    NearV(bSignal.AngleDegrees, -45); // -90 -> 90, a quarter through
    var childRest = Matrix4x4.CreateTranslation(2, 0, 0);   // B rests 2 m from A
    var parentRest = Matrix4x4.Identity;                     // A rests at the prefab origin
    var parentLive = Matrix4x4.CreateTranslation(aLive.Translation);
    var composed = Value(ItemMotionTransforms.ComposeVisual(childRest, parentRest, parentLive, bSignal.Signal));
    Vector(Vector3.Transform(Vector3.Zero, composed), new(5, 0, 1));
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
static void Vector(Vector3 actual, Vector3 expected) { NearV(actual.X, expected.X); NearV(actual.Y, expected.Y); NearV(actual.Z, expected.Z); }
static void NearV(double actual, double expected) => Require(Math.Abs(actual - expected) < .0001, $"Expected {expected}, got {actual}");
static T Value<T>(ItemMotionResult<T> result) { Require(result.Success, result.Reason ?? "operation failed"); return result.Value!; }

// Issue #22 minimal experiment pair: object A constrained world->A, object B constrained A->B,
// distinct axes/ranges, synchronized 3000 ms timelines. interleaved reproduces the documented
// DTC_Firework200 alternating dyna-object/constraint entity layout.
static CGameItemModel ExperimentItem(bool interleaved)
{
    CPlugPrefab.EntRef Body(int offset) => new()
    {
        Position = new Vec3(offset, 0, 0),
        Rotation = new Quat(0, 0, 0, 1),
        Params = new NPlugDynaObjectModel_SInstanceParams { Version = 2, PeriodSc = 1, PeriodScMax = -1, Phase01 = -1, Phase01Max = -1, IsKinematic = true },
        Model = new CPlugDynaObjectModel { Version = 13, IsStatic = false, Mass = 10, BreakSpeedKmh = 100, Mesh = Solid(),
            StaticShape = new CPlugSurface(), DynaShape = new CPlugSurface() }
    };
    var a = Body(0);
    var b = Body(2);
    var world = ConstraintEntry(-1, 0, KC.EAxis.X, 0, 4, 0, 0,
        [(KC.AnimEase.Linear, false, 1000), (KC.AnimEase.Linear, false, 1000), (KC.AnimEase.Linear, false, 1000)],
        (KC.AnimEase.Linear, false, 3000));
    var chained = ConstraintEntry(0, 1, KC.EAxis.Z, 0, 2, -90, 90,
        [(KC.AnimEase.Linear, false, 1500), (KC.AnimEase.Linear, false, 1500)],
        (KC.AnimEase.Linear, false, 3000));
    var prefab = new CPlugPrefab { Version = 11, Ents = interleaved
        ? [a, world, b, chained]
        : [a, b, world, chained] };
    var item = new CGameItemModel
    {
        Ident = new Ident("KinematicExperiment", 26, "KinematicReference"),
        ItemType = CGameItemModel.EItemType.PickUp,
        EntityModel = prefab
    };
    item.CreateChunk<CGameCtnCollector.HeaderChunk2E001003>().Version = 8;
    item.CreateChunk<CGameItemModel.HeaderChunk2E002000>();
    item.CreateChunk<CGameItemModel.Chunk2E002015>();
    item.ItemTypeE = CGameItemModel.EItemType.PickUp;
    item.CreateChunk<CGameItemModel.Chunk2E002019>().Version = 15;
    return item;
}

static CPlugPrefab.EntRef ConstraintEntry(int ent1, int ent2, KC.EAxis axis, float min, float max,
    float rotMin, float rotMax, (KC.AnimEase Ease, bool Reverse, int Milliseconds)[] keys,
    (KC.AnimEase Ease, bool Reverse, int Milliseconds) rotKey) => new()
{
    Rotation = new Quat(0, 0, 0, 1),
    Params = new NPlugDyna_SPrefabConstraintParams { Ent1 = ent1, Ent2 = ent2 },
    Model = new KC
    {
        SubVersion = 3,
        TransAxis = axis, TransMin = min, TransMax = max,
        TransAnimFunc = new() { IsDuration = true, SubFuncs = keys.Select(k => new KC.SubAnimFunc { Ease = k.Ease, Reverse = k.Reverse, Duration = new TimeInt32(k.Milliseconds) }).ToArray() },
        RotAxis = KC.EAxis.Y, AngleMinDeg = rotMin, AngleMaxDeg = rotMax,
        RotAnimFunc = new() { IsDuration = true, SubFuncs = [new KC.SubAnimFunc { Ease = rotKey.Ease, Reverse = rotKey.Reverse, Duration = new TimeInt32(rotKey.Milliseconds) }] }
    }
};

static void VerifyExperiment(CGameItemModel item, bool interleaved, float chainedTranslationMax = 2,
    (KC.AnimEase Ease, bool Reverse, int Milliseconds)[]? chainedTranslationKeys = null, int chainedEnt1 = 0)
{
    var (aIx, bIx, worldIx, chainedIx) = interleaved ? (0, 2, 1, 3) : (0, 1, 2, 3);
    var prefab = (CPlugPrefab)item.EntityModel!;
    Require(prefab.Version == 11 && prefab.Ents!.Length == 4, "experiment envelope changed");
    foreach (var (ix, offset) in new[] { (aIx, 0), (bIx, 2) })
    {
        var entry = prefab.Ents[ix];
        var instance = (NPlugDynaObjectModel_SInstanceParams)entry.Params!;
        Require(instance.Version == 2 && instance.IsKinematic && instance.PeriodSc == 1 && instance.PeriodScMax == -1,
            "experiment body lost its kinematic classification");
        var dyna = (CPlugDynaObjectModel)entry.Model!;
        Require(dyna.Version == 13 && !dyna.IsStatic, "experiment body class changed");
        Require(dyna.StaticShape is CPlugSurface && dyna.DynaShape is CPlugSurface, "experiment shape fields lost");
        var solid = (CPlugSolid2Model)dyna.Mesh!;
        var visual = (CPlugVisualIndexedTriangles)solid.Visuals![0];
        var positions = visual.VertexStreams![0].Positions;
        Require(positions is [var p0, var p1, var p2] && p0 == new Vec3() && p1 == new Vec3(1, 0, 0) && p2 == new Vec3(0, 1, 0),
            "experiment body lost its authored triangle");
        Require(entry.Position.X == offset, "experiment rest transform lost");
    }
    var world = (NPlugDyna_SPrefabConstraintParams)prefab.Ents[worldIx].Params!;
    Require(world.Ent1 == -1 && world.Ent2 == 0 && world.Version == 0, "world constraint binding changed");
    var worldModel = (KC)prefab.Ents[worldIx].Model!;
    Require(worldModel.TransAxis == KC.EAxis.X && worldModel.TransMin == 0 && worldModel.TransMax == 4
        && worldModel.TransAnimFunc!.IsDuration && worldModel.TransAnimFunc.SubFuncs!.Length == 3
        && worldModel.TransAnimFunc.SubFuncs.All(k => k.Ease == KC.AnimEase.Linear && !k.Reverse && k.Duration.TotalMilliseconds == 1000),
        "world constraint channel changed");
    var chained = (NPlugDyna_SPrefabConstraintParams)prefab.Ents[chainedIx].Params!;
    Require(chained.Ent1 == chainedEnt1 && chained.Ent2 == 1 && chained.Version == 0
        && chained.Pos1 == default && chained.Pos2 == default, "chained constraint binding changed");
    var chainedModel = (KC)prefab.Ents[chainedIx].Model!;
    Require(chainedModel.TransAxis == KC.EAxis.Z && chainedModel.TransMin == 0 && Math.Abs(chainedModel.TransMax - chainedTranslationMax) < .0001,
        "chained translation channel changed");
    var expectedKeys = chainedTranslationKeys ?? [(KC.AnimEase.Linear, false, 1500), (KC.AnimEase.Linear, false, 1500)];
    var timeline = chainedModel.TransAnimFunc!;
    var subFuncs = timeline.SubFuncs!;
    Require(timeline.IsDuration && subFuncs.Length == expectedKeys.Length, "chained timeline count changed");
    for (int i = 0; i < expectedKeys.Length; i++)
        Require(subFuncs[i].Ease == expectedKeys[i].Item1 && subFuncs[i].Reverse == expectedKeys[i].Item2
            && subFuncs[i].Duration.TotalMilliseconds == expectedKeys[i].Item3, $"chained key {i} changed");
    Require(chainedModel.RotAxis == KC.EAxis.Y && chainedModel.AngleMinDeg == -90 && chainedModel.AngleMaxDeg == 90, "chained rotation channel changed");
    // Both constraints keep synchronized 3000 ms total timelines in every variant.
    Require(worldModel.TransAnimFunc!.SubFuncs!.Sum(k => k.Duration.TotalMilliseconds) == 3000
        && subFuncs.Sum(k => k.Duration.TotalMilliseconds) == 3000, "synchronized durations drifted");
}

// Mirrors Tests/Browser/FixtureGenerator: the bundled serializer exposes decoded arrays, but
// not its declarations or count setters. Reflection is restricted to this synthetic generator.
static CPlugSolid2Model Solid()
{
    const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
    var stream = new CPlugVertexStream { Positions = [new(), new(1, 0, 0), new(0, 1, 0)] };
    var declaration = new CPlugVertexStream.DataDecl();
    typeof(CPlugVertexStream.DataDecl).GetField("flags1", flags)!.SetValue(declaration,
        (uint)CPlugVertexStream.EPlugVDcl.Position | ((uint)CPlugVertexStream.EPlugVDclType.Float3 << 9) | (12u << 18));
    typeof(CPlugVertexStream).GetField("dataDecls", flags)!.SetValue(stream, new[] { declaration });
    typeof(CPlugVertexStream).GetField("count", flags)!.SetValue(stream, 3);
    stream.CreateChunk<CPlugVertexStream.Chunk09056000>().Version = 1;
    var indexBuffer = new CPlugIndexBuffer { Indices = [0, 1, 2] };
    indexBuffer.CreateChunk<CPlugIndexBuffer.Chunk09057000>();
    var visual = new CPlugVisualIndexedTriangles
    {
        VertexStreams = [stream], IndexBuffer = indexBuffer,
        IsGeometryStatic = true, IsIndexationStatic = true,
        BoundingBox = new BoxAligned(0, 0, 0, 1, 1, 0)
    };
    typeof(CPlugVisual).GetProperty("Count", flags)!.SetValue(visual, 3);
    visual.CreateChunk<CPlugVisual.Chunk0900600F>().Version = 6;
    visual.CreateChunk<CPlugVisualIndexed.Chunk0906A001>();
    var solid = new CPlugSolid2Model { Visuals = [visual], CustomMaterials = [], ShadedGeoms = [] };
    solid.CreateChunk<CPlugSolid2Model.Chunk090BB000>().Version = 34;
    return solid;
}

static void VerifyKinematic(CGameItemModel item, float translationMax = 1,
    (KC.AnimEase Ease, bool Reverse, int Milliseconds)[]? translationKeys = null)
{
    Require(item.ItemType == CGameItemModel.EItemType.Ornament && item.ItemTypeE == CGameItemModel.EItemType.Ornament,
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
