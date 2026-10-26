using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.GameData;
using TM_Item_Studio.Models;

Gbx.LZO = new GBX.NET.LZO.MiniLZO();
if (args is ["inspect", var file])
{
    var item = Gbx.Parse<CGameItemModel>(file).Node;
    Console.WriteLine(JsonSerializer.Serialize(new { id = item.Ident?.Id, name = item.Name, raw = item.Icon is { } icon ? new[] { icon.GetLength(0), icon.GetLength(1) } : null,
        webp = item.IconWebP?.Length, fid = item.IconFid?.GetType().Name, rotation = item.IconQuarterRotationY }));
    return;
}
if (args.Length >= 3 && args[0] == "prepare")
{
    var gbx = Gbx.Parse<CGameItemModel>(args[1]);
    var item = gbx.Node;
    item.Icon = new Color[32, 16];
    for (var y = 0; y < 16; y++) for (var x = 0; x < 32; x++)
        item.Icon[x, y] = y < 8 ? x < 16 ? new(255, 0, 0, 255) : new(0, 255, 0, 255)
            : x < 16 ? new(0, 0, 255, 255) : new(255, 255, 0, 255);
    item.CreateChunk<CGameCtnCollector.HeaderChunk2E001004>();
    item.IconWebP = args.Length == 4 ? File.ReadAllBytes(args[3]) : null;
    item.IconQuarterRotationY = 1;
    gbx.Save(args[2]);
    return;
}
var raw = new Color[3, 2];
raw[2, 1] = new(11, 22, 33, 44);
var packed = ItemIcon.ReadRaw(raw);
Require(packed.Width == 3 && packed.Height == 2 && packed.Pixels.Skip(20).SequenceEqual(new[] { 11, 22, 33, 44 }), "Rectangular [x,y] packing");
var source = Gbx.Parse<CGameItemModel>("Tests/Browser/Fixtures/animation-static-first.Item.Gbx");
var before = source.Node.Icon;
try { ItemIcon.Replace(source.Node, [1], 3, 2, [1]); throw new Exception("Invalid replacement accepted"); }
catch (ArgumentException) { Require(ReferenceEquals(before, source.Node.Icon), "Invalid replacement changed source"); }
ItemIcon.Replace(source.Node, packed.Pixels, 3, 2, [1, 2, 3]);
using var stream = new MemoryStream();
source.Save(stream); stream.Position = 0;
Require(Gbx.Parse<CGameItemModel>(stream).Node.IconWebP!.SequenceEqual(new byte[] { 1, 2, 3 }), "Missing header replacement survives serialization");
source.Node.IconWebP = null;
using var rawStream = new MemoryStream(); source.Save(rawStream); rawStream.Position = 0;
var roundtrip = Gbx.Parse<CGameItemModel>(rawStream).Node.Icon!;
Require(ItemIcon.ReadRaw(roundtrip).Pixels.SequenceEqual(packed.Pixels), "Raw rectangular icon retains orientation on roundtrip");
Console.WriteLine("PASS: rectangular raw icons, atomic refusal, new icon header persistence, raw serialization orientation.");
static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
