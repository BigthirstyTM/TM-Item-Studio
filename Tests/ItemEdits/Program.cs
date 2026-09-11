using System.Security.Cryptography;
using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.MetaNotPersistent;
using GBX.NET.Engines.MwFoundations;
using GBX.NET.Engines.Plug;
using GBX.NET.Serialization.Chunking;
using TM_Item_Studio.Models;

Gbx.LZO = new GBX.NET.LZO.MiniLZO();
var checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
void Reject(Action action) { try { action(); } catch (ArgumentException) { checks++; return; } catch (NotSupportedException) { checks++; return; } throw new Exception("Expected refusal"); }
byte[] Save<T>(T source) where T : CMwNod, new()
{
    using var stream = new MemoryStream();
    new Gbx<T>(source).Save(stream);
    return stream.ToArray();
}
T Reopen<T>(T source) where T : CMwNod, new() => Gbx.Parse<T>(new MemoryStream(Save(source))).Node;
void Unchanged<T>(T source, Action rejected) where T : CMwNod, new()
{
    var before = Save(source); Reject(rejected); Check(before.SequenceEqual(Save(source)), "Rejected edit changed serialized bytes");
}

var placement = new CGameItemPlacementParam { Flags = 0x51, CubeSize = 42, PivotSnapDistance = 17 };
placement.CreateChunk<CGameItemPlacementParam.Chunk2E020000>();
placement.Chunks.Add(new SkippableChunk(0x2E020099) { Data = [13, 24, 35, 46] });
Check(ItemEdits.ReadPivots(placement).Length == 0 && placement.PivotPositions is null, "Reading empty placement invented pivots");
Check(Reopen(placement).PivotPositions is null, "Unedited empty placement changed representation");
ItemEdits.AddPivot(placement, new(Vec3.Zero, Quat.Identity));
ItemEdits.AddPivot(placement, new(new(1, 2, 3), new(0, 0, 1, 0)));
ItemEdits.AddPivot(placement, new(new(4, 5, 6), new(1, 0, 0, 0)));
ItemEdits.RemovePivot(placement, 1);
Check(placement.PivotPositions!.Length == 2 && placement.PivotRotations![1] == new Quat(1, 0, 0, 0), "Middle deletion lost pairing");
ItemEdits.EditPivot(placement, 1, new(new(7, 8, 9), new(0, 1, 0, 0)));
var reopened = Reopen(placement);
Check(reopened.PivotPositions![1] == new Vec3(7, 8, 9) && reopened.PivotRotations![1] == new Quat(0, 1, 0, 0), "Paired edit did not round-trip");
Check(reopened.Flags == 0x51 && reopened.CubeSize == 42 && reopened.PivotSnapDistance == 17, "Unrelated placement fields changed");
Check(reopened.Chunks.Get(0x2E020099) is SkippableChunk { Data: [13, 24, 35, 46] }, "Opaque unrelated chunk changed");
Unchanged(placement, () => ItemEdits.AddPivot(placement, new(Vec3.Zero, Quat.Zero)));
Unchanged(placement, () => ItemEdits.EditPivot(placement, 0, new(new(float.NaN, 0, 0), Quat.Identity)));
Unchanged(placement, () => ItemEdits.EditPivot(placement, 0, new(Vec3.Zero, new(0, 0, 0, 2))));
Unchanged(placement, () => ItemEdits.RemovePivot(placement, 2));
ItemEdits.RemovePivot(placement, 0); ItemEdits.RemovePivot(placement, 0);
reopened = Reopen(placement);
Check(reopened.PivotPositions!.Length == 0 && reopened.PivotRotations!.Length == 0, "Last pivot removal must persist zero pairs");
placement.PivotPositions = [new(1, 2, 3), new(4, 5, 6)]; placement.PivotRotations = null;
var missing = placement.PivotRotations;
ItemEdits.EditPivotPosition(placement, 0, Vec3.Zero);
Check(placement.PivotRotations == missing && Reopen(placement).PivotPositions![0] == Vec3.Zero, "Position edit repaired missing rotations");
Unchanged(placement, () => ItemEdits.AddPivot(placement, new(Vec3.Zero, Quat.Identity)));
placement.PivotRotations = [new(0, 0, 1, 0)];
var rotations = placement.PivotRotations;
ItemEdits.EditPivotPosition(placement, 1, new(8, 0, 0));
Check(ReferenceEquals(rotations, placement.PivotRotations) && Reopen(placement).PivotRotations!.Length == 1, "Position-only edit changed mismatched rotations");
Unchanged(placement, () => ItemEdits.RemovePivot(placement, 1));
Unchanged(placement, () => ItemEdits.EditPivotPosition(placement, 0, new(float.PositiveInfinity, 0, 0)));
var opaquePlacement = new CGameItemPlacementParam { PivotPositions = [Vec3.Zero], PivotRotations = [Quat.Identity] };
opaquePlacement.Chunks.Add(new SkippableChunk(0x2E020001) { Data = [0, 0, 0, 0, 0, 0, 0, 0] });
Unchanged(opaquePlacement, () => ItemEdits.EditPivotPosition(opaquePlacement, 0, new(1, 2, 3)));
Unchanged(opaquePlacement, () => ItemEdits.AddPivot(opaquePlacement, new(Vec3.Zero, Quat.Identity)));

