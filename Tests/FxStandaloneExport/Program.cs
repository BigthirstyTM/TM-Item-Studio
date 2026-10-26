using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using GBX.NET;
using TM_Item_Studio.Models;

if (args.Length != 5) throw new ArgumentException("Usage: FxStandaloneExport <source> <output> <FX-local-x> <FX-local-y> <FX-local-z>");
Gbx.LZO = new GBX.NET.LZO.MiniLZO();
var input = File.ReadAllBytes(args[0]);
float Coordinate(int index) => float.Parse(args[index], CultureInfo.InvariantCulture);
var export = FxEmissionPoint.Export(input, new(Coordinate(2), Coordinate(3), Coordinate(4)));
var output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllBytes(output, export.ItemBytes);
var evidence = new { source = Path.GetFullPath(args[0]), output,
    sha256 = Convert.ToHexString(SHA256.HashData(export.ItemBytes)), bytes = export.ItemBytes.Length,
    export.Before, export.After, export.Proof, export.NativeOffsetWritten,
    coordinateSpace = "FX-owner-local (not item space)", nativeRuntimeVerdict = "pending" };
File.WriteAllText(output + ".probe.json", JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(new { output, evidence.sha256, evidence.bytes, nativePoint = export.After.Primary }));
