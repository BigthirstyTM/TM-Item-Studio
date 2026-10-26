using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;
using GBX.NET;
using TM_Item_Studio.Models;

Gbx.LZO = new GBX.NET.LZO.MiniLZO();
if (args.Length > 1 && args[0] == "--inspect")
{
    var reports = args.Skip(1).Select(path =>
    {
        var bytes = File.ReadAllBytes(path);
        return new
        {
            path = Path.GetFullPath(path), bytes = bytes.Length,
            sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)),
            inspection = FxEmitterTransform.Read(bytes)
        };
    }).ToArray();
    Console.WriteLine(JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
    return;
}
var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var sourcePath = @"C:\Users\PC\Documents\Trackmania\Items\ChatGPT\PackedOnlyV2\TestItem_Sparkler_BitmapRefs_v3.Item.Gbx";
var foggerPath = @"C:\Users\PC\Documents\Trackmania\Items\ShadocAlex Items\FogVolumetric2\FogVolumetric1_FogEmissionRate_0.Item.Gbx";
var checks = new List<string>();
var source = File.ReadAllBytes(sourcePath);
var inspection = FxEmitterTransform.Read(source);
Check(inspection.OwnerRecordOffset == 6197 && inspection.Emitters.Count == 2,
    "Original Sparkler owner is the actual second prefab record immediately after the validated FX node");
Check(Near(inspection.Primary, new(0, .69592285f, 0))
    && Near(inspection.Emitters[1], new(0, 10.695923f, 0)),
    "Sparkler FX-local Z maps to item-space height, including the owner's 0.69592285 metre translation");
Check(inspection.Native.Primary == new FxEmissionPoint.Coordinates(0, 0, 0)
    && inspection.Native.Emitters[1].Position == new FxEmissionPoint.Coordinates(0, 0, 10),
    "Native local offsets remain distinct from the item-space display coordinates");

var angle = MathF.Sqrt(.5f);
var rotated = new FxEmitterTransform.OwnerTransform(new(2, 3, 4), new(-angle, 0, 0, angle));
Check(Near(FxEmitterTransform.LocalToItem(rotated, new(0, 0, 5)), new(2, 8, 4))
    && Near(FxEmitterTransform.LocalToItem(rotated, new(0, 5, 0)), new(2, 3, -1)),
    "Analytic quaternion rotation uses the game's local-Z-to-item-Y convention");
Check(Near(FxEmitterTransform.ItemToLocal(rotated, new(2, 8, 4)), new(0, 0, 5)),
    "Inverse conversion subtracts translation and applies the normalized rotation transpose");
var signFlipped = rotated with { Rotation = new(angle, 0, 0, -angle) };
Check(Near(FxEmitterTransform.LocalToItem(signFlipped, new(1, 2, 3)),
    FxEmitterTransform.LocalToItem(rotated, new(1, 2, 3))), "Quaternion sign does not change the point");
var nearUnit = rotated with { Rotation = new(-angle * 1.0002f, 0, 0, angle * 1.0002f) };
Check(Near(FxEmitterTransform.LocalToItem(nearUnit, new(0, 0, 5)), new(2, 8, 4)),
    "Native float quaternion error is normalized before conversion");
var random = new Random(314159);
for (var index = 0; index < 64; index++)
{
    var q = Quaternion.CreateFromYawPitchRoll((float)(random.NextDouble() * 6),
        (float)(random.NextDouble() * 6), (float)(random.NextDouble() * 6));
    var owner = new FxEmitterTransform.OwnerTransform(new(3, -2, 8), new(q.X, q.Y, q.Z, q.W));
    var local = new FxEmissionPoint.Coordinates((float)(random.NextDouble() * 40 - 20),
        (float)(random.NextDouble() * 40 - 20), (float)(random.NextDouble() * 40 - 20));
    if (!Near(FxEmitterTransform.ItemToLocal(owner, FxEmitterTransform.LocalToItem(owner, local)), local))
        throw new InvalidDataException("FAILED: seeded quaternion inverse round-trip " + index);
}
checks.Add("64 seeded rotations round-trip analytically without axis swaps or translation drift");

