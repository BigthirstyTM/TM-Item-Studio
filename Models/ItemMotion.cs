using System.Numerics;
using GBX.NET.Engines.Meta;
using TmEssentials;
using KC = GBX.NET.Engines.Meta.NPlugDyna_SKinematicConstraint;

namespace TM_Item_Studio.Models;

public enum ItemMotionStatus { Supported, Absent, Unsupported, Unresolved, Invalid }

public sealed record ItemMotionResult<T>(ItemMotionStatus Status, T? Value, string? Reason = null)
{
    public bool Success => Status == ItemMotionStatus.Supported;
    public static ItemMotionResult<T> Ok(T value) => new(ItemMotionStatus.Supported, value);
    public static ItemMotionResult<T> Fail(ItemMotionStatus status, string reason) => new(status, default, reason);
}

/// <summary>Storage uses integral milliseconds; UI adapters may display DurationMilliseconds / 1000d.</summary>
public sealed record ItemMotionKey(KC.AnimEase Ease, bool Reverse, int DurationMilliseconds);
public sealed record ItemMotionTimeline(bool IsDuration, IReadOnlyList<ItemMotionKey> Keys);
/// <summary>Translation in metres; angles in degrees. Null timeline edits preserve the original timeline.</summary>
public sealed record ItemMotionEdit(KC.EAxis TranslationAxis, float TranslationMin, float TranslationMax,
    KC.EAxis RotationAxis, float AngleMinDegrees, float AngleMaxDegrees,
    ItemMotionTimeline? Translation = null, ItemMotionTimeline? Rotation = null);
public sealed record ItemMotionSnapshot(ItemMotionEdit Fields, int Version, int SubVersion,
    ItemMotionStatus TranslationStatus, ItemMotionStatus RotationStatus, string? TranslationReason, string? RotationReason);
public sealed record ItemMotionSample(float TranslationMetres, float AngleDegrees, Vector3 Translation,
    Quaternion Rotation, Matrix4x4 Signal);

/// <summary>Typed source adapter. Edits affect every occurrence sharing this authored source node.</summary>
public static class ItemMotion
{
    public static ItemMotionSnapshot Read(KC source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var trans = ReadTimeline(source.TransAnimFunc);
        var rot = ReadTimeline(source.RotAnimFunc);
        return new(new(source.TransAxis, source.TransMin, source.TransMax, source.RotAxis,
            source.AngleMinDeg, source.AngleMaxDeg, trans.Value, rot.Value), source.Version, source.SubVersion,
            trans.Status, rot.Status, trans.Reason, rot.Reason);
    }

    public static ItemMotionResult<ItemMotionTimeline> ReadTimeline(KC.AnimFunc? source)
    {
        if (source is null) return ItemMotionResult<ItemMotionTimeline>.Fail(ItemMotionStatus.Absent, "Timeline is absent.");
        if (source.SubFuncs is null) return ItemMotionResult<ItemMotionTimeline>.Fail(ItemMotionStatus.Unresolved, "Timeline key array is absent.");
        if (source.SubFuncs.Any(k => k is null)) return ItemMotionResult<ItemMotionTimeline>.Fail(ItemMotionStatus.Invalid, "Timeline contains a null key.");
        var value = new ItemMotionTimeline(source.IsDuration, Array.AsReadOnly(source.SubFuncs.Select(k =>
            new ItemMotionKey(k.Ease, k.Reverse, k.Duration.TotalMilliseconds)).ToArray()));
        var error = ValidateTimeline(value);
        return error is null ? ItemMotionResult<ItemMotionTimeline>.Ok(value)
            : new(error.Value.Status, value, error.Value.Reason);
    }

