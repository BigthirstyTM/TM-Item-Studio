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

/// <summary>Raw integral milliseconds: duration when IsDuration is true, cumulative endpoint otherwise.</summary>
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

    /// <summary>Explicit count edit: retain prefix keys and timing mode, remove only trailing
    /// keys or append one-second linear segments. Prepare/validate before mutating the array.</summary>
    public static ItemMotionResult<bool> ResizeTimeline(KC.AnimFunc? source, int count)
    {
        if (source?.SubFuncs is not { } keys)
            return ItemMotionResult<bool>.Fail(ItemMotionStatus.Absent, "Timeline is absent; no default functions were created.");
        if (count == keys.Length) return ItemMotionResult<bool>.Ok(false);
        if (count < 1 || count > 4)
            return ItemMotionResult<bool>.Fail(ItemMotionStatus.Invalid, "Choose between one and four segments.");
        if (keys.Any(k => k is null))
            return ItemMotionResult<bool>.Fail(ItemMotionStatus.Invalid, "Timeline contains an absent segment; stored functions are unchanged.");
        var resized = new KC.SubAnimFunc[count];
        Array.Copy(keys, resized, Math.Min(keys.Length, count));
        for (var i = keys.Length; i < count; i++)
        {
            var ms = source.IsDuration || i == 0 ? 1000L : (long)resized[i - 1].Duration.TotalMilliseconds + 1000;
            if (ms > int.MaxValue)
                return ItemMotionResult<bool>.Fail(ItemMotionStatus.Invalid, "New segment end time exceeds the supported range; stored functions are unchanged.");
            resized[i] = new KC.SubAnimFunc { Ease = KC.AnimEase.Linear, Reverse = false, Duration = new TimeInt32((int)ms) };
        }
        var candidate = new ItemMotionTimeline(source.IsDuration, resized.Select(k => new ItemMotionKey(k.Ease, k.Reverse, k.Duration.TotalMilliseconds)).ToArray());
        var error = ValidateTimeline(candidate);
        if (error is not null) return ItemMotionResult<bool>.Fail(error.Value.Status, error.Value.Reason);
        source.SubFuncs = resized;
        return ItemMotionResult<bool>.Ok(true);
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
    /// Time and each timeline's phase offset are independently rounded to integer microseconds, with midpoint
    /// away from zero. Sub-microsecond/adjacent-double ordering is not preserved by this visual preview clock.
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
        if (timeline.Keys.Any(k => k.DurationMilliseconds < 0)) return (ItemMotionStatus.Invalid, "Times must be nonnegative milliseconds.");
        if (TotalDuration(timeline) > int.MaxValue) return (ItemMotionStatus.Invalid, "Total duration exceeds the supported millisecond range.");
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

    // Native archive loading converts false-mode endpoints to durations. Difference
    // original neighbours (including decreasing/duplicate endpoints), never mutate storage.
    private static int SegmentDuration(ItemMotionTimeline timeline, int index) =>
        timeline.IsDuration || index == 0 ? timeline.Keys[index].DurationMilliseconds
        : Math.Max(0, timeline.Keys[index].DurationMilliseconds - timeline.Keys[index - 1].DurationMilliseconds);

    private static long TotalDuration(ItemMotionTimeline timeline) =>
        Enumerable.Range(0, timeline.Keys.Count).Sum(i => (long)SegmentDuration(timeline, i));

    private static float Scalar(ItemMotionTimeline timeline, double seconds, double phase, float min, float max)
    {
        if (timeline.Keys.Count == 0) return min;
        var total = TotalDuration(timeline);
        if (total == 0) return 0; // Distinct from an empty key array: native signal early-out ignores min/max.
        var totalMicroseconds = total * 1000;
        var scaledTime = seconds * 1_000_000;
        if (scaledTime > 9007199254740991d)
            // Reduce huge finite times by the timeline period expressed in seconds.
            // Storage durations are milliseconds, so convert before applying the modulus.
            scaledTime = (seconds % (total / 1000d)) * 1_000_000;
        var timeMicroseconds = (long)Math.Round(scaledTime, MidpointRounding.AwayFromZero);
        var phaseMicroseconds = (long)Math.Round(phase * totalMicroseconds, MidpointRounding.AwayFromZero);
        // Integer reduction/addition keeps the time-plus-phase boundary exact. The modulo inputs
        // remain safe integers in the JS analogue too; phase 1 naturally wraps to phase 0.
        var position = ((timeMicroseconds % totalMicroseconds) + phaseMicroseconds) % totalMicroseconds;
        for (var i = 0; i < timeline.Keys.Count; i++)
        {
            var key = timeline.Keys[i];
            var duration = SegmentDuration(timeline, i);
            if (duration == 0) continue;
            var keyMicroseconds = (long)duration * 1000;
            if (position >= keyMicroseconds) { position -= keyMicroseconds; continue; }
            var x = (double)position / keyMicroseconds;
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

    private static Vector3 Axis(KC.EAxis axis) => axis switch
    { KC.EAxis.X => Vector3.UnitX, KC.EAxis.Y => Vector3.UnitY, KC.EAxis.Z => Vector3.UnitZ,
        _ => throw new ArgumentOutOfRangeException(nameof(axis)) };
}
