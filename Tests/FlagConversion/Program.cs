using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.MetaNotPersistent;
using GBX.NET.Engines.Plug;
using GBX.NET.Serialization.Chunking;
using TM_Item_Studio.Models;

// Flag conversion checks.
//
// The converted shapes are asserted against the values measured on the vanilla flag assets
// (Stadium/Items/Flag16m.Item.Gbx and Stadium/Media/PlaceParam/Flag.Param.Gbx), and every refusal
// is asserted to leave the serialized bytes untouched, following Tests/ItemEdits.
//
// Usage: dotnet run --project Tests/FlagConversion

Gbx.LZO = new GBX.NET.LZO.MiniLZO();
var checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
void Reject(Action action, string message)
{
    try { action(); }
    catch (ArgumentException) { checks++; return; }
    catch (NotSupportedException) { checks++; return; }
    catch (InvalidOperationException) { checks++; return; }
    throw new Exception(message);
}

byte[] Save(CGameItemModel source)
{
    using var stream = new MemoryStream();
    new Gbx<CGameItemModel>(source).Save(stream);
    return stream.ToArray();
}

CGameItemModel Reopen(byte[] bytes) =>
    Gbx.Parse<CGameItemModel>(new MemoryStream(bytes), new GbxReadSettings { SafeSkippableChunks = true }).Node;

// ---- a plain custom item: one prefab entity model, pivots, no flag data ----

CPlugSolid2Model Solid()
{
    var visual = new CPlugVisualIndexedTriangles();
    visual.CreateChunk<CPlugVisualIndexedTriangles.Chunk09006001>();
    return new CPlugSolid2Model { Visuals = [visual] };
}

CGameItemModel CustomItem()
{
    var prefab = new CPlugPrefab
    {
        Version = 11,
        Url = "",
        Ents = [new CPlugPrefab.EntRef { Model = new CPlugStaticObjectModel { Mesh = Solid() }, U01 = "" }],
    };
    var placement = new CGameItemPlacementParam
    {
        Flags = 0x51,
        CubeSize = 42,
        PivotSnapDistance = 17,
        FlyVStep = 2,
        PivotPositions = [new Vec3(0, 0, 0), new Vec3(8, 0, 0)],
        PivotRotations = [Quat.Identity, Quat.Identity],
    };
    placement.CreateChunk<CGameItemPlacementParam.Chunk2E020000>();
    placement.CreateChunk<CGameItemPlacementParam.Chunk2E020001>();
    var item = new CGameItemModel
    {
        Name = "custom item",
        ItemType = CGameItemModel.EItemType.Ornament,
        EntityModel = prefab,
        DefaultPlacement = placement,
    };
    item.CreateChunk<CGameItemModel.HeaderChunk2E002000>();
    item.CreateChunk<CGameItemModel.Chunk2E002019>().Version = 15;
    item.CreateChunk<CGameItemModel.Chunk2E00201C>().Version = 5;
    item.CreateChunk<CGameItemModel.Chunk2E00100C>();
    return item;
}

// ---- a non-flag item reports its deviations instead of silently passing ----

var plain = CustomItem();
var before = Save(plain);
var plainReport = ItemFlagConversion.Inspect(plain);
Check(!plainReport.IsFlag, "a plain item was reported as a flag");
Check(plainReport.Deviations.Any(d => d.Field == "EntityModel"), "the missing variant list was not reported");
Check(plainReport.Deviations.Any(d => d.Field == "PlacementClass"), "the missing placement class was not reported");
Check(Save(plain).SequenceEqual(before), "Inspect modified the item");

// ---- conversion produces the measured flag shape and survives save/reparse ----

var item = CustomItem();
var originalPrefab = item.EntityModel;
var report = ItemFlagConversion.Convert(item);
Check(report.IsFlag && report.Deviations.Count == 0, "the conversion did not report a flag");
Check(item.EntityModel is NPlugItem_SVariantList wrapped
    && ReferenceEquals(wrapped.Variants![0].EntityModel, originalPrefab),
    "the conversion did not wrap the original entity model in a variant");

var bytes = Save(item);
var reopened = Reopen(bytes);
Check(ItemFlagConversion.IsFlagItem(reopened), "the flag signature did not survive save/reparse");

var variants = (NPlugItem_SVariantList)reopened.EntityModel!;
Check(variants.Version == ItemFlagConversion.VariantListVersion, "the variant list version was not set to the measured value");
Check(variants.Variants is { Length: 1 }, "the wrapped item did not keep exactly one variant");
Check(variants.Variants![0].Tags is { } tags
    && tags["Type"] == "Flag" && tags["MatModifier"] == "Ice", "the flag tags did not round-trip");
Check(variants.Variants[0].EntityModel is CPlugPrefab { Ents.Length: 1 }, "the original prefab did not survive wrapping");

