// Kinematic reference analysis against the tracked exported pair. Dump mode prints the full
// kinematic structure of item archives; the default mode runs the regression checks below.
// Expectations are derived from the reference dump (docs/kinematic-conversion-analysis.md),
// not from the evaluator under test.
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.MwFoundations;
using GBX.NET.Engines.Plug;
using GBX.NET.LZO;
using TM_Item_Studio.Models;
using TmEssentials;
using KC = GBX.NET.Engines.Meta.NPlugDyna_SKinematicConstraint;

Gbx.LZO = new MiniLZO();
var repoRoot = FindRepoRoot();
if (args.Length > 0 && args[0] == "--dump")
{
    foreach (var path in args.Skip(1).Select(a => Path.IsPathRooted(a) ? a : Path.Combine(repoRoot, a)))
        DumpItem(path);
    return 0;
}
if (args.Length > 1 && args[0] == "--verify-item")
{
    var targetPath = Path.IsPathRooted(args[1]) ? args[1] : Path.Combine(repoRoot, args[1]);
    var item = ParseItem(targetPath);
    var scene = ItemScene.Build(item, 0);
    Console.WriteLine($"Scene Nodes: {scene.Preview.Nodes.Count}, Diagnostics: {scene.Preview.Diagnostics.Count}");
    var preview = AuthoredMotionPreview.Build(scene);
    Console.WriteLine($"Preview Tracks: {preview.Tracks.Count}, Diagnostics: {preview.Diagnostics.Count}");
    foreach (var d in preview.Diagnostics)
        Console.WriteLine($"  Diag: {d}");
    for (int i = 0; i < preview.Tracks.Count; i++)
    {
        var t = preview.Tracks[i];
        Console.WriteLine($"  Track[{i}]: Path={t.Path}, Child={t.ChildPath}, Parent={t.ParentPath ?? "null(World)"}, Axis={t.Fields.TranslationAxis}, Range=[{t.Fields.TranslationMin}..{t.Fields.TranslationMax}]");
    }
    return 0;
}
if (args.Length > 1 && args[0] == "--fix-carrier")
{
    var targetPath = Path.IsPathRooted(args[1]) ? args[1] : Path.Combine(repoRoot, args[1]);
    var item = ParseItem(targetPath);
    var prefab = (CPlugPrefab)item.EntityModel!;
    int patched = 0;
    foreach (var ent in prefab.Ents!)
    {
        if (ent.Model is CPlugDynaObjectModel dyna && dyna.Mesh is null)
        {
            var mesh = Solid();
            var shape = new CPlugSurface
            {
                Surf = new CPlugSurface.Mesh
                {
                    Version = 6,
                    Vertices = [new(0, 0, 0), new(0.0001f, 0, 0), new(0, 0.0001f, 0)],
                    Triangles = [new(new Int3(0, 1, 2), 0, 0, 0)]
                }
            };
            shape.CreateChunk<CPlugSurface.Chunk0900C003>().Version = 2;
            dyna.Mesh = mesh;
            dyna.StaticShape = shape;
            dyna.DynaShape = shape;
            patched++;
        }
    }
    File.WriteAllBytes(targetPath, Save(item));
    Console.WriteLine($"Successfully patched {patched} carrier bodies in {targetPath}");
    return 0;
}
if (args.Length > 1 && args[0] == "--fix-collision")
{
    var targetPath = Path.IsPathRooted(args[1]) ? args[1] : Path.Combine(repoRoot, args[1]);
    var item = ParseItem(targetPath);
    var prefab = (CPlugPrefab)item.EntityModel!;
    var dyna0 = (CPlugDynaObjectModel)prefab.Ents[0].Model!;
    var mesh = (CPlugSolid2Model)dyna0.Mesh!;
    if (mesh.Visuals?.Length > 0 && mesh.Visuals[0] is CPlugVisualIndexedTriangles vit
        && vit.VertexStreams?.Count > 0 && vit.IndexBuffer?.Indices is not null)
    {
        var positions = vit.VertexStreams[0].Positions!;
        var indices = vit.IndexBuffer.Indices;
        var tris = new CPlugSurface.Mesh.Triangle[indices.Length / 3];
        for (int i = 0; i < tris.Length; i++)
        {
            tris[i] = new CPlugSurface.Mesh.Triangle(new Int3(indices[i * 3], indices[i * 3 + 1], indices[i * 3 + 2]), 0, 0, 0);
        }
        var surfMesh = new CPlugSurface.Mesh
        {
            Version = 6,
            Vertices = positions,
            Triangles = tris
        };
        var newShape = new CPlugSurface { Surf = surfMesh };
        newShape.CreateChunk<CPlugSurface.Chunk0900C003>().Version = 2;
        dyna0.StaticShape = newShape;
        dyna0.DynaShape = newShape;
        File.WriteAllBytes(targetPath, Save(item));
        Console.WriteLine($"Successfully regenerated collision surface from visual mesh: {positions.Length} verts, {tris.Length} tris");
    }
    return 0;
}
if (args.Length > 1 && args[0] == "--inspect-mesh")
{
    var targetPath = Path.IsPathRooted(args[1]) ? args[1] : Path.Combine(repoRoot, args[1]);
    var item = ParseItem(targetPath);
    var prefab = (CPlugPrefab)item.EntityModel!;
    var dyna0 = (CPlugDynaObjectModel)prefab.Ents[0].Model!;
    var mesh = (CPlugSolid2Model)dyna0.Mesh!;
    Console.WriteLine($"Visuals count: {mesh.Visuals?.Length}");
    if (mesh.Visuals?.Length > 0 && mesh.Visuals[0] is CPlugVisualIndexedTriangles vit)
    {
        Console.WriteLine($"Mesh BoundingBox: {vit.BoundingBox}");
        var verts = vit.VertexStreams![0].Positions!;
        Console.WriteLine($"Mesh verts count: {verts.Length}");
        Console.WriteLine($"Mesh vert[0]: {verts[0]} vert[max]: {verts[^1]}");
        Console.WriteLine($"IndexBuffer indices: {vit.IndexBuffer?.Indices?.Length}");
    }
    var surf = (CPlugSurface)dyna0.StaticShape!;
    Console.WriteLine($"StaticShape Materials: {surf.Materials?.Length ?? 0}");
    if (surf.Surf is CPlugSurface.Mesh sm)
    {
        Console.WriteLine($"Surface Mesh Triangles: {sm.Triangles?.Length ?? 0}");
        if (sm.Triangles?.Length > 0)
        {
            var tri = sm.Triangles[0];
            foreach (var prop in tri.GetType().GetProperties())
                Console.WriteLine($"  Tri prop: {prop.Name} = {prop.GetValue(tri)}");
            foreach (var field in tri.GetType().GetFields())
                Console.WriteLine($"  Tri field: {field.Name} = {field.GetValue(tri)}");
        }
    }
    return 0;
}
if (args.Length > 0 && args[0] == "--reflect")
{
    var path = @"C:\Users\PC\Documents\Trackmania\Items\BF2_ASSETS\Test_Items\CustomItem.Item.Gbx";
    var item = ParseItem(path);
    var prefab = (CPlugPrefab)item.EntityModel!;
    for (int i = 0; i < prefab.Ents!.Length; i++)
    {
        var e = prefab.Ents[i];
        Console.WriteLine($"ent[{i}]: model={e.Model?.GetType().Name}");
        if (e.Model is CPlugDynaObjectModel d)
        {
            Console.WriteLine($"  IsStatic={d.IsStatic}, DynamizeOnSpawn={d.DynamizeOnSpawn}, Mass={d.Mass}, BreakSpeed={d.BreakSpeedKmh}");
            Console.WriteLine($"  LocAnimIsPhysical={d.LocAnimIsPhysical}, u01-u10={d.U01},{d.U02},{d.U03},{d.U04},{d.U05},{d.U06},{d.U07},{d.U08},{d.U09},{d.U10}");
            Console.WriteLine($"  StaticShape={d.StaticShape is not null}, DynaShape={d.DynaShape is not null}");
        }
        if (e.Model is KC kc)
        {
            Console.WriteLine($"  KC: TransAxis={kc.TransAxis}, RotAxis={kc.RotAxis}, SubVersion={kc.SubVersion}");
        }
    }
    return 0;
}
if (args.Length > 0 && args[0] == "--generate-test-items")
{
    var folder = @"C:\Users\PC\Documents\Trackmania\Items\BF2_ASSETS\Test_Items";
    if (args.Length > 1) folder = args[1];

    // 1. Relay A->B->C (100% Havok Collision)
    {
        var template = ItemKinematicEntityTemplate.GetDefaultMovingTemplate()!;
        var prefab = (CPlugPrefab)template.EntityModel!;
        var constraint = (KC)prefab.Ents![1].Model!;
        var res = ItemKinematicEntityTemplate.ConfigureRelayCollisionPath(
            prefab, constraint, "doc:0/variant:none/root",
            KC.EAxis.X, 5f, 2000,
            KC.EAxis.Y, 5f, 2000);
        Console.WriteLine($"Relay config: {res.Success} ({res.Reason})");
        var path = Path.Combine(folder, "Test_Relay_Collision_A_B_C.Item.Gbx");
        File.WriteAllBytes(path, Save(template));
        Console.WriteLine($"Saved Relay item to: {path}");
    }

    // 2. Chained L-Path (Single body, smooth visual path)
    {
        var template = ItemKinematicEntityTemplate.GetDefaultMovingTemplate()!;
        var prefab = (CPlugPrefab)template.EntityModel!;
        var constraint = (KC)prefab.Ents![1].Model!;
        var res = ItemKinematicEntityTemplate.ConfigureChainedLPath(
            prefab, constraint, "doc:0/variant:none/root",
            KC.EAxis.X, 5f, 2000,
            KC.EAxis.Y, 5f, 2000, isPingPong: true);
        Console.WriteLine($"Chained config: {res.Success} ({res.Reason})");
        var path = Path.Combine(folder, "Test_Chained_Visual_A_B_C.Item.Gbx");
        File.WriteAllBytes(path, Save(template));
        Console.WriteLine($"Saved Chained item to: {path}");
    }

    return 0;
}
if (args.Length > 0 && args[0] == "--generate-letter-b")
{
    var folder = @"C:\Users\PC\Documents\Trackmania\Items\BF2_ASSETS\Test_Items";
    if (args.Length > 1) folder = args[1];

    CPlugSolid2Model? userMesh = null;
    CPlugSurface? userShape = null;
    CGameItemModel? sourceItem = null;

    string[] candidatePaths = [
        Path.Combine(folder, "CustomItem_DualRoot.Item.Gbx"),
        Path.Combine(folder, "CustomItem.Item.Gbx"),
        Path.Combine(repoRoot, @"Test Exported items\CustomItem_Static.Item.Gbx"),
        Path.Combine(folder, "Test_Chained_Visual_A_B_C.Item.Gbx")
    ];

    foreach (var cand in candidatePaths)
    {
        if (File.Exists(cand))
        {
            try
            {
                var parsed = ParseItem(cand);
                sourceItem ??= parsed;
                if (parsed.EntityModel is CPlugPrefab candPrefab)
                {
                    var dyna = candPrefab.Ents!
                        .Select(e => e.Model as CPlugDynaObjectModel)
                        .Where(d => d?.Mesh is not null)
                        .OrderByDescending(d => d!.Mesh!.Visuals?.Length ?? 0)
                        .FirstOrDefault();
                    if (dyna?.Mesh is not null)
                    {
                        userMesh = dyna.Mesh;
                        userShape = dyna.StaticShape as CPlugSurface ?? ItemKinematicEntityTemplate.GenerateCollisionSurfaceFromMesh(userMesh);
                        Console.WriteLine($"Found user mesh from {Path.GetFileName(cand)}: {userMesh.Visuals?.Length} visuals");
                        break;
                    }
                }
                else if (parsed.EntityModel is CGameCommonItemEntityModel { StaticObject: CPlugStaticObjectModel som } && som.Mesh is not null)
                {
                    userMesh = som.Mesh;
                    userShape = ItemKinematicEntityTemplate.GenerateCollisionSurfaceFromMesh(userMesh);
                    Console.WriteLine($"Found user mesh from static {Path.GetFileName(cand)}");
                    break;
                }
            }
            catch { }
        }
    }

    var template = ItemKinematicEntityTemplate.GetDefaultMovingTemplate()!;
    if (sourceItem is not null)
    {
        template.Ident = new Ident("Letter_B", "Stadium2020", sourceItem.Ident.Author);
        template.Name = "Letter_B";
        template.DefaultPlacement = sourceItem.DefaultPlacement;
        template.GroundPoint = sourceItem.GroundPoint;
        template.Icon = sourceItem.Icon;
        template.IconWebP = sourceItem.IconWebP;
    }
    else
    {
        template.Ident = new Ident("Letter_B", "Stadium2020", "TM_Item_Studio");
        template.Name = "Letter_B";
    }

    var carrierMesh = (CPlugSolid2Model)typeof(ItemKinematicEntityTemplate)
        .GetMethod("CreateCarrierMesh", BindingFlags.NonPublic | BindingFlags.Static)!
        .Invoke(null, null)!;
    var carrierShape = (CPlugSurface)typeof(ItemKinematicEntityTemplate)
        .GetMethod("CreateCarrierShape", BindingFlags.NonPublic | BindingFlags.Static)!
        .Invoke(null, null)!;

    CPlugDynaObjectModel MakeCarrier() => new()
    {
        Version = 13,
        IsStatic = false,
        DynamizeOnSpawn = false,
        Mass = 100,
        BreakSpeedKmh = 100,
        Mesh = carrierMesh,
        StaticShape = carrierShape,
        DynaShape = carrierShape
    };

    NPlugDynaObjectModel_SInstanceParams MakeInstance(bool castShadow = false) => new()
    {
        Version = 2,
        PeriodSc = 1,
        PeriodScMax = -1,
        Phase01 = -1,
        Phase01Max = -1,
        TextureId = 0,
        IsKinematic = true,
        CastStaticShadow = castShadow
    };

    var visibleBody = new CPlugDynaObjectModel
    {
        Version = 13,
        IsStatic = false,
        DynamizeOnSpawn = false,
        Mass = 100,
        BreakSpeedKmh = 100,
        Mesh = userMesh ?? ((CPlugDynaObjectModel)((CPlugPrefab)template.EntityModel!).Ents![0].Model!).Mesh,
        StaticShape = userShape ?? ((CPlugDynaObjectModel)((CPlugPrefab)template.EntityModel!).Ents![0].Model!).StaticShape,
        DynaShape = userShape ?? ((CPlugDynaObjectModel)((CPlugPrefab)template.EntityModel!).Ents![0].Model!).DynaShape
    };

    // Constraint 0: Z_bottom (Ent1 = -1, Ent2 = 0)
    // Moves Z from +5 down to 0, holds, then moves 0 up to +5.
    var c0 = new KC
    {
        Version = 0,
        SubVersion = 3,
        TransAxis = KC.EAxis.Z,
        TransMin = 0,
        TransMax = 5,
        RotAxis = KC.EAxis.Y,
        AngleMinDeg = 0,
        AngleMaxDeg = 0,
        TransAnimFunc = new KC.AnimFunc
        {
            IsDuration = true,
            SubFuncs =
            [
                new() { Ease = KC.AnimEase.QuadInOut, Reverse = true, Duration = new TimeInt32(1500) },
                new() { Ease = KC.AnimEase.Constant, Reverse = false, Duration = new TimeInt32(5000) },
                new() { Ease = KC.AnimEase.QuadInOut, Reverse = false, Duration = new TimeInt32(3500) }
            ]
        },
        RotAnimFunc = new KC.AnimFunc
        {
            IsDuration = true,
            SubFuncs = [new() { Ease = KC.AnimEase.Linear, Reverse = false, Duration = new TimeInt32(10000) }]
        }
    };
    var p0 = new NPlugDyna_SPrefabConstraintParams { Version = 0, Ent1 = -1, Ent2 = 0, Pos1 = default, Pos2 = default };

    // Constraint 1: Z_top (Ent1 = 0, Ent2 = 1)
    // Holds at 0, moves 0 down to -5, moves -5 up to 0, holds at 0.
    var c1 = new KC
    {
        Version = 0,
        SubVersion = 3,
        TransAxis = KC.EAxis.Z,
        TransMin = 0,
        TransMax = -5,
        RotAxis = KC.EAxis.Y,
        AngleMinDeg = 0,
        AngleMaxDeg = 0,
        TransAnimFunc = new KC.AnimFunc
        {
            IsDuration = true,
            SubFuncs =
            [
                new() { Ease = KC.AnimEase.Constant, Reverse = false, Duration = new TimeInt32(1500) },
                new() { Ease = KC.AnimEase.QuadInOut, Reverse = false, Duration = new TimeInt32(1500) },
                new() { Ease = KC.AnimEase.QuadInOut, Reverse = true, Duration = new TimeInt32(3500) },
                new() { Ease = KC.AnimEase.Constant, Reverse = false, Duration = new TimeInt32(3500) }
            ]
        },
        RotAnimFunc = new KC.AnimFunc
        {
            IsDuration = true,
            SubFuncs = [new() { Ease = KC.AnimEase.Linear, Reverse = false, Duration = new TimeInt32(10000) }]
        }
    };
    var p1 = new NPlugDyna_SPrefabConstraintParams { Version = 0, Ent1 = 0, Ent2 = 1, Pos1 = default, Pos2 = default };

    // Constraint 2: X_upper (Ent1 = 1, Ent2 = 2)
    // Holds at 0, arches out to +4 and returns to 0 (upper loop), holds at 0.
    var c2 = new KC
    {
        Version = 0,
        SubVersion = 3,
        TransAxis = KC.EAxis.X,
        TransMin = 0,
        TransMax = 4,
        RotAxis = KC.EAxis.Y,
        AngleMinDeg = 0,
        AngleMaxDeg = 0,
        TransAnimFunc = new KC.AnimFunc
        {
            IsDuration = true,
            SubFuncs =
            [
                new() { Ease = KC.AnimEase.Constant, Reverse = false, Duration = new TimeInt32(3000) },
                new() { Ease = KC.AnimEase.QuadInOut, Reverse = false, Duration = new TimeInt32(1750) },
                new() { Ease = KC.AnimEase.QuadInOut, Reverse = true, Duration = new TimeInt32(1750) },
                new() { Ease = KC.AnimEase.Constant, Reverse = false, Duration = new TimeInt32(3500) }
            ]
        },
        RotAnimFunc = new KC.AnimFunc
        {
            IsDuration = true,
            SubFuncs = [new() { Ease = KC.AnimEase.Linear, Reverse = false, Duration = new TimeInt32(10000) }]
        }
    };
    var p2 = new NPlugDyna_SPrefabConstraintParams { Version = 0, Ent1 = 1, Ent2 = 2, Pos1 = default, Pos2 = default };

    // Constraint 3: X_lower (Ent1 = 2, Ent2 = 3)
    // Holds at 0, arches out to +4 and returns to 0 (lower loop).
    var c3 = new KC
    {
        Version = 0,
        SubVersion = 3,
        TransAxis = KC.EAxis.X,
        TransMin = 0,
        TransMax = 4,
        RotAxis = KC.EAxis.Y,
        AngleMinDeg = 0,
        AngleMaxDeg = 0,
        TransAnimFunc = new KC.AnimFunc
        {
            IsDuration = true,
            SubFuncs =
            [
                new() { Ease = KC.AnimEase.Constant, Reverse = false, Duration = new TimeInt32(6500) },
                new() { Ease = KC.AnimEase.QuadInOut, Reverse = false, Duration = new TimeInt32(1750) },
                new() { Ease = KC.AnimEase.QuadInOut, Reverse = true, Duration = new TimeInt32(1750) }
            ]
        },
        RotAnimFunc = new KC.AnimFunc
        {
            IsDuration = true,
            SubFuncs = [new() { Ease = KC.AnimEase.Linear, Reverse = false, Duration = new TimeInt32(10000) }]
        }
    };
    var p3 = new NPlugDyna_SPrefabConstraintParams { Version = 0, Ent1 = 2, Ent2 = 3, Pos1 = default, Pos2 = default };

    var prefab = (CPlugPrefab)template.EntityModel!;
    prefab.Ents =
    [
        new() { Model = MakeCarrier(), Params = MakeInstance(false), Position = default, Rotation = new(0, 0, 0, 1), U01 = "" },
        new() { Model = c0, Params = p0, Position = default, Rotation = new(0, 0, 0, 1), U01 = "" },
        new() { Model = MakeCarrier(), Params = MakeInstance(false), Position = default, Rotation = new(0, 0, 0, 1), U01 = "" },
        new() { Model = c1, Params = p1, Position = default, Rotation = new(0, 0, 0, 1), U01 = "" },
        new() { Model = MakeCarrier(), Params = MakeInstance(false), Position = default, Rotation = new(0, 0, 0, 1), U01 = "" },
        new() { Model = c2, Params = p2, Position = default, Rotation = new(0, 0, 0, 1), U01 = "" },
        new() { Model = visibleBody, Params = MakeInstance(true), Position = default, Rotation = new(0, 0, 0, 1), U01 = "" },
        new() { Model = c3, Params = p3, Position = default, Rotation = new(0, 0, 0, 1), U01 = "" }
    ];

    var outPath = Path.Combine(folder, "Letter_B.Item.Gbx");
    File.WriteAllBytes(outPath, Save(template));
    Console.WriteLine($"Saved Letter B item to: {outPath}");

    var customItemOutPath = Path.Combine(folder, "CustomItem_Letter_B.Item.Gbx");
    File.WriteAllBytes(customItemOutPath, Save(template));
    Console.WriteLine($"Saved copy to: {customItemOutPath}");

    return 0;
}
if (args.Length > 0 && args[0] == "--generate-loop-relay-proof")
{
    var folder = @"C:\Users\PC\Documents\Trackmania\Items\BF2_ASSETS\Test_Items";
    if (args.Length > 1) folder = args[1];
    var explicitMeshSource = args.Length > 2 ? args[2] : null;
    Directory.CreateDirectory(folder);

    var template = ItemKinematicEntityTemplate.GetDefaultMovingTemplate()!;
    CPlugSolid2Model visualMesh = ProofCubeSolid(1.0f);
    CPlugSurface? collisionShape = ItemKinematicEntityTemplate.GenerateCollisionSurfaceFromMesh(visualMesh);
    var usingFallbackCube = true;
    CGameItemModel? sourceItem = null;

    var candidatePaths = new List<string>();
    if (!string.IsNullOrWhiteSpace(explicitMeshSource))
        candidatePaths.Add(explicitMeshSource);
    candidatePaths.AddRange(
    [
        Path.Combine(folder, "CustomItem_Merged.Item.Gbx"),
        Path.Combine(folder, "CustomItem_Kinematic.Item.Gbx"),
        Path.Combine(folder, "CustomItem_Static.Item.Gbx"),
        Path.Combine(folder, "CustomItem.Item.Gbx"),
        Path.Combine(repoRoot, @"Test Exported items\CustomItem_Static.Item.Gbx")
    ]);

    foreach (var candidate in candidatePaths)
    {
        if (!File.Exists(candidate))
            continue;

        try
        {
            var parsed = ParseItem(candidate);
            sourceItem ??= parsed;
            if (parsed.EntityModel is CPlugPrefab sourcePrefab)
            {
                var sourceBody = FindRenderableDynaBody(sourcePrefab);
                if (sourceBody?.Mesh is CPlugSolid2Model sourceMesh)
                {
                    visualMesh = sourceMesh;
                    collisionShape = sourceBody.StaticShape as CPlugSurface
                        ?? ItemKinematicEntityTemplate.GenerateCollisionSurfaceFromMesh(sourceMesh);
                    usingFallbackCube = false;
                    Console.WriteLine($"Using renderable mesh from {candidate}");
                }
            }
            else if (parsed.EntityModel is CGameCommonItemEntityModel { StaticObject: CPlugStaticObjectModel staticModel } && staticModel.Mesh is CPlugSolid2Model staticMesh && HasGameRenderableMesh(staticMesh))
            {
                visualMesh = staticMesh;
                collisionShape = parsed.EntityModel is CGameCommonItemEntityModel { PhyModel: CPlugSurface phyShape } && phyShape.Surf is not null
                    ? phyShape
                    : ItemKinematicEntityTemplate.GenerateCollisionSurfaceFromMesh(staticMesh);
                usingFallbackCube = false;
                Console.WriteLine($"Using renderable static mesh from {candidate}");
            }
            Console.WriteLine($"Using metadata source from {candidate}");
            if (!usingFallbackCube) break;
        }
        catch
        {
            // Continue scanning fallback candidates.
        }
    }

    template.Ident = new Ident("Proof_Relay_Coaster_Loop", "Stadium2020", sourceItem?.Ident.Author ?? "TM_Item_Studio");
    template.Name = "Proof_Relay_Coaster_Loop";
    if (sourceItem is not null)
    {
        template.DefaultPlacement = sourceItem.DefaultPlacement;
        template.GroundPoint = sourceItem.GroundPoint;
        template.Icon = sourceItem.Icon;
        template.IconWebP = sourceItem.IconWebP;
    }
    var exportId = string.IsNullOrWhiteSpace(template.Ident.Id) ? "Proof_Relay_Coaster_Loop" : template.Ident.Id;
    if (string.IsNullOrWhiteSpace(template.Ident.Author))
        template.Ident = new Ident(exportId, template.Ident.Collection, "TM_Item_Studio");
    var inventoryFolder = TryGetInventoryRelativeFolder(folder);
    template.ArchetypeRef = string.IsNullOrWhiteSpace(inventoryFolder)
        ? exportId
        : $"{inventoryFolder}\\{exportId}";
    if (string.IsNullOrWhiteSpace(template.PageName))
        template.PageName = "Items";
    if (template.CatalogPosition <= 0)
        template.CatalogPosition = 1;
    template.ItemType = CGameItemModel.EItemType.Ornament;
    template.ItemTypeE = CGameItemModel.EItemType.Ornament;

    collisionShape ??= ItemKinematicEntityTemplate.GenerateCollisionSurfaceFromMesh(visualMesh)
        ?? (CPlugSurface)typeof(ItemKinematicEntityTemplate)
            .GetMethod("CreateCarrierShape", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, null)!;
    if (usingFallbackCube)
        Console.WriteLine("Falling back to synthetic cube mesh (no source mesh with game-ready render bindings found).");

    var controlPoints = new[]
    {
        new Vector3(0f, 0.2f, 0f),
        new Vector3(4f, 0.8f, 1f),
        new Vector3(10f, 1.7f, 4f),
        new Vector3(15f, 2.6f, 10f),
        new Vector3(13f, 1.8f, 16f),
        new Vector3(7f, 0.9f, 20f),
        new Vector3(1f, 0.3f, 18f),
        new Vector3(-3f, 1.0f, 12f),
        new Vector3(-1f, 1.8f, 6f),
        new Vector3(2f, 0.7f, 2f),
        new Vector3(0f, 0.2f, 0f)
    };
    var segments = BuildAxisRelaySegments(controlPoints);
    if (segments.Count == 0)
        throw new InvalidOperationException("Loop proof generation produced no motion segments.");

    const int cycleMs = 24000;
    const int resetMs = 80;
    var rootEntries = new List<CPlugPrefab.EntRef>(segments.Count * 2);

    for (int i = 0; i < segments.Count; i++)
    {
        var segment = segments[i];
        var startMs = (int)Math.Round(i * cycleMs / (double)segments.Count);
        var endMs = (int)Math.Round((i + 1) * cycleMs / (double)segments.Count);
        var windowMs = Math.Max(250, endMs - startMs);
        var moveMs = Math.Max(170, windowMs - resetMs);
        var holdMs = Math.Max(0, cycleMs - startMs - moveMs - resetMs);

        var body = new CPlugDynaObjectModel
        {
            Version = 13,
            IsStatic = false,
            DynamizeOnSpawn = false,
            Mass = 100,
            BreakSpeedKmh = 100,
            Mesh = visualMesh,
            StaticShape = collisionShape,
            DynaShape = collisionShape
        };

        var translationKeys = new List<KC.SubAnimFunc>();
        if (startMs > 0)
            translationKeys.Add(new KC.SubAnimFunc { Ease = KC.AnimEase.Constant, Reverse = false, Duration = new TimeInt32(startMs) });
        translationKeys.Add(new KC.SubAnimFunc { Ease = KC.AnimEase.QuadInOut, Reverse = false, Duration = new TimeInt32(moveMs) });
        if (holdMs > 0)
            translationKeys.Add(new KC.SubAnimFunc { Ease = KC.AnimEase.Constant, Reverse = false, Duration = new TimeInt32(holdMs) });
        translationKeys.Add(new KC.SubAnimFunc { Ease = KC.AnimEase.Linear, Reverse = true, Duration = new TimeInt32(resetMs) });

        var constraint = new KC
        {
            Version = 0,
            SubVersion = 3,
            TransAxis = segment.Axis,
            TransMin = 0,
            TransMax = segment.Delta,
            RotAxis = KC.EAxis.Y,
            AngleMinDeg = 0,
            AngleMaxDeg = 0,
            TransAnimFunc = new KC.AnimFunc
            {
                IsDuration = true,
                SubFuncs = translationKeys.ToArray()
            },
            RotAnimFunc = new KC.AnimFunc
            {
                IsDuration = true,
                SubFuncs = [new KC.SubAnimFunc { Ease = KC.AnimEase.Linear, Reverse = false, Duration = new TimeInt32(cycleMs) }]
            }
        };

        rootEntries.Add(new CPlugPrefab.EntRef
        {
            Position = new Vec3(segment.Start.X, segment.Start.Y, segment.Start.Z),
            Rotation = new Quat(0, 0, 0, 1),
            Model = body,
            Params = new NPlugDynaObjectModel_SInstanceParams
            {
                Version = 2,
                PeriodSc = 1,
                PeriodScMax = -1,
                Phase01 = -1,
                Phase01Max = -1,
                TextureId = 0,
                IsKinematic = true,
                CastStaticShadow = i == 0
            }
        });
        rootEntries.Add(new CPlugPrefab.EntRef
        {
            Position = default,
            Rotation = new Quat(0, 0, 0, 1),
            Model = constraint,
            Params = new NPlugDyna_SPrefabConstraintParams
            {
                Version = 0,
                Ent1 = -1,
                Ent2 = i,
                Pos1 = default,
                Pos2 = default
            }
        });
    }

    template.EntityModel = new CPlugPrefab
    {
        Version = 11,
        Url = "",
        Ents = rootEntries.ToArray()
    };

    var outPath = Path.Combine(folder, "Proof_Relay_Coaster_Loop.Item.Gbx");
    File.WriteAllBytes(outPath, Save(template));
    var customOutPath = Path.Combine(folder, "CustomItem_Proof_Relay_Coaster_Loop.Item.Gbx");
    var customCopy = Reparse(Save(template));
    var customId = "CustomItem_Proof_Relay_Coaster_Loop";
    customCopy.Ident = new Ident(customId, template.Ident.Collection, template.Ident.Author);
    customCopy.Name = customId;
    customCopy.ArchetypeRef = string.IsNullOrWhiteSpace(inventoryFolder)
        ? customId
        : $"{inventoryFolder}\\{customId}";
    File.WriteAllBytes(customOutPath, Save(customCopy));
    Console.WriteLine($"Saved segmented relay loop proof to: {outPath}");
    Console.WriteLine($"Saved copy to: {customOutPath}");

    var reparsed = ParseItem(outPath);
    var scene = ItemScene.Build(reparsed, 0);
    var preview = AuthoredMotionPreview.Build(scene);
    Console.WriteLine($"Preview verification: tracks={preview.Tracks.Count}, diagnostics={preview.Diagnostics.Count}");
    foreach (var diag in preview.Diagnostics)
        Console.WriteLine($"  Diag: {diag}");

    return 0;
}
if (args.Length > 0 && args[0] == "--create-l-item")
{
    var folder = @"C:\Users\PC\Documents\Trackmania\Items\BF2_ASSETS\Test_Items";
    var customItemPath = Path.Combine(folder, "CustomItem.Item.Gbx");
    var existingItem = ParseItem(customItemPath);
    
    CPlugSolid2Model? userMesh = null;
    CPlugSurface? userShape = null;

    var staticItemPath = Path.Combine(repoRoot, @"Test Exported items\CustomItem_Static.Item.Gbx");
    if (File.Exists(staticItemPath))
    {
        var staticItem = ParseItem(staticItemPath);
        if (staticItem.EntityModel is CGameCommonItemEntityModel { StaticObject: CPlugStaticObjectModel som } && som.Mesh is not null)
        {
            userMesh = som.Mesh;
            userShape = ItemKinematicEntityTemplate.GenerateCollisionSurfaceFromMesh(userMesh);
            Console.WriteLine($"Loaded user mesh from CustomItem_Static: {(userShape?.Surf is CPlugSurface.Mesh sm ? sm.Vertices.Length : 0)} verts");
        }
    }

    if (userMesh is null)
    {
        var existingPrefab = (CPlugPrefab)existingItem.EntityModel!;
        var largestDyna = existingPrefab.Ents!
            .Select(e => e.Model as CPlugDynaObjectModel)
            .Where(d => d?.Mesh is not null)
            .OrderByDescending(d => d!.Mesh!.Visuals?.Length ?? 0)
            .FirstOrDefault();
        userMesh = largestDyna?.Mesh!;
        userShape = ItemKinematicEntityTemplate.GenerateCollisionSurfaceFromMesh(userMesh)
            ?? largestDyna?.StaticShape as CPlugSurface;
    }

    // 1. Clean Topological Parent-Child L-Path (Carrier Ent 0 -> Visible Ent 2)
    {
        var template = ItemKinematicEntityTemplate.GetDefaultMovingTemplate()!;
        template.Ident = existingItem.Ident;
        template.Name = existingItem.Name;
        template.DefaultPlacement = existingItem.DefaultPlacement;

        var carrierMesh = (CPlugSolid2Model)typeof(ItemKinematicEntityTemplate)
            .GetMethod("CreateCarrierMesh", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, null)!;
        var carrierShape = (CPlugSurface)typeof(ItemKinematicEntityTemplate)
            .GetMethod("CreateCarrierShape", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, null)!;

        var carrierBody = new CPlugDynaObjectModel
        {
            Version = 13,
            IsStatic = false,
            DynamizeOnSpawn = false,
            Mass = 100,
            BreakSpeedKmh = 100,
            Mesh = carrierMesh,
            StaticShape = carrierShape,
            DynaShape = carrierShape
        };
        var carrierInstance = new NPlugDynaObjectModel_SInstanceParams
        {
            Version = 2,
            PeriodSc = 1,
            PeriodScMax = -1,
            Phase01 = -1,
            Phase01Max = -1,
            TextureId = 0,
            IsKinematic = true,
            CastStaticShadow = false
        };

        var visibleBody = new CPlugDynaObjectModel
        {
            Version = 13,
            IsStatic = false,
            DynamizeOnSpawn = false,
            Mass = 100,
            BreakSpeedKmh = 100,
            Mesh = userMesh,
            StaticShape = userShape,
            DynaShape = userShape
        };
        var visibleInstance = new NPlugDynaObjectModel_SInstanceParams
        {
            Version = 2,
            PeriodSc = 1,
            PeriodScMax = -1,
            Phase01 = -1,
            Phase01Max = -1,
            TextureId = 0,
            IsKinematic = true,
            CastStaticShadow = true
        };

        // Constraint 0: Carrier to World (Ent1 = -1, Ent2 = 0) -> Moves X 0..5
        var c0 = new KC
        {
            Version = 0,
            SubVersion = 3,
            TransAxis = KC.EAxis.X,
            TransMin = 0,
            TransMax = 5,
            RotAxis = KC.EAxis.Y,
            AngleMinDeg = 0,
            AngleMaxDeg = 0,
            TransAnimFunc = new KC.AnimFunc
            {
                IsDuration = true,
                SubFuncs =
                [
                    new() { Ease = KC.AnimEase.QuadInOut, Reverse = false, Duration = new TimeInt32(2000) },
                    new() { Ease = KC.AnimEase.Constant, Reverse = true, Duration = new TimeInt32(4000) },
                    new() { Ease = KC.AnimEase.QuadInOut, Reverse = true, Duration = new TimeInt32(2000) }
                ]
            },
            RotAnimFunc = new KC.AnimFunc
            {
                IsDuration = true,
                SubFuncs = [new() { Ease = KC.AnimEase.Linear, Reverse = false, Duration = new TimeInt32(6600) }]
            }
        };
        var p0 = new NPlugDyna_SPrefabConstraintParams
        {
            Version = 0,
            Ent1 = -1,
            Ent2 = 0,
            Pos1 = default,
            Pos2 = default
        };

        // Constraint 1: Visible to Carrier (Ent1 = 0, Ent2 = 1) -> Moves Y 0..5
        var c1 = new KC
        {
            Version = 0,
            SubVersion = 3,
            TransAxis = KC.EAxis.Y,
            TransMin = 0,
            TransMax = 5,
            RotAxis = KC.EAxis.Y,
            AngleMinDeg = 0,
            AngleMaxDeg = 0,
            TransAnimFunc = new KC.AnimFunc
            {
                IsDuration = true,
                SubFuncs =
                [
                    new() { Ease = KC.AnimEase.Constant, Reverse = false, Duration = new TimeInt32(2000) },
                    new() { Ease = KC.AnimEase.QuadInOut, Reverse = false, Duration = new TimeInt32(2000) },
                    new() { Ease = KC.AnimEase.QuadInOut, Reverse = true, Duration = new TimeInt32(2000) },
                    new() { Ease = KC.AnimEase.Constant, Reverse = false, Duration = new TimeInt32(2000) }
                ]
            },
            RotAnimFunc = new KC.AnimFunc
            {
                IsDuration = true,
                SubFuncs = [new() { Ease = KC.AnimEase.Linear, Reverse = false, Duration = new TimeInt32(6600) }]
            }
        };
        var p1 = new NPlugDyna_SPrefabConstraintParams
        {
            Version = 0,
            Ent1 = 0,
            Ent2 = 1,
            Pos1 = default,
            Pos2 = default
        };

        var prefab = (CPlugPrefab)template.EntityModel!;
        prefab.Ents =
        [
            new() { Model = carrierBody, Params = carrierInstance, Position = default, Rotation = new(0, 0, 0, 1), U01 = "" },
            new() { Model = c0, Params = p0, Position = default, Rotation = new(0, 0, 0, 1), U01 = "" },
            new() { Model = visibleBody, Params = visibleInstance, Position = default, Rotation = new(0, 0, 0, 1), U01 = "" },
            new() { Model = c1, Params = p1, Position = default, Rotation = new(0, 0, 0, 1), U01 = "" }
        ];

        File.WriteAllBytes(customItemPath, Save(template));
        Console.WriteLine($"[1] Saved clean topological L-item to: {customItemPath}");
    }

    // 2. Single Body Dual Root Constraints (Ent1 = -1, Ent2 = 0 on both)
    {
        var template = ItemKinematicEntityTemplate.GetDefaultMovingTemplate()!;
        template.Ident = existingItem.Ident;
        template.Name = existingItem.Name;
        template.DefaultPlacement = existingItem.DefaultPlacement;

        var visibleBody = new CPlugDynaObjectModel
        {
            Version = 13,
            IsStatic = false,
            DynamizeOnSpawn = false,
            Mass = 100,
            BreakSpeedKmh = 100,
            Mesh = userMesh,
            StaticShape = userShape,
            DynaShape = userShape
        };
        var visibleInstance = new NPlugDynaObjectModel_SInstanceParams
        {
            Version = 2,
            PeriodSc = 1,
            PeriodScMax = -1,
            Phase01 = -1,
            Phase01Max = -1,
            TextureId = 0,
            IsKinematic = true,
            CastStaticShadow = true
        };

        // Constraint 0: X axis (0..5m)
        var c0 = new KC
        {
            Version = 0,
            SubVersion = 3,
            TransAxis = KC.EAxis.X,
            TransMin = 0,
            TransMax = 5,
            RotAxis = KC.EAxis.Y,
            AngleMinDeg = 0,
            AngleMaxDeg = 0,
            TransAnimFunc = new KC.AnimFunc
            {
                IsDuration = true,
                SubFuncs =
                [
                    new() { Ease = KC.AnimEase.QuadInOut, Reverse = false, Duration = new TimeInt32(2000) },
                    new() { Ease = KC.AnimEase.Constant, Reverse = false, Duration = new TimeInt32(4000) },
                    new() { Ease = KC.AnimEase.QuadInOut, Reverse = true, Duration = new TimeInt32(2000) }
                ]
            },
            RotAnimFunc = new KC.AnimFunc
            {
                IsDuration = true,
                SubFuncs = [new() { Ease = KC.AnimEase.Linear, Reverse = false, Duration = new TimeInt32(6600) }]
            }
        };
        var p0 = new NPlugDyna_SPrefabConstraintParams
        {
            Version = 0,
            Ent1 = -1,
            Ent2 = 0,
            Pos1 = default,
            Pos2 = default
        };

        // Constraint 1: Y axis (0..5m)
        var c1 = new KC
        {
            Version = 0,
            SubVersion = 3,
            TransAxis = KC.EAxis.Y,
            TransMin = 0,
            TransMax = 5,
            RotAxis = KC.EAxis.Y,
            AngleMinDeg = 0,
            AngleMaxDeg = 0,
            TransAnimFunc = new KC.AnimFunc
            {
                IsDuration = true,
                SubFuncs =
                [
                    new() { Ease = KC.AnimEase.Constant, Reverse = false, Duration = new TimeInt32(2000) },
                    new() { Ease = KC.AnimEase.QuadInOut, Reverse = false, Duration = new TimeInt32(2000) },
                    new() { Ease = KC.AnimEase.QuadInOut, Reverse = true, Duration = new TimeInt32(2000) },
                    new() { Ease = KC.AnimEase.Constant, Reverse = false, Duration = new TimeInt32(2000) }
                ]
            },
            RotAnimFunc = new KC.AnimFunc
            {
                IsDuration = true,
                SubFuncs = [new() { Ease = KC.AnimEase.Linear, Reverse = false, Duration = new TimeInt32(6600) }]
            }
        };
        var p1 = new NPlugDyna_SPrefabConstraintParams
        {
            Version = 0,
            Ent1 = -1,
            Ent2 = 0,
            Pos1 = default,
            Pos2 = default
        };

        var prefab = (CPlugPrefab)template.EntityModel!;
        prefab.Ents =
        [
            new() { Model = visibleBody, Params = visibleInstance, Position = default, Rotation = new(0, 0, 0, 1), U01 = "" },
            new() { Model = c0, Params = p0, Position = default, Rotation = new(0, 0, 0, 1), U01 = "" },
            new() { Model = c1, Params = p1, Position = default, Rotation = new(0, 0, 0, 1), U01 = "" }
        ];

        var dualPath = Path.Combine(folder, "CustomItem_DualRoot.Item.Gbx");
        File.WriteAllBytes(dualPath, Save(template));
        Console.WriteLine($"[2] Saved single-body dual-root L-item to: {dualPath}");
    }

    return 0;
}

