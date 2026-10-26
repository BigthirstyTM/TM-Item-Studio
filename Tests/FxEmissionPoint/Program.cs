using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using GBX.NET;
using TM_Item_Studio.Models;

Gbx.LZO = new GBX.NET.LZO.MiniLZO();
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var sparklerPath = args.Length > 0 ? Path.GetFullPath(args[0])
    : @"C:\Users\PC\Documents\Trackmania\Items\ChatGPT\PackedOnlyV2\TestItem_Sparkler_BitmapRefs_v3.Item.Gbx";
var foggerPath = @"C:\Users\PC\Documents\Trackmania\Items\ShadocAlex Items\FogVolumetric2\FogVolumetric1_FogEmissionRate_0.Item.Gbx";
var checks = new List<string>();
var source = File.ReadAllBytes(sparklerPath);
var sparkler = FxEmissionPoint.Read(source);
Check(sparkler.Emitters.Count == 2 && sparkler.Primary == new FxEmissionPoint.Coordinates(0, 0, 0)
    && sparkler.Emitters[1].Position == new FxEmissionPoint.Coordinates(0, 0, 10), "Real Sparkler reads its two original native emitter offsets");
var target = new FxEmissionPoint.Coordinates(1.25f, 2.5f, -.75f);
foreach (var compression in new[] { 'C', 'U' })
{
    var input = Rebody(source, ReadBody(source), compression);
    var result = FxEmissionPoint.Export(input, target);
    Check(result.After.Primary == target && result.After.Emitters[1].Position == new FxEmissionPoint.Coordinates(1.25f, 2.5f, 9.25f),
        $"{compression}: requested XYZ and the secondary emitter's ten metre spacing survive reparse");
    Check(result.Proof.HeaderAndRefTablePreserved && result.Proof.OriginalBodyRestorableExactly
        && result.Proof.CompressionModeRetained && result.NativeOffsetWritten, $"{compression}: exact splice and container proofs pass");
    Check(result.Proof.Patches.Count == 2 && result.Proof.OutputBodyBytes != result.Proof.SourceBodyBytes,
        $"{compression}: both variable-length expressions are edited");
    Check(FxEmissionPoint.Export(result.ItemBytes, target).ItemBytes.AsSpan().SequenceEqual(result.ItemBytes),
        $"{compression}: identical coordinates produce byte-exact no-op export");
    var roundtrip = FxEmissionPoint.Export(result.ItemBytes, new(0, 0, 0));
    Check(roundtrip.After.Emitters[1].Position == new FxEmissionPoint.Coordinates(0, 0, 10),
        $"{compression}: a second edit preserves the original relative spacing");
}
var fogger = File.ReadAllBytes(foggerPath);
var foggerResult = FxEmissionPoint.Export(fogger, target);
Check(foggerResult.Before.Emitters.Count == 1 && foggerResult.After.Primary == target,
    "Real single-emitter Fogger supports the same native XYZ edit");

