using GBX.NET;
using GBX.NET.Engines.Plug;
using System.Security.Cryptography;

Gbx.LZO = new GBX.NET.LZO.MiniLZO();
Console.WriteLine($"Parser SHA256: {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Gbx).Assembly.Location))).ToLowerInvariant()}");
var expected = new (string Suffix, string? Type)[] {
    ("-1", null), ("0", "Sphere"), ("1", "Ellipsoid"), ("6", "Box"), ("7", "Mesh"),
    ("8", "VCylinder"), ("9", "MultiSphere"), ("10", "ConvexPolyhedron"), ("11", "Capsule"),
    ("12", "Circle"), ("13", "Compound"), ("14", "SphereLocated"), ("15", "CompoundInstance"),
    ("16", "Cylinder"), ("17", "SphericalShell"), ("18", "Voxel"), ("19", "Diggable"),
    ("10Procedural", "ConvexPolyhedron"), ("8Version0", "VCylinder"), ("14Version0", "SphereLocated"),
    ("16Version0", "Cylinder"), ("9Primary", "MultiSphere"), ("9Fallback", "MultiSphere")
};
var directory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
var actual = Directory.GetFiles(directory, "*.Shape.Gbx").Select(Path.GetFileName).Order().ToArray();
var names = expected.Select(t => $"GmSurfFixture{t.Suffix}.Shape.Gbx").Order().ToArray();
if (!actual.SequenceEqual(names)) throw new Exception("Every contributed shape must have an explicit case.");
foreach (var test in expected)
{
    var path = Path.Combine(directory, $"GmSurfFixture{test.Suffix}.Shape.Gbx");
    using var original = new MemoryStream();
    Gbx.Decompress(path, original);
    var file = Gbx.Parse<CPlugSurface>(path);
    if (file.Node.Surf?.GetType().Name != test.Type) throw new Exception($"Wrong surface type: {test.Suffix}");
    file.BodyCompression = GbxCompression.Uncompressed;
    using var output = new MemoryStream();
    file.Save(output);
    if (!output.ToArray().SequenceEqual(original.ToArray())) throw new Exception($"Archive changed: {test.Suffix}");
    output.Position = 0;
    if (Gbx.Parse<CPlugSurface>(output).Node.Surf?.GetType().Name != test.Type) throw new Exception($"Reparse failed: {test.Suffix}");
    Console.WriteLine($"PASS: {Path.GetFileName(path)} parse/save/reparse, unchanged decompressed archive");
}
Console.WriteLine($"{expected.Length} shape fixtures passed.");
