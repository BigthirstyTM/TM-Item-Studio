using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;
using KC = GBX.NET.Engines.Meta.NPlugDyna_SKinematicConstraint;

static partial class Research
{
    // Parent-child control: the aircraft remains world-driven by the original
    // native constraint, while the cloth is a child of that same animated
    // aircraft. The child constraint has zero local motion; VertexTween is the
    // only local cloth deformation. This removes the independent-clock path.
    public static void GenerateSharedFlightParentChildControl(string output)
    {
        output = Path.GetFullPath(output);
        Require(!Directory.Exists(output) && !File.Exists(output), "Choose a new evidence directory");
        var root = Root();
        var sourceDir = Path.Combine(root, "artifacts/skyward-banner-20261023/flight-contrast-oneuv-v1");
        var sourceItemName = "TMIS_Skyward_OneUVFlightContrastCircleWave.Item.Gbx";
        var sourceMapName = "TMIS_Skyward_OneUVFlightContrastCircleWave.Map.Gbx";
        var sourceItemPath = Path.Combine(sourceDir, "files", sourceItemName);
        var sourceMapPath = Path.Combine(sourceDir, sourceMapName);
        var sourceItemBytes = File.ReadAllBytes(sourceItemPath);
        var sourceMapBytes = File.ReadAllBytes(sourceMapPath);

        var originalPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Trackmania/Items/BF2_ASSETS/Skyward_DoubleDecker_Plane/PromoPlane_Skyward.Item.Gbx");
        var original = Parse(File.ReadAllBytes(originalPath));
        var originalMesh = ((CPlugDynaObjectModel)((CPlugPrefab)original.Node.EntityModel!).Ents![0].Model!).Mesh!;
        var banner = ReadBanner((CPlugVisualIndexedTriangles)originalMesh.Visuals![BannerIndex]);
        var edge = banner.Positions.Where(p => p.Z >= banner.MaximumZ - .1f).ToArray();
        Require(edge.Length > 0, "Banner attachment edge could not be identified");
        var attachment = new Vec3(edge.Average(p => p.X) + FlightShiftX, edge.Average(p => p.Y), edge.Average(p => p.Z));

        var item = Parse(sourceItemBytes);
        var prefab = FlagPrefab(item.Node);
        Require(prefab.Ents is { Length: 3 }, "Expected rigid, cloth and one native constraint");
        Require(prefab.Ents![2].Model is KC, "Expected native kinematic constraint");
        var rigidHash = NodeHash(prefab.Ents[0].Model!);
        var cloth = (CPlugDynaObjectModel)prefab.Ents[1].Model!;
        var clothMesh = cloth.Mesh!;
        Require(clothMesh.Visuals is { Count: 5 }, "Expected five cloth LOD visuals");

        var shape = new CPlugSurface { Surf = new CPlugSurface.Mesh {
            Version = 6,
            Vertices = [new(0, 0, 0), new(.02f, 0, 0), new(0, .02f, 0)],
            Triangles = [new(new Int3(0, 1, 2), 0, 0, 0)]
        } };
        shape.CreateChunk<CPlugSurface.Chunk0900C003>().Version = 2;
        cloth.StaticShape = shape;
        cloth.DynaShape = shape;
        if (cloth.References.StaticShape.Descriptor is not null) cloth.References.StaticShape.Clear();
        if (cloth.References.DynaShape.Descriptor is not null) cloth.References.DynaShape.Clear();

        foreach (var visual in clothMesh.Visuals.Cast<CPlugVisualIndexedTriangles>())
        {
            var n = visual.Vertices.Length / Frames;
            Require(n * Frames == visual.Vertices.Length, "Cloth frame table is not divisible by 86");
            var unrotated = visual.Vertices.Select((v, index) =>
            {
                var frame = index / n;
                var angleFrame = frame == Frames - 1 ? 0 : frame;
                return v with { Position = Subtract(InverseYaw(v.Position, angleFrame), attachment) };
            }).ToArray();
            visual.Vertices = unrotated;
            SetCount(visual, unrotated.Length);
            visual.BoundingBox = Bounds(unrotated.Select(v => v.Position));
        }
        // This is a local offset because entity 1 is parented to entity 0.
        prefab.Ents[1].Position = attachment;
        ((NPlugDynaObjectModel_SInstanceParams)prefab.Ents[1].Params!).IsKinematic = true;

        var worldConstraint = (KC)prefab.Ents[2].Model!;
        var childConstraint = CloneNode(worldConstraint);
        childConstraint.TransMin = childConstraint.TransMax = 0;
        childConstraint.AngleMinDeg = childConstraint.AngleMaxDeg = 0;
        var childParams = new NPlugDyna_SPrefabConstraintParams { Version = 0, Ent1 = 0, Ent2 = 1 };
        var childEntry = new CPlugPrefab.EntRef {
            Position = default,
            Rotation = Identity,
            Model = childConstraint,
            Params = childParams
        };
        prefab.Ents = [prefab.Ents[0], prefab.Ents[1], prefab.Ents[2], childEntry];

        var bytes = Save(item);
        var check = Parse(bytes);
        var checkedPrefab = FlagPrefab(check.Node);
        Require(checkedPrefab.Ents!.Length == 4 && checkedPrefab.Ents.Count(e => e.Model is KC) == 2, "Parent-child graph did not survive save");
        Require(NodeHash(checkedPrefab.Ents[0].Model!) == rigidHash, "Textured aircraft entity changed");
        Require(checkedPrefab.Ents[1].Position == attachment, "Cloth local anchor did not survive save");
        var pWorld = (NPlugDyna_SPrefabConstraintParams)checkedPrefab.Ents[2].Params!;
        var pChild = (NPlugDyna_SPrefabConstraintParams)checkedPrefab.Ents[3].Params!;
        Require(pWorld.Ent1 == -1 && pWorld.Ent2 == 0, "World constraint binding changed");
        Require(pChild.Ent1 == 0 && pChild.Ent2 == 1, "Parent-child constraint binding changed");
        var checkedChild = (KC)checkedPrefab.Ents[3].Model!;
        Require(checkedChild.TransMin == 0 && checkedChild.TransMax == 0 && checkedChild.AngleMinDeg == 0 && checkedChild.AngleMaxDeg == 0, "Child constraint is not zero-local-motion");

        Directory.CreateDirectory(output);
        Directory.CreateDirectory(Path.Combine(output, "files"));
        var itemName = "TMIS_Skyward_OneUVParentChildFlightAnchor.Item.Gbx";
        var mapName = "TMIS_Skyward_OneUVParentChildFlightAnchor.Map.Gbx";
        File.WriteAllBytes(Path.Combine(output, "files", itemName), bytes);
        var dependencies = DependencyHashes.ToDictionary(p => p.Key, p => File.ReadAllBytes(Path.Combine(root, "Resources/Flags", p.Key)));
        foreach (var pair in dependencies) File.WriteAllBytes(Path.Combine(output, "files", pair.Key), pair.Value);
        var folder = "TMIS_SkywardParentChild_" + Guid.NewGuid().ToString("N")[..16];
        var map = Map(sourceMapBytes, bytes, itemName, dependencies, folder, "OneUVParentChildFlightAnchor", true);
        File.WriteAllBytes(Path.Combine(output, mapName), map);
        var manifest = new {
            operation = "offline-textured-flight-parent-child-native-constraint",
            sourceItemPath,
            sourceItemSha256 = Hash(sourceItemBytes),
            sourceMapPath,
            sourceMapSha256 = Hash(sourceMapBytes),
            itemName,
            itemSha256 = Hash(bytes),
            mapName,
            mapSha256 = Hash(map),
            embeddedFolder = folder,
            attachmentLocal = attachment,
            attachmentWorldAtPhase0 = new Vec3(FlightAnchorPosition.X + attachment.X, FlightAnchorPosition.Y + attachment.Y, FlightAnchorPosition.Z + attachment.Z),
            rigidTexturesPreserved = true,
            rigidEntityHashPreserved = true,
            nativeConstraintCount = 2,
            planeBinding = new { Ent1 = -1, Ent2 = 0 },
            clothBinding = new { Ent1 = 0, Ent2 = 1 },
            clothLocalConstraintZero = true,
            clothFramesUnrotated = true,
            clothUsesParentTransform = true,
            clothUsesVertexTweenForLocalWave = true,
            clothKinematic = true,
            runtimeTested = false,
            offlineChecksPassed = true,
            limitation = "The game must verify native parent-child composition and whether a kinematic VertexTween child keeps its local rack edge attached."
        };
        File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(manifest, Json));
        File.WriteAllText(Path.Combine(output, "README.md"), "# Textured Skyward flight with parent-child cloth constraint\n\nThe aircraft is driven by the original world-to-plane native constraint. The cloth is a kinematic child of the aircraft (Ent1=0, Ent2=1) with zero local native motion, so the aircraft's transform is inherited instead of sampled through an independent world clock. The cloth mesh keeps local VertexTween wave frames and is rebased to the measured rack contact. An inline triangle collision surface is included for the kinematic child.\n");
        Console.WriteLine(JsonSerializer.Serialize(manifest, Json));
    }

    // Native-layout A/B: keep the same parent-child graph but interleave each
    // body with its constraint, matching the alternate layout seen in native
    // multi-body items. The filtered kinematic slot bindings remain 0 -> 1.
    public static void GenerateSharedFlightParentChildInterleavedControl(string output)
    {
        output = Path.GetFullPath(output);
        Require(!Directory.Exists(output) && !File.Exists(output), "Choose a new evidence directory");
        var root = Root();
        var sourceDir = Path.Combine(root, "artifacts/skyward-banner-20261023/parent-child-flight-anchor-v1");
        var sourceItemName = "TMIS_Skyward_OneUVParentChildFlightAnchor.Item.Gbx";
        var sourceMapName = "TMIS_Skyward_OneUVParentChildFlightAnchor.Map.Gbx";
        var sourceItemPath = Path.Combine(sourceDir, "files", sourceItemName);
        var sourceMapPath = Path.Combine(sourceDir, sourceMapName);
        var sourceItemBytes = File.ReadAllBytes(sourceItemPath);
        var sourceMapBytes = File.ReadAllBytes(sourceMapPath);
        var item = Parse(sourceItemBytes);
        var prefab = FlagPrefab(item.Node);
        Require(prefab.Ents is { Length: 4 }, "Expected parent-child source graph");
        var originalOrder = prefab.Ents.ToArray();
        Require(originalOrder[2].Model is KC && originalOrder[3].Model is KC, "Expected two constraints");
        prefab.Ents = [originalOrder[0], originalOrder[2], originalOrder[1], originalOrder[3]];
        var bytes = Save(item);
        var check = Parse(bytes);
        var checkedPrefab = FlagPrefab(check.Node);
        Require(checkedPrefab.Ents!.Length == 4 && checkedPrefab.Ents[1].Model is KC && checkedPrefab.Ents[3].Model is KC, "Interleaved graph did not survive save");
        var world = (NPlugDyna_SPrefabConstraintParams)checkedPrefab.Ents[1].Params!;
        var child = (NPlugDyna_SPrefabConstraintParams)checkedPrefab.Ents[3].Params!;
        Require(world.Ent1 == -1 && world.Ent2 == 0 && child.Ent1 == 0 && child.Ent2 == 1, "Interleaved bindings changed");

        Directory.CreateDirectory(output);
        Directory.CreateDirectory(Path.Combine(output, "files"));
        var itemName = "TMIS_Skyward_OneUVParentChildInterleavedFlightAnchor.Item.Gbx";
        var mapName = "TMIS_Skyward_OneUVParentChildInterleavedFlightAnchor.Map.Gbx";
        File.WriteAllBytes(Path.Combine(output, "files", itemName), bytes);
        var dependencies = DependencyHashes.ToDictionary(p => p.Key, p => File.ReadAllBytes(Path.Combine(root, "Resources/Flags", p.Key)));
        foreach (var pair in dependencies) File.WriteAllBytes(Path.Combine(output, "files", pair.Key), pair.Value);
        var folder = "TMIS_SkywardParentChildInterleaved_" + Guid.NewGuid().ToString("N")[..16];
        var map = Map(sourceMapBytes, bytes, itemName, dependencies, folder, "OneUVParentChildInterleavedFlightAnchor", true);
        File.WriteAllBytes(Path.Combine(output, mapName), map);
        var manifest = new {
            operation = "offline-textured-flight-parent-child-native-constraint-interleaved-layout",
            sourceItemPath,
            sourceItemSha256 = Hash(sourceItemBytes),
            sourceMapPath,
            sourceMapSha256 = Hash(sourceMapBytes),
            itemName,
            itemSha256 = Hash(bytes),
            mapName,
            mapSha256 = Hash(map),
            embeddedFolder = folder,
            entityLayout = "plane,world-constraint,cloth,parent-child-constraint",
            planeBinding = new { Ent1 = -1, Ent2 = 0 },
            clothBinding = new { Ent1 = 0, Ent2 = 1 },
            runtimeTested = false,
            offlineChecksPassed = true,
            limitation = "The game must verify whether the interleaved native entity layout enables parent-child visual composition."
        };
        File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(manifest, Json));
        File.WriteAllText(Path.Combine(output, "README.md"), "# Skyward parent-child flight, interleaved entity layout\n\nThis is the same parent-child cloth experiment as parent-child-flight-anchor-v1, but serialized as plane, world constraint, cloth, parent-child constraint. The alternate ordering is tested because native multi-body items use both compact and alternating entity layouts.\n");
        Console.WriteLine(JsonSerializer.Serialize(manifest, Json));
    }

    // A/B control for the native visual/physics path: the cloth keeps the same
    // parent-child binding and local wave frames, but is not marked kinematic.
    // This checks whether kinematic classification is what prevents inherited
    // parent transforms in the editor render pass.
    public static void GenerateSharedFlightParentChildDynamicControl(string output)
    {
        output = Path.GetFullPath(output);
        Require(!Directory.Exists(output) && !File.Exists(output), "Choose a new evidence directory");
        var root = Root();
        var sourceDir = Path.Combine(root, "artifacts/skyward-banner-20261023/parent-child-flight-anchor-v1");
        var sourceItemName = "TMIS_Skyward_OneUVParentChildFlightAnchor.Item.Gbx";
        var sourceMapName = "TMIS_Skyward_OneUVParentChildFlightAnchor.Map.Gbx";
        var sourceItemPath = Path.Combine(sourceDir, "files", sourceItemName);
        var sourceMapPath = Path.Combine(sourceDir, sourceMapName);
        var item = Parse(File.ReadAllBytes(sourceItemPath));
        var prefab = FlagPrefab(item.Node);
        Require(prefab.Ents is { Length: 4 }, "Expected parent-child source graph");
        var clothParams = (NPlugDynaObjectModel_SInstanceParams)prefab.Ents![1].Params!;
        clothParams.IsKinematic = false;
        var bytes = Save(item);
        var check = Parse(bytes);
        Require(!((NPlugDynaObjectModel_SInstanceParams)FlagPrefab(check.Node).Ents![1].Params!).IsKinematic, "Dynamic child flag did not survive save");

        Directory.CreateDirectory(output);
        Directory.CreateDirectory(Path.Combine(output, "files"));
        var itemName = "TMIS_Skyward_OneUVDynamicParentChildFlightAnchor.Item.Gbx";
        var mapName = "TMIS_Skyward_OneUVDynamicParentChildFlightAnchor.Map.Gbx";
        File.WriteAllBytes(Path.Combine(output, "files", itemName), bytes);
        var dependencies = DependencyHashes.ToDictionary(p => p.Key, p => File.ReadAllBytes(Path.Combine(root, "Resources/Flags", p.Key)));
        foreach (var pair in dependencies) File.WriteAllBytes(Path.Combine(output, "files", pair.Key), pair.Value);
        var folder = "TMIS_SkywardDynamicParentChild_" + Guid.NewGuid().ToString("N")[..16];
        var map = Map(File.ReadAllBytes(sourceMapPath), bytes, itemName, dependencies, folder, "OneUVDynamicParentChildFlightAnchor", true);
        File.WriteAllBytes(Path.Combine(output, mapName), map);
        var manifest = new {
            operation = "offline-textured-flight-parent-child-dynamic-cloth",
            sourceItemPath,
            sourceItemSha256 = Hash(File.ReadAllBytes(sourceItemPath)),
            sourceMapPath,
            sourceMapSha256 = Hash(File.ReadAllBytes(sourceMapPath)),
            itemName,
            itemSha256 = Hash(bytes),
            mapName,
            mapSha256 = Hash(map),
            embeddedFolder = folder,
            clothBinding = new { Ent1 = 0, Ent2 = 1 },
            clothKinematic = false,
            runtimeTested = false,
            offlineChecksPassed = true,
            limitation = "The game must verify whether a dynamic parent-child cloth body inherits the aircraft transform without falling or detaching."
        };
        File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(manifest, Json));
        File.WriteAllText(Path.Combine(output, "README.md"), "# Skyward dynamic parent-child cloth control\n\nThis A/B file keeps the parent-child constraint graph and local VertexTween cloth but clears IsKinematic on the cloth body. It is intended to distinguish a kinematic inheritance limitation from a general parent-child composition limitation.\n");
        Console.WriteLine(JsonSerializer.Serialize(manifest, Json));
    }

    // Kinematic body with DynamizeOnSpawn enabled. This is a narrower A/B than
    // the dynamic-child crash test and keeps the inline collision surface.
    public static void GenerateSharedFlightParentChildDynamizeControl(string output)
    {
        output = Path.GetFullPath(output);
        Require(!Directory.Exists(output) && !File.Exists(output), "Choose a new evidence directory");
        var root = Root();
        var sourceDir = Path.Combine(root, "artifacts/skyward-banner-20261023/parent-child-flight-anchor-v1");
        var sourceItemName = "TMIS_Skyward_OneUVParentChildFlightAnchor.Item.Gbx";
        var sourceMapName = "TMIS_Skyward_OneUVParentChildFlightAnchor.Map.Gbx";
        var sourceItemPath = Path.Combine(sourceDir, "files", sourceItemName);
        var sourceMapPath = Path.Combine(sourceDir, sourceMapName);
        var item = Parse(File.ReadAllBytes(sourceItemPath));
        var prefab = FlagPrefab(item.Node);
        Require(prefab.Ents is { Length: 4 }, "Expected parent-child source graph");
        var clothBody = (CPlugDynaObjectModel)prefab.Ents![1].Model!;
        clothBody.DynamizeOnSpawn = true;
        var bytes = Save(item);
        var check = Parse(bytes);
        Require(((CPlugDynaObjectModel)FlagPrefab(check.Node).Ents![1].Model!).DynamizeOnSpawn, "DynamizeOnSpawn did not survive save");

        Directory.CreateDirectory(output);
        Directory.CreateDirectory(Path.Combine(output, "files"));
        var itemName = "TMIS_Skyward_OneUVDynamizeParentChildFlightAnchor.Item.Gbx";
        var mapName = "TMIS_Skyward_OneUVDynamizeParentChildFlightAnchor.Map.Gbx";
        File.WriteAllBytes(Path.Combine(output, "files", itemName), bytes);
        var dependencies = DependencyHashes.ToDictionary(p => p.Key, p => File.ReadAllBytes(Path.Combine(root, "Resources/Flags", p.Key)));
        foreach (var pair in dependencies) File.WriteAllBytes(Path.Combine(output, "files", pair.Key), pair.Value);
        var folder = "TMIS_SkywardDynamizeParentChild_" + Guid.NewGuid().ToString("N")[..16];
        var map = Map(File.ReadAllBytes(sourceMapPath), bytes, itemName, dependencies, folder, "OneUVDynamizeParentChildFlightAnchor", true);
        File.WriteAllBytes(Path.Combine(output, mapName), map);
        var manifest = new {
            operation = "offline-textured-flight-parent-child-dynamize-on-spawn",
            sourceItemPath,
            sourceItemSha256 = Hash(File.ReadAllBytes(sourceItemPath)),
            sourceMapPath,
            sourceMapSha256 = Hash(File.ReadAllBytes(sourceMapPath)),
            itemName,
            itemSha256 = Hash(bytes),
            mapName,
            mapSha256 = Hash(map),
            embeddedFolder = folder,
            clothBinding = new { Ent1 = 0, Ent2 = 1 },
            clothKinematic = true,
            clothDynamizeOnSpawn = true,
            runtimeTested = false,
            offlineChecksPassed = true,
            limitation = "The game must verify whether DynamizeOnSpawn is compatible with a kinematic parent-child cloth body."
        };
        File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(manifest, Json));
        File.WriteAllText(Path.Combine(output, "README.md"), "# Skyward parent-child cloth with DynamizeOnSpawn\n\nThis A/B keeps the stable kinematic parent-child graph and inline collision surface but enables DynamizeOnSpawn on the cloth body. It probes whether the native visual pass requires the body to be dynamically instantiated before applying the parent transform.\n");
        Console.WriteLine(JsonSerializer.Serialize(manifest, Json));
    }

    // Constraint-anchor A/B: express the rack contact through Pos1/Pos2 and
    // leave the cloth entity at the parent origin. This avoids relying on the
    // renderer to interpret a child entity Position as the attachment point.
    public static void GenerateSharedFlightParentChildAnchorParamControl(string output)
    {
        output = Path.GetFullPath(output);
        Require(!Directory.Exists(output) && !File.Exists(output), "Choose a new evidence directory");
        var root = Root();
        var sourceDir = Path.Combine(root, "artifacts/skyward-banner-20261023/parent-child-flight-anchor-v1");
        var sourceItemName = "TMIS_Skyward_OneUVParentChildFlightAnchor.Item.Gbx";
        var sourceMapName = "TMIS_Skyward_OneUVParentChildFlightAnchor.Map.Gbx";
        var sourceItemPath = Path.Combine(sourceDir, "files", sourceItemName);
        var sourceMapPath = Path.Combine(sourceDir, sourceMapName);
        var item = Parse(File.ReadAllBytes(sourceItemPath));
        var prefab = FlagPrefab(item.Node);
        Require(prefab.Ents is { Length: 4 }, "Expected parent-child source graph");
        var originalAttachment = prefab.Ents![1].Position;
        prefab.Ents[1].Position = default;
        var childParams = (NPlugDyna_SPrefabConstraintParams)prefab.Ents[3].Params!;
        childParams.Pos1 = originalAttachment;
        childParams.Pos2 = default;
        var bytes = Save(item);
        var check = Parse(bytes);
        var checkedPrefab = FlagPrefab(check.Node);
        Require(checkedPrefab.Ents![1].Position == default, "Child entity position was not cleared");
        var checkedParams = (NPlugDyna_SPrefabConstraintParams)checkedPrefab.Ents[3].Params!;
        Require(checkedParams.Ent1 == 0 && checkedParams.Ent2 == 1 && checkedParams.Pos1 == originalAttachment && checkedParams.Pos2 == default, "Constraint anchor params did not survive save");

        Directory.CreateDirectory(output);
        Directory.CreateDirectory(Path.Combine(output, "files"));
        var itemName = "TMIS_Skyward_OneUVParentChildAnchorParamFlight.Item.Gbx";
        var mapName = "TMIS_Skyward_OneUVParentChildAnchorParamFlight.Map.Gbx";
        File.WriteAllBytes(Path.Combine(output, "files", itemName), bytes);
        var dependencies = DependencyHashes.ToDictionary(p => p.Key, p => File.ReadAllBytes(Path.Combine(root, "Resources/Flags", p.Key)));
        foreach (var pair in dependencies) File.WriteAllBytes(Path.Combine(output, "files", pair.Key), pair.Value);
        var folder = "TMIS_SkywardAnchorParamParentChild_" + Guid.NewGuid().ToString("N")[..16];
        var map = Map(File.ReadAllBytes(sourceMapPath), bytes, itemName, dependencies, folder, "OneUVParentChildAnchorParamFlight", true);
        File.WriteAllBytes(Path.Combine(output, mapName), map);
        var manifest = new {
            operation = "offline-textured-flight-parent-child-constraint-anchor-params",
            sourceItemPath,
            sourceItemSha256 = Hash(File.ReadAllBytes(sourceItemPath)),
            sourceMapPath,
            sourceMapSha256 = Hash(File.ReadAllBytes(sourceMapPath)),
            itemName,
            itemSha256 = Hash(bytes),
            mapName,
            mapSha256 = Hash(map),
            embeddedFolder = folder,
            clothBinding = new { Ent1 = 0, Ent2 = 1 },
            childEntityPosition = new Vec3(0, 0, 0),
            childConstraintPos1 = originalAttachment,
            childConstraintPos2 = new Vec3(0, 0, 0),
            clothKinematic = true,
            runtimeTested = false,
            offlineChecksPassed = true,
            limitation = "The game must verify whether native Pos1/Pos2 anchors compose the rack attachment and preserve the local VertexTween wave."
        };
        File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(manifest, Json));
        File.WriteAllText(Path.Combine(output, "README.md"), "# Skyward parent-child constraint-anchor control\n\nThe cloth remains a kinematic child of the aircraft, but its entity Position is reset to the parent origin. The measured rack contact is supplied through the child constraint's Pos1 anchor; Pos2 remains the child origin. This isolates native constraint-anchor semantics from entity-local offsets.\n");
        Console.WriteLine(JsonSerializer.Serialize(manifest, Json));
    }

    // Nested-prefab control: keep one world kinematic transform owner and put
    // the ordinary VertexTween cloth inside a child CPlugPrefab. This tests
    // prefab transform composition without a second native constraint.
    public static void GenerateSharedFlightNestedPrefabControl(string output)
    {
        output = Path.GetFullPath(output);
        Require(!Directory.Exists(output) && !File.Exists(output), "Choose a new evidence directory");
        var root = Root();
        var sourceDir = Path.Combine(root, "artifacts/skyward-banner-20261023/parent-child-flight-anchor-v1");
        var sourceItemName = "TMIS_Skyward_OneUVParentChildFlightAnchor.Item.Gbx";
        var sourceMapName = "TMIS_Skyward_OneUVParentChildFlightAnchor.Map.Gbx";
        var sourceItemPath = Path.Combine(sourceDir, "files", sourceItemName);
        var sourceMapPath = Path.Combine(sourceDir, sourceMapName);
        var item = Parse(File.ReadAllBytes(sourceItemPath));
        var prefab = FlagPrefab(item.Node);
        Require(prefab.Ents is { Length: 4 }, "Expected parent-child source graph");
        var plane = prefab.Ents![0];
        var worldConstraint = prefab.Ents[2];
        var cloth = prefab.Ents[1];
        cloth.Position = default;
        ((NPlugDynaObjectModel_SInstanceParams)cloth.Params!).IsKinematic = false;
        var nested = new CPlugPrefab {
            Version = prefab.Version,
            FileWriteTime = prefab.FileWriteTime,
            Url = prefab.Url,
            U01 = prefab.U01,
            U02 = prefab.U02
        };
        nested.Ents = [cloth];
        var nestedEntry = new CPlugPrefab.EntRef {
            Position = prefab.Ents[1].Position == default ? new Vec3(-20.343857f, 0.9601176f, -9.020493f) : prefab.Ents[1].Position,
            Rotation = Identity,
            Model = nested,
            Params = null
        };
        prefab.Ents = [plane, worldConstraint, nestedEntry];
        var bytes = Save(item);
        var check = Parse(bytes);
        var checkedPrefab = FlagPrefab(check.Node);
        Require(checkedPrefab.Ents!.Length == 3 && checkedPrefab.Ents[1].Model is KC && checkedPrefab.Ents[2].Model is CPlugPrefab, "Nested prefab graph did not survive save");
        var checkedNested = (CPlugPrefab)checkedPrefab.Ents[2].Model!;
        Require(checkedNested.Ents is { Length: 1 } && checkedNested.Ents[0].Model is CPlugDynaObjectModel, "Nested cloth body missing");

        Directory.CreateDirectory(output);
        Directory.CreateDirectory(Path.Combine(output, "files"));
        var itemName = "TMIS_Skyward_OneUVNestedPrefabFlightAnchor.Item.Gbx";
        var mapName = "TMIS_Skyward_OneUVNestedPrefabFlightAnchor.Map.Gbx";
        File.WriteAllBytes(Path.Combine(output, "files", itemName), bytes);
        var dependencies = DependencyHashes.ToDictionary(p => p.Key, p => File.ReadAllBytes(Path.Combine(root, "Resources/Flags", p.Key)));
        foreach (var pair in dependencies) File.WriteAllBytes(Path.Combine(output, "files", pair.Key), pair.Value);
        var folder = "TMIS_SkywardNestedPrefab_" + Guid.NewGuid().ToString("N")[..16];
        var map = Map(File.ReadAllBytes(sourceMapPath), bytes, itemName, dependencies, folder, "OneUVNestedPrefabFlightAnchor", true);
        File.WriteAllBytes(Path.Combine(output, mapName), map);
        var manifest = new {
            operation = "offline-textured-flight-nested-prefab-transform-control",
            sourceItemPath,
            sourceItemSha256 = Hash(File.ReadAllBytes(sourceItemPath)),
            sourceMapPath,
            sourceMapSha256 = Hash(File.ReadAllBytes(sourceMapPath)),
            itemName,
            itemSha256 = Hash(bytes),
            mapName,
            mapSha256 = Hash(map),
            embeddedFolder = folder,
            nativeConstraintCount = 1,
            nestedPrefabCount = 1,
            nestedChildTransform = new Vec3(-20.343857f, 0.9601176f, -9.020493f),
            nestedClothKinematic = false,
            runtimeTested = false,
            offlineChecksPassed = true,
            limitation = "The game must verify whether a nested prefab inherits the outer kinematic transform while its cloth remains a non-kinematic VertexTween body."
        };
        File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(manifest, Json));
        File.WriteAllText(Path.Combine(output, "README.md"), "# Skyward nested-prefab flight control\n\nThe item keeps one world-to-plane native constraint. The ordinary waving cloth is moved into a nested CPlugPrefab positioned at the rack contact, with no second native constraint. This isolates prefab transform inheritance from chained kinematic-body composition.\n");
        Console.WriteLine(JsonSerializer.Serialize(manifest, Json));
    }

}
