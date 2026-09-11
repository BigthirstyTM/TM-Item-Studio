using GBX.NET.Engines.Meta;

namespace TM_Item_Studio.Models;

/// <summary>Exact persisted instance values. Null fields are absent in this wire version, never inferred zero defaults.</summary>
public sealed record ItemMotionInstanceTiming(int Version, float PeriodSeconds, float? PeriodMaxSeconds,
    float? Phase01, float? PhaseMax01, bool IsKinematic, int TextureId, bool? CastStaticShadow)
{
    public bool? InheritsPhase => Phase01 is null ? null : Phase01 < 0;
    public bool? RandomizesPeriod => PeriodMaxSeconds is null ? null : PeriodMaxSeconds >= 0;
    public bool? RandomizesPhase => PhaseMax01 is null ? null : PhaseMax01 >= 0;
}
public sealed record ItemMotionTimingEdit(float PeriodSeconds, float? PeriodMaxSeconds = null,
    float? Phase01 = null, float? PhaseMax01 = null);

public static class ItemMotionTiming
{
    public static ItemMotionResult<ItemMotionInstanceTiming> Read(NPlugDynaObjectModel_SInstanceParams? source)
    {
        if (source is null) return ItemMotionResult<ItemMotionInstanceTiming>.Fail(ItemMotionStatus.Absent, "Instance parameters are absent; native defaults are not assumed.");
        var value = new ItemMotionInstanceTiming(source.Version, source.PeriodSc,
            source.Version >= 1 ? source.PeriodScMax : null,
            source.Version >= 1 ? source.Phase01 : null,
            source.Version >= 1 ? source.Phase01Max : null,
            source.IsKinematic, source.TextureId, source.Version >= 2 ? source.CastStaticShadow : null);
        if (source.Version is < 0 or > 2) return new(ItemMotionStatus.Unsupported, value, "Unsupported instance-params version.");
        if (!float.IsFinite(value.PeriodSeconds) || value.PeriodMaxSeconds is float max && !float.IsFinite(max)
            || value.Phase01 is float phase && !StoredPhase(phase) || value.PhaseMax01 is float phaseMax && !StoredPhase(phaseMax))
            return new(ItemMotionStatus.Invalid, value, "Stored timing contains nonfinite or out-of-range values.");
        if (value.PeriodSeconds < 0) return new(ItemMotionStatus.Unsupported, value, "Negative base period is preserved; its consumer semantics are unverified.");
        return ItemMotionResult<ItemMotionInstanceTiming>.Ok(value);
    }

    public static ItemMotionResult<bool> Apply(NPlugDynaObjectModel_SInstanceParams source, ItemMotionTimingEdit edit)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(edit);
        if (source.Version is < 0 or > 2) return ItemMotionResult<bool>.Fail(ItemMotionStatus.Unsupported, "Unsupported instance-params version.");
        if (source.Version == 0 && (edit.PeriodMaxSeconds.HasValue || edit.Phase01.HasValue || edit.PhaseMax01.HasValue))
            return ItemMotionResult<bool>.Fail(ItemMotionStatus.Unsupported, "Version 0 does not serialize period max or phase fields; no automatic version upgrade.");
        if (!ValidPeriod(edit.PeriodSeconds)
            || edit.PeriodMaxSeconds is float max && !(ValidPeriod(max) || max == -1)
            || edit.Phase01 is float phase && !(ValidPhase(phase) || phase == -1)
            || edit.PhaseMax01 is float phaseMax && !(ValidPhase(phaseMax) || phaseMax == -1))
            return ItemMotionResult<bool>.Fail(ItemMotionStatus.Invalid, "Periods must be finite/nonnegative and phases in [0, 1]; -1 means inherited phase or disabled max randomization.");
        source.PeriodSc = edit.PeriodSeconds;
        if (edit.PeriodMaxSeconds.HasValue) source.PeriodScMax = edit.PeriodMaxSeconds.Value;
        if (edit.Phase01.HasValue) source.Phase01 = edit.Phase01.Value;
        if (edit.PhaseMax01.HasValue) source.Phase01Max = edit.PhaseMax01.Value;
        return ItemMotionResult<bool>.Ok(true);
    }

    public static ItemMotionResult<double> ResolvePreviewPhase(NPlugDynaObjectModel_SInstanceParams? source)
    {
        var read = Read(source);
        if (!read.Success) return ItemMotionResult<double>.Fail(read.Status, read.Reason!);
        return ItemMotionResult<double>.Fail(ItemMotionStatus.Unsupported,
            "Persisted instance period/phase to kinematic signal mapping is unverified. Preview uses a separately labelled phase control.");
    }

    private static bool ValidPeriod(float value) => float.IsFinite(value) && value >= 0;
    private static bool ValidPhase(float value) => float.IsFinite(value) && value >= 0 && value <= 1;
    private static bool StoredPhase(float value) => float.IsFinite(value) && value <= 1; // Any negative stored phase/max is a native sentinel.
}
