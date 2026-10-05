using System.Numerics;
using System.Security.Cryptography;
using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;
using GBX.NET.Serialization;
using TM_Item_Studio.Models;
using TmEssentials;
using KC = GBX.NET.Engines.Meta.NPlugDyna_SKinematicConstraint;

// Synthetic authored data only. Expected numbers below are hand-calculated, not produced by the evaluator.
int passed = 0, failed = 0;
Gbx.LZO ??= new GBX.NET.LZO.MiniLZO();
Console.WriteLine("Bundled parser SHA256: " + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Gbx).Assembly.Location))).ToLowerInvariant());

Check("static-to-kinematic conversion requires a valid kinematic template body", () =>
{
    var staticItem = new CGameItemModel
    {
        Ident = new Ident("ConvertedMotion", 26, "FixtureGenerator"),
        Name = "Converted Motion",
        ItemType = CGameItemModel.EItemType.Ornament,
        ItemTypeE = CGameItemModel.EItemType.Ornament,
        EntityModel = new CPlugStaticObjectModel { Mesh = new CPlugSolid2Model() }
    };
    var result = ItemKinematicEntityTemplate.ConvertStaticToKinematic(staticItem, movingTemplate: null);
    Require(!result.Success && result.Status == ItemMotionStatus.Unsupported, "conversion without template should be rejected");

    var template = KinematicTemplate();
    var explicitConstraint = new KC
    {
        Version = 0,
        SubVersion = 3,
        TransAxis = KC.EAxis.Z,
        TransMin = -2,
        TransMax = 5,
        RotAxis = KC.EAxis.X,
        AngleMinDeg = -45,
        AngleMaxDeg = 45,
        TransAnimFunc = Timeline(Key(KC.AnimEase.Constant, 500)),
        RotAnimFunc = Timeline(Key(KC.AnimEase.QuadInOut, 1200))
    };
    result = ItemKinematicEntityTemplate.ConvertStaticToKinematic(staticItem, template, explicitConstraint);
    Require(result.Success, result.Reason ?? "conversion failed");
    var prefab = (CPlugPrefab)result.Value!.EntityModel!;
    Require(prefab.Ents!.Length == 2, "conversion did not build a two-entry prefab");
    var body = (CPlugDynaObjectModel)prefab.Ents[0].Model!;
    Require(!body.IsStatic && body.Mesh is not null && body.StaticShape is CPlugSurface && body.DynaShape is CPlugSurface,
        "dyna body did not reuse the static mesh and surface nodes");
    Require(((CPlugSurface)body.StaticShape).Surf is not null && ((CPlugSurface)body.DynaShape).Surf is not null,
        "converted body lost required collision surface payload");
    var instance = (NPlugDynaObjectModel_SInstanceParams)prefab.Ents[0].Params!;
    Require(instance.Version == 2 && instance.IsKinematic, "instance params did not set kinematic mode");
    Require(instance.PeriodSc == 1 && instance.PeriodScMax == -1 && instance.Phase01 == -1 && instance.Phase01Max == -1,
        "template timing sentinels were not preserved");
    var constraint = (NPlugDyna_SKinematicConstraint)prefab.Ents[1].Model!;
    Require(constraint.SubVersion == 3 && constraint.TransAxis == NPlugDyna_SKinematicConstraint.EAxis.Z
        && constraint.RotAxis == NPlugDyna_SKinematicConstraint.EAxis.X
        && constraint.TransMin == -2 && constraint.TransMax == 5
        && constraint.AngleMinDeg == -45 && constraint.AngleMaxDeg == 45,
        "explicit constraint template was not applied");
    var binding = ItemMotionBindings.Resolve(constraint, prefab, (NPlugDyna_SPrefabConstraintParams)prefab.Ents[1].Params!, "doc:0/variant:none/root");
    Require(binding.Status == ItemMotionStatus.Supported, binding.Reason ?? "binding failed");
});

Check("segment count edits retain existing keys and timing mode, append in the right units, and reject atomically", () =>
{
    foreach (var mode in new[] { true, false })
    {
        var key = Key(KC.AnimEase.QuadOut, 2000, true);
        var timeline = new KC.AnimFunc { IsDuration = mode, SubFuncs = new[] { key } };
        Require(ItemMotion.ResizeTimeline(timeline, 3).Success, "Count increase rejected.");
        Require(ReferenceEquals(timeline.SubFuncs[0], key) && timeline.IsDuration == mode, "Existing key or flag replaced.");
        Require(timeline.SubFuncs[1].Duration.TotalMilliseconds == (mode ? 1000 : 3000)
            && timeline.SubFuncs[2].Duration.TotalMilliseconds == (mode ? 1000 : 4000), "New segment uses wrong timing representation.");
        Require(timeline.SubFuncs.Skip(1).All(k => k.Ease == KC.AnimEase.Linear && !k.Reverse), "New defaults differ.");
        Require(ItemMotion.ResizeTimeline(timeline, 1).Success && timeline.SubFuncs.Length == 1
            && ReferenceEquals(timeline.SubFuncs[0], key), "Count reduction rewrote retained key.");
        timeline.SubFuncs[0].Duration = new TimeInt32(int.MaxValue);
        var before = timeline.SubFuncs;
        Require(!ItemMotion.ResizeTimeline(timeline, 2).Success && ReferenceEquals(timeline.SubFuncs, before), "Overflow partially mutated timeline.");
        Require(!ItemMotion.ResizeTimeline(timeline, 5).Success && ReferenceEquals(timeline.SubFuncs, before), "Count overflow mutated timeline.");
    }
    Require(!ItemMotion.ResizeTimeline(null, 1).Success, "Absent timeline created implicitly.");
    var empty = new KC.AnimFunc { IsDuration = false, SubFuncs = Array.Empty<KC.SubAnimFunc>() };
    Require(ItemMotion.ResizeTimeline(empty, 1).Success && empty.SubFuncs[0].Duration.TotalMilliseconds == 1000, "Explicit creation from empty timeline failed.");
});

Check("duration and endpoint archives sample equally without rewriting authored data", () =>
{
    foreach (var isDuration in new[] { true, false })
    {
        var model = Model(); model.TransMin = 0; model.TransMax = 10;
        model.TransAnimFunc = new() { IsDuration = isDuration, SubFuncs = new[] {
            Key(KC.AnimEase.Linear, 1000), Key(KC.AnimEase.Linear, isDuration ? 1000 : 2000, true) } };
        var before = Bytes(model);
        foreach (var (seconds, expected) in new[] { (0d, 0d), (.5, 5d), (1d, 10d), (1.5, 5d), (2d, 0d) })
            Near(Value(ItemMotion.Evaluate(model, seconds)).TranslationMetres, expected);
        Require(Bytes(model).SequenceEqual(before), "Evaluation rewrote the archive representation");
    }
    var longer = Model(); longer.TransMin = 0; longer.TransMax = 10;
    longer.TransAnimFunc = new() { IsDuration = true, SubFuncs = new[] {
        Key(KC.AnimEase.Linear, 1000), Key(KC.AnimEase.Linear, 2000, true) } };
    Near(Value(ItemMotion.Evaluate(longer, 1.5)).TranslationMetres, 7.5);
});

