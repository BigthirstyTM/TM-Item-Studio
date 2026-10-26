using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;
using TmEssentials;

if (args.Length != 1) throw new ArgumentException("Usage: FxMeshShells <output-directory>");
Gbx.LZO = new GBX.NET.LZO.MiniLZO();
var output = Path.GetFullPath(args[0]);
Directory.CreateDirectory(output);
var rows = new List<object>();
foreach (var shape in new[] { "Pyramid", "Diamond" })
{
    var item = new CGameItemModel
    {
        Ident = new Ident($"TMIS_FX_{shape}Shell", 26, "TM_Item_Studio"),
        Name = $"TMIS FX {shape} shell",
        Description = "Companion mesh shell for the opaque FX carrier probe",
        ItemType = CGameItemModel.EItemType.Ornament,
        EntityModel = new CPlugPrefab
        {
            Version = 11,
            Ents = [new CPlugPrefab.EntRef
            {
                Model = new CPlugStaticObjectModel { Mesh = Solid(shape) },
                Rotation = new Quat(0, 0, 0, 1),
                Position = new Vec3(0, 0, 0)
            }]
        }
    };
    item.CreateChunk<CGameCtnCollector.HeaderChunk2E001003>().Version = 8;
    item.CreateChunk<CGameItemModel.HeaderChunk2E002000>();
    item.CreateChunk<CGameItemModel.Chunk2E002015>();
    item.CreateChunk<CGameItemModel.Chunk2E002019>().Version = 15;
    using var stream = new MemoryStream();
    new Gbx<CGameItemModel>(item).Save(stream);
    var bytes = stream.ToArray();
    var path = Path.Combine(output, $"TMIS_FX_{shape}Shell.Item.Gbx");
    File.WriteAllBytes(path, bytes);
    var reopened = Gbx.Parse<CGameItemModel>(new MemoryStream(bytes)).Node;
    var geometry = TM_Item_Studio.Models.ItemScene.Build(reopened, 0).Preview.Geometry;
    if (geometry.Count != 1 || geometry[0].Positions.Length < 9) throw new InvalidDataException($"{shape} shell geometry did not round-trip.");
    rows.Add(new { shape, path, bytes = bytes.Length, sha256 = Convert.ToHexString(SHA256.HashData(bytes)), vertexCount = geometry[0].Positions.Length / 3, status = "offline parsed companion shell; native loading/rendering is unverified; pair with FX carrier only after a separate native test" });
}
File.WriteAllText(Path.Combine(output, "fx-mesh-shells-manifest.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));

static CPlugSolid2Model Solid(string shape)
{
    Vec3[] positions = shape == "Pyramid"
        ? [new(-.7f, 0, -.7f), new(.7f, 0, -.7f), new(.7f, 0, .7f), new(-.7f, 0, .7f), new(0, 1.5f, 0)]
        : [new(0, 1.6f, 0), new(.75f, .4f, 0), new(0, 0, .75f), new(-.75f, .4f, 0), new(0, .4f, -.75f), new(0, -.5f, 0)];
    Int3[] triangles = shape == "Pyramid"
        ? [new(0, 1, 4), new(1, 2, 4), new(2, 3, 4), new(3, 0, 4), new(3, 2, 1), new(3, 1, 0)]
        : [new(0, 1, 2), new(0, 2, 3), new(0, 3, 4), new(0, 4, 1), new(5, 2, 1), new(5, 3, 2), new(5, 4, 3), new(5, 1, 4)];
    var stream = new CPlugVertexStream { Positions = positions };
    const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
    var declaration = new CPlugVertexStream.DataDecl();
    typeof(CPlugVertexStream.DataDecl).GetField("flags1", flags)!.SetValue(declaration,
        (uint)CPlugVertexStream.EPlugVDcl.Position | ((uint)CPlugVertexStream.EPlugVDclType.Float3 << 9) | (12u << 18));
    typeof(CPlugVertexStream).GetField("dataDecls", flags)!.SetValue(stream, new[] { declaration });
    typeof(CPlugVertexStream).GetField("count", flags)!.SetValue(stream, positions.Length);
    stream.CreateChunk<CPlugVertexStream.Chunk09056000>().Version = 1;
    var index = new CPlugIndexBuffer { Indices = triangles.SelectMany(t => new[] { t.X, t.Y, t.Z }).ToArray() };
    index.CreateChunk<CPlugIndexBuffer.Chunk09057000>();
    var visual = new CPlugVisualIndexedTriangles
    {
        VertexStreams = [stream], IndexBuffer = index, IsGeometryStatic = true, IsIndexationStatic = true,
        BoundingBox = new BoxAligned(0, .5f, 0, 1, 1, 1)
    };
    typeof(CPlugVisual).GetProperty("Count", flags)!.SetValue(visual, positions.Length);
    visual.CreateChunk<CPlugVisual.Chunk0900600F>().Version = 6;
    visual.CreateChunk<CPlugVisualIndexed.Chunk0906A001>();
    var solid = new CPlugSolid2Model { Visuals = [visual], CustomMaterials = [], ShadedGeoms = [] };
    solid.CreateChunk<CPlugSolid2Model.Chunk090BB000>().Version = 34;
    return solid;
}
