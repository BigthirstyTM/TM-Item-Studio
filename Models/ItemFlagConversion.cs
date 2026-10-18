using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.MetaNotPersistent;
using GBX.NET.Engines.Plug;

namespace TM_Item_Studio.Models;

/// <summary>
/// Convert an item into a Trackmania 2020 flag item, and report how far a given item is from the
/// measured vanilla shape.
///
/// Everything in this file was read from the game's own files, not from documentation:
/// <c>Stadium/Items/Flag16m.Item.Gbx</c>, <c>Stadium/Items/Flag8m.Item.Gbx</c> and
/// <c>Stadium/Media/PlaceParam/Flag.PlaceParam.Gbx</c> (plus their RedIsland copies). The two
/// measured signatures are:
///
/// 1. The entity model is a <c>NPlugItem_SVariantList</c> (0x2F0BC000) whose variant carries the
///    tag <c>Type=Flag</c>. <c>Type=Flag</c> occurs on exactly four of the 8,802 item files
///    measured (the four flag items), and on nothing else.
/// 2. The placement class is the only one in the whole corpus with
///    <c>AlwaysUp = true</c> with <c>AlignToWorldDir = true</c>: that combination occurs on
///    exactly those same four files, so a flag is the item that stays upright and aligns itself
///    to its world direction. Its eight patch layouts describe where the flag may be patched
///    (border groups at 12 m and 16 m spacing, at the border, water, open and curve positions).
///
/// The caller keeps ownership of the node: nothing here saves, exports or clones an item, and an
/// edit that cannot be expressed in the measured shape raises instead of guessing. Conversion
/// cannot invent the flag's cloth or its mesh: it changes classification and placement only.
/// </summary>
public static class ItemFlagConversion
{
    public const string TypeTagKey = "Type";
    public const string TypeTagValue = "Flag";
    public const string MaterialModifierTagKey = "MatModifier";
    public const string MaterialModifierTagValue = "Ice";

    /// <summary>The variant list version the vanilla flag items use; version 1 stores HiddenInManualCycle.</summary>
    public const int VariantListVersion = 1;

    /// <summary>The placement-class version of the vanilla flag place-param.</summary>
    public const int PlacementClassVersion = 10;

    public const string PlacementSizeGroup = "0x0";

    /// <summary>The world direction a flag aligns to. Measured as +Z (north) on both vanilla flags.</summary>
    public static Vec3 PlacementWorldDir => new(0, 0, 1);

    // Placement-parameter values measured on Stadium/Media/PlaceParam/Flag.PlaceParam.Gbx.
    public const int PlacementFlags = 1;
    public const float PlacementPivotSnapDistance = -1f;
    public const float PlacementCubeSize = 0f;
    public const float PlacementFlyVStep = 0f;
    public const float PlacementFlyVOffset = 0f;
    public const float PlacementSnapStep = 0f;
    public const float PlacementSnapOffset = 0f;

    /// <summary>One patch layout of the flag's placement class, as measured on the vanilla place-param.</summary>
    public readonly record struct PatchLayout(
        int ItemCount,
        float ItemSpacing,
        int FillAlign,
        int FillDir,
        float NormedPos,
        float U01,
        IReadOnlyList<string> OnlyOnGroups,
        float Altitude,
        float U02);

    private static readonly string[] BorderGroups =
    [
        "Border", "StructureSupport", "BorderInGround", "BorderWaterSmall", "BorderPlatform",
        "BorderPlatformWater", "BorderDecoWall", "BorderTrackWall", "BorderMultiSize",
    ];

    /// <summary>
    /// The flag's eight patch layouts in file order. Every entry uses the same fill settings; only
    /// the spacing (12 m / 16 m), the normalised position and the border group differ.
    /// </summary>
    public static PatchLayout[] CreatePatchLayouts() =>
    [
        new(0, 12f, 0, 1, 0.5f, 0f, BorderGroups, 0f, 0f),
        new(0, 16f, 0, 1, 0.5f, 0f, BorderGroups, 0f, 0f),
        new(0, 12f, 0, 1, 0.9f, 0f, ["BorderOpen"], 0f, 0f),
        new(0, 16f, 0, 1, 0.9f, 0f, ["BorderOpen"], 0f, 0f),
        new(0, 12f, 0, 1, 0.915f, 0f, ["BorderWater"], 0f, 0f),
        new(0, 16f, 0, 1, 0.915f, 0f, ["BorderWater"], 0f, 0f),
        new(0, 12f, 0, 1, 0.875f, 0f, ["BorderOpenCurve1"], 0f, 0f),
        new(0, 16f, 0, 1, 0.875f, 0f, ["BorderOpenCurve1"], 0f, 0f),
    ];

