using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.MetaNotPersistent;
using GBX.NET.Engines.MwFoundations;
using GBX.NET.Engines.Plug;
using TM_Item_Studio.Models;

// All fixtures are constructed here; no game assets or private files are required.
Gbx.LZO = new GBX.NET.LZO.MiniLZO();
var passed = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    passed++;
}

Gbx<CGameItemModel> Document(CMwNod? root, string name = "Fixture")
{
    var item = new CGameItemModel { EntityModel = root, Name = name, ItemType = CGameItemModel.EItemType.Ornament };
    item.CreateChunk<CGameItemModel.HeaderChunk2E002000>();
    item.CreateChunk<CGameItemModel.Chunk2E002019>().Version = 15;
    item.CreateChunk<CGameCtnCollector.Chunk2E00100C>();
    return new Gbx<CGameItemModel>(item);
}

byte[] Save(Action<Stream> save)
{
    using var stream = new MemoryStream();
    save(stream);
    return stream.ToArray();
}

Gbx<CGameItemModel> Reopen(byte[] bytes) => Gbx.Parse<CGameItemModel>(new MemoryStream(bytes));
byte[] SaveDoc(Gbx<CGameItemModel> file) => Save(output => file.Save(output));
NPlugItem_SVariantList ListOf(params NPlugItem_SVariant[] variants) => new() { Version = 1, Variants = variants };
NPlugItem_SVariant Tagged(CMwNod? model) => new()
{
    EntityModel = model,
    Tags = new() { ["surface"] = "road", ["weather"] = "wet" },
    HiddenInManualCycle = true
};

// Selection is a view, and exporting that view preserves the complete file.
var original = Document(ListOf(Tagged(new CPlugPrefab()), new NPlugItem_SVariant { EntityModel = new CPlugStaticObjectModel() }));
var parsed = Reopen(SaveDoc(original));
var root = parsed.Node.EntityModel;
var sources = ItemVariantSource.FromFile("two", parsed);
Check(sources.Count == 2, "Both variants must be available");
Check(sources[1].PreviewRoot is CPlugStaticObjectModel, "Non-prefab preview must use its actual entity");
Check(ReferenceEquals(parsed.Node.EntityModel, root), "Selecting a preview must not replace the root");
var saved = Reopen(Save(sources[1].Save));
var savedList = (NPlugItem_SVariantList)saved.Node.EntityModel!;
Check(savedList.Version == 1 && savedList.Variants!.Length == 2, "Save must preserve list version/count");
Check(savedList.Variants![0].Tags["surface"] == "road" && savedList.Variants[0].Tags["weather"] == "wet", "Tags must round-trip");
Check(savedList.Variants[0].HiddenInManualCycle, "Hidden flag must round-trip");
Check(savedList.Variants[1].EntityModel is CPlugStaticObjectModel, "Non-prefab entity must round-trip");

var sharedModel = new CPlugPrefab();
var versionZero = new NPlugItem_SVariantList { Version = 0, Variants = [new() { EntityModel = sharedModel }, new() { EntityModel = sharedModel }] };
var zeroViews = ItemVariantSource.FromFile("version-zero", Reopen(SaveDoc(Document(versionZero))));
var zeroSaved = (NPlugItem_SVariantList)Reopen(Save(zeroViews[1].Save)).Node.EntityModel!;
Check(zeroSaved.Version == 0, "Ordinary export must not upgrade a version-zero list");
Check(ReferenceEquals(zeroSaved.Variants![0].EntityModel, zeroSaved.Variants[1].EntityModel), "Shared entity references must remain shared");

// Single, empty and null-reference variants retain their authored representation.
foreach (var list in new[] { ListOf(Tagged(new CPlugPrefab())), ListOf(), ListOf(Tagged(null)) })
{
    var doc = Reopen(SaveDoc(Document(list)));
    var views = ItemVariantSource.FromFile("boundary", doc);
    Check(views.Count == Math.Max(1, list.Variants!.Length), "Document must remain accessible at the zero/one boundary");
    var result = (NPlugItem_SVariantList)Reopen(Save(views[0].Save)).Node.EntityModel!;
    Check(result.Variants!.Length == list.Variants.Length, "Empty/single/null entry count must survive");
    if (list.Variants.Length > 0) Check(result.Variants[0].HiddenInManualCycle, "Single/null variant flags must survive");
}

// External references are retained even when the dependency isn't loaded locally.
var referenceTable = new GbxRefTable { AncestorLevel = 1 };
var external = Tagged(null);
external.EntityModelFile = new GbxRefTableFile(referenceTable, 0, true, "Includes/External.Prefab.Gbx");
var externalDoc = Reopen(SaveDoc(new ReferencedFixture(Document(ListOf(external)).Node, referenceTable)));
var externalViews = ItemVariantSource.FromFile("external", externalDoc);
Check(externalViews.Count == 1 && externalViews[0].PreviewRoot is null, "Unresolved reference must still be selectable");
var externalSaved = Reopen(Save(externalViews[0].Save));
var externalVariant = ((NPlugItem_SVariantList)externalSaved.Node.EntityModel!).Variants![0];
Check(externalVariant.EntityModelFile?.FilePath.Replace('\\', '/') == "Includes/External.Prefab.Gbx", "External path must survive export");
Check(externalVariant.HiddenInManualCycle && externalVariant.Tags["weather"] == "wet", "External variant metadata must survive");
Check(externalSaved.RefTable?.AncestorLevel == 1, "External reference base must survive export");

