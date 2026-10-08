using System.Numerics;
using GBX.NET;
using GBX.NET.Engines.Game;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.MetaNotPersistent;
using GBX.NET.Engines.MwFoundations;
using GBX.NET.Engines.Plug;
using TM_Item_Studio.Models;

Gbx.LZO = new GBX.NET.LZO.MiniLZO();

var failed = 0;
var passed = 0;

void Check(string name, Action body)
{
    try
    {
        body();
        passed++;
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"FAIL {name}: {ex.Message}");
    }
}

void Require(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static void Near(float actual, float expected, float tolerance, string label)
{
    if (Math.Abs(actual - expected) > tolerance)
        throw new Exception($"{label}: {actual} != {expected} (±{tolerance})");
}

var fileArgs = args.Where(arg => !arg.StartsWith("--", StringComparison.Ordinal)).ToArray();
var itemPath = fileArgs.Length > 0 ? fileArgs[0] : Path.Combine("Test Exported items", "CustomItem_Static.Item.Gbx");
var itemGbx = Gbx.Parse<CGameItemModel>(itemPath);
var item = itemGbx.Node;
// Same normalization the editor runs before an item export, so a fixture without an
// Ident.Id gets the id the app would have written.
ItemExportValidator.EnsureGameReady(item, "StudioFixtureItem");
var itemId = item.Ident?.Id;
Require(!string.IsNullOrWhiteSpace(itemId), "Fixture item has no Ident.Id; instancing cannot reference it.");

Console.WriteLine($"Item: {itemId} ({new FileInfo(itemPath).Length} bytes)");

Check("macroblock exports with one instance and reparses", () =>
{
    var instances = new[] { new ItemMacroblockInstance { Label = "Copy 1", PosX = 8, PosY = 0, PosZ = -16, YawDeg = 90 } };
    var export = ItemMacroblock.Export(item, instances, "StudioTest_Macroblock");
    Require(export.InstanceCount == 1, "Export reported the wrong instance count.");
    Require(export.FileName == "StudioTest_Macroblock.Macroblock.Gbx", $"Unexpected file name {export.FileName}.");
    Require(export.Bytes.Length > 0, "Export produced no bytes.");
});

Check("twelve instances keep every placement and the shared item reference", () =>
{
    var instances = ItemMacroblock.Grid(12, columns: 4, spacingX: 8f, spacingZ: 8f, yawDeg: 45f).ToList();
    instances[7].PosX += 1.5f;
    instances[7].PosZ -= 0.25f;
    instances[7].Scale = 2f;
    var export = ItemMacroblock.Export(item, instances, null);
    var node = Gbx.Parse<CGameCtnMacroBlockInfo>(new MemoryStream(export.Bytes)).Node!;

    Require(node.ObjectSpawns.Count == 12, $"Reparsed {node.ObjectSpawns.Count} spawns instead of 12.");
    for (var i = 0; i < instances.Count; i++)
    {
        var spawn = node.ObjectSpawns[i];
        Require(string.Equals(spawn.ItemModel?.Id, itemId, StringComparison.OrdinalIgnoreCase),
            $"Spawn {i} references {spawn.ItemModel?.Id} instead of {itemId}.");
        Near(spawn.AbsolutePositionInMap.X, instances[i].PosX, 1e-3f, $"spawn {i} X");
        Near(spawn.AbsolutePositionInMap.Z, instances[i].PosZ, 1e-3f, $"spawn {i} Z");
        Near(spawn.PitchYawRoll.Y, instances[i].YawDeg * MathF.PI / 180f, 1e-4f, $"spawn {i} yaw");
    }

    Near(node.ObjectSpawns[7].Scale, 2f, 1e-4f, "scaled spawn");
    Require(node.Name != null && node.Name.EndsWith("_Macroblock"), "Macroblock name was not stored.");
    Require(node.Ident?.Id == node.Name, "Macroblock ident was not stored.");
});

Check("duplicating instances does not duplicate item data (instancing stays cheap)", () =>
{
    var one = ItemMacroblock.Save(item, ItemMacroblock.Grid(1, 1, 8, 8), "SizeTest").LongLength;
    var many = ItemMacroblock.Save(item, ItemMacroblock.Grid(24, 6, 8, 8), "SizeTest").LongLength;
    var itemBytes = new FileInfo(itemPath).Length;

    Require(many < itemBytes, $"Macroblock ({many} bytes) is not smaller than the item ({itemBytes} bytes).");
    var perInstance = (many - one) / 23.0;
    Require(perInstance < 256, $"Each extra instance costs {perInstance:0.0} bytes; instancing should stay tiny.");
    Console.WriteLine($"  item={itemBytes}B macroblock(1)={one}B macroblock(24)={many}B perInstance≈{perInstance:0.0}B");
});

Check("macroblock is written as class id 0x0310D000", () =>
{
    var bytes = ItemMacroblock.Save(item, ItemMacroblock.Grid(2, 2, 8, 8), "ClassIdTest");
    var classId = Gbx.ParseClassId(new MemoryStream(bytes));
    Require(classId == 0x0310D000, $"Class id 0x{classId:X8} is not the macroblock class.");
});

Check("a macroblock loads back into editable instances and re-exports", () =>
{
    var instances = ItemMacroblock.Grid(4, columns: 2, spacingX: 6f, spacingZ: 6f).ToList();
    instances[1].YawDeg = 30f;
    instances[2].ItemId = itemId;
    var bytes = ItemMacroblock.Save(item, instances, "RoundTrip");

    Require(ItemMacroblock.IsMacroblock(bytes), "Saved bytes were not detected as a macroblock.");
    Require(!ItemMacroblock.IsMacroblock(File.ReadAllBytes(itemPath)), "An item file was not detected as an item.");

    var node = Gbx.Parse<CGameCtnMacroBlockInfo>(new MemoryStream(bytes)).Node!;
    var loaded = ItemMacroblock.ReadInstances(node);
    Require(loaded.Count == 4, $"Loaded {loaded.Count} instances instead of 4.");
    Near(loaded[1].YawDeg, 30f, 0.01f, "loaded yaw degrees");
    Near(loaded[2].PosX, instances[2].PosX, 1e-3f, "loaded X");
    Require(loaded[2].ItemId == itemId, "Loaded instance lost its item reference.");

    var again = ItemMacroblock.Save(item, loaded, "RoundTrip");
    var reopened = Gbx.Parse<CGameCtnMacroBlockInfo>(new MemoryStream(again)).Node!;
    Require(reopened.ObjectSpawns.Count == 4, "Re-export lost instances.");
    Require(reopened.ObjectSpawns[2].ItemModel?.Id == itemId, "Re-export changed the item reference.");
    Near(reopened.ObjectSpawns[1].PitchYawRoll.Y, 30f * MathF.PI / 180f, 1e-4f, "re-exported yaw");
});

Check("export refuses an instance that references a different item", () =>
{
    var instances = ItemMacroblock.Grid(1, 1, 8, 8).ToList();
    instances[0].ItemId = "SomeOtherItem";
    try
    {
        ItemMacroblock.Save(item, instances, "Foreign");
        throw new Exception("Export accepted a foreign item reference.");
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("SomeOtherItem"))
    {
        // expected
    }
});

Check("export refuses an item without an Ident.Id", () =>
{
    var identityLess = Gbx.Parse<CGameItemModel>(itemPath).Node!;
    identityLess.Ident = null;
    try
    {
        ItemMacroblock.Save(identityLess, ItemMacroblock.Grid(1, 1, 8, 8), "NoIdent");
        throw new Exception("Export accepted an item without an ident.");
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("Ident.Id"))
    {
        // expected
    }
});

Check("export refuses an empty instance list", () =>
{
    try
    {
        ItemMacroblock.Save(item, Array.Empty<ItemMacroblockInstance>(), "Empty");
        throw new Exception("Export accepted zero instances.");
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("at least one instance"))
    {
        // expected
    }
});

Check("a reparsed macroblock can be saved again without losing spawns", () =>
{
    var bytes = ItemMacroblock.Save(item, ItemMacroblock.Grid(5, 5, 8, 8), "ReSave");
    var node = Gbx.Parse<CGameCtnMacroBlockInfo>(new MemoryStream(bytes)).Node!;
    using var second = new MemoryStream();
    new Gbx<CGameCtnMacroBlockInfo>(node).Save(second);
    var reopened = Gbx.Parse<CGameCtnMacroBlockInfo>(new MemoryStream(second.ToArray())).Node!;
    Require(reopened.ObjectSpawns.Count == 5, $"Re-save kept {reopened.ObjectSpawns.Count} spawns instead of 5.");
    Require(reopened.Name == node.Name, "Re-save lost the macroblock name.");
});

Check("a macroblock that reaches the item parser is rescued by the macroblock fallback", () =>
{
    var bytes = ItemMacroblock.Save(item, ItemMacroblock.Grid(2, 2, 8, 8), "Dispatch");

    // This is what LoadFiles does first, and its InvalidCastException is the reported
    // "Specified cast is not valid." error when a macroblock reaches the item parser.
    string itemParserFailure;
    try
    {
        Gbx.Parse<CGameItemModel>(new MemoryStream(bytes));
        itemParserFailure = "no exception";
    }
    catch (Exception ex)
    {
        itemParserFailure = $"{ex.GetType().Name}: {ex.Message}";
    }
    Console.WriteLine($"  macroblock -> item parser: {itemParserFailure}");
    Require(itemParserFailure.StartsWith("InvalidCastException", StringComparison.Ordinal),
        "Parsing a macroblock as an item no longer fails; the fallback would be untested.");

    // The rescue path LoadFiles takes when that happens.
    Require(ItemMacroblock.TryRead(bytes, out var node, out var macroError),
        "TryRead did not recognise the macroblock after the item parser refused it.");
    Require(node?.ObjectSpawns is { Count: 2 }, "The rescued macroblock lost its instances.");
    Require(macroError is null, $"Unexpected macroblock parse error: {macroError}");

    // An item is not a macroblock: the fallback must decline it without inventing an error.
    Require(!ItemMacroblock.TryRead(File.ReadAllBytes(itemPath), out var itemNode, out var itemReadError),
        "TryRead claimed an item file is a macroblock.");
    Require(itemNode is null && itemReadError is null,
        $"TryRead reported '{itemReadError}' for a non-macroblock file.");

    // A truncated file is neither a macroblock nor a crash.
    Require(!ItemMacroblock.TryRead(new byte[] { 1, 2, 3 }, out _, out var shortError),
        "TryRead accepted a truncated file.");
    Require(shortError is not null, "TryRead gave no reason for rejecting a truncated file.");
});

Check("placement matrix puts a copy where its spawn is written", () =>
{
    var instance = new ItemMacroblockInstance
    {
        Label = "Copy 1", PosX = 8, PosY = 3.5f, PosZ = -16, YawDeg = 90, Scale = 2f
    };
    var placed = ItemMacroblock.Placement(instance);

    Near(placed.M41, instance.PosX, 1e-4f, "placement translation X");
    Near(placed.M42, instance.PosY, 1e-4f, "placement translation Y");
    Near(placed.M43, instance.PosZ, 1e-4f, "placement translation Z");

    // Uniform scale, no shear: every axis of the transform has the same length.
    float AxisLength(float a, float b, float c) => MathF.Sqrt(a * a + b * b + c * c);
    Near(AxisLength(placed.M11, placed.M21, placed.M31), 2f, 1e-3f, "scaled X axis");
    Near(AxisLength(placed.M12, placed.M22, placed.M32), 2f, 1e-3f, "scaled Y axis");
    Near(AxisLength(placed.M13, placed.M23, placed.M33), 2f, 1e-3f, "scaled Z axis");
    Near(placed.M14 + placed.M24 + placed.M34, 0f, 1e-5f, "affine row stays zero");

    // The rotation and position handed to the viewport come from the same values the
    // export writes into the spawn.
    var spawn = Gbx.Parse<CGameCtnMacroBlockInfo>(
        new MemoryStream(ItemMacroblock.Save(item, new[] { instance }, "Placement"))).Node!.ObjectSpawns[0];
    Near(spawn.PitchYawRoll.Y, instance.YawDeg * MathF.PI / 180f, 1e-4f, "spawn yaw equals placement yaw");
    Near(spawn.AbsolutePositionInMap.Y, instance.PosY, 1e-3f, "spawn Y equals placement translation");
    Near(spawn.Scale, instance.Scale, 1e-4f, "spawn scale equals placement scale");
});

Check("instance composition keeps the item's own placement and moves it to the copy", () =>
{
    // Home.razor builds a copy's worldTransform as geometryWorld * Placement(instance):
    // the part keeps its place inside the item, then the whole copy is placed.
    var geometryWorld = Matrix4x4.CreateTranslation(-4, 0, 2);
    var instance = new ItemMacroblockInstance { PosX = 8, PosY = 1, PosZ = -16, YawDeg = 0, Scale = 1 };
    var composed = geometryWorld * ItemMacroblock.Placement(instance);

    var local = new Vector3(1, 2, 3);
    var expected = Vector3.Transform(Vector3.Transform(local, geometryWorld), ItemMacroblock.Placement(instance));
    var actual = Vector3.Transform(local, composed);
    Near(actual.X, expected.X, 1e-4f, "composed X");
    Near(actual.Y, expected.Y, 1e-4f, "composed Y");
    Near(actual.Z, expected.Z, 1e-4f, "composed Z");

    // Order matters once a copy is rotated: the item is placed first, the copy second.
    var yawed = geometryWorld * ItemMacroblock.Placement(new ItemMacroblockInstance
    {
        PosX = 8, PosY = 1, PosZ = -16, YawDeg = 90, Scale = 1
    });
    var backwards = ItemMacroblock.Placement(new ItemMacroblockInstance
    {
        PosX = 8, PosY = 1, PosZ = -16, YawDeg = 90, Scale = 1
    }) * geometryWorld;
    Require((Vector3.Transform(local, yawed) - Vector3.Transform(local, backwards)).Length() > 1e-3f,
        "Composition order is not observable; the test cannot protect it.");

    // The item's own origin lands on the copy's placement at any yaw, so a rotated copy spins
    // about its own origin instead of orbiting the world.
    foreach (var yaw in new[] { 0f, 90f, -37.5f })
    {
        var origin = Vector3.Transform(Vector3.Zero, ItemMacroblock.Placement(new ItemMacroblockInstance
        {
            PosX = 8, PosY = 1, PosZ = -16, YawDeg = yaw
        }));
        Near(origin.X, 8, 1e-3f, $"yaw {yaw} origin X");
        Near(origin.Y, 1, 1e-3f, $"yaw {yaw} origin Y");
        Near(origin.Z, -16, 1e-3f, $"yaw {yaw} origin Z");
    }
});

byte[] SaveDocument(Gbx<CGameItemModel> file)
{
    using var stream = new MemoryStream();
    file.Save(stream);
    return stream.ToArray();
}

NPlugItem_SVariantList? MultiVariantList(CGameItemModel node)
    => node.EntityModel as NPlugItem_SVariantList;

CPlugPrefab? MultiRoot(CGameItemModel node)
    => MultiVariantList(node) is { Variants.Length: > 0 } list
        ? list.Variants[0].EntityModel as CPlugPrefab
        : node.EntityModel as CPlugPrefab;

int EntriesPerCopy(CGameItemModel node)
{
    var root = MultiVariantList(node) is { Variants.Length: > 0 } list
        ? list.Variants[0].EntityModel
        : node.EntityModel;
    return root is CPlugPrefab { Ents.Length: > 0 } prefab ? prefab.Ents.Length : 1;
}

Check("multi-instance item embeds one mesh for twenty copies", () =>
{
    var fresh = Gbx.Parse<CGameItemModel>(itemPath);
    var before = SaveDocument(fresh);
    var entriesPerCopy = EntriesPerCopy(fresh.Node);
    var instances = ItemMacroblock.Grid(20, columns: 5, spacingX: 8f, spacingZ: 8f).ToList();

    var export = ItemMultiInstance.Export(fresh, 1, instances, itemId);

    Require(SaveDocument(fresh).SequenceEqual(before), "The export modified the document it was handed.");
    Require(export.InstanceCount == 20, $"Exported {export.InstanceCount} copies instead of 20.");
    Require(export.EntryCount == 20 * entriesPerCopy,
        $"Exported {export.EntryCount} entries instead of {20 * entriesPerCopy}.");
    Require(export.ItemId == ItemMultiInstance.SuffixedId(itemId, 20), $"Unexpected id {export.ItemId}.");
    Require(export.FileName == export.ItemId + ".Item.Gbx", $"Unexpected file name {export.FileName}.");
    Require(export.Warnings.Count == 0, $"Unexpected warnings: {string.Join(" ", export.Warnings)}");

    var reparsed = Gbx.Parse<CGameItemModel>(new MemoryStream(export.Bytes)).Node!;
    var root = MultiRoot(reparsed) ?? throw new Exception("The export root is not a prefab.");
    Require(root.Ents.Length == 20 * entriesPerCopy, $"Reparsed {root.Ents.Length} entries.");
    Require(reparsed.Ident?.Id == export.ItemId, $"Reparsed id was '{reparsed.Ident?.Id}'.");

    // Copies are placed relative to the root: entry offsets between copies equal the placed
    // offsets (the grid has no rotation, so nothing is rotated in between).
    for (var copy = 1; copy < 5; copy++)
    {
        var moved = root.Ents[copy * entriesPerCopy].Position;
        var origin = root.Ents[0].Position;
        Near(moved.X - origin.X, instances[copy].PosX - instances[0].PosX, 1e-3f, $"copy {copy} relative X");
        Near(moved.Y - origin.Y, instances[copy].PosY - instances[0].PosY, 1e-3f, $"copy {copy} relative Y");
        Near(moved.Z - origin.Z, instances[copy].PosZ - instances[0].PosZ, 1e-3f, $"copy {copy} relative Z");
    }
});

Check("twenty copies cost a transform each, not another mesh", () =>
{
    var oneItem = Gbx.Parse<CGameItemModel>(itemPath);
    var one = ItemMultiInstance.Export(oneItem, 1, ItemMacroblock.Grid(1, 1, 8, 8), itemId);
    var manyItem = Gbx.Parse<CGameItemModel>(itemPath);
    var many = ItemMultiInstance.Export(manyItem, 1,
        ItemMacroblock.Grid(20, columns: 5, spacingX: 8f, spacingZ: 8f), itemId);

    var itemBytes = new FileInfo(itemPath).Length;
    var perCopy = (many.Bytes.LongLength - one.Bytes.LongLength) / 19.0;
    Console.WriteLine($"  item={itemBytes}B multi(1)={one.Bytes.Length}B multi(20)={many.Bytes.Length}B perCopy≈{perCopy:0.0}B");
    Require(perCopy < 1024, $"Each extra copy costs {perCopy:0.0} bytes; a transform should cost far less than a mesh.");
    Require(many.Bytes.LongLength < itemBytes * 2,
        $"Twenty copies ({many.Bytes.Length} B) are not below twice the item ({itemBytes * 2} B).");
});

Check("copies of another item are skipped and the skip is reported", () =>
{
    var instances = ItemMacroblock.Grid(4, columns: 2, spacingX: 8f, spacingZ: 8f).ToList();
    instances[1].ItemId = "SomeOtherItem";
    instances[3].ItemId = "SomeOtherItem";
    instances[0].Scale = 2f;

    var mixed = Gbx.Parse<CGameItemModel>(itemPath);
    var export = ItemMultiInstance.Export(mixed, 1, instances, itemId);
    Require(export.InstanceCount == 2, $"Exported {export.InstanceCount} copies instead of 2.");
    Require(export.Warnings.Count == 2,
        $"Expected a foreign-item and a scale warning, got: {string.Join(" | ", export.Warnings)}");
    Require(export.Warnings.Any(w => w.Contains("different item", StringComparison.Ordinal)),
        "The skipped copies were not reported.");
    Require(export.Warnings.Any(w => w.Contains("scale", StringComparison.Ordinal)),
        "The dropped scale was not reported.");

    var foreignOnly = Gbx.Parse<CGameItemModel>(itemPath);
    try
    {
        ItemMultiInstance.Export(foreignOnly, 1,
            new[] { new ItemMacroblockInstance { ItemId = "SomeOtherItem" } }, itemId);
        throw new Exception("Export accepted copies that all belong to another item.");
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("nothing to embed", StringComparison.Ordinal))
    {
        // expected
    }
});

Check("a prefab item keeps one shared model per source entry across all copies", () =>
{
    var kinematicPath = Path.Combine("Test Exported items", "CustomItem_Kinematic.Item.Gbx");
    var source = Gbx.Parse<CGameItemModel>(kinematicPath);
    var entriesPerCopy = EntriesPerCopy(source.Node);
    var instances = ItemMacroblock.Grid(6, columns: 3, spacingX: 4f, spacingZ: 4f).ToList();
    // Each copy turns another quarter: copy 1 differs from copy 0 by 90 degrees.
    for (var i = 0; i < instances.Count; i++)
        instances[i].YawDeg = 90f * i;

    var export = ItemMultiInstance.Export(source, 1, instances, "StudioKinematicItem");
    var reparsed = Gbx.Parse<CGameItemModel>(new MemoryStream(export.Bytes)).Node!;
    var root = MultiRoot(reparsed) ?? throw new Exception("The export root is not a prefab.");
    Require(root.Ents.Length == 6 * entriesPerCopy,
        $"Reparsed {root.Ents.Length} entries instead of {6 * entriesPerCopy}.");

    static Quaternion AsSystem(Quat value) => new(value.X, value.Y, value.Z, value.W);
    for (var copy = 1; copy < 6; copy++)
    {
        for (var entry = 0; entry < entriesPerCopy; entry++)
        {
            Require(ReferenceEquals(root.Ents[entry].Model, root.Ents[copy * entriesPerCopy + entry].Model),
                $"Copy {copy} entry {entry} carries its own model node.");
        }

        // Every copy is the same set of entries, turned by its own placed yaw.
        var first = AsSystem(root.Ents[0].Rotation);
        var moved = AsSystem(root.Ents[copy * entriesPerCopy].Rotation);
        var relative = Quaternion.Conjugate(first) * moved;
        var angle = 2f * MathF.Acos(MathF.Min(1f, MathF.Abs(relative.W))) * 180f / MathF.PI;
        var expected = (90f * copy) % 360f;
        if (expected > 180f) expected = 360f - expected;
        Near(angle, expected, 0.5f, $"yaw of copy {copy}");
    }

    Console.WriteLine($"  kinematic source {new FileInfo(kinematicPath).Length}B, {entriesPerCopy} entrie(s) per copy -> {export.EntryCount} entries, {export.Bytes.Length}B");
});

Console.WriteLine($"\n{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
