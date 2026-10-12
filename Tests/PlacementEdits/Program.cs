using System.Buffers.Binary;
using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Serialization.Chunking;
using TM_Item_Studio.Models;

// Round-trip and refusal checks for the 2E020004 placement-alternative list, following the
// pattern of Tests/ItemEdits: every rejected edit must leave the serialized bytes untouched.

Gbx.LZO = new GBX.NET.LZO.MiniLZO();
var checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
void Reject(Action action, string message)
{
    try { action(); }
    catch (ArgumentException) { checks++; return; }
    catch (NotSupportedException) { checks++; return; }
    throw new Exception(message);
}

byte[] Save(CGameItemPlacementParam source)
{
    using var stream = new MemoryStream();
    new Gbx<CGameItemPlacementParam>(source).Save(stream);
    return stream.ToArray();
}

CGameItemPlacementParam Reopen(CGameItemPlacementParam source) =>
    Gbx.Parse<CGameItemPlacementParam>(new MemoryStream(Save(source)), new GbxReadSettings { SafeSkippableChunks = true }).Node;

// ---- an absent chunk reads as an empty list, and writing an empty list stores count 0 ----
var placement = new CGameItemPlacementParam { Flags = 0x51, CubeSize = 42, PivotSnapDistance = 17 };
placement.CreateChunk<CGameItemPlacementParam.Chunk2E020000>();
Check(ItemPlacementAlternatives.Read(placement).Length == 0, "absent chunk invented alternatives");

ItemPlacementAlternatives.Write(placement, []);
var empty = placement.Chunks.Get(ItemPlacementAlternatives.ChunkId) as ISkippableChunk;
Check(empty is SkippableChunk { Data: { Length: 8 } }, "empty list was not written as an 8-byte payload");
Check(empty!.Data!.All(b => b == 0), "empty list payload is not all zero bytes");
Check(ItemPlacementAlternatives.Read(placement).Length == 0, "empty list did not read back empty");

// ---- a real list survives save and reparse, and leaves unrelated chunks alone ----
var opaque = new SkippableChunk(0x2E020099) { Data = [13, 24, 35, 46] };
placement.Chunks.Add(opaque);
ItemPlacementAlternatives.Add(placement, new(new Vec3(8, 8, 0), new Vec3(90, 0, 0)));
ItemPlacementAlternatives.Add(placement, new(new Vec3(-8, 8, 0), new Vec3(-90, 0, 0)));
Check(((ISkippableChunk)placement.Chunks.Get(ItemPlacementAlternatives.ChunkId)!).Data!.Length == 8 + 2 * 24,
    "two alternatives did not encode as 8 + 2 x 24 bytes");

var reopened = Reopen(placement);
var alternatives = ItemPlacementAlternatives.Read(reopened);
Check(alternatives.Length == 2, "alternative count changed across save/reparse");
Check(alternatives[0].Position == new Vec3(8, 8, 0) && alternatives[0].RotationDegrees == new Vec3(90, 0, 0),
    "first alternative changed across save/reparse");
Check(alternatives[1].Position == new Vec3(-8, 8, 0) && alternatives[1].RotationDegrees == new Vec3(-90, 0, 0),
    "second alternative changed across save/reparse");
Check(reopened.Flags == 0x51 && reopened.CubeSize == 42 && reopened.PivotSnapDistance == 17,
    "unrelated placement fields changed");
Check(reopened.Chunks.Get(0x2E020099) is SkippableChunk { Data: [13, 24, 35, 46] }, "opaque unrelated chunk changed");

// ---- single-field edits keep the other field ----
ItemPlacementAlternatives.EditPosition(reopened, 0, new Vec3(1, 2, 3));
var moved = ItemPlacementAlternatives.Read(Reopen(reopened));
Check(moved[0].Position == new Vec3(1, 2, 3) && moved[0].RotationDegrees == new Vec3(90, 0, 0),
    "position edit changed the stored rotation");

