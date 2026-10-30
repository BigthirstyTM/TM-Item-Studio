using System.IO.Compression;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.Game;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;

static partial class Research
{
    // Rebase only the separately animated banner entity in the textured flight
    // fixture. The rigid aircraft entity and its native material slots remain
    // byte-identical. World geometry is preserved by adding the same constant
    // to the entity position that is subtracted from every baked frame vertex.
    public static void GenerateFlightAnchorControl(string output)
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
        var sourceItemHash = Hash(sourceItemBytes);
        var sourceMapHash = Hash(sourceMapBytes);

        var sourceOriginalPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Trackmania/Items/BF2_ASSETS/Skyward_DoubleDecker_Plane/PromoPlane_Skyward.Item.Gbx");
        var original = Parse(File.ReadAllBytes(sourceOriginalPath));
        var originalMesh = ((CPlugDynaObjectModel)((CPlugPrefab)original.Node.EntityModel!).Ents![0].Model!).Mesh!;
        var banner = ReadBanner((CPlugVisualIndexedTriangles)originalMesh.Visuals![BannerIndex]);
        var edge = banner.Positions.Where(p => p.Z >= banner.MaximumZ - .1f).ToArray();
        Require(edge.Length > 0, "Banner attachment edge could not be identified");
        var attachment = new Vec3(edge.Average(p => p.X) + FlightShiftX, edge.Average(p => p.Y), edge.Average(p => p.Z));

        var item = Parse(sourceItemBytes);
        var prefab = FlagPrefab(item.Node);
        Require(prefab.Ents is { Length: 3 }, "Expected rigid, cloth and native constraint entities");
        Require(prefab.Ents![0].Model is CPlugDynaObjectModel && prefab.Ents[1].Model is CPlugDynaObjectModel, "Expected two dynamic visual entities");
        var rigidBefore = NodeHash(prefab.Ents[0].Model!);
        var cloth = (CPlugDynaObjectModel)prefab.Ents[1].Model!;
        var clothMesh = cloth.Mesh!;
        Require(clothMesh.Visuals is { Count: 5 }, "Expected five cloth LOD visuals");
        Require(clothMesh.Visuals.All(v => v is CPlugVisualIndexedTriangles), "Unexpected cloth visual class");
        foreach (var visual in clothMesh.Visuals.Cast<CPlugVisualIndexedTriangles>())
        {
            var rebased = visual.Vertices.Select(v => v with { Position = new Vec3(v.Position.X - attachment.X, v.Position.Y - attachment.Y, v.Position.Z - attachment.Z) }).ToArray();
            visual.Vertices = rebased;
            SetCount(visual, rebased.Length);
            visual.BoundingBox = Bounds(rebased.Select(v => v.Position));
        }
        prefab.Ents[1].Position = attachment;

        var bytes = Save(item);
        var check = Parse(bytes);
        var checkedPrefab = FlagPrefab(check.Node);
        Require(checkedPrefab.Ents!.Length == 3, "Entity count changed during save");
        Require(NodeHash(checkedPrefab.Ents[0].Model!) == rigidBefore, "Rigid textured aircraft entity changed");
        Require(checkedPrefab.Ents[1].Position == attachment, "Banner anchor position did not survive save");
        var checkedCloth = ((CPlugDynaObjectModel)checkedPrefab.Ents[1].Model!).Mesh!;
        Require(checkedCloth.Visuals!.All(v => ((CPlugVisualIndexedTriangles)v).Vertices.Length > 0), "Rebased cloth has no frame vertices");

        Directory.CreateDirectory(output);
        Directory.CreateDirectory(Path.Combine(output, "files"));
        var itemName = "TMIS_Skyward_OneUVFlightContrastCircleWaveAnchor.Item.Gbx";
        File.WriteAllBytes(Path.Combine(output, "files", itemName), bytes);
        var dependencies = DependencyHashes.ToDictionary(p => p.Key, p => File.ReadAllBytes(Path.Combine(root, "Resources/Flags", p.Key)));
        foreach (var pair in dependencies) File.WriteAllBytes(Path.Combine(output, "files", pair.Key), pair.Value);
        var folder = "TMIS_SkywardAnchor_" + Guid.NewGuid().ToString("N")[..16];
        var mapName = "TMIS_Skyward_OneUVFlightContrastCircleWaveAnchor.Map.Gbx";
        var map = Map(sourceMapBytes, bytes, itemName, dependencies, folder, "OneUVFlightContrastCircleWaveAnchor", true);
        File.WriteAllBytes(Path.Combine(output, mapName), map);
        var manifest = new
        {
            operation = "offline-textured-flight-banner-anchor-rebase",
            sourceItemPath,
            sourceItemSha256 = sourceItemHash,
            sourceMapPath,
            sourceMapSha256 = sourceMapHash,
            itemName,
            itemSha256 = Hash(bytes),
            mapName,
            mapSha256 = Hash(map),
            embeddedFolder = folder,
            attachmentLocal = attachment,
            attachmentWorldAtPhase0 = new Vec3(FlightAnchorPosition.X + attachment.X, FlightAnchorPosition.Y + attachment.Y, FlightAnchorPosition.Z + attachment.Z),
            rigidTexturesPreserved = true,
            rigidEntityHashPreserved = true,
            clothFrameVertexTranslation = new Vec3(-attachment.X, -attachment.Y, -attachment.Z),
            worldGeometryPreservedByConstantRebase = true,
            nativeKinematics = true,
            clothKinematic = false,
            runtimeTested = false,
            offlineChecksPassed = true,
            limitation = "The rebase changes the internal cloth entity pivot while preserving authored world positions; it does not by itself synchronize two independent clocks or prove continuous rack attachment."
        };
        File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(manifest, Json));
        File.WriteAllText(Path.Combine(output, "README.md"), "# Textured Skyward flight with banner anchor rebase\n\nThis fixture starts from the previously observed textured-aircraft flight control. The rigid aircraft entity, source material slots, native constraint and map dependencies are retained. Only the separately animated banner entity is rebased at the measured leading-edge rack contact. The same attachment vector is subtracted from every baked frame vertex and assigned to the cloth entity position, preserving the authored world-space geometry while exposing the intended internal anchor. This is a controlled pivot experiment; it does not claim clock synchronization or continuous attachment.\n");
        Console.WriteLine(JsonSerializer.Serialize(manifest, Json));
    }
}