int passed = 0, failed = 0;
Console.WriteLine("Bundled parser SHA256: " + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Gbx).Assembly.Location))).ToLowerInvariant());
var kinematicPath = Path.Combine(repoRoot, "Test Exported items/CustomItem_Kinematic.Item.Gbx");
var staticPath = Path.Combine(repoRoot, "Test Exported items/CustomItem_Static.Item.Gbx");

Check("tracked fixture identity pins the analyzed archives", () =>
{
    Require(Sha256(kinematicPath) == "a51f35b4b589cf39fd2fd3fdf0f9152c1b01b26091b3c76c467d67aad84796f4", "kinematic fixture bytes changed");
    Require(Sha256(staticPath) == "afde25629cd7030844b1cbf2f1c400bed1280205f7c1e167b6d89048ddf4939c", "static fixture bytes changed");
    var kinematic = ParseItem(kinematicPath);
    var @static = ParseItem(staticPath);
    Require(kinematic.Ident.Author != @static.Ident.Author, "pair provenance assumption changed: same author");
});

Check("kinematic archive parses into the full mapped graph", () => VerifyKinematic(ParseItem(kinematicPath)));

Check("static archive parses into the common-item graph", () => VerifyStatic(ParseItem(staticPath)));

Check("parse, save and reparse preserve both tracked archives structurally", () =>
{
    foreach (var path in new[] { kinematicPath, staticPath })
    {
        var original = ParseItem(path);
        var first = Save(original);
        var reparsed = Reparse(first);
        if (path == kinematicPath) VerifyKinematic(reparsed); else VerifyStatic(reparsed);
        var second = Save(reparsed);
        Require(first.SequenceEqual(second), "second save of " + Path.GetFileName(path) + " is not byte-stable");
    }
});