Check("endpoint duplicates, decreases and normalized limits retain native segment boundaries", () =>
{
    foreach (var middle in new[] { 500, 1000 })
    {
        var model = Model(); model.TransMin = 0; model.TransMax = 10;
        model.TransAnimFunc = new() { IsDuration = false, SubFuncs = new[] {
            Key(KC.AnimEase.Linear, 1000), Key(KC.AnimEase.Constant, middle),
            Key(KC.AnimEase.Linear, middle + 1000, true) } };
        Near(Value(ItemMotion.Evaluate(model, 1)).TranslationMetres, 10);
        Near(Value(ItemMotion.Evaluate(model, 1.5)).TranslationMetres, 5);
        Near(Value(ItemMotion.Evaluate(model, 2)).TranslationMetres, 0);
        var edit = ItemMotion.Read(model).Fields;
        var before = Bytes(model);
        Require(ItemMotion.Apply(model, edit).Success, "raw endpoint edit refused");
        Require(Bytes(model).SequenceEqual(before), "unchanged endpoint edit rewrote storage");
    }
    var limit = Model();
    limit.TransAnimFunc = new() { IsDuration = false, SubFuncs = new[] {
        Key(KC.AnimEase.Linear, int.MaxValue), Key(KC.AnimEase.Linear, int.MaxValue) } };
    Require(ItemMotion.Evaluate(limit, .5).Success, "validated raw sum rather than normalized duration");
    limit.TransAnimFunc.SubFuncs = new[] { Key(KC.AnimEase.Linear, int.MaxValue),
        Key(KC.AnimEase.Linear, 0), Key(KC.AnimEase.Linear, int.MaxValue) };
    Require(ItemMotion.Evaluate(limit, .5).Status == ItemMotionStatus.Invalid, "normalized period overflow accepted");
    foreach (var mode in new[] { false, true })
    {
        limit.TransAnimFunc = new() { IsDuration = mode, SubFuncs = Array.Empty<KC.SubAnimFunc>() };
        Near(Value(ItemMotion.Evaluate(limit, 1)).TranslationMetres, 2);
        limit.TransAnimFunc.SubFuncs = new[] { Key(KC.AnimEase.Linear, 0) };
        Near(Value(ItemMotion.Evaluate(limit, 1)).TranslationMetres, 0);
    }
});

Check("independent timelines, scalar units and phase", () =>
{
    var model = Model();
    var sample = Value(ItemMotion.Evaluate(model, .5));
    Near(sample.TranslationMetres, 4); // 2 + (10 - 2) * .25
    Near(sample.AngleDegrees, 90); // independent one-second rotation cycle
    Vector(sample.Translation, new(4, 0, 0));
    Vector(Vector3.Transform(Vector3.UnitX, sample.Rotation), Vector3.UnitY);
    sample = Value(ItemMotion.Evaluate(model, 1));
    Near(sample.TranslationMetres, 6);
    Near(sample.AngleDegrees, 0);
    sample = Value(ItemMotion.Evaluate(model, 0, .25));
    Near(sample.TranslationMetres, 4);
    Near(sample.AngleDegrees, 45);
});

Check("empty and zero-total timelines are different", () =>
{
    var model = Model();
    model.TransAnimFunc = Timeline();
    model.AngleMinDeg = 30;
    model.RotAnimFunc = Timeline(Key(KC.AnimEase.Linear, 0));
    var sample = Value(ItemMotion.Evaluate(model, 3));
    Near(sample.TranslationMetres, 2);
    Near(sample.AngleDegrees, 0);
    model.TransAnimFunc = Timeline(Key(KC.AnimEase.Constant, 0, true));
    model.RotAnimFunc = Timeline();
    sample = Value(ItemMotion.Evaluate(model, 0));
    Near(sample.TranslationMetres, 0);
    Near(sample.AngleDegrees, 30);
});

Check("easing quarter points and reversal", () =>
{
    foreach (var (ease, expected) in new[] { (KC.AnimEase.Constant, 0d), (KC.AnimEase.Linear, .25),
        (KC.AnimEase.QuadIn, .0625), (KC.AnimEase.QuadOut, .4375), (KC.AnimEase.QuadInOut, .125) })
    {
        var model = Model(); model.TransMin = 0; model.TransMax = 1;
        model.TransAnimFunc = Timeline(Key(ease, 1000));
        Near(Value(ItemMotion.Evaluate(model, .25)).TranslationMetres, expected);
        model.TransAnimFunc.SubFuncs![0].Reverse = true;
        Near(Value(ItemMotion.Evaluate(model, .25)).TranslationMetres, 1 - expected);
    }
    var quad = Model(); quad.TransMin = 0; quad.TransMax = 1;
    quad.TransAnimFunc = Timeline(Key(KC.AnimEase.QuadInOut, 1000));
    Near(Value(ItemMotion.Evaluate(quad, .5)).TranslationMetres, .5);
    Near(Value(ItemMotion.Evaluate(quad, .75)).TranslationMetres, .875);
});

Check("key boundary, zero-duration skip, wrap, constant reverse", () =>
{
    var model = Model(); model.TransMin = 0; model.TransMax = 1;
    model.TransAnimFunc = Timeline(Key(KC.AnimEase.Linear, 1000), Key(KC.AnimEase.Constant, 0),
        Key(KC.AnimEase.Linear, 1000, true));
    Near(Value(ItemMotion.Evaluate(model, 0)).TranslationMetres, 0);
    Near(Value(ItemMotion.Evaluate(model, 1)).TranslationMetres, 1);
    Near(Value(ItemMotion.Evaluate(model, 1.5)).TranslationMetres, .5);
    Near(Value(ItemMotion.Evaluate(model, 2)).TranslationMetres, 0);
    model.TransAnimFunc = Timeline(Key(KC.AnimEase.Constant, 1000, true));
    Near(Value(ItemMotion.Evaluate(model, 99)).TranslationMetres, 1);
    Require(ItemMotion.Evaluate(model, double.MaxValue).Success, "large finite times overflowed");
});

