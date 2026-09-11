using System.Reflection;
using System.Security.Cryptography;
using GBX.NET;
using GBX.NET.Serialization;

// All fixtures are synthetic byte sequences; no game/item assets are required.
bool official = args.Contains("--official");
if (args.Any(arg => arg != "--official"))
{
    Console.Error.WriteLine("Usage: ParserSentinels [--official]");
    return 2;
}

var assembly = typeof(Gbx).Assembly;
Console.WriteLine($"Assembly: {assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}");
Console.WriteLine($"SHA256: {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))).ToLowerInvariant()}");
int passed = 0;
int failed = 0;
Check("ordinary null index", () => NullThenMarker([255, 255, 255, 255, 0x78, 0x56, 0x34, 0x12]));
Check("indexed null class id", () =>
{
    byte[] bytes = [1, 0, 0, 0, 255, 255, 255, 255, 0x78, 0x56, 0x34, 0x12];
    if (official)
        Rejected(bytes, typeof(Exception), "Unknown class ID: 0xFFFFFFFF", 8, trailingMarker: true);
    else
        NullThenMarker(bytes);
});
Check("unknown class id rejected", () => Rejected(
    [1, 0, 0, 0, 0x44, 0x33, 0x22, 0x11, 0x78, 0x56, 0x34, 0x12],
    typeof(Exception), "Unknown class ID: 0x11223344", 8, trailingMarker: true));
Check("truncated class id rejected", () => Rejected(
    [1, 0, 0, 0, 255, 255], typeof(EndOfStreamException), null, 6));
Check("truncated node index rejected", () => Rejected(
    [255, 255], typeof(EndOfStreamException), null, 2));
Check("empty stream rejected", () => Rejected(
    [], typeof(EndOfStreamException), null, 0));
Console.WriteLine($"{passed} passed, {failed} failed ({(official ? "official control" : "compatibility patch")}).");
return failed == 0 ? 0 : 1;

void Check(string name, Action test)
{
    try { test(); passed++; Console.WriteLine($"PASS: {name}"); }
    catch (Exception ex) { failed++; Console.Error.WriteLine($"FAIL: {name}: {ex.Message}"); }
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void NullThenMarker(byte[] bytes)
{
    using var stream = new MemoryStream(bytes);
    using var reader = new GbxReader(stream);
    Require(reader.ReadNodeRef(out var external) is null, "Expected null node");
    Require(external is null, "Null sentinel must not return an external reference");
    Require(stream.Position == bytes.Length - 4, "Incorrect sentinel consumption");
    Require(reader.ReadUInt32() == 0x12345678, "Trailing marker was not preserved");
    Require(stream.Position == stream.Length, "Unexpected trailing bytes");
}

static void Rejected(byte[] bytes, Type exceptionType, string? prefix, long position, bool trailingMarker = false)
{
    using var stream = new MemoryStream(bytes);
    using var reader = new GbxReader(stream);
    Exception? caught = null;
    try { reader.ReadNodeRef(); }
    catch (Exception ex) { caught = ex; }
    Require(caught?.GetType() == exceptionType, $"Expected {exceptionType.Name}, got {caught?.GetType().Name ?? "no exception"}");
    if (prefix is not null)
        Require(caught!.Message.StartsWith(prefix, StringComparison.Ordinal), $"Unexpected error: {caught.Message}");
    Require(stream.Position == position, $"Expected {position} bytes consumed, got {stream.Position}");
    if (trailingMarker)
        Require(reader.ReadUInt32() == 0x12345678, "Rejection consumed trailing marker");
}