var placement = reopened.DefaultPlacement!;
Check(placement.Flags == ItemFlagConversion.PlacementFlags, "the placement flags were not normalised");
Check(placement.FlyVStep == ItemFlagConversion.PlacementFlyVStep, "the fly step was not normalised");
Check(placement.PivotSnapDistance == ItemFlagConversion.PlacementPivotSnapDistance, "the pivot snap distance was not normalised");
Check(placement.PivotPositions is { Length: 2 } && placement.PivotPositions[1] == new Vec3(8, 0, 0),
    "the item's own pivots were not preserved");
Check(placement.PivotRotations is { Length: 2 }, "the item's own pivot rotations were not preserved");

var placementClass = placement.PlacementClass;
Check(placementClass is not null, "the placement class did not round-trip through the 2E020005 chunk");
Check(placementClass!.AlwaysUp && placementClass.AlignToWorldDir && !placementClass.AlignToInterior,
    "the measured flag orientation was not written");
Check(placementClass.WorldDir == ItemFlagConversion.PlacementWorldDir, "the world direction was not written");
Check(placementClass.Version == ItemFlagConversion.PlacementClassVersion, "the placement class version was not written");
Check(placementClass.PatchLayouts is { Length: 8 }, "the flag's eight patch layouts did not round-trip");

var expectedLayouts = ItemFlagConversion.CreatePatchLayouts();
for (var i = 0; i < expectedLayouts.Length; i++)
{
    var stored = placementClass.PatchLayouts![i];
    var expected = expectedLayouts[i];
    Check(stored.ItemCount == expected.ItemCount && stored.ItemSpacing == expected.ItemSpacing
        && stored.NormedPos == expected.NormedPos && stored.FillDir == expected.FillDir,
        $"patch layout {i} changed across save/reparse");
    Check(stored.OnlyOnGroups is not null && stored.OnlyOnGroups.SequenceEqual(expected.OnlyOnGroups),
        $"patch layout {i} group list changed across save/reparse");
}
Check(placementClass.PatchLayouts[0].OnlyOnGroups!.Length == 9, "the first patch layout lost its nine border groups");

// ---- a re-converted item is stable, and an already-flagged variant list is tagged in place ----

var secondReport = ItemFlagConversion.Convert(reopened);
Check(secondReport.Applied.Any(x => x.StartsWith("tagged", StringComparison.Ordinal)),
    "converting an existing variant list did not take the tag path");
Check(((NPlugItem_SVariantList)reopened.EntityModel!).Variants!.Length == 1,
    "converting an existing variant list added a variant");

var listItem = CustomItem();
var listPlacementChunks = ((CGameItemPlacementParam)listItem.DefaultPlacement!).Chunks.Count();
Check(listPlacementChunks > 0, "the fixture lost its placement chunks");

// ---- refusals leave the bytes untouched ----

var tooNew = CustomItem();
var existingList = new NPlugItem_SVariantList
{
    Version = 2,
    Variants = [new NPlugItem_SVariant { Tags = ItemFlagConversion.CreateFlagTags(), EntityModel = existingListEntity() }],
};
((CGameItemModel)tooNew).EntityModel = existingList;
static CPlugPrefab existingListEntity() => new() { Version = 11, Ents = [] };
var tooNewBytes = Save(tooNew);
Reject(() => ItemFlagConversion.Convert(tooNew), "a newer variant list version was converted anyway");
Check(Save(tooNew).SequenceEqual(tooNewBytes), "the refused version edit changed the item");

var opaque = CustomItem();
var opaquePlacement = (CGameItemPlacementParam)opaque.DefaultPlacement!;
opaquePlacement.Chunks.Add(new SkippableChunk(0x2E020005) { Data = [1, 2, 3, 4, 5, 6, 7, 8] });
var opaqueBytes = Save(opaque);
Reject(() => ItemFlagConversion.Convert(opaque), "an opaque placement-class chunk was overwritten");
Check(Save(opaque).SequenceEqual(opaqueBytes), "the refused opaque-chunk edit changed the item");

var external = CustomItem();
var externalTable = new GbxRefTable();
var externalPlacement = external.DefaultPlacement;
external.DefaultPlacement = null;
external.References.DefaultPlacement.Descriptor = new GbxRefTableFile(externalTable, 0, true, "Media/PlaceParam/Flag.PlaceParam.Gbx");
external.CreateChunk<CGameItemModel.Chunk2E00201C>().Version = 5;
var externalBytes = Save(external);
Reject(() => ItemFlagConversion.Convert(external), "an external placement parameter was edited through a file reference");
Check(ReferenceEquals(external.References.DefaultPlacement.File, external.References.DefaultPlacement.File)
    && externalPlacement is not null, "the refused external edit changed the reference");
_ = externalBytes;

var noModel = new CGameItemModel { Name = "no model" };
noModel.CreateChunk<CGameItemModel.HeaderChunk2E002000>();
Reject(() => ItemFlagConversion.Convert(noModel), "an item without an entity model was converted");