Check("fractional-second exact loop boundaries", () =>
{
    var model = Model(); model.TransMin = 0; model.TransMax = 1;
    model.TransAnimFunc = Timeline(Key(KC.AnimEase.Linear, 100));
    foreach (var seconds in new[] { .3, .6, .7 })
        foreach (var phase in new[] { 0d, 1d })
        {
            Near(Value(ItemMotion.Evaluate(model, seconds, phase)).TranslationMetres, 0);
            Require(Value(ItemMotion.Evaluate(model, seconds - .000001, phase)).TranslationMetres > .99f,
                "preceding microsecond selected the wrong loop");
            Require(Value(ItemMotion.Evaluate(model, seconds + .000001, phase)).TranslationMetres > 0,
                "following microsecond selected the wrong loop");
            Near(Value(ItemMotion.Evaluate(model, seconds - .0000001, phase)).TranslationMetres, 0);
            Near(Value(ItemMotion.Evaluate(model, seconds + .0000001, phase)).TranslationMetres, 0);
        }
    model.TransAnimFunc = Timeline(Key(KC.AnimEase.Linear, 1));
    Near(Value(ItemMotion.Evaluate(model, 1.001)).TranslationMetres, 0); // 1001 ms; binary seconds*1000 is just below 1001.
});

Check("microsecond step boundaries and phase retain neighboring intervals", () =>
{
    var model = Model(); model.TransMin = 0; model.TransMax = 1;
    model.TransAnimFunc = Timeline(Key(KC.AnimEase.Constant, 100), Key(KC.AnimEase.Constant, 100, true));
    foreach (var (seconds, phase, expected) in new[] { (.6, 0d, 0d), (.7, 0d, 1d),
        (.6, 1d, 0d), (.7, 1d, 1d), (.6, .5, 1d), (.7, .5, 0d), (.55, .25, 0d), (.65, .25, 1d) })
    {
        Near(Value(ItemMotion.Evaluate(model, seconds, phase)).TranslationMetres, expected);
        Near(Value(ItemMotion.Evaluate(model, seconds - .000001, phase)).TranslationMetres, 1 - expected);
        Near(Value(ItemMotion.Evaluate(model, seconds + .000001, phase)).TranslationMetres, expected);
        Near(Value(ItemMotion.Evaluate(model, seconds - .0000001, phase)).TranslationMetres, expected);
        Near(Value(ItemMotion.Evaluate(model, seconds + .0000001, phase)).TranslationMetres, expected);
    }
    // Huge finite times still yield a finite sample; integer seconds are exact 100 ms loop boundaries.
    Near(Value(ItemMotion.Evaluate(model, double.MaxValue)).TranslationMetres, 0);
});

Check("preview quantizes time and phase to nearest microsecond away from midpoint zero", () =>
{
    var model = Model(); model.TransMin = 0; model.TransMax = 1000;
    model.TransAnimFunc = Timeline(Key(KC.AnimEase.Linear, 1));
    foreach (var (seconds, expected) in new[] { (.00000049, 0d), (.0000005, 1d), (.00000051, 1d), (.0000015, 2d) })
        Near(Value(ItemMotion.Evaluate(model, seconds)).TranslationMetres, expected);
    foreach (var (phase, expected) in new[] { (.00049, 0d), (.0005, 1d), (.00051, 1d), (.0015, 2d), (1d, 0d) })
        Near(Value(ItemMotion.Evaluate(model, 0, phase)).TranslationMetres, expected);
    // Quantize clock and phase separately, so .49us + .49us remains 0us, not a rounded 1us sum.
    Near(Value(ItemMotion.Evaluate(model, .00000049, .00049)).TranslationMetres, 0);
    Near(Value(ItemMotion.Evaluate(model, .0000005, .0005)).TranslationMetres, 2);
    model.TransMax = 1;
    model.TransAnimFunc = Timeline(Key(KC.AnimEase.Constant, 1), Key(KC.AnimEase.Constant, 2, true));
    // 98800us + .4*3000us = 100000us; modulo 3000 is exactly the second key's start (1000us).
    Near(Value(ItemMotion.Evaluate(model, .0988, .4)).TranslationMetres, 1);
    Near(Value(ItemMotion.Evaluate(model, .098799, .4)).TranslationMetres, 0);
    Near(Value(ItemMotion.Evaluate(model, .098801, .4)).TranslationMetres, 1);
    model.TransAnimFunc = Timeline(Key(KC.AnimEase.Linear, 3000)); model.TransMax = 3;
    foreach (var seconds in new[] { 1e13, 1e20, double.MaxValue })
    {
        var result = ItemMotion.Evaluate(model, seconds);
        Require(result.Success && double.IsFinite(Value(result).TranslationMetres), "huge-time evaluation must remain finite");
    }
    // The longest supported period exercises the safe-integer fallback's upper bound.
    model.TransAnimFunc = Timeline(Key(KC.AnimEase.Linear, int.MaxValue));
    Require(ItemMotion.Evaluate(model, double.MaxValue, 1).Success, "maximum supported duration overflowed");
});

Check("unsupported modes and missing data stay visible", () =>
{
    var model = Model();
    foreach (var ease in new[] { KC.AnimEase.CubicIn, (KC.AnimEase)255 })
    {
        model.TransAnimFunc = Timeline(Key(ease, 1000));
        Require(ItemMotion.Read(model).Fields.Translation!.Keys[0].Ease == ease, "raw easing lost");
        Require(ItemMotion.Evaluate(model, 0).Status == ItemMotionStatus.Unsupported, "unsupported easing coerced");
    }
    model.TransAnimFunc = null;
    Require(ItemMotion.Evaluate(model, 0).Status == ItemMotionStatus.Absent, "absent timeline defaulted");
    model.TransAnimFunc = new();
    Require(ItemMotion.Evaluate(model, 0).Status == ItemMotionStatus.Unresolved, "null key array defaulted");
});

Check("invalid edits are atomic across fields and both timelines", () =>
{
    var model = Model();
    var before = Bytes(model);
    var fields = ItemMotion.Read(model).Fields;
    foreach (var invalid in new[] { fields with { TranslationMin = float.NaN }, fields with { AngleMaxDegrees = float.PositiveInfinity },
        fields with { RotationAxis = (KC.EAxis)255 }, fields with { Translation = new(false, [new(KC.AnimEase.Linear, false, -1)]) },
        fields with { Rotation = new(true, [new(KC.AnimEase.Linear, false, int.MaxValue), new(KC.AnimEase.Linear, false, 1)]) },
        fields with { TranslationMin = 9, Rotation = new(false, [new(KC.AnimEase.CubicIn, false, 500)]) },
        fields with { Translation = new(false, Enumerable.Repeat(new ItemMotionKey(KC.AnimEase.Linear, false, 1), 5).ToArray()) } })
    {
        Require(!ItemMotion.Apply(model, invalid).Success, "invalid edit accepted");
        Require(before.SequenceEqual(Bytes(model)), "invalid edit partially mutated source");
    }
    foreach (double time in new[] { -1, double.NaN, double.PositiveInfinity })
        Require(ItemMotion.Evaluate(model, time).Status == ItemMotionStatus.Invalid, "invalid time accepted");
    Require(!ItemMotion.Evaluate(model, 0, double.NaN).Success, "invalid phase accepted");
    Require(ItemMotion.Apply(model, fields with { TranslationMin = 10, TranslationMax = -10 }).Success, "directed endpoints refused");
});