    /// <summary>One measured difference between an item and the vanilla flag shape.</summary>
    public readonly record struct Deviation(string Field, string Expected, string Actual);

    /// <summary>What a flag conversion changed, and what it refused to change.</summary>
    public sealed record FlagReport(bool IsFlag, IReadOnlyList<Deviation> Deviations, IReadOnlyList<string> Applied, IReadOnlyList<string> Notes)
    {
        public string Summary
        {
            get
            {
                if (!IsFlag && Deviations.Count == 0) return "No deviation was measured.";
                if (Deviations.Count == 0) return "The item carries the measured flag signature.";
                return string.Join(" ", Deviations.Select(d => $"{d.Field}: {d.Actual} instead of {d.Expected}."));
            }
        }
    }

    /// <summary>
    /// Read-only check against the measured flag shape. Never changes the item; an item without an
    /// entity model counts as a deviation, not as an error.
    /// </summary>
    public static bool IsFlagItem(CGameItemModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return Inspect(item).IsFlag;
    }

    /// <summary>Read-only check against the measured flag shape.</summary>
    public static FlagReport Inspect(CGameItemModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var deviations = new List<Deviation>();
        var notes = new List<string>();

        var variants = item.EntityModel as NPlugItem_SVariantList;
        if (variants is null)
        {
            deviations.Add(new("EntityModel", $"NPlugItem_SVariantList with a {TypeTagKey}={TypeTagValue} variant",
                item.EntityModel?.GetType().Name ?? "none"));
        }
        else
        {
            if (variants.Version != VariantListVersion)
                deviations.Add(new("VariantList.Version", VariantListVersion.ToString(), variants.Version.ToString()));
            var modeled = (variants.Variants ?? []).Where(v => v.EntityModel is not null || v.EntityModelFile is not null).ToArray();
            if (modeled.Length == 0)
            {
                deviations.Add(new("Variants", "at least one variant with a model", "none"));
            }
            else
            {
                for (var i = 0; i < modeled.Length; i++)
                {
                    var tags = modeled[i].Tags;
                    var index = Array.IndexOf(variants.Variants!, modeled[i]);
                    if (tags is null || !tags.TryGetValue(TypeTagKey, out var typeValue))
                        deviations.Add(new($"Variants[{index}].Tags[{TypeTagKey}]", TypeTagValue, "(absent)"));
                    else if (typeValue != TypeTagValue)
                        deviations.Add(new($"Variants[{index}].Tags[{TypeTagKey}]", TypeTagValue, typeValue));
                    if (tags is not null && tags.TryGetValue(MaterialModifierTagKey, out var modifier)
                        && modifier != MaterialModifierTagValue)
                    {
                        notes.Add($"Variants[{index}] keeps {MaterialModifierTagKey}={modifier}; the vanilla flag uses "
                            + $"{MaterialModifierTagValue}. It was not overwritten.");
                    }
                }
            }
        }

        InspectPlacement(item, deviations, notes);
        return new FlagReport(deviations.Count == 0, deviations, [], notes);
    }