Check("constraint and timeline fields round-trip through the typed adapter", () =>
{
    var item = ParseItem(kinematicPath);
    var constraint = KinematicConstraint(item);
    var fields = ItemMotion.Read(constraint).Fields;
    Require(fields.TranslationAxis == KC.EAxis.Y && fields.TranslationMin == 0 && fields.TranslationMax == 1, "translation scalars lost");
    Require(fields.RotationAxis == KC.EAxis.Y && fields.AngleMinDegrees == 0 && fields.AngleMaxDegrees == 0, "rotation scalars lost");
    Require(fields.Translation!.IsDuration, "duration flag lost");
    Require(fields.Translation.Keys.Count == 2
        && fields.Translation.Keys[0].Ease == KC.AnimEase.QuadInOut && !fields.Translation.Keys[0].Reverse && fields.Translation.Keys[0].DurationMilliseconds == 10000
        && fields.Translation.Keys[1].Ease == KC.AnimEase.QuadInOut && fields.Translation.Keys[1].Reverse && fields.Translation.Keys[1].DurationMilliseconds == 10000,
        "translation keys lost");
    Require(fields.Rotation!.Keys.Count == 1 && fields.Rotation.Keys[0].Ease == KC.AnimEase.Linear
        && fields.Rotation.Keys[0].DurationMilliseconds == 10000 && !fields.Rotation.Keys[0].Reverse, "rotation keys lost");
    var before = Save(item);
    Require(ItemMotion.Apply(constraint, fields).Success, "unchanged edit refused");
    Require(Save(item).SequenceEqual(before), "unchanged edit rewrote the archive");
    var edited = fields with { TranslationMax = 2, Translation = new(true, [new(KC.AnimEase.QuadOut, true, 2500)]) };
    Require(ItemMotion.Apply(constraint, edited).Success, "scalar/timeline edit refused");
    VerifyKinematic(Reparse(Save(item)), translationMax: 2, translationKeys: [(KC.AnimEase.QuadOut, true, 2500)]);
});