foreach (var mode in new[] { 'C', 'U' })
{
    var bytes = Rebody(source, Body(source), mode);
    var before = FxEmitterTransform.Read(bytes);
    Check(FxEmitterTransform.Export(bytes, before.Primary).ItemBytes.AsSpan().SequenceEqual(bytes),
        $"{mode}: unchanged item-space point is a byte-exact no-op");
    var target = new FxEmissionPoint.Coordinates(0, 3, 0);
    var result = FxEmitterTransform.Export(bytes, target);
    Check(Near(result.After.Primary, target)
        && Near(result.After.Native.Primary, new(0, 0, 2.3040771f)),
        $"{mode}: item-space mouth at Y=3 writes FX-local Z around 2.304077 metres");
    Check(Near(Difference(result.After.Emitters[1], result.After.Primary),
        Difference(before.Emitters[1], before.Primary)),
        $"{mode}: both emitters retain their owner-space and item-space relative spacing");
    Check(result.Proof.HeaderAndRefTablePreserved && result.Proof.OriginalBodyRestorableExactly
        && result.Proof.CompressionModeRetained && result.NativeOffsetWritten,
        $"{mode}: native writer's exact inverse-splice and container evidence is retained");
    Check(result.Before.Owner == result.After.Owner,
        $"{mode}: export preserves the actual prefab rotation and translation");
    var second = FxEmitterTransform.Export(result.ItemBytes, new(1.25f, 4.5f, -.75f));
    Check(Near(second.After.Primary, new(1.25f, 4.5f, -.75f)),
        $"{mode}: a second item-space edit works after variable-length expression offsets move");
}

var fogger = File.ReadAllBytes(foggerPath);
var fog = FxEmitterTransform.Read(fogger);
Check(fog.Owner.Rotation == new FxEmitterTransform.Rotation(0, 0, 0, 1)
    && fog.Owner.Position == new FxEmissionPoint.Coordinates(0, 0, 0),
    "The real Fogger identity owner is recovered from its own prefab record");
var fogResult = FxEmitterTransform.Export(fogger, new(1, 2, 3));
Check(fogResult.After.Primary == new FxEmissionPoint.Coordinates(1, 2, 3)
    && fogResult.After.Native.Primary == fogResult.After.Primary && fogResult.After.Emitters.Count == 1,
    "Identity-owner Fogger converts item-space and local coordinates without rotation");

var raw = Body(source);
var zeroQuaternion = raw.ToArray(); zeroQuaternion.AsSpan(inspection.OwnerRecordOffset, 16).Clear();
Reject(Rebody(source, zeroQuaternion, 'U'), "Zero-length owner quaternion is rejected");
var nonfiniteOwner = raw.ToArray();
BinaryPrimitives.WriteSingleLittleEndian(nonfiniteOwner.AsSpan(inspection.OwnerRecordOffset + 20), float.NaN);
Reject(Rebody(source, nonfiniteOwner, 'C'), "Nonfinite actual prefab position is rejected");
var scaledOwner = raw.ToArray();
BinaryPrimitives.WriteSingleLittleEndian(scaledOwner.AsSpan(inspection.OwnerRecordOffset + 12), 2);
Reject(Rebody(source, scaledOwner, 'U'), "A quaternion outside native unit tolerance is rejected");
var extraEntity = raw.ToArray(); BinaryPrimitives.WriteInt32LittleEndian(extraEntity.AsSpan(334), 3);
Reject(Rebody(source, extraEntity, 'C'), "A graph outside the exact two-entry prefab shape is rejected");
var wrongFirstModel = raw.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(wrongFirstModel.AsSpan(346), 0x0915C000);
Reject(Rebody(source, wrongFirstModel, 'U'), "An unsupported first prefab entity cannot lend a false owner record");
var localOffset = inspection.Native.Emitters[0].LengthOffset;
var worldOffset = localOffset + 4 + BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(localOffset));
Reject(Rebody(source, ReplaceExpression(raw, worldOffset, "float3(0,1,0)"), 'C'),
    "Nonzero world offsets cannot be presented as a physical item-space emission point");
Reject(Rebody(source, ReplaceExpression(raw, worldOffset, "float3(Time)"), 'U'),
    "Dynamic world offsets cannot be presented as a static item-space point");