    /// <summary>Validate and prepare both timelines before changing any field. Unedited shader/unknown data stays untouched.</summary>
    public static ItemMotionResult<bool> Apply(KC source, ItemMotionEdit edit)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(edit);
        var error = ValidateFields(edit);
        if (error is not null) return ItemMotionResult<bool>.Fail(ItemMotionStatus.Invalid, error);
        foreach (var timeline in new[] { edit.Translation, edit.Rotation })
        {
            if (timeline is null) continue;
            var validation = ValidateTimeline(timeline);
            if (validation is not null) return ItemMotionResult<bool>.Fail(validation.Value.Status, validation.Value.Reason);
        }
        var trans = edit.Translation is null ? null : ToSource(edit.Translation);
        var rot = edit.Rotation is null ? null : ToSource(edit.Rotation);
        source.TransAxis = edit.TranslationAxis;
        source.TransMin = edit.TranslationMin;
        source.TransMax = edit.TranslationMax;
        source.RotAxis = edit.RotationAxis;
        source.AngleMinDeg = edit.AngleMinDegrees;
        source.AngleMaxDeg = edit.AngleMaxDegrees;
        if (trans is not null) source.TransAnimFunc = trans;
        if (rot is not null) source.RotAnimFunc = rot;
        return ItemMotionResult<bool>.Ok(true);
    }

    /// <summary>
    /// Deterministic visual preview, not physics/game parity. previewPhase01 is a preview-only runtime phase,
    /// NOT an interpretation of persisted SInstanceParams.Phase01. Time is seconds; loops advance independently.
    /// </summary>
    public static ItemMotionResult<ItemMotionSample> Evaluate(KC source, double seconds, double previewPhase01 = 0)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!double.IsFinite(seconds) || seconds < 0 || !double.IsFinite(previewPhase01) || previewPhase01 < 0 || previewPhase01 > 1)
            return ItemMotionResult<ItemMotionSample>.Fail(ItemMotionStatus.Invalid, "Time must be finite and nonnegative; preview phase must be in [0, 1].");
        var state = Read(source);
        var error = ValidateFields(state.Fields);
        if (error is not null) return ItemMotionResult<ItemMotionSample>.Fail(ItemMotionStatus.Invalid, error);
        if (state.TranslationStatus != ItemMotionStatus.Supported)
            return ItemMotionResult<ItemMotionSample>.Fail(state.TranslationStatus, "Translation: " + state.TranslationReason);
        if (state.RotationStatus != ItemMotionStatus.Supported)
            return ItemMotionResult<ItemMotionSample>.Fail(state.RotationStatus, "Rotation: " + state.RotationReason);
        var trans = Scalar(state.Fields.Translation!, seconds, previewPhase01, source.TransMin, source.TransMax);
        var angle = Scalar(state.Fields.Rotation!, seconds, previewPhase01, source.AngleMinDeg, source.AngleMaxDeg);
        var translation = Axis(source.TransAxis) * trans;
        var rotation = Quaternion.CreateFromAxisAngle(Axis(source.RotAxis), (float)(angle * (Math.PI / 180)));
        var signal = Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(translation);
        if (!ItemMotionTransforms.IsFinite(signal))
            return ItemMotionResult<ItemMotionSample>.Fail(ItemMotionStatus.Invalid, "Preview transform overflowed.");
        return ItemMotionResult<ItemMotionSample>.Ok(new(trans, angle, translation, rotation, signal));
    }

    private static string? ValidateFields(ItemMotionEdit edit)
    {
        if (!Enum.IsDefined(edit.TranslationAxis) || !Enum.IsDefined(edit.RotationAxis)) return "Unknown motion axis.";
        if (!float.IsFinite(edit.TranslationMin) || !float.IsFinite(edit.TranslationMax)
            || !float.IsFinite(edit.AngleMinDegrees) || !float.IsFinite(edit.AngleMaxDegrees)) return "Ranges must be finite.";
        // Endpoints are directed, so descending min/max pairs are intentionally retained.
        return null;
    }

    private static (ItemMotionStatus Status, string Reason)? ValidateTimeline(ItemMotionTimeline timeline)
    {
        if (timeline.Keys is null || timeline.Keys.Any(k => k is null)) return (ItemMotionStatus.Invalid, "Missing timeline keys.");
        if (timeline.Keys.Count > 4) return (ItemMotionStatus.Unsupported, "The supported native layout has at most four keys.");
        if (timeline.IsDuration) return (ItemMotionStatus.Unsupported, "IsDuration=true semantics have not been verified; data is preserved.");
        if (timeline.Keys.Any(k => k.DurationMilliseconds < 0)) return (ItemMotionStatus.Invalid, "Durations must be nonnegative milliseconds.");
        if (timeline.Keys.Sum(k => (long)k.DurationMilliseconds) > int.MaxValue) return (ItemMotionStatus.Invalid, "Total duration exceeds the supported millisecond range.");
        if (timeline.Keys.Any(k => k.Ease < KC.AnimEase.Constant || k.Ease > KC.AnimEase.QuadInOut))
            return (ItemMotionStatus.Unsupported, "Preview/edit supports Constant, Linear, QuadIn, QuadOut and QuadInOut only; other keys are preserved.");
        return null;
    }

    private static KC.AnimFunc ToSource(ItemMotionTimeline timeline) => new()
    {
        IsDuration = timeline.IsDuration,
        SubFuncs = timeline.Keys.Select(k => new KC.SubAnimFunc { Ease = k.Ease, Reverse = k.Reverse,
            Duration = new TimeInt32(k.DurationMilliseconds) }).ToArray()
    };

    private static float Scalar(ItemMotionTimeline timeline, double seconds, double phase, float min, float max)
    {
        if (timeline.Keys.Count == 0) return min;
        var total = timeline.Keys.Sum(k => (long)k.DurationMilliseconds);
        if (total == 0) return 0; // Distinct from an empty key array: native signal early-out ignores min/max.
        // Convert ordinary times to authored millisecond units before periodic reduction. Modulo
        // by a fractional-second period can misclassify exact boundaries such as .6s / .1s.
        var milliseconds = seconds * 1000;
        if (milliseconds <= 9007199254740991d)
            milliseconds = ScaleAtIntegerBoundaries(seconds, 1000);
        else
            // An integer-second modulus is exactly 1000 periods, so it avoids both overflow
            // and division by an inexact fractional period at very large finite times.
            milliseconds = (seconds % total) * 1000;
        var phaseMilliseconds = phase == 1 ? 0 : ScaleAtIntegerBoundaries(phase, total);
        var position = ((milliseconds % total) + phaseMilliseconds) % total;
        foreach (var key in timeline.Keys)
        {
            if (key.DurationMilliseconds == 0) continue;
            if (position >= key.DurationMilliseconds) { position -= key.DurationMilliseconds; continue; }
            var x = position / key.DurationMilliseconds;
            var u = key.Ease switch
            {
                KC.AnimEase.Constant => 0,
                KC.AnimEase.Linear => x,
                KC.AnimEase.QuadIn => x * x,
                KC.AnimEase.QuadOut => x * (2 - x),
                KC.AnimEase.QuadInOut => x < .5 ? 2 * x * x : 1 - 2 * (1 - x) * (1 - x),
                _ => throw new InvalidOperationException("Unvalidated easing.")
            };
            if (key.Reverse) u = 1 - u;
            return (float)((1 - u) * min + u * max);
        }
        return min;
    }

    private static double ScaleAtIntegerBoundaries(double input, double scale)
    {
        var scaled = input * scale;
        var integer = Math.Round(scaled);
        var boundary = integer / scale;
        // Exact round-trip equality recognizes the double representing a requested integer
        // millisecond boundary (e.g. 1.001s), without an epsilon that would snap nearby times.
        if (input == boundary) return integer;
        if (scaled == integer)
            // Multiplication itself may round a neighboring representable input onto the
            // boundary. Retain which side it came from instead of changing the key interval.
            return input < boundary ? Math.BitDecrement(integer) : Math.BitIncrement(integer);
        return scaled;
    }

    private static Vector3 Axis(KC.EAxis axis) => axis switch
    { KC.EAxis.X => Vector3.UnitX, KC.EAxis.Y => Vector3.UnitY, KC.EAxis.Z => Vector3.UnitZ,
        _ => throw new ArgumentOutOfRangeException(nameof(axis)) };
}
