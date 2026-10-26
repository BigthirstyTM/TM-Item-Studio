using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.GameData;
using TM_Item_Studio.Models;

if (args.Length is not (5 or 6) || (args.Length == 6 && args[5] != "--fx-local"))
    throw new ArgumentException("FxPointWriter <source.Item.Gbx> <output.Item.Gbx> <X> <Y> <Z> [--fx-local]");
Gbx.LZO = new GBX.NET.LZO.MiniLZO();
var sourcePath = Path.GetFullPath(args[0]);
var outputPath = Path.GetFullPath(args[1]);
if (string.Equals(sourcePath, outputPath, StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("This offline tool requires a separate output file.");
var source = File.ReadAllBytes(sourcePath);
var target = new FxEmissionPoint.Coordinates(float.Parse(args[2], CultureInfo.InvariantCulture),
    float.Parse(args[3], CultureInfo.InvariantCulture), float.Parse(args[4], CultureInfo.InvariantCulture));
var isFxLocal = args.Length == 6;
var itemResult = isFxLocal ? null : FxEmitterTransform.Export(source, target);
var nativeResult = isFxLocal ? FxEmissionPoint.Export(source, target) : null;
var itemBytes = nativeResult?.ItemBytes ?? itemResult!.ItemBytes;
var proof = nativeResult?.Proof ?? itemResult!.Proof;
// A raw FX-local edit deliberately needs no decoded prefab/mesh. In particular,
// a research graph can share an inline bitmap with a second material after the
// FX node, which the preview-only FX stripping route does not retain.
var after = isFxLocal ? null : FxEmitterTransform.Read(itemBytes);
var nativeAfter = nativeResult?.After ?? after!.Native;
var beforeHeader = Gbx.ParseHeader<CGameItemModel>(new MemoryStream(source));
var afterHeader = Gbx.ParseHeader<CGameItemModel>(new MemoryStream(itemBytes));
var beforeIdentity = FxIdentity.Read(source);
var afterIdentity = FxIdentity.Read(itemBytes);
if (beforeHeader.Node.Ident != afterHeader.Node.Ident || beforeIdentity != afterIdentity
    || !proof.HeaderAndRefTablePreserved || !proof.OriginalBodyRestorableExactly)
    throw new InvalidDataException("The bounded origin edit did not preserve original identity/container evidence.");
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
File.WriteAllBytes(outputPath, itemBytes);
var report = new
{
    generator = "Tests/FxPointWriter", source = sourcePath, sourceSha256 = Hash(source),
    output = outputPath, outputSha256 = Hash(itemBytes), outputBytes = itemBytes.Length,
    inputCoordinateSpace = isFxLocal ? "FX owner-local" : "item-local metres", inputPoint = target,
    headerId = afterHeader.Node.Ident?.Id, bodyId = afterIdentity.Id,
    headerName = afterHeader.Node.Name, bodyName = afterIdentity.Name, author = afterIdentity.Author,
    originalIdentityAndNamePreserved = true,
    point = after?.Primary ?? nativeAfter.Primary,
    pointCoordinateSpace = isFxLocal ? "FX owner-local" : "item-local metres",
    nativeOffsets = nativeAfter.Emitters,
    owner = after?.Owner, proof,
    nativeStatus = "Offline artifact only; this tool neither stages files nor operates Trackmania."
};
var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(outputPath + ".point-manifest.json", json);
Console.WriteLine(json);
static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
