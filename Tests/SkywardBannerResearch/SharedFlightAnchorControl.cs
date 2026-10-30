using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;
using KC = GBX.NET.Engines.Meta.NPlugDyna_SKinematicConstraint;

static partial class Research
{
    // Synchronization experiment: the textured aircraft and the animated cloth
    // are driven by separate native constraints that use the same timeline. The
    // cloth frames are unrotated first, so the native constraint supplies the
    // rigid flight transform and VertexTween supplies only local waving.
    public static void GenerateSharedFlightAnchorControl(string output, bool clothKinematic = false)
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
        if (clothKinematic)
        {
            // The earlier kinematic-cloth load boundary used a collisionless
            // generated Dyna body. Give this control the exact inline shapes
            // from the proven flag seed so kinematic-body validity is tested
            // independently of null-shape handling.
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
        }
        foreach (var visual in clothMesh.Visuals.Cast<CPlugVisualIndexedTriangles>())
        {
            var n = visual.Vertices.Length / Frames;
            Require(n * Frames == visual.Vertices.Length, "Cloth frame table is not divisible by 86");
            var unrotated = visual.Vertices.Select((v, index) =>
            {
                var frame = index / n;
                var angleFrame = frame == Frames - 1 ? 0 : frame;
                return v with
                {
                    Position = Subtract(InverseYaw(v.Position, angleFrame), attachment)
                };
            }).ToArray();
            visual.Vertices = unrotated;
            SetCount(visual, unrotated.Length);
            visual.BoundingBox = Bounds(unrotated.Select(v => v.Position));
        }
        prefab.Ents[1].Position = attachment;
        var clothTiming = (NPlugDynaObjectModel_SInstanceParams)prefab.Ents[1].Params!;
        clothTiming.IsKinematic = clothKinematic;
        var originalConstraint = (KC)prefab.Ents[2].Model!;
        var secondConstraint = CloneNode(originalConstraint);
        var secondConstraintParams = new NPlugDyna_SPrefabConstraintParams { Version = 0, Ent1 = -1, Ent2 = 1 };
        var clothConstraint = new CPlugPrefab.EntRef
        {
            Position = default,
            Rotation = Identity,
            Model = secondConstraint,
            Params = secondConstraintParams
        };
        prefab.Ents = [prefab.Ents[0], prefab.Ents[1], prefab.Ents[2], clothConstraint];

        var bytes = Save(item);
        var check = Parse(bytes);
        var checkedPrefab = FlagPrefab(check.Node);
        Require(checkedPrefab.Ents!.Length == 4 && checkedPrefab.Ents.Count(e => e.Model is KC) == 2, "Shared constraint graph did not survive save");
        Require(NodeHash(checkedPrefab.Ents[0].Model!) == rigidHash, "Textured aircraft entity changed");
        Require(checkedPrefab.Ents[1].Position == attachment, "Cloth anchor did not survive save");
        Require(((NPlugDyna_SPrefabConstraintParams)checkedPrefab.Ents[2].Params!).Ent2 == 0, "Plane constraint target changed");
        Require(((NPlugDyna_SPrefabConstraintParams)checkedPrefab.Ents[3].Params!).Ent2 == 1, "Cloth constraint target changed");

        Directory.CreateDirectory(output);
        Directory.CreateDirectory(Path.Combine(output, "files"));
        var itemName = "TMIS_Skyward_OneUVSharedKinematicFlightAnchor.Item.Gbx";
        var mapName = "TMIS_Skyward_OneUVSharedKinematicFlightAnchor.Map.Gbx";
        File.WriteAllBytes(Path.Combine(output, "files", itemName), bytes);
        var dependencies = DependencyHashes.ToDictionary(p => p.Key, p => File.ReadAllBytes(Path.Combine(root, "Resources/Flags", p.Key)));
        foreach (var pair in dependencies) File.WriteAllBytes(Path.Combine(output, "files", pair.Key), pair.Value);
        var folder = "TMIS_SkywardSharedKinematic_" + Guid.NewGuid().ToString("N")[..16];
        var map = Map(sourceMapBytes, bytes, itemName, dependencies, folder, "OneUVSharedKinematicFlightAnchor", true);
        File.WriteAllBytes(Path.Combine(output, mapName), map);
        var manifest = new
        {
            operation = clothKinematic ? "offline-textured-flight-shared-native-constraint-kinematic-cloth" : "offline-textured-flight-shared-native-constraint",
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
            planeConstraintTarget = 0,
            clothConstraintTarget = 1,
            clothFramesUnrotated = true,
            clothUsesNativeRigidTransform = true,
            clothUsesVertexTweenForLocalWave = true,
            clothKinematic,
            runtimeTested = false,
            offlineChecksPassed = true,
            limitation = "The game must verify that two native constraints consume the same effective clock and that the cloth local VertexTween does not alter the attachment edge."
        };
        File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(manifest, Json));
        File.WriteAllText(Path.Combine(output, "README.md"), "# Textured Skyward flight with shared native constraint\n\nThe original textured aircraft entity keeps its source material slots and is driven by the existing native kinematic constraint. The waving banner is a second entity with the same local wave frames after removing the previously baked rigid rotation. A cloned native constraint targets the banner entity and uses the same 20-second timeline. The banner entity is rebased to the measured rack contact; the frame positions are translated by the inverse constant. This fixture variant requests cloth IsKinematic=" + clothKinematic + ". It isolates clock synchronization from the earlier baked-flight separation and requires native game observation.\n");
        Console.WriteLine(JsonSerializer.Serialize(manifest, Json));
    }

    static Vec3 InverseYaw(Vec3 p, int frame)
    {
        var angle = -2 * Math.PI * frame / (Frames - 1);
        var c = Math.Cos(angle);
        var s = Math.Sin(angle);
        return new((float)(c * p.X + s * p.Z), p.Y, (float)(-s * p.X + c * p.Z));
    }

    static Vec3 Subtract(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
}
