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

// Version 1 serializes independent channel buffers even when declaration
// metadata retains shared-buffer offsets from the importer. Version 0 must
// continue rejecting incompatible shared strides.
foreach (var version in new[] { 1, 0 })
{
    var stream = new CPlugVertexStream
    {
        VertexCount = 2,
        CompressLocalFloat3 = false,
        Positions = [new(1, 2, 3), new(4, 5, 6)],
        Normals = [new(0, 1, 0), new(1, 0, 0)],
        DataDecls = [
            new() { Flags1 = (uint)CPlugVertexStream.EPlugVDcl.Position | (2u << 9) | (12u << 18) },
            new() { Flags1 = (uint)CPlugVertexStream.EPlugVDcl.Normal | (2u << 9) | (24u << 18),
                Flags2 = 4u << 2, SharedDeclIndex = 0, Offset = 4 }
        ]
    };
    stream.CreateChunk<CPlugVertexStream.Chunk09056000>().Version = version;
    using var output = new MemoryStream();
    if (version == 0)
    {
        try { new Gbx<CPlugVertexStream>(stream).Save(output); }
        catch (InvalidDataException ex) when (ex.Message.Contains("Shared declaration"))
        { Console.WriteLine("PASS: version 0 invalid shared strides remain rejected"); continue; }
        throw new Exception("Version 0 accepted incompatible shared strides.");
    }
    new Gbx<CPlugVertexStream>(stream).Save(output);
    output.Position = 0;
    var read = Gbx.Parse<CPlugVertexStream>(output).Node;
    if (!read.Positions!.SequenceEqual(stream.Positions) || !read.Normals!.SequenceEqual(stream.Normals))
        throw new Exception("Version 1 independent channel data changed.");
    Console.WriteLine("PASS: version 1 independent buffers survive shared declaration metadata");
}

// Custom roots used to be silently written as null by the 2.5 writer.
foreach (var version in new[] { 8, 9, 11, 12, 15 })
foreach (var type in new[] { GBX.NET.Engines.GameData.CGameItemModel.EItemType.Ornament,
    GBX.NET.Engines.GameData.CGameItemModel.EItemType.Vehicle })
{
    var item = new GBX.NET.Engines.GameData.CGameItemModel { ItemType = type,
        PhyModelCustom = new CPlugSurface(), VisModelCustom = new CPlugSolid2Model() };
    item.CreateChunk<GBX.NET.Engines.GameData.CGameItemModel.HeaderChunk2E002000>();
    item.CreateChunk<GBX.NET.Engines.GameData.CGameItemModel.Chunk2E002019>().Version = version;
    using var output = new MemoryStream();
    new Gbx<GBX.NET.Engines.GameData.CGameItemModel>(item).Save(output);
    output.Position = 0;
    var read = Gbx.Parse<GBX.NET.Engines.GameData.CGameItemModel>(output).Node;
    if (read.PhyModelCustom is not CPlugSurface || read.VisModelCustom is not CPlugSolid2Model)
        throw new Exception($"Custom model roots lost for {type}, chunk version {version}.");
    Console.WriteLine($"PASS: custom model roots preserved for {type}, chunk version {version}");
}