Reject(source[..^1], "Truncated item data fails owner inspection");

ExpectInvalid(() => FxEmitterTransform.ItemToLocal(rotated, new(float.PositiveInfinity, 0, 0)),
    "Nonfinite requested item coordinates are rejected");
ExpectInvalid(() => FxEmitterTransform.LocalToItem(rotated with { Rotation = new(0, 0, 0, 0) }, new(0, 0, 0)),
    "Pure conversion cannot silently replace an invalid quaternion with identity");
ExpectInvalid(() => FxEmitterTransform.Export(source, new(0, 10000, 0)),
    "Translation that would exceed a secondary emitter's coordinate range is rejected");

var embeddedPath = Path.Combine(repo, "artifacts/fx-clones/fx-sequential-bitmap-20261025/TMIS_Sparkler_SequentialPixelsLookbackFixed.Item.Gbx");
if (File.Exists(embeddedPath))
{
    var pixels = File.ReadAllBytes(embeddedPath);
    var pixelInspection = FxEmitterTransform.Read(pixels);
    Check(pixelInspection.Owner == inspection.Owner && pixelInspection.OwnerRecordOffset > 8_000_000,
        "Owner validation works after large inline bitmap payloads without hard-coded body offsets");
    var edited = FxEmitterTransform.Export(pixels, new(0, 3, 0));
    Check(Near(edited.After.Primary, new(0, 3, 0)) && edited.Proof.OriginalBodyRestorableExactly,
        "Full-pixel item's item-space edit keeps all unrelated opaque image and FX bytes restorable");
}
Console.WriteLine(JsonSerializer.Serialize(new { passed = checks.Count, checks }, new JsonSerializerOptions { WriteIndented = true }));

void Check(bool condition, string message)
{
    if (!condition) throw new InvalidDataException("FAILED: " + message);
    checks.Add(message);
}
void Reject(byte[] bytes, string message) => Check(!FxEmitterTransform.TryRead(bytes, out var result, out var error)
    && result is null && !string.IsNullOrWhiteSpace(error), message);
void ExpectInvalid(Action action, string message)
{
    var rejected = false;
    try { action(); } catch (InvalidDataException) { rejected = true; }
    Check(rejected, message);
}
bool Near(FxEmissionPoint.Coordinates a, FxEmissionPoint.Coordinates b)
    => Math.Abs(a.X - b.X) < .00003 && Math.Abs(a.Y - b.Y) < .00003 && Math.Abs(a.Z - b.Z) < .00003;
FxEmissionPoint.Coordinates Difference(FxEmissionPoint.Coordinates a, FxEmissionPoint.Coordinates b)
    => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
byte[] Body(byte[] bytes)
{
    using var stream = new MemoryStream(bytes); Gbx.ParseHeader(stream);
    if (bytes[7] == 'U') return bytes[checked((int)stream.Position)..];
    var offset = checked((int)stream.Position - 8);
    var body = new byte[BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset))];
    Gbx.LZO!.Decompress(bytes[(offset + 8)..], body); return body;
}
byte[] Rebody(byte[] bytes, byte[] body, char mode)
{
    using var stream = new MemoryStream(bytes); Gbx.ParseHeader(stream);
    var count = checked((int)stream.Position - (bytes[7] == 'C' ? 8 : 0));
    var prefix = bytes[..count]; prefix[7] = (byte)mode;
    using var output = new MemoryStream(); output.Write(prefix);
    if (mode == 'C')
    {
        var compressed = Gbx.LZO!.Compress(body);
        output.Write(BitConverter.GetBytes(body.Length)); output.Write(BitConverter.GetBytes(compressed.Length)); output.Write(compressed);
    }
    else output.Write(body);
    return output.ToArray();
}
byte[] ReplaceExpression(byte[] bytes, int offset, string expression)
{
    var length = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset));
    var replacement = System.Text.Encoding.ASCII.GetBytes(expression);
    return bytes[..offset].Concat(BitConverter.GetBytes(replacement.Length)).Concat(replacement)
        .Concat(bytes[(offset + 4 + length)..]).ToArray();
}