Check("save/reparse preserves unknown shader fields and independent timeline edits", () =>
{
    var model = Model();
    model.Version = 7; model.SubVersion = 23;
    model.ShaderTcType = KC.EShaderTcType.TransSubTexture; model.ShaderTcVersion = 9;
    model.ShaderTcAnimFunc = [new() { Duration = new TimeInt32(123), TextureId = 71 }];
    model.ShaderTcDataTransSub = new() { NbSubTexture = 8, NbSubTexturePerLine = 4, NbSubTexturePerColumn = 2, TopToBottom = true };
    model.RotAnimFunc!.SubFuncs![0].Ease = (KC.AnimEase)255;
    var oldRot = model.RotAnimFunc;
    var edit = ItemMotion.Read(model).Fields with { TranslationMin = -4, TranslationMax = 6,
        Translation = new(false, [new(KC.AnimEase.QuadOut, true, 450)]), Rotation = null };
    Require(ItemMotion.Apply(model, edit).Success, "valid scalar/timeline edit refused");
    Require(ReferenceEquals(oldRot, model.RotAnimFunc), "untouched unknown timeline replaced");
    var reloaded = Reparse<KC>(Bytes(model));
    Require(reloaded.Version == 7 && reloaded.SubVersion == 23 && reloaded.ShaderTcVersion == 9, "unknown versions changed");
    Require(reloaded.ShaderTcAnimFunc![0].TextureId == 71 && reloaded.ShaderTcAnimFunc[0].Duration.TotalMilliseconds == 123, "shader key changed");
    Require(reloaded.ShaderTcDataTransSub!.TopToBottom && reloaded.ShaderTcDataTransSub.NbSubTexture == 8, "shader payload lost");
    Require(reloaded.RotAnimFunc!.SubFuncs![0].Ease == (KC.AnimEase)255, "untouched unknown easing changed");
    Require(reloaded.TransAnimFunc!.SubFuncs![0].Duration.TotalMilliseconds == 450 && reloaded.TransAnimFunc.SubFuncs[0].Reverse, "edited key lost");
    Near(reloaded.TransMin, -4); Near(reloaded.TransMax, 6);
});

Check("filtered slots ignore interleaved static, free and missing-params entries", () =>
{
    var (prefab, model, parameters) = Prefab();
    var binding = ItemMotionBindings.Resolve(model, prefab, parameters, ItemMotionBindings.RootPath(2, 1));
    Require(binding.Status == ItemMotionStatus.Supported, binding.Reason ?? "binding failed");
    Require(binding.Slots.Count == 2 && binding.Slots[0].OriginalArrayIndex == 1 && binding.Slots[1].OriginalArrayIndex == 4, "used raw indexes");
    Require(binding.Parent.RawSlot == 0 && binding.Parent.Path == "doc:2/variant:1/root/ent:1", "parent wrong");
    Require(binding.Child.RawSlot == 1 && binding.Child.Path == "doc:2/variant:1/root/ent:4", "child wrong");
    Require(ReferenceEquals(binding.Child.SourceEntry, prefab.Ents[4]), "source handle lost");
});

Check("two shared nested occurrences refuse guessed targets, preserve topology on reparse", () =>
{
    var (nested, model, parameters) = Prefab();
    var root = new CPlugPrefab { Ents = [new() { Model = nested, Position = new(10, 0, 0), Rotation = new(0, 0, .70710677f, .70710677f) },
        new() { Model = nested, Position = new(20, 0, 0), Rotation = new(0, 0, 0, 1) }] };
    var a = ItemMotionBindings.Resolve(model, nested, parameters, "doc:0/variant:none/root/ent:0");
    var b = ItemMotionBindings.Resolve(model, nested, parameters, "doc:0/variant:none/root/ent:1");
    Require(a.Status == ItemMotionStatus.Unsupported && b.Status == ItemMotionStatus.Unsupported, "nested bindings guessed");
    Require(a.Child.Path is null && b.Child.Path is null && a.Child.SourceEntry is null && b.Child.SourceEntry is null, "wrong nested target exposed");
    Require(a.Slots[1].Path != b.Slots[1].Path && ReferenceEquals(a.Source, b.Source), "inventory scope or shared source lost");
    Require(ItemMotionBindings.Resolve(model, nested, parameters, "custom-owned-edge", isNestedPrefabOccurrence: true).Status == ItemMotionStatus.Unsupported, "explicit nested guard ignored");
    var saved = Reparse<CPlugPrefab>(Bytes(root));
    Require(ReferenceEquals(saved.Ents[0].Model, saved.Ents[1].Model), "shared prefab topology lost");
    var loaded = (CPlugPrefab)saved.Ents[0].Model!;
    Require(loaded.Ents.Length == 6 && loaded.Ents[0].U01 == "static-sentinel", "untouched entries changed");
    var loadedParams = (NPlugDyna_SPrefabConstraintParams)loaded.Ents[5].Params!;
    Require(loadedParams.Ent1 == 0 && loadedParams.Ent2 == 1, "raw slots changed on save");
});

Check("invalid child and world parent never select fallback mesh", () =>
{
    var (prefab, model, parameters) = Prefab();
    foreach (int world in new[] { -1, 2, int.MaxValue })
    {
        parameters.Ent1 = world;
        var binding = ItemMotionBindings.Resolve(model, prefab, parameters, "doc:2/variant:none/root");
        Require(binding.Status == ItemMotionStatus.Supported && binding.Parent.IsWorld, "world sentinel rejected");
        Require(binding.Parent.Path == "doc:2/variant:none/root" && binding.Parent.SourceEntry is null, "wrong world scope");
    }
    parameters.Ent2 = -1;
    var invalid = ItemMotionBindings.Resolve(model, prefab, parameters, "doc:0/variant:none/root");
    Require(invalid.Status == ItemMotionStatus.Unresolved && invalid.Child.Path is null && invalid.Child.SourceEntry is null, "fallback mesh animated");
    Require(ItemMotionBindings.Resolve(model, prefab, null, "doc:0/variant:none/root").Status == ItemMotionStatus.Absent, "missing params guessed");
    parameters.Ent1 = 0; parameters.Ent2 = 0;
    Require(ItemMotionBindings.Resolve(model, prefab, parameters, "root").Status == ItemMotionStatus.Unsupported, "self-parent accepted");
});