Check("binding resolver classifies the tracked constraint as world-relative and supported", () =>
{
    var item = ParseItem(kinematicPath);
    var prefab = (CPlugPrefab)item.EntityModel!;
    var constraint = KinematicConstraint(item);
    var parameters = (NPlugDyna_SPrefabConstraintParams)prefab.Ents[1].Params!;
    var binding = ItemMotionBindings.Resolve(constraint, prefab, parameters, ItemMotionBindings.RootPath(0, null));
    Require(binding.Status == ItemMotionStatus.Supported, binding.Reason ?? "world binding failed");
    Require(binding.Parent.IsWorld && binding.Parent.RawSlot == -1 && binding.Parent.OriginalArrayIndex is null
        && binding.Parent.Path == "doc:0/variant:none/root", "world parent misclassified");
    Require(binding.Child.RawSlot == 0 && binding.Child.OriginalArrayIndex == 0
        && binding.Child.Path == "doc:0/variant:none/root/ent:0", "child misclassified");
    Require(binding.Slots.Count == 1 && binding.Slots[0].OriginalArrayIndex == 0, "constraint entity leaked into the slot table");
});

Check("typed segment schema exposes no per-key waypoints", () =>
{
    var keyParameters = typeof(ItemMotionKey).GetConstructors().Single().GetParameters();
    Require(keyParameters.Select(p => p.Name).SequenceEqual(new[] { "Ease", "Reverse", "DurationMilliseconds" }),
        "timeline keys gained positional data");
    var editProperties = typeof(ItemMotionEdit).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToArray();
    Require(editProperties.SequenceEqual(new[] { "TranslationAxis", "TranslationMin", "TranslationMax",
        "RotationAxis", "AngleMinDegrees", "AngleMaxDegrees", "Translation", "Rotation" }), "channel fields changed shape");
    var constraint = KinematicConstraint(ParseItem(kinematicPath));
    var timeline = constraint.TransAnimFunc!;
    Require(timeline.SubFuncs!.Length == 2 && constraint.TransMin == 0 && constraint.TransMax == 1
        && timeline.SubFuncs.All(k => k.Duration.TotalMilliseconds is 10000), "keys do not share one scalar channel range");
});