ItemPlacementAlternatives.EditRotation(reopened, 1, new Vec3(180, -90, 0));
var rotated = ItemPlacementAlternatives.Read(Reopen(reopened));
Check(rotated[1].RotationDegrees == new Vec3(180, -90, 0) && rotated[1].Position == new Vec3(-8, 8, 0),
    "rotation edit changed the stored position");

ItemPlacementAlternatives.Edit(reopened, 0, new(new Vec3(4, 0, 0), new Vec3(0, 90, 0)));
Check(ItemPlacementAlternatives.Read(Reopen(reopened))[0] == new ItemPlacementAlternatives.Alternative(new Vec3(4, 0, 0), new Vec3(0, 90, 0)),
    "paired edit did not round-trip");

ItemPlacementAlternatives.Remove(reopened, 0);
var removed = ItemPlacementAlternatives.Read(Reopen(reopened));
Check(removed.Length == 1 && removed[0].RotationDegrees == new Vec3(180, -90, 0), "removal lost the remaining entry");

// ---- the version and count header is what the game reads ----
var payload = ((ISkippableChunk)reopened.Chunks.Get(ItemPlacementAlternatives.ChunkId)!).Data!;
Check(BinaryPrimitives.ReadInt32LittleEndian(payload) == 0, "encoded version is not 0");
Check(BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(4)) == 1, "encoded count does not match the list");

// ---- rejected edits leave the bytes untouched ----
var before = Save(reopened);
Reject(() => ItemPlacementAlternatives.Write(reopened, [new(new Vec3(float.NaN, 0, 0), Vec3.Zero)]),
    "non-finite position was accepted");
Reject(() => ItemPlacementAlternatives.Write(reopened, [new(Vec3.Zero, new Vec3(0, float.PositiveInfinity, 0))]),
    "non-finite rotation was accepted");
Reject(() => ItemPlacementAlternatives.Write(reopened,
    Enumerable.Repeat(new ItemPlacementAlternatives.Alternative(Vec3.Zero, Vec3.Zero), ItemPlacementAlternatives.MaxEntries + 1).ToArray()),
    "too many alternatives were accepted");
Reject(() => ItemPlacementAlternatives.Edit(reopened, 1, new(Vec3.Zero, Vec3.Zero)), "out-of-range index was accepted");
Reject(() => ItemPlacementAlternatives.Remove(reopened, -1), "negative index was accepted");
Reject(() => ItemPlacementAlternatives.EditPosition(reopened, 0, new Vec3(0, 0, float.NaN)), "non-finite position edit was accepted");
Check(before.SequenceEqual(Save(reopened)), "a rejected edit mutated the serialized bytes");

// ---- an unrecognised payload is never overwritten ----
foreach (var (data, reason) in new (byte[], string)[]
{
    ([0x00, 0x00, 0x00, 0x00], "truncated header"),
    (EncodeRaw(1, 0, []), "unknown version"),
    (EncodeRaw(0, -1, []), "negative count"),
    (EncodeRaw(0, 3, [new Vec3(1, 2, 3)]), "count larger than the payload"),
    (EncodeRaw(0, 0, [new Vec3(1, 2, 3)]), "payload larger than the count"),
})
{
    var fixture = new CGameItemPlacementParam();
    fixture.Chunks.Add(new SkippableChunk(ItemPlacementAlternatives.ChunkId) { Data = data });
    Reject(() => ItemPlacementAlternatives.Read(fixture), $"read accepted {reason}");
    Reject(() => ItemPlacementAlternatives.Write(fixture, [new(Vec3.Zero, Vec3.Zero)]), $"write overwrote {reason}");
    Check(ReferenceEquals(((ISkippableChunk)fixture.Chunks.Get(ItemPlacementAlternatives.ChunkId)!).Data, data), $"write replaced the payload of {reason}");
}

// ---- a chunk without a payload is reported instead of treated as empty ----
var payloadless = new CGameItemPlacementParam();
payloadless.Chunks.Add(new SkippableChunk(ItemPlacementAlternatives.ChunkId));
Reject(() => ItemPlacementAlternatives.Read(payloadless), "payload-less chunk read as empty");

