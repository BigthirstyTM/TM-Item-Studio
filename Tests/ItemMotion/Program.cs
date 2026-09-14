using System.Numerics;
using System.Security.Cryptography;
using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;
using GBX.NET.Serialization;
using TM_Item_Studio.Models;
using TmEssentials;
using KC = GBX.NET.Engines.Meta.NPlugDyna_SKinematicConstraint;

// Synthetic authored data only. Expected numbers below are hand-calculated, not produced by the evaluator.
int passed = 0, failed = 0;
Console.WriteLine("Bundled parser SHA256: " + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Gbx).Assembly.Location))).ToLowerInvariant());

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
static T Reparse<T>(byte[] bytes) where T : GBX.NET.Engines.MwFoundations.CMwNod, new()
{
    using var stream = new MemoryStream(bytes);
    using var reader = new GbxReader(stream);
    using var rw = new GbxReaderWriter(reader);
    var result = new T(); result.ReadWrite(rw);
    Require(stream.Position == stream.Length, "trailing bytes after reparse");
    return result;
}