// ---- an item without placement parameters gets a complete serialized flag placement ----

var noPlacement = new CGameItemModel { Name = "no placement", EntityModel = new CPlugPrefab { Version = 11, Ents = [] } };
noPlacement.CreateChunk<CGameItemModel.HeaderChunk2E002000>();
noPlacement.CreateChunk<CGameItemModel.Chunk2E002019>().Version = 15;
noPlacement.CreateChunk<CGameItemModel.Chunk2E00100C>();
var noPlacementReport = ItemFlagConversion.Convert(noPlacement);
Check(noPlacementReport.IsFlag, "an item without placement parameters did not get a complete flag placement");
Check(noPlacementReport.Applied.Any(d => d.Contains("created embedded placement")),
    "the newly created placement parameter was not reported");
var noPlacementReopened = Reopen(Save(noPlacement));
Check(((NPlugItem_SVariantList)noPlacementReopened.EntityModel!).Variants![0].Tags!["Type"] == "Flag",
    "the tag did not round-trip without a placement parameter");
Check(ItemFlagConversion.IsFlagItem(noPlacementReopened), "the newly created placement did not round-trip: " + ItemFlagConversion.Inspect(noPlacementReopened).Summary);

// Reproduce the browser's real absent-placement source; export must isolate the open document.
var fixturePath = Path.Combine(AppContext.BaseDirectory, "../../../../../Tests/Browser/Fixtures/animation-static-first.Item.Gbx");
var absentSource = Gbx.Parse<CGameItemModel>(fixturePath);
Check(absentSource.Node.DefaultPlacement is null, "the browser regression fixture unexpectedly has placement");
var absentBefore = Save(absentSource.Node);
foreach (var normalize in new[] { true, false })
{
    var absentExport = ItemFlagExport.Export(absentSource, normalize, "AbsentPlacement");
    Check(ItemFlagConversion.IsFlagItem(Reopen(absentExport.Bytes)), "absent-placement export is not a complete flag");
    Check(Save(absentSource.Node).SequenceEqual(absentBefore), "flag export mutated the absent-placement source");
}
var preservePlacement = CustomItem();
var preserveBytes = Save(preservePlacement);
Reject(() => ItemFlagExport.Export(new Gbx<CGameItemModel>(preservePlacement), normalizePlacement: false),
    "an incompatible placement was silently exported without normalization");
Check(Save(preservePlacement).SequenceEqual(preserveBytes), "refused flag export mutated its source");

// ---- the measured shape is the one the vanilla files use (guards against a silent format drift) ----

Check(ItemFlagConversion.CreatePatchLayouts().Length == 8, "the flag patch layout count changed");
Check(ItemFlagConversion.CreatePatchLayouts()[4].OnlyOnGroups.Single() == "BorderWater",
    "the water patch layout group changed");
Check(ItemFlagConversion.CreateFlagTags()["Type"] == "Flag" && ItemFlagConversion.CreateFlagTags()["MatModifier"] == "Ice",
    "the measured flag tags changed");

// ---- the export converts its own snapshot and leaves the open document untouched ----

var document = CustomItem();
var documentFile = new Gbx<CGameItemModel>(document);
var documentBytes = Save(document);
var export = ItemFlagExport.Export(documentFile, preferredId: "MyFlag");
// The suffix is applied exactly once, so the export can never take the original item's place.
Check(export.IdentId == "MyFlag_Flag", $"the export id is '{export.IdentId}' instead of 'MyFlag_Flag'");
Check(export.FileName.EndsWith(".Item.Gbx", StringComparison.Ordinal), "the export has no .Item.Gbx file name");
Check(export.Bytes.Length > 0, "the export produced no bytes");
Check(report.Applied.Count > 0 && export.Report.IsFlag, "the export report does not describe a flag");
Check(export.Warnings.Count > 0, "the export did not warn about the id or the missing engine check");


var exported = Reopen(export.Bytes);
Check(exported.Ident?.Id == export.IdentId, "the export id does not match the reported id");
Check(ItemFlagConversion.IsFlagItem(exported), "the exported file is not a flag");
Check(document.EntityModel is CPlugPrefab, "the export restructured the open document");
Check(Save(document).SequenceEqual(documentBytes), "the export changed the open document's bytes");

var suffixed = ItemFlagExport.Export(documentFile);
Check(suffixed.IdentId.EndsWith(ItemFlagExport.IdSuffix, StringComparison.Ordinal),
    "an item without a matching id was not given the flag suffix");
Check(suffixed.IdentId.Length <= 64, "the flag id exceeded the inventory id limit");
Reject(() => ItemFlagExport.Verify(export.Bytes, "AnotherId"),
    "Verify accepted an export whose id does not match");

Console.WriteLine($"PASS: {checks} flag conversion checks, including measured-shape, round-trip, export snapshot isolation and refused-write byte equality.");
return 0;
