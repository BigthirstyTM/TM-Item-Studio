using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Plug;

static partial class Research
{
    // Local-motion alternative to the two-constraint experiment. Both visual
    // bodies receive the same native CPlugAnimLocSimple node; the banner keeps
    // only local VertexTween waving after its baked rigid rotation is removed.
    public static void GenerateSharedLocAnimFlightControl(string output)
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
        var planeBody = (CPlugDynaObjectModel)prefab.Ents![0].Model!;
        var clothBody = (CPlugDynaObjectModel)prefab.Ents[1].Model!;
        var rigidMeshHash = NodeHash(planeBody.Mesh!);
        var clothMesh = clothBody.Mesh!;
        Require(clothMesh.Visuals is { Count: 5 }, "Expected five cloth LOD visuals");
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
        prefab.Ents[1].Position = attachment;
        var motion = new CPlugAnimLocSimple
        {
            RotPeriod = (int)(FlightPeriod * 1000),
            RotPeriodMax = (int)(FlightPeriod * 1000),
            TransPeriod = 0,
            TransPeriodMax = 0,
            TransY = 0,
            Axis = 1,
            RotFunc = 0,
            RotAngle = 360
        };
        motion.CreateChunk<CPlugAnimLocSimple.Chunk090F8000>().Version = 3;
        planeBody.LocAnim = motion;
        clothBody.LocAnim = motion;
        planeBody.LocAnimIsPhysical = false;
        clothBody.LocAnimIsPhysical = false;
        prefab.Ents = [prefab.Ents[0], prefab.Ents[1]];

        var bytes = Save(item);
        var check = Parse(bytes);
        var checkedPrefab = FlagPrefab(check.Node);
        Require(checkedPrefab.Ents!.Length == 2, "Local-motion graph did not remove the constraint entity");
        Require(NodeHash(((CPlugDynaObjectModel)checkedPrefab.Ents[0].Model!).Mesh!) == rigidMeshHash, "Textured aircraft mesh changed");
        Require(checkedPrefab.Ents[1].Position == attachment, "Cloth anchor did not survive save");
        Require(((CPlugDynaObjectModel)checkedPrefab.Ents[0].Model!).LocAnim is not null && ((CPlugDynaObjectModel)checkedPrefab.Ents[1].Model!).LocAnim is not null, "Shared local motion did not survive save");

        Directory.CreateDirectory(output);
        Directory.CreateDirectory(Path.Combine(output, "files"));
        var itemName = "TMIS_Skyward_OneUVSharedLocAnimFlightAnchor.Item.Gbx";
        var mapName = "TMIS_Skyward_OneUVSharedLocAnimFlightAnchor.Map.Gbx";
        File.WriteAllBytes(Path.Combine(output, "files", itemName), bytes);
        var dependencies = DependencyHashes.ToDictionary(p => p.Key, p => File.ReadAllBytes(Path.Combine(root, "Resources/Flags", p.Key)));
        foreach (var pair in dependencies) File.WriteAllBytes(Path.Combine(output, "files", pair.Key), pair.Value);
        var folder = "TMIS_SkywardSharedLocAnim_" + Guid.NewGuid().ToString("N")[..16];
        var map = Map(sourceMapBytes, bytes, itemName, dependencies, folder, "OneUVSharedLocAnimFlightAnchor", true);
        File.WriteAllBytes(Path.Combine(output, mapName), map);
        var manifest = new
        {
            operation = "offline-textured-flight-shared-locanim",
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
            rigidMeshHashPreserved = true,
            nativeLocalMotionShared = true,
            nativeConstraintCount = 0,
            clothFramesUnrotated = true,
            clothUsesVertexTweenForLocalWave = true,
            runtimeTested = false,
            offlineChecksPassed = true,
            limitation = "The game must verify CPlugAnimLocSimple playback on both entities and the interaction between the shared rigid transform and the cloth VertexTween."
        };
        File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(manifest, Json));
        File.WriteAllText(Path.Combine(output, "README.md"), "# Textured Skyward flight with shared native local motion\n\nBoth the textured aircraft and the banner entity receive the same native CPlugAnimLocSimple rotation. The banner frames have the earlier baked rigid rotation removed, so local VertexTween is reserved for the cloth wave. The banner entity is rebased to the measured rack contact. This is a bounded native local-motion experiment; it requires an in-game load and motion check.\n");
        Console.WriteLine(JsonSerializer.Serialize(manifest, Json));
    }
}