Check("external and nested/cyclic table ambiguity fail visibly", () =>
{
    var (prefab, model, parameters) = Prefab();
    prefab.Ents[0].ModelFile = new GbxRefTableFile(new GbxRefTable(), 0, true, "never-resolve.Gbx");
    var external = ItemMotionBindings.Resolve(model, prefab, parameters, "root");
    Require(external.Status == ItemMotionStatus.Unresolved && external.Child.Path is null, "external classification guessed");
    prefab.Ents[0].ModelFile = null; prefab.Ents[0].Model = prefab;
    Require(ItemMotionBindings.Resolve(model, prefab, parameters, "root").Status == ItemMotionStatus.Unsupported, "cyclic nesting not bounded");
    parameters.Pos1 = new(float.NaN, 0, 0);
    prefab.Ents[0].Model = new CPlugStaticObjectModel();
    Require(!ItemMotionBindings.ApplyTargets(model, prefab, parameters, "root", -1, 1).Success, "unverified anchors accepted");
});

Check("target editing validates before either slot changes", () =>
{
    var (prefab, model, parameters) = Prefab();
    Require(!ItemMotionBindings.ApplyTargets(model, prefab, parameters, "root", -1, 99).Success, "invalid target accepted");
    Require(parameters.Ent1 == 0 && parameters.Ent2 == 1, "partial target mutation");
    Require(ItemMotionBindings.ApplyTargets(model, prefab, parameters, "root", -1, 1).Success, "world binding refused");
    Require(parameters.Ent1 == -1 && parameters.Ent2 == 1, "valid targets not persisted");
});

Check("kinematic entity template appends one independently bound body and constraint atomically", () =>
{
    var (prefab, source, parameters) = Prefab();
    source.Version = 0;
    source.SubVersion = 3;
    var result = ItemKinematicEntityTemplate.AppendFromConstraint(prefab, source, "doc:0/variant:none/root");
    Require(result.Success && result.Value == 2, result.Reason ?? "template append failed");
    Require(prefab.Ents!.Length == 8, "template did not append exactly two entries");
    var body = prefab.Ents[6];
    var instance = (NPlugDynaObjectModel_SInstanceParams)body.Params!;
    Require(ReferenceEquals(body.Model, prefab.Ents[4].Model) && instance.Version == 2 && instance.IsKinematic,
        "template did not preserve the proven shared dyna model and kinematic instance classification");
    var constraint = (KC)prefab.Ents[7].Model!;
    var binding = (NPlugDyna_SPrefabConstraintParams)prefab.Ents[7].Params!;
    Require(binding.Ent1 == parameters.Ent1 && binding.Ent2 == 2 && binding.Pos1 == default && binding.Pos2 == default,
        "template binding did not target the new filtered slot");
    Require(constraint.SubVersion == 3 && constraint.TransAnimFunc!.SubFuncs![0].Duration == source.TransAnimFunc!.SubFuncs![0].Duration,
        "template did not copy typed constraint fields");
    Require(ItemMotionBindings.Resolve(constraint, prefab, binding, "doc:0/variant:none/root").Status == ItemMotionStatus.Supported,
        "appended binding is not independently resolvable");

    var length = prefab.Ents.Length;
    source.SubVersion = 2;
    var rejected = ItemKinematicEntityTemplate.AppendFromConstraint(prefab, source, "doc:0/variant:none/root");
    Require(!rejected.Success && prefab.Ents.Length == length, "unsupported template partially mutated the entity list");
});

Check("visible path proxy inserts a complete body before the original visible constraint", () =>
{
    var (prefab, source, parameters) = Prefab();
    source.Version = 0;
    source.SubVersion = 3;
    var result = ItemKinematicEntityTemplate.InsertVisiblePathProxy(prefab, source, "doc:0/variant:none/root");
    Require(result.Success, result.Reason ?? "visible path proxy insertion failed");
    Require(prefab.Ents!.Length == 8 && parameters.Ent1 == 2 && parameters.Ent2 == 1,
        "original visible constraint was not rebound to the proxy");
    Require(ReferenceEquals(prefab.Ents[6].Model, prefab.Ents[4].Model),
        "path proxy did not reuse the proven complete dyna model");
    var proxyParameters = (NPlugDyna_SPrefabConstraintParams)prefab.Ents[7].Params!;
    Require(proxyParameters.Ent1 == 0 && proxyParameters.Ent2 == 2,
        "path proxy constraint did not preserve the old parent and target the new slot");
    Require(ItemMotionBindings.Resolve(source, prefab, parameters, "doc:0/variant:none/root").Status == ItemMotionStatus.Supported,
        "original child constraint is not resolvable after proxy insertion");
    Require(ItemMotionBindings.Resolve(result.Value!, prefab, proxyParameters, "doc:0/variant:none/root").Status == ItemMotionStatus.Supported,
        "new proxy constraint is not independently resolvable");
});

Check("hidden carrier parent inserts one meshless helper and keeps chain bindings supported", () =>
{
    var (prefab, source, parameters) = Prefab();
    source.Version = 0;
    source.SubVersion = 3;
    var sourceBody = (CPlugDynaObjectModel)prefab.Ents![4].Model!;
    sourceBody.StaticShape = ReopenSurface(new CPlugSurface.Mesh
    {
        Version = 6,
        Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
        Triangles = [new(new Int3(0, 1, 2), 0, 0, 0)]
    });
    sourceBody.DynaShape = sourceBody.StaticShape;

    var result = ItemKinematicEntityTemplate.InsertHiddenCarrierParent(prefab, source, "doc:0/variant:none/root");
    Require(result.Success, result.Reason ?? "hidden carrier insertion failed");
    Require(prefab.Ents.Length == 8 && parameters.Ent1 == 2 && parameters.Ent2 == 1,
        "visible constraint was not rebound to the carrier slot");
    var carrier = (CPlugDynaObjectModel)prefab.Ents[6].Model!;
    Require(carrier.Mesh is not null && carrier.StaticShape is CPlugSurface { Surf: not null } && carrier.DynaShape is CPlugSurface { Surf: not null },
        "carrier body is not safely mesh-backed and collision-complete");
    var carrierParameters = (NPlugDyna_SPrefabConstraintParams)prefab.Ents[7].Params!;
    Require(carrierParameters.Ent1 == 0 && carrierParameters.Ent2 == 2,
        "carrier constraint did not preserve parent slot and target new helper slot");
    Require(ItemMotionBindings.Resolve(source, prefab, parameters, "doc:0/variant:none/root").Status == ItemMotionStatus.Supported,
        "visible child constraint is not resolvable after carrier insertion");
    Require(ItemMotionBindings.Resolve(result.Value!, prefab, carrierParameters, "doc:0/variant:none/root").Status == ItemMotionStatus.Supported,
        "carrier constraint is not independently resolvable");
});