Check("exported kinematic template carries explicit collision surface meshes", () =>
{
    var body = (CPlugDynaObjectModel)((CPlugPrefab)ParseItem(kinematicPath).EntityModel!).Ents[0].Model!;
    Require(body.StaticShape is CPlugSurface && body.DynaShape is CPlugSurface, "shape classes changed");
    Require(!ReferenceEquals(body.StaticShape, body.DynaShape), "shapes collapsed into one shared node");
    foreach (var shape in new[] { body.StaticShape, body.DynaShape })
    {
        var surface = (CPlugSurface)shape!;
        Require(surface.Surf is CPlugSurface.Mesh && surface.Geom is null && surface.Materials.Length == 0,
            "exported collision surfaces changed");
    }
});

Check("synthetic two-object/two-constraint item builds from scratch and reparses", () =>
{
    foreach (var interleaved in new[] { false, true })
    {
        var item = ExperimentItem(interleaved);
        using var bytes = new MemoryStream();
        new Gbx<CGameItemModel>(item) { BodyCompression = GbxCompression.Uncompressed }.Save(bytes);
        bytes.Position = 0;
        var reopened = Gbx.Parse<CGameItemModel>(bytes, new GbxReadSettings { SafeSkippableChunks = true }).Node;
        VerifyExperiment(reopened, interleaved);
    }
});

Check("binding resolver classifies world->A and A->B constraints as supported flat bindings", () =>
{
    foreach (var interleaved in new[] { false, true })
    {
        var prefab = (CPlugPrefab)ExperimentItem(interleaved).EntityModel!;
        var (aIx, bIx) = interleaved ? (0, 2) : (0, 1);
        var world = (KC)prefab.Ents[interleaved ? 1 : 2].Model!;
        var chained = (KC)prefab.Ents[3].Model!;
        var worldParams = (NPlugDyna_SPrefabConstraintParams)prefab.Ents[interleaved ? 1 : 2].Params!;
        var chainedParams = (NPlugDyna_SPrefabConstraintParams)prefab.Ents[3].Params!;
        var root = ItemMotionBindings.RootPath(0, null);
        var worldBinding = ItemMotionBindings.Resolve(world, prefab, worldParams, root);
        Require(worldBinding.Status == ItemMotionStatus.Supported, worldBinding.Reason ?? "world constraint failed");
        Require(worldBinding.Parent.IsWorld && worldBinding.Parent.RawSlot == -1 && worldBinding.Child.RawSlot == 0
            && worldBinding.Child.OriginalArrayIndex == aIx, "world constraint targets wrong slot");
        var chainedBinding = ItemMotionBindings.Resolve(chained, prefab, chainedParams, root);
        Require(chainedBinding.Status == ItemMotionStatus.Supported, chainedBinding.Reason ?? "chained constraint failed");
        Require(!chainedBinding.Parent.IsWorld && chainedBinding.Parent.RawSlot == 0
            && chainedBinding.Parent.OriginalArrayIndex == aIx && chainedBinding.Parent.Path == $"{root}/ent:{aIx}",
            "chained parent is not object A");
        Require(chainedBinding.Child.RawSlot == 1 && chainedBinding.Child.OriginalArrayIndex == bIx
            && chainedBinding.Child.Path == $"{root}/ent:{bIx}", "chained child is not object B");
        Require(chainedBinding.Slots.Count == 2 && chainedBinding.Slots[0].OriginalArrayIndex == aIx
            && chainedBinding.Slots[1].OriginalArrayIndex == bIx, "filtered slot table order changed with layout");
    }
});

Check("experiment graph keeps every binding-resolver guard active", () =>
{
    var prefab = (CPlugPrefab)ExperimentItem(false).EntityModel!;
    var chained = (KC)prefab.Ents[3].Model!;
    var parameters = (NPlugDyna_SPrefabConstraintParams)prefab.Ents[3].Params!;
    var root = ItemMotionBindings.RootPath(0, null);
    Require(ItemMotionBindings.Resolve(chained, prefab, parameters, $"{root}/ent:0").Status == ItemMotionStatus.Unsupported,
        "nested occurrence guard inactive");
    Require(ItemMotionBindings.Resolve(chained, prefab, parameters, "owned-edge", isNestedPrefabOccurrence: true).Status == ItemMotionStatus.Unsupported,
        "explicit nested guard inactive");
    parameters.Version = 1;
    Require(ItemMotionBindings.Resolve(chained, prefab, parameters, root).Status == ItemMotionStatus.Unsupported, "params version guard inactive");
    parameters.Version = 0; parameters.Pos1 = new(1, 0, 0);
    Require(ItemMotionBindings.Resolve(chained, prefab, parameters, root).Status == ItemMotionStatus.Unsupported, "anchor guard inactive");
    parameters.Pos1 = default; parameters.Ent1 = 1;
    Require(ItemMotionBindings.Resolve(chained, prefab, parameters, root).Status == ItemMotionStatus.Unsupported, "self-parent guard inactive");
    parameters.Ent1 = 0;
});

Check("chained constraint edits rebind and round-trip through save and reparse", () =>
{
    var item = ExperimentItem(false);
    var prefab = (CPlugPrefab)item.EntityModel!;
    var chained = (KC)prefab.Ents[3].Model!;
    var parameters = (NPlugDyna_SPrefabConstraintParams)prefab.Ents[3].Params!;
    Require(!ItemMotionBindings.ApplyTargets(chained, prefab, parameters, "root", 0, 9).Success
        && parameters.Ent1 == 0 && parameters.Ent2 == 1, "invalid rebind mutated targets");
    var fields = ItemMotion.Read(chained).Fields;
    Require(ItemMotion.Apply(chained, fields with { TranslationMax = 3,
        Translation = new(true, [new(KC.AnimEase.QuadInOut, false, 3000)]) }).Success, "chained edit refused");
    Require(ItemMotionBindings.ApplyTargets(chained, prefab, parameters, "root", -1, 1).Success, "decoupling rebind refused");
    using var bytes = new MemoryStream();
    new Gbx<CGameItemModel>(item) { BodyCompression = GbxCompression.Uncompressed }.Save(bytes);
    bytes.Position = 0;
    VerifyExperiment(Gbx.Parse<CGameItemModel>(bytes, new GbxReadSettings { SafeSkippableChunks = true }).Node, false,
        chainedTranslationMax: 3, chainedTranslationKeys: [(KC.AnimEase.QuadInOut, false, 3000)], chainedEnt1: -1);
});