// ---- the measured vanilla gate payload decodes as two alternatives ----
var gate = new CGameItemPlacementParam();
gate.Chunks.Add(new SkippableChunk(ItemPlacementAlternatives.ChunkId) { Data = EncodeRaw(0, 2, [new Vec3(8, 8, 0), new Vec3(-8, 8, 0)]) });
var gateAlternatives = ItemPlacementAlternatives.Read(gate);
Check(gateAlternatives.Length == 2 && gateAlternatives[0].Position == new Vec3(8, 8, 0),
    "a captured gate payload did not decode");

// ---- an item-level round trip must survive the parser settings the app uses ----
// The list lives in a chunk GBX.NET ignores, so it only survives a save when the payload was
// retained on read. This is the regression guard for the export path.
var itemPlacement = new CGameItemPlacementParam();
itemPlacement.CreateChunk<CGameItemPlacementParam.Chunk2E020000>();
ItemPlacementAlternatives.Write(itemPlacement,
    [new(new Vec3(8, 8, 0), new Vec3(90, 0, 0)), new(new Vec3(-8, 8, 0), new Vec3(-90, 0, 0))]);
var item = new CGameItemModel { Name = "alternatives", DefaultPlacement = itemPlacement };
item.CreateChunk<CGameItemModel.HeaderChunk2E002000>();
item.CreateChunk<CGameItemModel.Chunk2E002019>().Version = 15;
item.CreateChunk<CGameItemModel.Chunk2E00201C>().Version = 5;
using var itemBytes = new MemoryStream();
new Gbx<CGameItemModel>(item).Save(itemBytes);

foreach (var (safeChunks, label) in new (bool, string)[] { (false, "default parse settings"), (true, "SafeSkippableChunks") })
{
    itemBytes.Position = 0;
    if (!safeChunks)
    {
        // This mirrors what a plain Gbx.Parse does when SafeSkippableChunks is left at its default.
        var parsed = Gbx.Parse<CGameItemModel>(itemBytes, new GbxReadSettings()).Node;
        using var resaved = new MemoryStream();
        new Gbx<CGameItemModel>(parsed).Save(resaved);
        resaved.Position = 0;
        var reread = Gbx.Parse<CGameItemModel>(resaved, new GbxReadSettings { SafeSkippableChunks = true }).Node;
        Check(reread.DefaultPlacement is not null && ItemPlacementAlternatives.Read(reread.DefaultPlacement).Length == 2,
            $"a two-entry alternative list did not survive an item round trip through {label}");
        continue;
    }

    var kept = Gbx.Parse<CGameItemModel>(itemBytes, new GbxReadSettings { SafeSkippableChunks = true }).Node;
    using var rekept = new MemoryStream();
    new Gbx<CGameItemModel>(kept).Save(rekept);
    rekept.Position = 0;
    var rekeptRead = Gbx.Parse<CGameItemModel>(rekept, new GbxReadSettings { SafeSkippableChunks = true }).Node;
    Check(rekeptRead.DefaultPlacement is not null && ItemPlacementAlternatives.Read(rekeptRead.DefaultPlacement).Length == 2,
        $"a two-entry alternative list did not survive an item round trip through {label}");
}

Console.WriteLine($"PASS: {checks} placement-alternative checks, including save/reparse and refused edits.");

static byte[] EncodeRaw(int version, int count, Vec3[] entries)
{
    var buffer = new byte[8 + entries.Length * 24];
    BinaryPrimitives.WriteInt32LittleEndian(buffer, version);
    BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4), count);
    for (var i = 0; i < entries.Length; i++)
    {
        var offset = 8 + i * 24;
        BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset), entries[i].X);
        BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset + 4), entries[i].Y);
        BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset + 8), entries[i].Z);
        BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset + 12), 0);
        BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset + 16), 0);
        BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset + 20), 0);
    }

    return buffer;
}