Check("binding preview serialization excludes source graph", () =>
{
    var (prefab, model, parameters) = Prefab();
    var binding = ItemMotionBindings.Resolve(model, prefab, parameters, "doc:0/variant:none/root");
    using var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(binding));
    Require(!json.RootElement.TryGetProperty("Source", out _) && !json.RootElement.TryGetProperty("Parameters", out _), "source graph serialized");
    var child = json.RootElement.GetProperty("Child");
    Require(!child.TryGetProperty("SourceEntry", out _) && child.GetProperty("Path").GetString() == "doc:0/variant:none/root/ent:4", "target DTO lost path or leaked graph");
    parameters.Version = 1;
    Require(!ItemMotionBindings.ApplyTargets(model, prefab, parameters, "root", 0, 1).Success, "unknown constraint params version rewritten");
});

Check("rest-relative motion uses parent and child rotations independently", () =>
{
    var owner = Matrix4x4.CreateRotationZ(MathF.PI / 2) * Matrix4x4.CreateTranslation(10, 0, 0);
    var child = Matrix4x4.CreateTranslation(2, 0, 0) * owner;
    var signal = Matrix4x4.CreateTranslation(3, 0, 0);
    var world = Value(ItemMotionTransforms.ComposeVisual(child, owner, owner, signal));
    Vector(Vector3.Transform(Vector3.Zero, world), new(10, 5, 0));
    // Parent now rotates 180 degrees and moves to (20, 0); child's relative rest is still +2 X.
    var live = Matrix4x4.CreateRotationZ(MathF.PI) * Matrix4x4.CreateTranslation(20, 0, 0);
    world = Value(ItemMotionTransforms.ComposeVisual(child, owner, live, signal));
    Vector(Vector3.Transform(Vector3.Zero, world), new(15, 0, 0));
    var childRotated = Matrix4x4.CreateRotationZ(MathF.PI / 2) * Matrix4x4.CreateTranslation(2, 0, 0);
    world = Value(ItemMotionTransforms.ComposeVisual(childRotated, Matrix4x4.Identity, Matrix4x4.Identity, signal));
    Vector(Vector3.Transform(Vector3.Zero, world), new(2, 3, 0));
    var bad = Matrix4x4.Identity; bad.M41 = float.NaN;
    Require(!ItemMotionTransforms.ComposeVisual(bad, owner, live, signal).Success, "nonfinite child accepted");
    Require(!ItemMotionTransforms.ComposeVisual(child, new(), live, signal).Success, "singular parent accepted");
    Require(!ItemMotionTransforms.ComposeVisual(child, owner, Matrix4x4.CreateScale(2), signal).Success, "scaled parent accepted");
});

Check("multi-constraint chain follows A-to-E axis path with supported slot bindings", () =>
{
    static CPlugPrefab.EntRef Body() => new()
    {
        Model = new CPlugDynaObjectModel { IsStatic = false, Mesh = new CPlugSolid2Model() },
        Params = new NPlugDynaObjectModel_SInstanceParams { Version = 2, IsKinematic = true },
        Position = default,
        Rotation = Quat.Identity
    };
    static KC Constraint(KC.EAxis axis) => new()
    {
        Version = 0,
        SubVersion = 3,
        TransAxis = axis,
        TransMin = 0,
        TransMax = 5,
        RotAxis = KC.EAxis.Y,
        AngleMinDeg = 0,
        AngleMaxDeg = 0,
        TransAnimFunc = new KC.AnimFunc { IsDuration = true, SubFuncs = [new() { Ease = KC.AnimEase.Linear, Duration = new TimeInt32(1000) }] },
        RotAnimFunc = new KC.AnimFunc { IsDuration = true, SubFuncs = [new() { Ease = KC.AnimEase.Linear, Duration = new TimeInt32(1000) }] }
    };

    var a = Body(); var b = Body(); var c = Body(); var d = Body(); var e = Body();
    var ab = Constraint(KC.EAxis.X);
    var bc = Constraint(KC.EAxis.Y);
    var cd = Constraint(KC.EAxis.Z);
    var de = Constraint(KC.EAxis.X);
    var pAb = new NPlugDyna_SPrefabConstraintParams { Version = 0, Ent1 = -1, Ent2 = 0 };
    var pBc = new NPlugDyna_SPrefabConstraintParams { Version = 0, Ent1 = 0, Ent2 = 1 };
    var pCd = new NPlugDyna_SPrefabConstraintParams { Version = 0, Ent1 = 1, Ent2 = 2 };
    var pDe = new NPlugDyna_SPrefabConstraintParams { Version = 0, Ent1 = 2, Ent2 = 3 };
    var prefab = new CPlugPrefab
    {
        Ents =
        [
            a, b, c, d, e,
            new() { Model = ab, Params = pAb, Rotation = Quat.Identity },
            new() { Model = bc, Params = pBc, Rotation = Quat.Identity },
            new() { Model = cd, Params = pCd, Rotation = Quat.Identity },
            new() { Model = de, Params = pDe, Rotation = Quat.Identity }
        ]
    };

    foreach (var (constraint, parameters, parent, child) in new[] { (ab, pAb, -1, 0), (bc, pBc, 0, 1), (cd, pCd, 1, 2), (de, pDe, 2, 3) })
    {
        var binding = ItemMotionBindings.Resolve(constraint, prefab, parameters, "doc:0/variant:none/root");
        Require(binding.Status == ItemMotionStatus.Supported, binding.Reason ?? "binding rejected");
        Require(binding.Parent.RawSlot == parent && binding.Child.RawSlot == child, "unexpected slot table mapping");
    }

    var aLive = Value(ItemMotion.Evaluate(ab, 0.5)).Signal;
    var bSignal = Value(ItemMotion.Evaluate(bc, 0.5)).Signal;
    var cSignal = Value(ItemMotion.Evaluate(cd, 0.5)).Signal;
    var dSignal = Value(ItemMotion.Evaluate(de, 0.5)).Signal;
    var aWorld = Value(ItemMotionTransforms.ComposeVisual(Matrix4x4.Identity, Matrix4x4.Identity, Matrix4x4.Identity, aLive));
    var bWorld = Value(ItemMotionTransforms.ComposeVisual(Matrix4x4.Identity, Matrix4x4.Identity, aWorld, bSignal));
    var cWorld = Value(ItemMotionTransforms.ComposeVisual(Matrix4x4.Identity, Matrix4x4.Identity, bWorld, cSignal));
    var dWorld = Value(ItemMotionTransforms.ComposeVisual(Matrix4x4.Identity, Matrix4x4.Identity, cWorld, dSignal));

    Vector(Vector3.Transform(Vector3.Zero, aWorld), new(2.5f, 0, 0));
    Vector(Vector3.Transform(Vector3.Zero, bWorld), new(2.5f, 2.5f, 0));
    Vector(Vector3.Transform(Vector3.Zero, cWorld), new(2.5f, 2.5f, 2.5f));
    Vector(Vector3.Transform(Vector3.Zero, dWorld), new(5f, 2.5f, 2.5f));
});