var light = new CPlugLightUserModel { Color = new(2, 3, 4), Intensity = 4, Distance = 12, PointEmissionRadius = 8, NightOnly = true };
var lightChunk = light.CreateChunk<CPlugLightUserModel.Chunk090F9000>(); lightChunk.Version = 1; lightChunk.U01 = 123;
var entry = new CPlugPrefab.EntRef { Model = light, Position = new(1, 2, 3), Rotation = Quat.Identity, U01 = "owner metadata" };
var otherEntry = new CPlugPrefab.EntRef { Model = light, Position = new(4, 5, 6), Rotation = Quat.Identity };
var prefab = new CPlugPrefab { Ents = [entry, otherEntry], U01 = 13, Url = "synthetic" };
ItemEdits.EditLight(light, Vec3.Zero, 0, 0);
ItemEdits.EditOwnerPosition(prefab, entry, Vec3.Zero);
var savedPrefab = Reopen(prefab);
var savedLight = (CPlugLightUserModel)savedPrefab.Ents[0].Model!;
Check(savedLight.Color == Vec3.Zero && savedLight.Intensity == 0 && savedLight.Distance == 0, "Zero light values lost on reopen");
Check(savedLight.NightOnly && savedLight.PointEmissionRadius == 8 && savedLight.Chunks.Get<CPlugLightUserModel.Chunk090F9000>()!.U01 == 123, "Unedited light fields changed");
Check(ReferenceEquals(savedLight, savedPrefab.Ents[1].Model), "Shared light model was cloned");
Check(savedPrefab.Ents[0].Position == Vec3.Zero && savedPrefab.Ents[1].Position == new Vec3(4, 5, 6), "Explicit owner movement affected sibling owner");
Check(savedPrefab.Ents[0].U01 == "owner metadata" && savedPrefab.U01 == 13, "Owner metadata changed");
var repeated = new CPlugPrefab { Ents = [new() { Model = prefab, Rotation = Quat.Identity }, new() { Model = prefab, Rotation = new(0, 0, 1, 0), Position = new(10, 0, 0) }] };
ItemEdits.EditOwnerPosition(prefab, entry, new(9, 8, 7));
var repeatedSaved = Reopen(repeated);
Check(ReferenceEquals(repeatedSaved.Ents[0].Model, repeatedSaved.Ents[1].Model) && ((CPlugPrefab)repeatedSaved.Ents[1].Model!).Ents[0].Position == new Vec3(9, 8, 7), "Repeated prefab source edit lost shared scope");
ItemEdits.EditLight(light, new(2, 3, 4), 5, 6);
Check(Reopen(light).Color == new Vec3(2, 3, 4), "HDR input was clipped");
ItemEdits.EditLight(light, Vec3.Zero, 0, 0);
Unchanged(prefab, () => ItemEdits.EditLight(light, new(1, 1, 1), 12, float.NaN));
Unchanged(prefab, () => ItemEdits.EditLight(light, new(-1, 1, 1), 12, 3));
Unchanged(prefab, () => ItemEdits.EditLight(light, new(1, 1, 1), -1, 3));
Unchanged(prefab, () => ItemEdits.EditOwnerPosition(prefab, new(), Vec3.Zero));
Unchanged(prefab, () => ItemEdits.EditOwnerPosition(prefab, entry, new(0, float.NegativeInfinity, 0)));
Reject(() => ItemEdits.EditLight(new(), Vec3.Zero, 0, 0));

var transform = new CPlugSolid2Model.Light { U04 = "synthetic", U05 = Iso4.Identity with { XX = 2, YY = 3 }, U06 = 7 };
var solid = new CPlugSolid2Model { Lights = [transform], LightUserModels = [light, light], LightInsts = [new() { ModelIndex = 0, SocketIndex = 5 }] };
solid.CreateChunk<CPlugSolid2Model.Chunk090BB000>().Version = 10;
ItemEdits.EditLightTransform(solid, transform, new(4, 5, 6));
var savedSolid = Reopen(solid);
Check(savedSolid.Lights![0].U05 == transform.U05 && savedSolid.Lights[0].U05.XX == 2 && savedSolid.Lights[0].U06 == 7, "Light transform round-trip lost matrix/unknown fields");
Check(savedSolid.LightInsts![0].SocketIndex == 5 && savedSolid.LightInsts[0].ModelIndex == 0, "Light instance linkage changed");
Check(ReferenceEquals(savedSolid.LightUserModels![0], savedSolid.LightUserModels[1]), "Shared Solid2 light source lost");
Unchanged(solid, () => ItemEdits.EditLightTransform(solid, new(), Vec3.Zero));
transform.U05 = Iso4.Zero;
Unchanged(solid, () => ItemEdits.EditLightTransform(solid, transform, Vec3.Zero));