// Combining is explicit, preserves per-variant data, and uses the first document's metadata.
var second = Reopen(SaveDoc(Document(new CPlugStaticObjectModel(), "Second")));
var combinedSources = sources.Concat(ItemVariantSource.FromFile("second", second)).ToArray();
var combined = Reopen(Save(output => ItemVariantSource.SaveCombined(combinedSources, output)));
var combinedList = (NPlugItem_SVariantList)combined.Node.EntityModel!;
Check(combined.Node.Name == "Fixture", "First file must supply item metadata");
Check(combinedList.Version == 1 && combinedList.Variants!.Length == 3, "Combine must retain all three entries");
Check(combinedList.Variants![0].HiddenInManualCycle && combinedList.Variants[0].Tags["surface"] == "road", "Combine must retain flags and tags");
Check(combinedList.Variants[1].EntityModel is CPlugStaticObjectModel && combinedList.Variants[2].EntityModel is CPlugStaticObjectModel, "Combine must retain non-prefab entities and order");
Check(ReferenceEquals(parsed.Node.EntityModel, root), "Successful combine must restore source root");
Check(Reopen(Save(sources[0].Save)).Node.EntityModel is NPlugItem_SVariantList { Variants.Length: 2 }, "Normal export after combine must still save original file");

try
{
    using var failing = new FailingStream();
    ItemVariantSource.SaveCombined(combinedSources, failing);
    throw new Exception("Expected output failure");
}
catch (IOException)
{
    Check(ReferenceEquals(parsed.Node.EntityModel, root), "Failed output must restore source root");
}

try
{
    ItemVariantSource.SaveCombined(externalViews.Concat(ItemVariantSource.FromFile("second", second)).ToArray(), new MemoryStream());
    throw new Exception("Expected unresolved-dependency combine refusal");
}
catch (InvalidOperationException)
{
    Check(externalDoc.Node.EntityModel is NPlugItem_SVariantList, "Rejected combination must leave document intact");
}

foreach (var unsupported in new[] { Document(ListOf()), Document(null) })
{
    var unsupportedRoot = unsupported.Node.EntityModel;
    using var output = new MemoryStream();
    try
    {
        ItemVariantSource.SaveCombined(ItemVariantSource.FromFile("unsupported", unsupported).Concat(ItemVariantSource.FromFile("second", second)).ToArray(), output);
        throw new Exception("Expected unsupported combination refusal");
    }
    catch (InvalidOperationException)
    {
        Check(output.Length == 0 && ReferenceEquals(unsupportedRoot, unsupported.Node.EntityModel), "Refused input must not be omitted or partially written");
    }
}

if (args.Length == 2 && args[0] == "--fixtures")
{
    Directory.CreateDirectory(args[1]);
    File.WriteAllBytes(Path.Combine(args[1], "two.Item.Gbx"), SaveDoc(original));
    File.WriteAllBytes(Path.Combine(args[1], "single.Item.Gbx"), SaveDoc(Document(ListOf(Tagged(new CPlugPrefab())))));
    File.WriteAllBytes(Path.Combine(args[1], "second.Item.Gbx"), SaveDoc(second));
}
if (args.Length == 2 && args[0] == "--verify-browser")
{
    Gbx<CGameItemModel> Download(string name) => Reopen(File.ReadAllBytes(Path.Combine(args[1], name + ".Item.Gbx")));
    var preserved = (NPlugItem_SVariantList)Download("browser-preserved").Node.EntityModel!;
    Check(preserved.Variants!.Length == 2 && preserved.Version == 1, "Browser selection export must retain the whole list");
    Check(preserved.Variants[0].HiddenInManualCycle && preserved.Variants[0].Tags["surface"] == "road", "Browser export must retain first variant metadata after selecting second");
    Check(Download("browser-second").Node.EntityModel is CPlugStaticObjectModel, "Selected-file export must not implicitly combine");
    var combinedDownload = Download("browser-combined");
    var browserList = (NPlugItem_SVariantList)combinedDownload.Node.EntityModel!;
    Check(browserList.Variants!.Length == 3 && browserList.Variants[0].HiddenInManualCycle, "Browser explicit combine must retain all entries and flags");
    Check(combinedDownload.Node.Name == "Fixture", "Browser combine must use first file metadata even when second is selected");
    Check(Download("browser-single").Node.EntityModel is NPlugItem_SVariantList { Variants.Length: 1 }, "Browser one-entry export must retain variant wrapper");
}
Console.WriteLine($"PASS: {passed} variant preservation checks (parse/save/reparse and failed output).");

sealed class FailingStream : MemoryStream
{
    public override void Write(byte[] buffer, int offset, int count) => throw new IOException("Synthetic output failure");
    public override void Write(ReadOnlySpan<byte> buffer) => throw new IOException("Synthetic output failure");
    public override void WriteByte(byte value) => throw new IOException("Synthetic output failure");
}

sealed class ReferencedFixture : Gbx<CGameItemModel>
{
    public ReferencedFixture(CGameItemModel item, GbxRefTable references) : base(item) { RefTable = references; }
}