Check("version-gated exact timing storage and save/reparse", () =>
{
    for (int version = 0; version <= 2; version++)
    {
        var timing = new NPlugDynaObjectModel_SInstanceParams { Version = version, PeriodSc = 2, PeriodScMax = 4,
            Phase01 = .25f, Phase01Max = .75f, IsKinematic = true, TextureId = 991, CastStaticShadow = true };
        var read = Value(ItemMotionTiming.Read(timing));
        Require((read.Phase01 is null) == (version == 0), "absent phase defaulted");
        Require((read.CastStaticShadow is null) == (version < 2), "absent shadow defaulted");
        var edit = version == 0 ? new ItemMotionTimingEdit(3) : new(3, 8, .125f, .625f);
        Require(ItemMotionTiming.Apply(timing, edit).Success, "valid timing rejected");
        var loaded = Reparse<NPlugDynaObjectModel_SInstanceParams>(Bytes(timing));
        Near(loaded.PeriodSc, 3);
        Require(loaded.Version == version && loaded.TextureId == 991 && loaded.IsKinematic, "timing metadata changed");
        if (version > 0) { Near(loaded.PeriodScMax, 8); Near(loaded.Phase01, .125); Near(loaded.Phase01Max, .625); }
        if (version == 2) Require(loaded.CastStaticShadow, "shadow lost");
        Require(ItemMotionTiming.ResolvePreviewPhase(timing).Status == ItemMotionStatus.Unsupported, "persisted phase silently became preview phase");
    }
    Require(ItemMotionTiming.Read(null).Status == ItemMotionStatus.Absent, "missing params defaulted");
    var sentinels = new NPlugDynaObjectModel_SInstanceParams { Version = 2, PeriodSc = 1, PeriodScMax = -1, Phase01 = -1, Phase01Max = -1 };
    var sentinelRead = Value(ItemMotionTiming.Read(sentinels));
    Require(sentinelRead.InheritsPhase == true && sentinelRead.RandomizesPeriod == false && sentinelRead.RandomizesPhase == false, "native timing sentinels rejected or mislabelled");
    Require(ItemMotionTiming.Apply(sentinels, new(2, -1, -1, -1)).Success, "canonical sentinels rejected");
    var savedSentinels = Reparse<NPlugDynaObjectModel_SInstanceParams>(Bytes(sentinels));
    Require(savedSentinels.PeriodScMax == -1 && savedSentinels.Phase01 == -1 && savedSentinels.Phase01Max == -1, "sentinels lost on reparse");
});

Check("timing edits reject absent and invalid fields atomically", () =>
{
    var timing = new NPlugDynaObjectModel_SInstanceParams { Version = 0, PeriodSc = 2 };
    Require(!ItemMotionTiming.Apply(timing, new(3, Phase01: .5f)).Success && timing.PeriodSc == 2, "version gate partial mutation");
    timing.Version = 2;
    foreach (var edit in new[] { new ItemMotionTimingEdit(float.NaN), new(float.PositiveInfinity), new(-1),
        new(3, float.NaN), new(3, Phase01: float.NaN), new(3, PhaseMax01: 2), new(3, Phase01: -.1f) })
    {
        var before = Bytes(timing);
        Require(!ItemMotionTiming.Apply(timing, edit).Success, "invalid timing accepted");
        Require(Bytes(timing).SequenceEqual(before), "invalid timing mutated source");
    }
    timing.Version = 3;
    Require(!ItemMotionTiming.Apply(timing, new(9)).Success, "unknown version rewritten");
    timing.Version = 2; timing.Phase01 = float.NaN;
    var read = ItemMotionTiming.Read(timing);
    Require(read.Status == ItemMotionStatus.Invalid && float.IsNaN(read.Value!.Phase01!.Value), "stored invalid timing hidden");
});

Check("default template fallback and collision generation from visual mesh", () =>
{
    var defaultTemplate = ItemKinematicEntityTemplate.GetDefaultMovingTemplate();
    Require(defaultTemplate is not null, "default moving template should load from bundled resources/disk");
    var prefab = defaultTemplate!.EntityModel as CPlugPrefab;
    Require(prefab?.Ents?.Length >= 2, "template should contain valid prefab entities");

    var stream = new CPlugVertexStream { Positions = [new(0, 0, 0), new(2, 0, 0), new(0, 2, 0)] };
    var indexBuffer = new CPlugIndexBuffer { Indices = [0, 1, 2] };
    var vit = new CPlugVisualIndexedTriangles
    {
        VertexStreams = [stream],
        IndexBuffer = indexBuffer,
        BoundingBox = new BoxAligned(0, 0, 0, 2, 2, 0)
    };
    var solid = new CPlugSolid2Model { Visuals = [vit] };
    var generatedSurface = ItemKinematicEntityTemplate.GenerateCollisionSurfaceFromMesh(solid);
    var sm = generatedSurface?.Surf as CPlugSurface.Mesh;
    Require(sm is not null, "collision generation failed");
    Require(sm.Vertices?.Length == 3 && sm.Triangles?.Length == 1, "generated surface mesh did not match source");
});

Check("relay collision path configures two root bodies with 100% collision", () =>
{
    var template = KinematicTemplate();
    var prefab = (CPlugPrefab)template.EntityModel!;
    var constraint = (KC)prefab.Ents![1].Model!;
    var result = ItemKinematicEntityTemplate.ConfigureRelayCollisionPath(
        prefab, constraint, "doc:0/variant:none/root",
        KC.EAxis.X, 5f, 2000,
        KC.EAxis.Y, 5f, 2000);
    Require(result.Success, result.Reason ?? "relay configuration failed");
    Require(prefab.Ents.Length == 4, "relay path did not build 4 entries (2 bodies + 2 constraints)");
    var p0 = (NPlugDyna_SPrefabConstraintParams)prefab.Ents[1].Params!;
    var p1 = (NPlugDyna_SPrefabConstraintParams)prefab.Ents[3].Params!;
    Require(p0.Ent1 == -1 && p0.Ent2 == 0, "body 0 constraint must be root-bound for full collision");
    Require(p1.Ent1 == -1 && p1.Ent2 == 1, "body 1 constraint must be root-bound for full collision");
    var c0 = (KC)prefab.Ents[1].Model!;
    var c1 = (KC)prefab.Ents[3].Model!;
    Require(c0.TransAxis == KC.EAxis.X && c0.TransMax == 5f, "c0 axis/max mismatch");
    Require(c1.TransAxis == KC.EAxis.Y && c1.TransMax == 5f, "c1 axis/max mismatch");
});