var variant = new NPlugItem_SVariant { EntityModel = prefab, HiddenInManualCycle = true, Tags = new() { ["weather"] = "wet", ["surface"] = "road" } };
var sibling = new NPlugItem_SVariant { EntityModel = prefab, Tags = new() { ["keep"] = "yes" } };
var variants = new NPlugItem_SVariantList { Version = 1, Variants = [variant, sibling] };
ItemEdits.EditVariant(variants, variant, new Dictionary<string, string?> { ["weather"] = "dry", ["new"] = "" }, false);
var savedVariants = Reopen(variants);
Check(savedVariants.Variants![0].Tags["weather"] == "dry" && savedVariants.Variants[0].Tags["surface"] == "road" && savedVariants.Variants[0].Tags["new"] == "", "Tag patch lost unrelated/empty tags");
Check(!savedVariants.Variants[0].HiddenInManualCycle && savedVariants.Variants[1].Tags["keep"] == "yes", "Variant metadata changed incorrectly");
Check(ReferenceEquals(savedVariants.Variants[0].EntityModel, savedVariants.Variants[1].EntityModel), "Variant shared topology changed");
Unchanged(variants, () => ItemEdits.EditVariant(variants, variant, new Dictionary<string, string?> { ["weather"] = "bad", [""] = "bad" }, true));
Unchanged(variants, () => ItemEdits.EditVariant(variants, new(), new Dictionary<string, string?>()));
ItemEdits.EditVariant(variants, variant, new Dictionary<string, string?> { ["new"] = null }, true);
Check(!Reopen(variants).Variants![0].Tags.ContainsKey("new") && variant.HiddenInManualCycle, "Tag removal/hidden update failed");
variants.Version = 0;
Unchanged(variants, () => ItemEdits.EditVariant(variants, variant, new Dictionary<string, string?> { ["weather"] = "bad" }, false));
ItemEdits.EditVariant(variants, variant, new Dictionary<string, string?> { ["weather"] = "version-zero" });
Check(Reopen(variants).Version == 0 && Reopen(variants).Variants![0].Tags["weather"] == "version-zero", "Tag edit upgraded version-zero list");

// Metadata edits do not resolve or replace external entity references.
var refTable = new GbxRefTable();
var external = new GbxRefTableFile(refTable, 0, true, "Includes/Unresolved.Gbx");
variant.EntityModelFile = external;
ItemEdits.EditVariant(variants, variant, new Dictionary<string, string?> { ["external"] = "kept" });
Check(ReferenceEquals(variant.EntityModelFile, external), "External reference replaced");
variants.Version = 1;
var item = new CGameItemModel { EntityModel = variants, Name = "synthetic edits", ItemType = CGameItemModel.EItemType.Ornament, DefaultPlacement = placement };
item.CreateChunk<CGameItemModel.HeaderChunk2E002000>();
item.CreateChunk<CGameItemModel.Chunk2E002019>().Version = 15;
item.CreateChunk<CGameCtnCollector.Chunk2E00100C>();
item.CreateChunk<CGameItemModel.Chunk2E00201C>().Version = 5;
using var documentBytes = new MemoryStream();
new ReferencedDocument(item, refTable).Save(documentBytes);
documentBytes.Position = 0;
var savedItem = Gbx.Parse<CGameItemModel>(documentBytes).Node;
var externalVariant = ((NPlugItem_SVariantList)savedItem.EntityModel!).Variants![0];
Check(savedItem.Name == "synthetic edits" && savedItem.DefaultPlacement!.PivotPositions![1] == new Vec3(8, 0, 0), "Full item did not retain edited placement/metadata");
Check(externalVariant.EntityModelFile?.FilePath.Replace('\\', '/') == "Includes/Unresolved.Gbx" && externalVariant.Tags["external"] == "kept" && externalVariant.HiddenInManualCycle, "External variant metadata/reference did not round-trip");
Console.WriteLine($"PASS: {checks} edit checks, including binary save/reparse and rejected-write byte equality.");
Console.WriteLine($"Bundled GBX.NET SHA256: {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Gbx).Assembly.Location))).ToLowerInvariant()}");

sealed class ReferencedDocument : Gbx<CGameItemModel>
{
    public ReferencedDocument(CGameItemModel item, GbxRefTable references) : base(item) { RefTable = references; }
}