Check("composed preview evaluates both constraints of the chain independently", () =>
{
    var prefab = (CPlugPrefab)ExperimentItem(false).EntityModel!;
    var world = (KC)prefab.Ents[2].Model!;
    var chained = (KC)prefab.Ents[3].Model!;
    // Both timelines total 3000 ms; at 0.75 s A is three quarters through its first
    // sweep and B is mid-key of its first: A at +3 m on X, B at +1 m on Z, B at -45 deg.
    var aLive = Value(ItemMotion.Evaluate(world, 0.75));
    var bSignal = Value(ItemMotion.Evaluate(chained, 0.75));
    Vector(aLive.Translation, new(3, 0, 0));
    Vector(bSignal.Translation, new(0, 0, 1));
    NearV(bSignal.AngleDegrees, -45); // -90 -> 90, a quarter through
    var childRest = Matrix4x4.CreateTranslation(2, 0, 0);   // B rests 2 m from A
    var parentRest = Matrix4x4.Identity;                     // A rests at the prefab origin
    var parentLive = Matrix4x4.CreateTranslation(aLive.Translation);
    var composed = Value(ItemMotionTransforms.ComposeVisual(childRest, parentRest, parentLive, bSignal.Signal));
    Vector(Vector3.Transform(Vector3.Zero, composed), new(5, 0, 1));
});

Check("nested prefab item DeathPit parses, collects flattened slots, and resolves all 3 constraints", () =>
{
    var deathPitPath = Path.Combine(repoRoot, "Tests/Browser/Fixtures/Approved/TM2020/DeathPit.Item.gbx");
    if (!File.Exists(deathPitPath)) return;
    var item = ParseItem(deathPitPath);
    var rootPrefab = (CPlugPrefab)item.EntityModel!;
    var (slots, error, status) = ItemMotionBindings.CollectSlots(rootPrefab, "root");
    Require(error is null, "failed to collect slots: " + error);
    Require(slots.Count == 3, $"expected 3 flattened kinematic slots, got {slots.Count}");
    Require(slots[0].Path == "root/ent:0/ent:0", "slot 0 path mismatch");
    Require(slots[1].Path == "root/ent:1/ent:0", "slot 1 path mismatch");
    Require(slots[2].Path == "root/ent:2/ent:0", "slot 2 path mismatch");

    var sub0 = (CPlugPrefab)rootPrefab.Ents![0].Model!;
    var c0 = (KC)sub0.Ents![1].Model!;
    var p0 = (NPlugDyna_SPrefabConstraintParams)sub0.Ents![1].Params!;
    var b0 = ItemMotionBindings.Resolve(c0, slots, p0, "root");
    Require(b0.Status == ItemMotionStatus.Supported, "constraint 0 unsupported: " + b0.Reason);
    Require(b0.Parent.IsWorld, "constraint 0 should be world-relative");
    Require(b0.Child.Path == "root/ent:0/ent:0", "constraint 0 child mismatch");

    var sub1 = (CPlugPrefab)rootPrefab.Ents![1].Model!;
    var c1 = (KC)sub1.Ents![1].Model!;
    var p1 = (NPlugDyna_SPrefabConstraintParams)sub1.Ents![1].Params!;
    var b1 = ItemMotionBindings.Resolve(c1, slots, p1, "root");
    Require(b1.Status == ItemMotionStatus.Supported, "constraint 1 unsupported: " + b1.Reason);
    Require(!b1.Parent.IsWorld && b1.Parent.Path == "root/ent:0/ent:0", "constraint 1 parent mismatch");
    Require(b1.Child.Path == "root/ent:1/ent:0", "constraint 1 child mismatch");

    var sub2 = (CPlugPrefab)rootPrefab.Ents![2].Model!;
    var c2 = (KC)sub2.Ents![1].Model!;
    var p2 = (NPlugDyna_SPrefabConstraintParams)sub2.Ents![1].Params!;
    var b2 = ItemMotionBindings.Resolve(c2, slots, p2, "root");
    Require(b2.Status == ItemMotionStatus.Supported, "constraint 2 unsupported: " + b2.Reason);
    Require(!b2.Parent.IsWorld && b2.Parent.Path == "root/ent:1/ent:0", "constraint 2 parent mismatch");
    Require(b2.Child.Path == "root/ent:2/ent:0", "constraint 2 child mismatch");
});

Check("AuthoredMotionPreview builds all 3 valid tracks for DeathPit without diagnostics", () =>
{
    var deathPitPath = Path.Combine(repoRoot, "Tests/Browser/Fixtures/Approved/TM2020/DeathPit.Item.gbx");
    if (!File.Exists(deathPitPath)) return;
    var item = ParseItem(deathPitPath);
    var scene = ItemScene.Build(item, 0, null);
    var preview = AuthoredMotionPreview.Build(scene);
    Require(preview.Diagnostics.Count == 0, $"expected 0 diagnostics, got {preview.Diagnostics.Count}: {string.Join("; ", preview.Diagnostics)}");
    Require(preview.Tracks.Count == 3, $"expected 3 tracks, got {preview.Tracks.Count}");
    Require(preview.Tracks[0].ParentPath is null, "track 0 should be root/world");
    Require(preview.Tracks[0].ChildPath == "doc:0/variant:none/root/entityModel/ent:0/ent:0", "track 0 child mismatch");
    Require(preview.Tracks[1].ParentPath == "doc:0/variant:none/root/entityModel/ent:0/ent:0", "track 1 parent mismatch");
    Require(preview.Tracks[1].ChildPath == "doc:0/variant:none/root/entityModel/ent:1/ent:0", "track 1 child mismatch");
    Require(preview.Tracks[2].ParentPath == "doc:0/variant:none/root/entityModel/ent:1/ent:0", "track 2 parent mismatch");
    Require(preview.Tracks[2].ChildPath == "doc:0/variant:none/root/entityModel/ent:2/ent:0", "track 2 child mismatch");
});

Console.WriteLine($"{passed} passed, {failed} failed.");
return failed == 0 ? 0 : 1;