Check("chained L-path configures carrier helper and synchronized child", () =>
{
    var template = KinematicTemplate();
    var prefab = (CPlugPrefab)template.EntityModel!;
    var constraint = (KC)prefab.Ents![1].Model!;
    var result = ItemKinematicEntityTemplate.ConfigureChainedLPath(
        prefab, constraint, "doc:0/variant:none/root",
        KC.EAxis.X, 5f, 2000,
        KC.EAxis.Y, 5f, 2000, isPingPong: true);
    Require(result.Success, result.Reason ?? "chained L-path configuration failed");
    Require(prefab.Ents.Length == 4, "chained path did not insert carrier body and constraint");
    var carrierParams = (NPlugDyna_SPrefabConstraintParams)prefab.Ents[3].Params!;
    var childParams = (NPlugDyna_SPrefabConstraintParams)prefab.Ents[1].Params!;
    Require(carrierParams.Ent1 == -1, "carrier constraint must be attached to world");
    Require(childParams.Ent1 == 1, "visible constraint must be attached to carrier body");
});

Console.WriteLine($"{passed} passed, {failed} failed.");
return failed == 0 ? 0 : 1;

void Check(string name, Action action) { try { action(); passed++; Console.WriteLine("PASS: " + name); } catch (Exception e) { failed++; Console.Error.WriteLine("FAIL: " + name + ": " + e); } }
static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
static T Value<T>(ItemMotionResult<T> result) { Require(result.Success, result.Reason ?? "operation failed"); return result.Value!; }
static void Near(double actual, double expected) => Require(Math.Abs(actual - expected) < .0001, $"Expected {expected}, got {actual}");
static void Vector(Vector3 actual, Vector3 expected) { Near(actual.X, expected.X); Near(actual.Y, expected.Y); Near(actual.Z, expected.Z); }
static KC.SubAnimFunc Key(KC.AnimEase ease, int duration, bool reverse = false) => new() { Ease = ease, Duration = new TimeInt32(duration), Reverse = reverse };
static KC.AnimFunc Timeline(params KC.SubAnimFunc[] keys) => new() { IsDuration = true, SubFuncs = keys };
static KC Model() => new() { TransAxis = KC.EAxis.X, TransMin = 2, TransMax = 10, RotAxis = KC.EAxis.Z, AngleMinDeg = 0, AngleMaxDeg = 180,
    TransAnimFunc = Timeline(Key(KC.AnimEase.Linear, 2000)), RotAnimFunc = Timeline(Key(KC.AnimEase.Linear, 1000)) };
static CGameItemModel KinematicTemplate()
{
    var surfaceMesh = new CPlugSurface.Mesh
    {
        Version = 6,
        Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
        Triangles = [new(new Int3(0, 1, 2), 0, 0, 0)]
    };
    var surface = ReopenSurface(surfaceMesh);
    var item = new CGameItemModel
    {
        Ident = new Ident("KinematicTemplate", 26, "FixtureGenerator"),
        ItemType = CGameItemModel.EItemType.Ornament,
        ItemTypeE = CGameItemModel.EItemType.Ornament,
        EntityModel = new CPlugPrefab
        {
            Ents =
            [
                new()
                {
                    Model = new CPlugDynaObjectModel
                    {
                        Version = 13,
                        IsStatic = false,
                        Mesh = new CPlugSolid2Model(),
                        StaticShape = surface,
                        DynaShape = surface
                    },
                    Params = new NPlugDynaObjectModel_SInstanceParams
                    {
                        Version = 2,
                        PeriodSc = 1,
                        PeriodScMax = -1,
                        Phase01 = -1,
                        Phase01Max = -1,
                        IsKinematic = true
                    }
                },
                new()
                {
                    Model = new KC
                    {
                        Version = 0,
                        SubVersion = 3,
                        TransAxis = KC.EAxis.Y,
                        TransMin = 0,
                        TransMax = 1,
                        RotAxis = KC.EAxis.Y,
                        TransAnimFunc = Timeline(Key(KC.AnimEase.Linear, 1000)),
                        RotAnimFunc = Timeline(Key(KC.AnimEase.Linear, 1000))
                    },
                    Params = new NPlugDyna_SPrefabConstraintParams { Version = 0, Ent1 = -1, Ent2 = 0 }
                }
            ]
        }
    };
    item.CreateChunk<CGameCtnCollector.HeaderChunk2E001003>().Version = 8;
    item.CreateChunk<CGameItemModel.HeaderChunk2E002000>();
    item.CreateChunk<CGameItemModel.Chunk2E002015>();
    item.CreateChunk<CGameItemModel.Chunk2E002019>().Version = 15;
    return item;
}
static (CPlugPrefab Prefab, KC Model, NPlugDyna_SPrefabConstraintParams Parameters) Prefab()
{
    var model = Model();
    var parameters = new NPlugDyna_SPrefabConstraintParams { Ent1 = 0, Ent2 = 1 };
    var prefab = new CPlugPrefab { Ents = [
        new() { Model = new CPlugStaticObjectModel(), U01 = "static-sentinel" },
        new() { Model = new CPlugDynaObjectModel(), Params = new NPlugDynaObjectModel_SInstanceParams { Version = 2, IsKinematic = true } },
        new() { Model = new CPlugDynaObjectModel(), Params = new NPlugDynaObjectModel_SInstanceParams { IsKinematic = false } },
        new() { Model = new CPlugDynaObjectModel() },
        new() { Model = new CPlugDynaObjectModel(), Params = new NPlugDynaObjectModel_SInstanceParams { Version = 2, IsKinematic = true } },
        new() { Model = model, Params = parameters } ] };
    return (prefab, model, parameters);
}
static byte[] Bytes(GBX.NET.Engines.MwFoundations.CMwNod model)
{
    using var stream = new MemoryStream();
    using var writer = new GbxWriter(stream);
    using var rw = new GbxReaderWriter(writer);
    model.ReadWrite(rw);
    return stream.ToArray();
}
static CPlugSurface ReopenSurface(CPlugSurface.Mesh mesh)
{
    var surface = new CPlugSurface { Surf = mesh };
    surface.CreateChunk<CPlugSurface.Chunk0900C003>().Version = 2;
    using var stream = new MemoryStream();
    new Gbx<CPlugSurface>(surface) { BodyCompression = GbxCompression.Uncompressed }.Save(stream);
    stream.Position = 0;
    return Gbx.Parse<CPlugSurface>(stream).Node;
}
static T Reparse<T>(byte[] bytes) where T : GBX.NET.Engines.MwFoundations.CMwNod, new()
{
    using var stream = new MemoryStream(bytes);
    using var reader = new GbxReader(stream);
    using var rw = new GbxReaderWriter(reader);
    var result = new T(); result.ReadWrite(rw);
    Require(stream.Position == stream.Length, "trailing bytes after reparse");
    return result;
}
