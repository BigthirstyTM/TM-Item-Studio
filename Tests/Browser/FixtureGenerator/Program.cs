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
        SubVersion = 3,
        TransAxis = NPlugDyna_SKinematicConstraint.EAxis.X,
        TransMin = 0,
        TransMax = 10,
        TransAnimFunc = new()
        {
            IsDuration = true,
            SubFuncs = [new() { Ease = NPlugDyna_SKinematicConstraint.AnimEase.Linear, Duration = new TimeInt32(1000) },
                new() { Ease = NPlugDyna_SKinematicConstraint.AnimEase.Linear, Reverse = true }, new(), new()]
        },
        RotAxis = NPlugDyna_SKinematicConstraint.EAxis.Z,
        RotAnimFunc = new() { IsDuration = true, SubFuncs = [new() { Duration = new TimeInt32(1000) }, new(), new(), new()] }
    };
    var constraintEntry = new CPlugPrefab.EntRef
    {
        Model = constraint,
        Rotation = new Quat(0, 0, 0, 1),
        // Targets index the kinematic-body table, not the prefab entity array.
        // There is exactly one kinematic body, with the world as its parent.
        Params = new NPlugDyna_SPrefabConstraintParams { Ent1 = -1, Ent2 = 0 }
    };
    var item = new CGameItemModel
    {
        Ident = new Ident("BespokeAnimation", 26, "FixtureGenerator"),
        ItemType = CGameItemModel.EItemType.PickUp,
        EntityModel = new CPlugPrefab { Version = 11, Ents = staticFirst ? [stationary, moving, constraintEntry] : [moving, stationary, constraintEntry] }
    };
    item.CreateChunk<CGameCtnCollector.HeaderChunk2E001003>().Version = 8;
    item.CreateChunk<CGameItemModel.HeaderChunk2E002000>();
    item.CreateChunk<CGameItemModel.Chunk2E002015>();
    item.ItemTypeE = CGameItemModel.EItemType.PickUp;
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

// Static ornament carrying one serialized CPlugLightUserModel for the
// capability-gating browser check: light color/intensity/radius edits must
// round-trip while position/add/remove stay read-only.
{
    var light = new CPlugLightUserModel { Color = new Vec3(0.25f, 0.5f, 0.75f), Intensity = 4, Distance = 12 };
    light.CreateChunk<CPlugLightUserModel.Chunk090F9000>().Version = 1;
    var lightItem = new CGameItemModel
    {
        Ident = new Ident("BespokeLight", 26, "FixtureGenerator"),
        ItemType = CGameItemModel.EItemType.Ornament,
        EntityModel = new CPlugPrefab
        {
            Version = 11,
            Ents =
            [
                new CPlugPrefab.EntRef { Model = new CPlugStaticObjectModel { Mesh = Solid() }, Rotation = Quat.Identity },
                new CPlugPrefab.EntRef { Model = light, Position = new Vec3(1, 2, 3), Rotation = Quat.Identity }
            ]
        }
    };
    lightItem.CreateChunk<CGameCtnCollector.HeaderChunk2E001003>().Version = 8;
    lightItem.CreateChunk<CGameItemModel.HeaderChunk2E002000>();
    lightItem.CreateChunk<CGameItemModel.Chunk2E002015>();
    lightItem.ItemTypeE = CGameItemModel.EItemType.Ornament;
    lightItem.CreateChunk<CGameItemModel.Chunk2E002019>().Version = 15;
    using var bytes = new MemoryStream();
    new Gbx<CGameItemModel>(lightItem) { BodyCompression = GbxCompression.Uncompressed }.Save(bytes);
    bytes.Position = 0;
    var reopened = Gbx.Parse<CGameItemModel>(bytes);
    var reopenedLight = ((CPlugPrefab)reopened.Node.EntityModel!).Ents![1].Model as CPlugLightUserModel;
    if (reopenedLight is null || reopenedLight.Color != new Vec3(0.25f, 0.5f, 0.75f)
        || reopenedLight.Intensity != 4 || reopenedLight.Distance != 12)
        throw new InvalidOperationException("Generated light fixture lost its serialized light.");
    File.WriteAllBytes(Path.Combine(args[0], "light-capability.Item.Gbx"), bytes.ToArray());
    Console.WriteLine($"PASS: generated and reparsed light-capability.Item.Gbx, {bytes.Length} bytes.");
}

static CPlugPrefab.EntRef Entry(bool isStatic) => new()
{
    Position = new Vec3(isStatic ? 0 : 2, 0, 0),
    Rotation = new Quat(0, 0, 0, 1),
    Params = isStatic ? null : new NPlugDynaObjectModel_SInstanceParams { Version = 0, IsKinematic = true },
    Model = new CPlugDynaObjectModel { Version = 13, IsStatic = isStatic, Mass = 10, BreakSpeedKmh = 100, Mesh = Solid() }
};

static CPlugSolid2Model Solid()
{
    var stream = new CPlugVertexStream { Positions = [new(), new(1, 0, 0), new(0, 1, 0)] };
    // The bundled serializer exposes decoded arrays, but not its declarations
    // or count setters. Reflection is restricted to this synthetic generator.
    const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
    var declaration = new CPlugVertexStream.DataDecl();
    typeof(CPlugVertexStream.DataDecl).GetField("flags1", flags)!.SetValue(declaration,
        (uint)CPlugVertexStream.EPlugVDcl.Position | ((uint)CPlugVertexStream.EPlugVDclType.Float3 << 9) | (12u << 18));
    typeof(CPlugVertexStream).GetField("dataDecls", flags)!.SetValue(stream, new[] { declaration });
    typeof(CPlugVertexStream).GetField("count", flags)!.SetValue(stream, 3);
    stream.CreateChunk<CPlugVertexStream.Chunk09056000>().Version = 1;
    var indexBuffer = new CPlugIndexBuffer { Indices = [0, 1, 2] };
    indexBuffer.CreateChunk<CPlugIndexBuffer.Chunk09057000>();
    var visual = new CPlugVisualIndexedTriangles
    {
        VertexStreams = [stream], IndexBuffer = indexBuffer,
        IsGeometryStatic = true, IsIndexationStatic = true,
        BoundingBox = new BoxAligned(0, 0, 0, 1, 1, 0)
    };
    typeof(CPlugVisual).GetProperty("Count", flags)!.SetValue(visual, 3);
    visual.CreateChunk<CPlugVisual.Chunk0900600F>().Version = 6;
    visual.CreateChunk<CPlugVisualIndexed.Chunk0906A001>();
    var solid = new CPlugSolid2Model
    {
        Visuals = [visual],
        CustomMaterials = [],
        ShadedGeoms = []
    };
    solid.CreateChunk<CPlugSolid2Model.Chunk090BB000>().Version = 34;
    return solid;
}
