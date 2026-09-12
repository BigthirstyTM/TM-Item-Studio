using System.Reflection;
using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;
using TM_Item_Studio.Models;
using TmEssentials;

if (args.Length != 1)
    throw new ArgumentException("Usage: FixtureGenerator <output-directory>");

Directory.CreateDirectory(args[0]);
foreach (var staticFirst in new[] { true, false })
{
    var stationary = Entry(true);
    var moving = Entry(false);
    var constraint = new NPlugDyna_SKinematicConstraint
    {
        TransAxis = NPlugDyna_SKinematicConstraint.EAxis.X,
        TransMin = 0,
        TransMax = 10,
        TransAnimFunc = new()
        {
            SubFuncs = [new() { Ease = NPlugDyna_SKinematicConstraint.AnimEase.Linear, Duration = new TimeInt32(1000) }]
        }
    };
    var constraintEntry = new CPlugPrefab.EntRef
    {
        Model = constraint,
        Rotation = new Quat(0, 0, 0, 1),
        Params = new NPlugDyna_SPrefabConstraintParams { Ent1 = staticFirst ? 0 : 1, Ent2 = staticFirst ? 1 : 0 }
    };
    var item = new CGameItemModel
    {
        ItemType = CGameItemModel.EItemType.Ornament,
        EntityModel = new CPlugPrefab { Ents = staticFirst ? [stationary, moving, constraintEntry] : [moving, stationary, constraintEntry] }
    };
    item.CreateChunk<CGameItemModel.HeaderChunk2E002000>();
    item.CreateChunk<CGameItemModel.Chunk2E002019>().Version = 15;
    using var bytes = new MemoryStream();
    new Gbx<CGameItemModel>(item) { BodyCompression = GbxCompression.Uncompressed }.Save(bytes);
    bytes.Position = 0;
    var reopened = Gbx.Parse<CGameItemModel>(bytes);
    var geometry = ItemScene.Build(reopened.Node, 0).Preview.Geometry.OrderBy(g => g.Positions[0]).ToArray();
    if (geometry.Length != 2 || !geometry[0].Positions.SequenceEqual(new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 })
        || !geometry[1].Positions.SequenceEqual(new float[] { 2, 0, 0, 3, 0, 0, 2, 1, 0 }))
        throw new InvalidOperationException("Generated fixture did not preserve the two authored triangles.");
    var prefab = (CPlugPrefab)reopened.Node.EntityModel!;
    if (((CPlugDynaObjectModel)prefab.Ents![staticFirst ? 0 : 1].Model!).IsStatic != true
        || ((CPlugDynaObjectModel)prefab.Ents[staticFirst ? 1 : 0].Model!).IsStatic != false)
        throw new InvalidOperationException("Generated fixture lost its static/dynamic flags.");
    var parsedConstraint = (NPlugDyna_SKinematicConstraint)prefab.Ents[2].Model!;
    if (parsedConstraint.TransMax != 10 || parsedConstraint.TransAnimFunc?.SubFuncs?[0].Duration.TotalMilliseconds != 1000)
        throw new InvalidOperationException("Generated fixture lost its constraint.");
    var name = staticFirst ? "animation-static-first.Item.Gbx" : "animation-static-last.Item.Gbx";
    File.WriteAllBytes(Path.Combine(args[0], name), bytes.ToArray());
    Console.WriteLine($"PASS: generated and reparsed {name}, {bytes.Length} bytes, two triangles.");
}

static CPlugPrefab.EntRef Entry(bool isStatic) => new()
{
    Position = new Vec3(isStatic ? 0 : 2, 0, 0),
    Rotation = new Quat(0, 0, 0, 1),
    Model = new CPlugDynaObjectModel { Version = 2, IsStatic = isStatic, DynamizeOnSpawn = !isStatic, Mass = 1, Mesh = Solid() }
};

static CPlugSolid2Model Solid()
{
    var stream = new CPlugVertexStream { Positions = [new(), new(1, 0, 0), new(0, 1, 0)] };
    // The bundled serializer exposes decoded arrays, but not its declarations
    // or count setters. Reflection is restricted to this synthetic generator.
    const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
    var declaration = new CPlugVertexStream.DataDecl();
    typeof(CPlugVertexStream.DataDecl).GetField("flags1", flags)!.SetValue(declaration,
        (uint)CPlugVertexStream.EPlugVDcl.Position | ((uint)CPlugVertexStream.EPlugVDclType.Float3 << 9));
    typeof(CPlugVertexStream).GetField("dataDecls", flags)!.SetValue(stream, new[] { declaration });
    typeof(CPlugVertexStream).GetField("count", flags)!.SetValue(stream, 3);
    stream.CreateChunk<CPlugVertexStream.Chunk09056000>().Version = 1;
    var indexBuffer = new CPlugIndexBuffer { Indices = [0, 1, 2] };
    indexBuffer.CreateChunk<CPlugIndexBuffer.Chunk09057000>();
    var visual = new CPlugVisualIndexedTriangles { VertexStreams = [stream], IndexBuffer = indexBuffer };
    visual.CreateChunk<CPlugVisual.Chunk0900600A>();
    visual.CreateChunk<CPlugVisualIndexed.Chunk0906A001>();
    var solid = new CPlugSolid2Model
    {
        Visuals = [visual],
        CustomMaterials = [new()],
        ShadedGeoms = [new() { VisualIndex = 0, MaterialIndex = 0, LodMask = 1 }]
    };
    solid.CreateChunk<CPlugSolid2Model.Chunk090BB000>().Version = 6;
    return solid;
}