void Check(string name, Action action) { try { action(); passed++; Console.WriteLine("PASS: " + name); } catch (Exception e) { failed++; Console.Error.WriteLine("FAIL: " + name + ": " + e); } }
static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TM-Item-Studio.csproj"))) dir = dir.Parent;
    return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
}
static string Sha256(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
static CGameItemModel ParseItem(string path) => Gbx.Parse<CGameItemModel>(
    new MemoryStream(File.ReadAllBytes(path)), new GbxReadSettings { SafeSkippableChunks = true }).Node;
static byte[] Save(CGameItemModel node)
{
    using var stream = new MemoryStream();
    new Gbx<CGameItemModel>(node).Save(stream);
    return stream.ToArray();
}
static CGameItemModel Reparse(byte[] bytes) => Gbx.Parse<CGameItemModel>(
    new MemoryStream(bytes), new GbxReadSettings { SafeSkippableChunks = true }).Node;
static KC KinematicConstraint(CGameItemModel item) => (KC)((CPlugPrefab)item.EntityModel!).Ents[1].Model!;
static void Vector(Vector3 actual, Vector3 expected) { NearV(actual.X, expected.X); NearV(actual.Y, expected.Y); NearV(actual.Z, expected.Z); }
static void NearV(double actual, double expected) => Require(Math.Abs(actual - expected) < .0001, $"Expected {expected}, got {actual}");
static T Value<T>(ItemMotionResult<T> result) { Require(result.Success, result.Reason ?? "operation failed"); return result.Value!; }

// Issue #22 minimal experiment pair: object A constrained world->A, object B constrained A->B,
// distinct axes/ranges, synchronized 3000 ms timelines. interleaved reproduces the documented
// DTC_Firework200 alternating dyna-object/constraint entity layout.
static CGameItemModel ExperimentItem(bool interleaved)
{
    CPlugPrefab.EntRef Body(int offset) => new()
    {
        Position = new Vec3(offset, 0, 0),
        Rotation = new Quat(0, 0, 0, 1),
        Params = new NPlugDynaObjectModel_SInstanceParams { Version = 2, PeriodSc = 1, PeriodScMax = -1, Phase01 = -1, Phase01Max = -1, IsKinematic = true },
        Model = new CPlugDynaObjectModel { Version = 13, IsStatic = false, Mass = 10, BreakSpeedKmh = 100, Mesh = Solid(),
            StaticShape = new CPlugSurface(), DynaShape = new CPlugSurface() }
    };
    var a = Body(0);
    var b = Body(2);
    var world = ConstraintEntry(-1, 0, KC.EAxis.X, 0, 4, 0, 0,
        [(KC.AnimEase.Linear, false, 1000), (KC.AnimEase.Linear, false, 1000), (KC.AnimEase.Linear, false, 1000)],
        (KC.AnimEase.Linear, false, 3000));
    var chained = ConstraintEntry(0, 1, KC.EAxis.Z, 0, 2, -90, 90,
        [(KC.AnimEase.Linear, false, 1500), (KC.AnimEase.Linear, false, 1500)],
        (KC.AnimEase.Linear, false, 3000));
    var prefab = new CPlugPrefab { Version = 11, Ents = interleaved
        ? [a, world, b, chained]
        : [a, b, world, chained] };
    var item = new CGameItemModel
    {
        Ident = new Ident("KinematicExperiment", 26, "KinematicReference"),
        ItemType = CGameItemModel.EItemType.PickUp,
        EntityModel = prefab
    };
    item.CreateChunk<CGameCtnCollector.HeaderChunk2E001003>().Version = 8;
    item.CreateChunk<CGameItemModel.HeaderChunk2E002000>();
    item.CreateChunk<CGameItemModel.Chunk2E002015>();
    item.ItemTypeE = CGameItemModel.EItemType.PickUp;
    item.CreateChunk<CGameItemModel.Chunk2E002019>().Version = 15;
    return item;
}

static CPlugPrefab.EntRef ConstraintEntry(int ent1, int ent2, KC.EAxis axis, float min, float max,
    float rotMin, float rotMax, (KC.AnimEase Ease, bool Reverse, int Milliseconds)[] keys,
    (KC.AnimEase Ease, bool Reverse, int Milliseconds) rotKey) => new()
{
    Rotation = new Quat(0, 0, 0, 1),
    Params = new NPlugDyna_SPrefabConstraintParams { Ent1 = ent1, Ent2 = ent2 },
    Model = new KC
    {
        SubVersion = 3,
        TransAxis = axis, TransMin = min, TransMax = max,
        TransAnimFunc = new() { IsDuration = true, SubFuncs = keys.Select(k => new KC.SubAnimFunc { Ease = k.Ease, Reverse = k.Reverse, Duration = new TimeInt32(k.Milliseconds) }).ToArray() },
        RotAxis = KC.EAxis.Y, AngleMinDeg = rotMin, AngleMaxDeg = rotMax,
        RotAnimFunc = new() { IsDuration = true, SubFuncs = [new KC.SubAnimFunc { Ease = rotKey.Ease, Reverse = rotKey.Reverse, Duration = new TimeInt32(rotKey.Milliseconds) }] }
    }
};

static void VerifyExperiment(CGameItemModel item, bool interleaved, float chainedTranslationMax = 2,
    (KC.AnimEase Ease, bool Reverse, int Milliseconds)[]? chainedTranslationKeys = null, int chainedEnt1 = 0)
{
    var (aIx, bIx, worldIx, chainedIx) = interleaved ? (0, 2, 1, 3) : (0, 1, 2, 3);
    var prefab = (CPlugPrefab)item.EntityModel!;
    Require(prefab.Version == 11 && prefab.Ents!.Length == 4, "experiment envelope changed");
    foreach (var (ix, offset) in new[] { (aIx, 0), (bIx, 2) })
    {
        var entry = prefab.Ents[ix];
        var instance = (NPlugDynaObjectModel_SInstanceParams)entry.Params!;
        Require(instance.Version == 2 && instance.IsKinematic && instance.PeriodSc == 1 && instance.PeriodScMax == -1,
            "experiment body lost its kinematic classification");
        var dyna = (CPlugDynaObjectModel)entry.Model!;
        Require(dyna.Version == 13 && !dyna.IsStatic, "experiment body class changed");
        Require(dyna.StaticShape is CPlugSurface && dyna.DynaShape is CPlugSurface, "experiment shape fields lost");
        var solid = (CPlugSolid2Model)dyna.Mesh!;
        var visual = (CPlugVisualIndexedTriangles)solid.Visuals![0];
        var positions = visual.VertexStreams![0].Positions;
        Require(positions is [var p0, var p1, var p2] && p0 == new Vec3() && p1 == new Vec3(1, 0, 0) && p2 == new Vec3(0, 1, 0),
            "experiment body lost its authored triangle");
        Require(entry.Position.X == offset, "experiment rest transform lost");
    }
    var world = (NPlugDyna_SPrefabConstraintParams)prefab.Ents[worldIx].Params!;
    Require(world.Ent1 == -1 && world.Ent2 == 0 && world.Version == 0, "world constraint binding changed");
    var worldModel = (KC)prefab.Ents[worldIx].Model!;
    Require(worldModel.TransAxis == KC.EAxis.X && worldModel.TransMin == 0 && worldModel.TransMax == 4
        && worldModel.TransAnimFunc!.IsDuration && worldModel.TransAnimFunc.SubFuncs!.Length == 3
        && worldModel.TransAnimFunc.SubFuncs.All(k => k.Ease == KC.AnimEase.Linear && !k.Reverse && k.Duration.TotalMilliseconds == 1000),
        "world constraint channel changed");
    var chained = (NPlugDyna_SPrefabConstraintParams)prefab.Ents[chainedIx].Params!;
    Require(chained.Ent1 == chainedEnt1 && chained.Ent2 == 1 && chained.Version == 0
        && chained.Pos1 == default && chained.Pos2 == default, "chained constraint binding changed");
    var chainedModel = (KC)prefab.Ents[chainedIx].Model!;
    Require(chainedModel.TransAxis == KC.EAxis.Z && chainedModel.TransMin == 0 && Math.Abs(chainedModel.TransMax - chainedTranslationMax) < .0001,
        "chained translation channel changed");
    var expectedKeys = chainedTranslationKeys ?? [(KC.AnimEase.Linear, false, 1500), (KC.AnimEase.Linear, false, 1500)];
    var timeline = chainedModel.TransAnimFunc!;
    var subFuncs = timeline.SubFuncs!;
    Require(timeline.IsDuration && subFuncs.Length == expectedKeys.Length, "chained timeline count changed");
    for (int i = 0; i < expectedKeys.Length; i++)
        Require(subFuncs[i].Ease == expectedKeys[i].Item1 && subFuncs[i].Reverse == expectedKeys[i].Item2
            && subFuncs[i].Duration.TotalMilliseconds == expectedKeys[i].Item3, $"chained key {i} changed");
    Require(chainedModel.RotAxis == KC.EAxis.Y && chainedModel.AngleMinDeg == -90 && chainedModel.AngleMaxDeg == 90, "chained rotation channel changed");
    // Both constraints keep synchronized 3000 ms total timelines in every variant.
    Require(worldModel.TransAnimFunc!.SubFuncs!.Sum(k => k.Duration.TotalMilliseconds) == 3000
        && subFuncs.Sum(k => k.Duration.TotalMilliseconds) == 3000, "synchronized durations drifted");
}

// Mirrors Tests/Browser/FixtureGenerator: the bundled serializer exposes decoded arrays, but
// not its declarations or count setters. Reflection is restricted to this synthetic generator.
static CPlugSolid2Model Solid()
{
    const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
    var stream = new CPlugVertexStream { Positions = [new(), new(1, 0, 0), new(0, 1, 0)] };
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
    var solid = new CPlugSolid2Model { Visuals = [visual], CustomMaterials = [], ShadedGeoms = [] };
    solid.CreateChunk<CPlugSolid2Model.Chunk090BB000>().Version = 34;
    return solid;
}

static CPlugSolid2Model ProofCubeSolid(float size = 1f)
{
    const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
    var h = size / 2f;
    var verts = new[]
    {
        new Vec3(-h, -h, -h), // 0
        new Vec3( h, -h, -h), // 1
        new Vec3( h,  h, -h), // 2
        new Vec3(-h,  h, -h), // 3
        new Vec3(-h, -h,  h), // 4
        new Vec3( h, -h,  h), // 5
        new Vec3( h,  h,  h), // 6
        new Vec3(-h,  h,  h)  // 7
    };

    var indices = new[]
    {
        4, 5, 6, 4, 6, 7, // front
        0, 2, 1, 0, 3, 2, // back
        0, 4, 7, 0, 7, 3, // left
        1, 2, 6, 1, 6, 5, // right
        3, 7, 6, 3, 6, 2, // top
        0, 1, 5, 0, 5, 4  // bottom
    };

    var stream = new CPlugVertexStream { Positions = verts };
    var declaration = new CPlugVertexStream.DataDecl();
    typeof(CPlugVertexStream.DataDecl).GetField("flags1", flags)!.SetValue(declaration,
        (uint)CPlugVertexStream.EPlugVDcl.Position | ((uint)CPlugVertexStream.EPlugVDclType.Float3 << 9) | (12u << 18));
    typeof(CPlugVertexStream).GetField("dataDecls", flags)!.SetValue(stream, new[] { declaration });
    typeof(CPlugVertexStream).GetField("count", flags)!.SetValue(stream, verts.Length);
    stream.CreateChunk<CPlugVertexStream.Chunk09056000>().Version = 1;

    var indexBuffer = new CPlugIndexBuffer { Indices = indices };
    indexBuffer.CreateChunk<CPlugIndexBuffer.Chunk09057000>();

    var visual = new CPlugVisualIndexedTriangles
    {
        VertexStreams = [stream],
        IndexBuffer = indexBuffer,
        IsGeometryStatic = true,
        IsIndexationStatic = true,
        BoundingBox = new BoxAligned(-h, -h, -h, h, h, h)
    };
    typeof(CPlugVisual).GetProperty("Count", flags)!.SetValue(visual, verts.Length);
    visual.CreateChunk<CPlugVisual.Chunk0900600F>().Version = 6;
    visual.CreateChunk<CPlugVisualIndexed.Chunk0906A001>();

    var solid = new CPlugSolid2Model { Visuals = [visual], CustomMaterials = [], ShadedGeoms = [] };
    solid.CreateChunk<CPlugSolid2Model.Chunk090BB000>().Version = 34;
    return solid;
}

static List<(Vector3 Start, KC.EAxis Axis, float Delta)> BuildAxisRelaySegments(IReadOnlyList<Vector3> controlPoints)
{
    var segments = new List<(Vector3 Start, KC.EAxis Axis, float Delta)>();
    const float epsilon = 0.0001f;
    for (int i = 0; i < controlPoints.Count - 1; i++)
    {
        var cursor = controlPoints[i];
        var next = controlPoints[i + 1];

        var dx = next.X - cursor.X;
        if (Math.Abs(dx) > epsilon)
        {
            segments.Add((cursor, KC.EAxis.X, dx));
            cursor = new Vector3(next.X, cursor.Y, cursor.Z);
        }

        var dz = next.Z - cursor.Z;
        if (Math.Abs(dz) > epsilon)
        {
            segments.Add((cursor, KC.EAxis.Z, dz));
            cursor = new Vector3(cursor.X, cursor.Y, next.Z);
        }

        var dy = next.Y - cursor.Y;
        if (Math.Abs(dy) > epsilon)
            segments.Add((cursor, KC.EAxis.Y, dy));
    }

    return segments;
}

static bool HasGameRenderableMesh(CPlugSolid2Model solid)
{
    if ((solid.Visuals?.Length ?? 0) == 0) return false;
    if ((solid.CustomMaterials?.Length ?? 0) == 0) return false;
    if ((solid.ShadedGeoms?.Length ?? 0) == 0) return false;
    return true;
}

static CPlugDynaObjectModel? FindRenderableDynaBody(CPlugPrefab prefab)
{
    foreach (var entry in prefab.Ents ?? [])
    {
        if (entry.Model is CPlugDynaObjectModel dyna && dyna.Mesh is CPlugSolid2Model solid && HasGameRenderableMesh(solid))
            return dyna;
        if (entry.Model is CPlugPrefab nested)
        {
            var fromNested = FindRenderableDynaBody(nested);
            if (fromNested is not null)
                return fromNested;
        }
    }

    return null;
}

static string? TryGetInventoryRelativeFolder(string absoluteFolder)
{
    if (string.IsNullOrWhiteSpace(absoluteFolder))
        return null;
    var normalized = absoluteFolder.Replace('/', '\\');
    var marker = "\\Items\\";
    var markerIx = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
    if (markerIx < 0)
        return null;
    var relative = normalized[(markerIx + marker.Length)..].Trim('\\');
    return string.IsNullOrWhiteSpace(relative) ? null : relative;
}

static void VerifyKinematic(CGameItemModel item, float translationMax = 1,
    (KC.AnimEase Ease, bool Reverse, int Milliseconds)[]? translationKeys = null)
{
    Require(item.ItemType == CGameItemModel.EItemType.Ornament && item.ItemTypeE == CGameItemModel.EItemType.Ornament,
        "item type changed");
    Require(item.DefaultPlacement is not null, "default placement lost");
    var prefab = (CPlugPrefab)(item.EntityModel ?? throw new Exception("kinematic archive lost its prefab entity model"));
    Require(prefab.Version == 11 && prefab.Ents!.Length == 2, "prefab envelope changed");
    var body = prefab.Ents[0];
    Require(body.ModelFile is null && body.Model is CPlugDynaObjectModel, "body entry changed");
    var instance = (NPlugDynaObjectModel_SInstanceParams)body.Params!;
    Require(instance.Version == 2 && instance.PeriodSc == 1 && instance.PeriodScMax == -1 && instance.Phase01 == -1
        && instance.Phase01Max == -1 && instance.TextureId == 0 && instance.IsKinematic && instance.CastStaticShadow,
        "instance params changed");
    var dyna = (CPlugDynaObjectModel)body.Model!;
    Require(dyna.Version == 13 && !dyna.IsStatic && !dyna.DynamizeOnSpawn && dyna.Mass == 100 && dyna.BreakSpeedKmh == 100,
        "dyna object fields changed");
    Require(dyna.Mesh is CPlugSolid2Model && dyna.StaticShape is CPlugSurface && dyna.DynaShape is CPlugSurface && dyna.LocAnim is null,
        "dyna subgraphs changed");
    var constraintEntry = prefab.Ents[1];
    var constraint = (KC)constraintEntry.Model!;
    Require(constraint.Version == 0 && constraint.SubVersion == 3, "constraint versions changed");
    Require(constraint.TransAxis == KC.EAxis.Y && constraint.TransMin == 0 && Near0(constraint.TransMax - translationMax), "translation channel changed");
    Require(constraint.RotAxis == KC.EAxis.Y && constraint.AngleMinDeg == 0 && constraint.AngleMaxDeg == 0, "rotation channel changed");
    Require(constraint.ShaderTcType == KC.EShaderTcType.None && constraint.ShaderTcAnimFunc is null or { Length: 0 }, "shader channel changed");
    var timeline = constraint.TransAnimFunc!;
    Require(timeline.IsDuration, "duration semantics changed");
    var expected = translationKeys ?? new[] { (KC.AnimEase.QuadInOut, false, 10000), (KC.AnimEase.QuadInOut, true, 10000) };
    Require(timeline.SubFuncs!.Length == expected.Length, "translation key count changed");
    for (int i = 0; i < expected.Length; i++)
        Require(timeline.SubFuncs[i].Ease == expected[i].Item1 && timeline.SubFuncs[i].Reverse == expected[i].Item2
            && timeline.SubFuncs[i].Duration.TotalMilliseconds == expected[i].Item3, $"translation key {i} changed");
    var rotation = constraint.RotAnimFunc!;
    Require(rotation.IsDuration && rotation.SubFuncs!.Length == 1 && rotation.SubFuncs[0].Ease == KC.AnimEase.Linear
        && !rotation.SubFuncs[0].Reverse && rotation.SubFuncs[0].Duration.TotalMilliseconds == 10000, "rotation timeline changed");
    var parameters = (NPlugDyna_SPrefabConstraintParams)constraintEntry.Params!;
    Require(parameters.Version == 0 && parameters.Ent1 == -1 && parameters.Ent2 == 0
        && parameters.Pos1 == default && parameters.Pos2 == default, "constraint binding changed");
    static bool Near0(double d) => Math.Abs(d) < .0001;
}

static void VerifyStatic(CGameItemModel item)
{
    Require(item.ItemType == CGameItemModel.EItemType.Ornament && item.ItemTypeE == CGameItemModel.EItemType.Ornament, "item type changed");
    Require(item.DefaultPlacement is not null, "default placement lost");
    var entity = (CGameCommonItemEntityModel)item.EntityModel!;
    Require(entity.VisModel is null && entity.PhyModel is null && entity.TriggerShape is null, "unexpected extra entity models");
    var body = (CPlugStaticObjectModel)entity.StaticObject!;
    Require(body.Version == 3 && body.Mesh is CPlugSolid2Model && body.Shape is null && body.IsMeshCollidable, "static body changed");
    Require(item.EntityModel is not CPlugPrefab, "static archive gained a prefab");
}

static void DumpItem(string path)
{
    var bytes = File.ReadAllBytes(path);
    Console.WriteLine($"# {Path.GetFileName(path)}");
    Console.WriteLine($"  sha256={Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()} bytes={bytes.Length}");
    using var decompressed = new MemoryStream();
    Gbx.Decompress(path, decompressed);
    Console.WriteLine($"  decompressed-bytes={decompressed.Length}");
    var file = Gbx.Parse<CGameItemModel>(new MemoryStream(bytes), new GbxReadSettings { SafeSkippableChunks = true });
    using var serialized = new MemoryStream();
    file.Save(serialized);
    _ = Gbx.Parse<CGameItemModel>(new MemoryStream(serialized.ToArray()), new GbxReadSettings { SafeSkippableChunks = true });
    Console.WriteLine($"  root={file.Node.GetType().Name} ident={file.Node.Ident} itemtype={file.Node.ItemType} itemtypee={file.Node.ItemTypeE}");
    Console.WriteLine($"  defaultplacement={(file.Node.DefaultPlacement is null ? "null" : "present")}");
    Console.WriteLine($"  parse-save-reparse=ok bytes={serialized.Length}");
    var seen = new HashSet<CMwNod>();
    DumpNode(file.Node.EntityModel, "  entity-model", seen, 0);
    Console.WriteLine();
}

static void DumpNode(CMwNod? node, string label, HashSet<CMwNod> seen, int depth)
{
    var indent = new string(' ', 2 + depth * 2);
    if (node is null) { Console.WriteLine($"{indent}{label}: null"); return; }
    if (!seen.Add(node)) { Console.WriteLine($"{indent}{label}: {TypeName(node)} <shared-ref-already-dumped>"); return; }
    switch (node)
    {
        case CPlugPrefab prefab:
            Console.WriteLine($"{indent}{label}: CPlugPrefab version={prefab.Version} url={prefab.Url ?? "null"} u01={prefab.U01} u02={prefab.U02} ents={prefab.Ents?.Length ?? -1}");
            for (int i = 0; i < (prefab.Ents?.Length ?? 0); i++)
            {
                var e = prefab.Ents![i];
                Console.WriteLine($"{indent}  ent[{i}]: model={TypeName(e.Model)} external={(e.ModelFile is null ? "null" : e.ModelFile.FilePath)} pos={e.Position} rot={e.Rotation} u01=\"{e.U01 ?? ""}\"");
                DumpParams(e.Params, indent + "    ", seen);
                DumpNode(e.Model, "model", seen, depth + 2);
            }
            return;
        case CGameCommonItemEntityModel common:
            Console.WriteLine($"{indent}{label}: CGameCommonItemEntityModel");
            Console.WriteLine($"{indent}  staticobject={TypeName(common.StaticObject)} vismodel={TypeName(common.VisModel)} phymodel={TypeName(common.PhyModel)} triggertype={TypeName(common.TriggerShape)}");
            DumpNode(common.StaticObject, "staticObject", seen, depth + 1);
            DumpNode(common.VisModel, "visModel", seen, depth + 1);
            DumpNode(common.PhyModel, "phyModel", seen, depth + 1);
            return;
        case NPlugItem_SVariantList variants:
            Console.WriteLine($"{indent}{label}: NPlugItem_SVariantList version={variants.Version} variants={variants.Variants?.Length ?? -1}");
            for (int i = 0; i < (variants.Variants?.Length ?? 0); i++)
            {
                var v = variants.Variants![i];
                Console.WriteLine($"{indent}  variant[{i}]: hidden={v.HiddenInManualCycle} tags={v.Tags?.Count ?? 0} entityModel={TypeName(v.EntityModel)} entityFile={(v.EntityModelFile is null ? "null" : v.EntityModelFile.FilePath)}");
                DumpNode(v.EntityModel, $"variant[{i}].entityModel", seen, depth + 2);
            }
            return;
        case KC constraint:
            Console.WriteLine($"{indent}{label}: NPlugDyna_SKinematicConstraint version={constraint.Version} subversion={constraint.SubVersion}");
            Console.WriteLine($"{indent}  trans: axis={constraint.TransAxis} min={constraint.TransMin} max={constraint.TransMax}");
            DumpTimeline("trans", constraint.TransAnimFunc, indent + "  ");
            Console.WriteLine($"{indent}  rot: axis={constraint.RotAxis} minDeg={constraint.AngleMinDeg} maxDeg={constraint.AngleMaxDeg}");
            DumpTimeline("rot", constraint.RotAnimFunc, indent + "  ");
            Console.WriteLine($"{indent}  shadertc: type={constraint.ShaderTcType} version={constraint.ShaderTcVersion} keys={constraint.ShaderTcAnimFunc?.Length.ToString() ?? "null"}");
            return;
        case CPlugDynaObjectModel dyna:
            Console.WriteLine($"{indent}{label}: CPlugDynaObjectModel version={dyna.Version} isstatic={dyna.IsStatic} dynamizeonspawn={dyna.DynamizeOnSpawn} mass={dyna.Mass} breakspeedkmh={dyna.BreakSpeedKmh}");
            Console.WriteLine($"{indent}  mesh={TypeName(dyna.Mesh)} staticshape={TypeName(dyna.StaticShape)} dynashape={TypeName(dyna.DynaShape)} locanim={TypeName(dyna.LocAnim)}");
            DumpNode(dyna.Mesh, "mesh", seen, depth + 2);
            DumpNode(dyna.StaticShape, "staticShape", seen, depth + 2);
            DumpNode(dyna.DynaShape, "dynaShape", seen, depth + 2);
            return;
        case CPlugSurface surface:
            Console.WriteLine($"{indent}{label}: CPlugSurface surf={surface.Surf?.GetType().Name ?? "null"} geom={TypeName(surface.Geom)} materials={surface.Materials?.Length.ToString() ?? "null"}");
            if (surface.Surf is CPlugSurface.Mesh surfMesh)
            {
                Console.WriteLine($"{indent}  surfMesh: verts={surfMesh.Vertices?.Length} tris={surfMesh.Triangles?.Length}");
                if (surfMesh.Vertices?.Length > 0)
                {
                    Console.WriteLine($"{indent}    vert[0]={surfMesh.Vertices[0]} vert[max]={surfMesh.Vertices[^1]}");
                }
            }
            return;
        case CPlugStaticObjectModel staticModel:
            Console.WriteLine($"{indent}{label}: CPlugStaticObjectModel version={staticModel.Version} mesh={TypeName(staticModel.Mesh)} shape={TypeName(staticModel.Shape)} meshcollidable={staticModel.IsMeshCollidable}");
            return;
        default:
            Console.WriteLine($"{indent}{label}: {TypeName(node)}");
            return;
    }
}

static void DumpParams(object? parameters, string indent, HashSet<CMwNod> seen)
{
    switch (parameters)
    {
        case null: Console.WriteLine($"{indent}params: null"); return;
        case NPlugDyna_SPrefabConstraintParams constraint:
            Console.WriteLine($"{indent}params: NPlugDyna_SPrefabConstraintParams version={constraint.Version} ent1={constraint.Ent1} ent2={constraint.Ent2} pos1={constraint.Pos1} pos2={constraint.Pos2}"); return;
        case NPlugDynaObjectModel_SInstanceParams instance:
            Console.WriteLine($"{indent}params: NPlugDynaObjectModel_SInstanceParams version={instance.Version} periodsc={instance.PeriodSc} periodscmax={instance.PeriodScMax} phase01={instance.Phase01} phase01max={instance.Phase01Max} textureid={instance.TextureId} iskinematic={instance.IsKinematic} caststaticshadow={instance.CastStaticShadow}"); return;
        default:
            Console.WriteLine($"{indent}params: {parameters.GetType().Name}"); return;
    }
}

static void DumpTimeline(string name, KC.AnimFunc? timeline, string indent)
{
    if (timeline is null) { Console.WriteLine($"{indent}{name}animfunc: null"); return; }
    Console.WriteLine($"{indent}{name}animfunc: isduration={timeline.IsDuration} keys={timeline.SubFuncs?.Length.ToString() ?? "null"}");
    for (int i = 0; i < (timeline.SubFuncs?.Length ?? 0); i++)
    {
        var k = timeline.SubFuncs![i];
        Console.WriteLine($"{indent}  key[{i}]: ease={k.Ease} reverse={k.Reverse} duration={k.Duration.TotalMilliseconds}ms");
    }
}

static string TypeName(CMwNod? node) => node?.GetType().Name ?? "null";