var body = ReadBody(source);
var local = sparkler.Emitters[0].LengthOffset;
var dynamicBody = ReplaceExpression(body, local, "float3(Time)");
Reject(Rebody(source, dynamicBody, 'C'), "Dynamic local expressions are rejected");
var invalidLength = body.ToArray(); BinaryPrimitives.WriteInt32LittleEndian(invalidLength.AsSpan(local), int.MaxValue);
Reject(Rebody(source, invalidLength, 'U'), "Corrupt expression lengths are rejected");
var unknown = body.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(unknown.AsSpan(sparkler.FxGraphOffset + 12), 99);
Reject(Rebody(source, unknown, 'U'), "Unknown system-node versions are rejected");
var conditionRoot = body.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(conditionRoot.AsSpan(sparkler.FxGraphOffset + 16), 1);
Reject(Rebody(source, conditionRoot, 'U'), "Unsupported graph root types are rejected");
var fakeLocal = ReplaceExpression(body, local, "float3(1+2,0,0)");
Reject(Rebody(source, fakeLocal, 'C'), "Nonconstant arithmetic expressions are rejected");
Reject(source[..^1], "Truncated compressed bodies are rejected");
Reject(source.Concat(new byte[] { 0 }).ToArray(), "Trailing compressed bytes are rejected");
var duplicate = body.Concat(body.AsSpan(sparkler.FxGraphOffset, 8).ToArray()).ToArray();
Reject(Rebody(source, duplicate, 'U'), "Multiple FX signatures are rejected as ambiguous");
var noFx = body.ToArray(); noFx.AsSpan(sparkler.FxGraphOffset, 8).Clear();
Reject(Rebody(source, noFx, 'U'), "An opaque ornament without a validated FX graph is rejected");
var badTail = body.ToArray();
badTail[local + 8 + BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(local))] = 0;
Reject(Rebody(source, badTail, 'U'), "Malformed emitter expression tails are rejected");
var rejectedNonfinite = false;
try { FxEmissionPoint.Export(source, new(float.NaN, 0, 0)); } catch (InvalidDataException) { rejectedNonfinite = true; }
Check(rejectedNonfinite, "Nonfinite requested coordinates are rejected");
var secondaryOverflow = false;
try { FxEmissionPoint.Export(source, new(0, 0, 10000)); } catch (InvalidDataException) { secondaryOverflow = true; }
Check(secondaryOverflow, "A translation that overflows a secondary emitter is rejected");

// The pixel candidate remains an offline parsing fixture; this test makes no
// native-load claim and must not imply that its image archive is production safe.
var embeddedPath = Path.Combine(root, "artifacts/fx-clones/fx-standalone-20261025/TMIS_Sparkler_EmbeddedPixelsProbe.Item.Gbx");
if (File.Exists(embeddedPath))
{
    var large = File.ReadAllBytes(embeddedPath);
    var result = FxEmissionPoint.Export(large, target);
    Check(result.Before.Emitters.Count == 2 && result.After.Primary == target && result.Proof.OriginalBodyRestorableExactly,
        "Large opaque image payloads do not require particle/image deserialization for XYZ editing");
}
Console.WriteLine(JsonSerializer.Serialize(new { passed = checks.Count, checks }, new JsonSerializerOptions { WriteIndented = true }));

void Check(bool condition, string message)
{
    if (!condition) throw new InvalidDataException("FAILED: " + message);
    checks.Add(message);
}
void Reject(byte[] bytes, string message)
{
    Check(!FxEmissionPoint.TryRead(bytes, out var inspection, out var error) && inspection is null && !string.IsNullOrWhiteSpace(error), message);
}
byte[] ReadBody(byte[] bytes)
{
    using var stream = new MemoryStream(bytes);
    Gbx.ParseHeader(stream);
    if (bytes[7] == 'U') return bytes[checked((int)stream.Position)..];
    var offset = checked((int)stream.Position - 8);
    var result = new byte[BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset))];
    Gbx.LZO!.Decompress(bytes[(offset + 8)..], result); return result;
}
byte[] Rebody(byte[] bytes, byte[] replacement, char compression)
{
    using var stream = new MemoryStream(bytes);
    Gbx.ParseHeader(stream);
    var prefixLength = checked((int)stream.Position - (bytes[7] == 'C' ? 8 : 0));
    var prefix = bytes[..prefixLength]; prefix[7] = (byte)compression;
    using var output = new MemoryStream(); output.Write(prefix);
    if (compression == 'C')
    {
        var compressed = Gbx.LZO!.Compress(replacement);
        output.Write(BitConverter.GetBytes(replacement.Length)); output.Write(BitConverter.GetBytes(compressed.Length)); output.Write(compressed);
    }
    else output.Write(replacement);
    return output.ToArray();
}
byte[] ReplaceExpression(byte[] bytes, int offset, string expression)
{
    var oldLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset));
    var encoded = Encoding.ASCII.GetBytes(expression);
    return bytes[..offset].Concat(BitConverter.GetBytes(encoded.Length)).Concat(encoded).Concat(bytes[(offset + 4 + oldLength)..]).ToArray();
}