    /// <summary>
    /// Turn an item into a flag: the measured variant tag, the variant list version, and the flag's
    /// placement class. Pivots, geometry, materials and metadata are left alone.
    /// </summary>
    /// <param name="item">The item to edit in place.</param>
    /// <param name="normalizePlacement">
    /// When true the placement parameter fields the vanilla flag sets (flags, cube, snapping and fly
    /// steps) are set to the measured values as well. When false only the placement class is applied.
    /// </param>
    /// <returns>What was applied and what could not be applied.</returns>
    public static FlagReport Convert(CGameItemModel item, bool normalizePlacement = true)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.EntityModel is null)
            throw new NotSupportedException("The item has no entity model, so it cannot be classified as a flag.");

        // Everything that can refuse runs before the first write, so a rejected conversion leaves
        // the item exactly as it was.
        EnsurePlacementIsEditable(item);

        var applied = new List<string>();
        var notes = new List<string>();

        if (item.EntityModel is NPlugItem_SVariantList existing)
        {
            if (existing.Variants is null || existing.Variants.Length == 0)
                throw new NotSupportedException("The item's variant list is empty; a flag needs a variant that carries a model.");
            if (existing.Version > VariantListVersion)
                throw new NotSupportedException(
                    $"The variant list version {existing.Version} is newer than the measured flag version {VariantListVersion}; no edit was applied.");

            foreach (var variant in existing.Variants)
            {
                if (variant.EntityModel is null && variant.EntityModelFile is null) continue;
                variant.Tags ??= new Dictionary<string, string>();
                // Type=Flag is the classification; an existing MatModifier is the author's choice and
                // is preserved, only a missing one is filled in with the vanilla value.
                variant.Tags[TypeTagKey] = TypeTagValue;
                if (!variant.Tags.ContainsKey(MaterialModifierTagKey))
                    variant.Tags[MaterialModifierTagKey] = MaterialModifierTagValue;
            }
            if (existing.Version < VariantListVersion) existing.Version = VariantListVersion;
            applied.Add($"tagged {TypeTagKey}={TypeTagValue} on every variant with a model");
        }
        else
        {
            // A single-model item is wrapped, because the flag signature lives on a variant.
            var root = item.EntityModel;
            item.EntityModel = new NPlugItem_SVariantList
            {
                Version = VariantListVersion,
                Variants = [new NPlugItem_SVariant { Tags = CreateFlagTags(), EntityModel = root }],
            };
            applied.Add($"wrapped {root.GetType().Name} in a {TypeTagValue}-tagged variant");
        }

        ApplyPlacement(item, normalizePlacement, applied, notes);

        // The report is re-measured instead of assumed: an item that could not receive the flag's
        // placement stays visibly short of the flag shape (with the reason in Notes).
        var measured = Inspect(item);
        return new FlagReport(measured.IsFlag, measured.Deviations,
            applied, [.. notes, .. measured.Notes]);
    }

    /// <summary>The tag pair the vanilla flag items carry on their model variant.</summary>
    public static Dictionary<string, string> CreateFlagTags() => new()
    {
        [TypeTagKey] = TypeTagValue,
        [MaterialModifierTagKey] = MaterialModifierTagValue,
    };

    /// <summary>
    /// Refuse a conversion that would have to invent data, before anything is written, so a
    /// rejected conversion leaves the item exactly as it was.
    /// </summary>
    private static void EnsurePlacementIsEditable(CGameItemModel item)
    {
        if (item.DefaultPlacementFile is not null && item.DefaultPlacement is null)
            throw new NotSupportedException(
                $"The item's placement parameters live in the external file '{item.DefaultPlacementFile.FilePath}'; "
                + "its placement class cannot be edited from here. Import the placement parameters into the item first.");

        // The placement class is serialized by 2E020005. An opaque payload there is data we cannot
        // rewrite, so it is refused instead of being cleared or replaced.
        if (item.DefaultPlacement?.Chunks.Get(0x2E020005) is { } chunk
            && chunk is not CGameItemPlacementParam.Chunk2E020005 { Data: null })
            throw new NotSupportedException("The placement-class chunk is opaque or retains raw data; no edit was applied.");
    }

    private static void ApplyPlacement(CGameItemModel item, bool normalizePlacement,
        List<string> applied, List<string> notes)
    {
        var placement = item.DefaultPlacement;
        if (placement is null)
        {
            // Not fatal: the item is still classified as a flag, but it will not get the flag's
            // upright, world-aligned placement. Say so instead of inventing a placement parameter.
            notes.Add("The item has no placement parameters, so the flag's upright, world-aligned placement "
                + "could not be applied. Add a placement parameter and convert again.");
            return;
        }

        if (normalizePlacement)
        {
            placement.Flags = PlacementFlags;
            placement.CubeSize = PlacementCubeSize;
            placement.PivotSnapDistance = PlacementPivotSnapDistance;
            placement.GridSnapHStep = PlacementSnapStep;
            placement.GridSnapVStep = PlacementSnapStep;
            placement.GridSnapHOffset = PlacementSnapOffset;
            placement.GridSnapVOffset = PlacementSnapOffset;
            placement.FlyVStep = PlacementFlyVStep;
            placement.FlyVOffset = PlacementFlyVOffset;
            applied.Add("placement flags, cube, snapping and fly steps set to the measured flag values");
        }

        placement.CreateChunk<CGameItemPlacementParam.Chunk2E020005>();
        placement.PlacementClass = CreatePlacementClass();
        placement.PlacementClassFile = null;
        applied.Add($"placement class set to AlwaysUp + AlignToWorldDir with {CreatePatchLayouts().Length} patch layouts");
    }

    /// <summary>The flag's placements class exactly as the vanilla place-param stores it.</summary>
    public static NPlugItemPlacement_SClass CreatePlacementClass() => new()
    {
        Version = PlacementClassVersion,
        SizeGroup = PlacementSizeGroup,
        AlwaysUp = true,
        AlignToInterior = false,
        AlignToWorldDir = true,
        WorldDir = PlacementWorldDir,
        PatchLayouts = CreatePatchLayouts().Select(layout => new NPlugItemPlacement_SClass.PatchLayout
        {
            ItemCount = layout.ItemCount,
            ItemSpacing = layout.ItemSpacing,
            FillAlign = layout.FillAlign,
            FillDir = layout.FillDir,
            NormedPos = layout.NormedPos,
            U01 = layout.U01,
            OnlyOnGroups = [.. layout.OnlyOnGroups],
            Altitude = layout.Altitude,
            U02 = layout.U02,
        }).ToArray(),
    };

    private static void InspectPlacement(CGameItemModel item, List<Deviation> deviations, List<string> notes)
    {
        if (item.DefaultPlacementFile is not null && item.DefaultPlacement is null)
        {
            notes.Add($"The placement parameters are external ('{item.DefaultPlacementFile.FilePath}') and were not evaluated.");
            return;
        }
        var placement = item.DefaultPlacement;
        if (placement is null)
        {
            deviations.Add(new("DefaultPlacement", "the flag place-param", "absent"));
            return;
        }

        if (placement.Flags != PlacementFlags)
            deviations.Add(new("Placement.Flags", PlacementFlags.ToString(), placement.Flags.ToString()));
        if (placement.PivotSnapDistance != PlacementPivotSnapDistance)
            deviations.Add(new("Placement.PivotSnapDistance", PlacementPivotSnapDistance.ToString("R"), placement.PivotSnapDistance.ToString("R")));
        if (placement.FlyVStep != PlacementFlyVStep)
            deviations.Add(new("Placement.FlyVStep", PlacementFlyVStep.ToString("R"), placement.FlyVStep.ToString("R")));

        var placementClass = placement.PlacementClass;
        if (placementClass is null)
        {
            if (placement.PlacementClassFile is not null)
                notes.Add($"The placement class is external ('{placement.PlacementClassFile.FilePath}') and was not evaluated.");
            else
                deviations.Add(new("PlacementClass", "the flag placement class", "absent"));
            return;
        }

        if (placementClass.AlwaysUp != true)
            deviations.Add(new("PlacementClass.AlwaysUp", "true", placementClass.AlwaysUp.ToString()));
        if (placementClass.AlignToWorldDir != true)
            deviations.Add(new("PlacementClass.AlignToWorldDir", "true", placementClass.AlignToWorldDir.ToString()));
        if (placementClass.AlignToInterior != false)
            deviations.Add(new("PlacementClass.AlignToInterior", "false", placementClass.AlignToInterior.ToString()));
        if (placementClass.Version != PlacementClassVersion)
            deviations.Add(new("PlacementClass.Version", PlacementClassVersion.ToString(), placementClass.Version.ToString()));

        var expected = CreatePatchLayouts();
        var actual = placementClass.PatchLayouts ?? [];
        if (actual.Length != expected.Length)
            deviations.Add(new("PlacementClass.PatchLayouts", $"{expected.Length} entries", $"{actual.Length} entries"));
        else
        {
            for (var i = 0; i < expected.Length; i++)
            {
                var stored = actual[i];
                var groups = stored.OnlyOnGroups is null ? "" : string.Join("|", stored.OnlyOnGroups);
                var wanted = string.Join("|", expected[i].OnlyOnGroups);
                if (stored.ItemSpacing != expected[i].ItemSpacing || stored.NormedPos != expected[i].NormedPos
                    || stored.ItemCount != expected[i].ItemCount || groups != wanted)
                {
                    deviations.Add(new($"PlacementClass.PatchLayouts[{i}]",
                        $"count {expected[i].ItemCount}, spacing {expected[i].ItemSpacing:R}, pos {expected[i].NormedPos:R}, groups {wanted}",
                        $"count {stored.ItemCount}, spacing {stored.ItemSpacing:R}, pos {stored.NormedPos:R}, groups {groups}"));
                }
            }
        }
    }
}
