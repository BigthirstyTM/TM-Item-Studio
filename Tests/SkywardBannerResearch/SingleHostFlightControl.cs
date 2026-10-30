using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;

static partial class Research
{
    // Single transform-owner experiment. The textured rigid visuals and the
    // animated cloth LODs are placed in one CPlugDynaObjectModel mesh. The
    // aircraft's native constraint then transforms both through one owner.
    public static void GenerateSingleHostFlightControl(string output, bool useItemFlagMaterial = false, bool repairLods = false)
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
        var item = Parse(sourceItemBytes);
        var prefab = FlagPrefab(item.Node);
        Require(prefab.Ents is { Length: 3 }, "Expected textured plane, cloth and native constraint entities");
        var planeEntry = prefab.Ents![0];
        var clothEntry = prefab.Ents[1];
        Require(planeEntry.Model is CPlugDynaObjectModel && clothEntry.Model is CPlugDynaObjectModel, "Expected two dynamic visual entities");
        var planeBody = (CPlugDynaObjectModel)planeEntry.Model!;
        var clothBody = (CPlugDynaObjectModel)clothEntry.Model!;
        var planeMesh = planeBody.Mesh!;
        var clothMesh = clothBody.Mesh!;
        Require(planeMesh.Visuals is { Count: 12 } && planeMesh.CustomMaterials is { Length: 13 } && planeMesh.ShadedGeoms is { Length: 12 }, "Unexpected textured plane layout");
        Require(clothMesh.Visuals is { Count: 5 } && clothMesh.ShadedGeoms is { Length: 5 }, "Unexpected animated cloth layout");
        var rigidHash = NodeHash(planeBody);

        // The split flight bake contains the rigid flight rotation in every
        // cloth frame. Remove only that authored rigid rotation; the common
        // native constraint will supply it once for the merged host.
        var clothVisuals = clothMesh.Visuals.Cast<CPlugVisualIndexedTriangles>().Select(CloneNode).ToArray();
        foreach (var visual in clothVisuals)
        {
            var n = visual.Vertices.Length / Frames;
            Require(n * Frames == visual.Vertices.Length, "Cloth frame table is not divisible by 86");
            var local = visual.Vertices.Select((v, index) =>
            {
                var frame = index / n;
                var angleFrame = frame == Frames - 1 ? 0 : frame;
                return v with { Position = InverseYaw(v.Position, angleFrame) };
            }).ToArray();
            visual.Vertices = local;
            SetCount(visual, local.Length);
            visual.BoundingBox = Bounds(local.Select(v => v.Position));
        }

        // Use the unused source banner material slot as a native VertexTween
        // material. The rigid aircraft keeps all twelve original mappings;
        // only the newly appended cloth shaded-geoms use material slot 9.
        Require(planeMesh.CustomMaterials![9].MaterialUserInst is not null, "Source banner material has no user instance");
        var clothUserInst = CloneNode(planeMesh.CustomMaterials[9].MaterialUserInst!);
        clothUserInst.MaterialName = @"Stadium256\Media\Material\WarpFlag";
        clothUserInst.Link = @"Stadium256\Media\Material\WarpFlag";
        clothUserInst.IsUsingGameMaterial = true;
        if (useItemFlagMaterial)
        {
            var repairPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Trackmania/Items/02. Freebuff RE Items/XertrovFlagRepair.Item.Gbx");
            var repair = Parse(File.ReadAllBytes(repairPath));
            var repairPrefab = FlagPrefab(repair.Node);
            var repairMesh = ((CPlugDynaObjectModel)repairPrefab.Ents![1].Model!).Mesh!;
            Require(repairMesh.CustomMaterials is { Length: >= 1 } && repairMesh.CustomMaterials[0].MaterialUserInst is not null, "Xertrov ItemFlag material source missing");
            clothUserInst = CloneNode(repairMesh.CustomMaterials[0].MaterialUserInst!);
        }
        var clothMaterial = new CPlugSolid2Model.Material { MaterialName = "", MaterialUserInst = clothUserInst };

        var oldVisuals = planeMesh.Visuals.ToArray();
        var oldGeoms = planeMesh.ShadedGeoms.ToArray();
        planeMesh.Visuals = [.. oldVisuals, .. clothVisuals.Cast<CPlugVisual>()];
        planeMesh.CustomMaterials = [.. planeMesh.CustomMaterials, clothMaterial];
        planeMesh.ShadedGeoms = [.. oldGeoms, .. clothMesh.ShadedGeoms.Select((g, i) => new CPlugSolid2Model.ShadedGeom
        {
            Lod = g.Lod,
            VisualIndex = oldVisuals.Length + i,
            MaterialIndex = 9,
            U01 = g.U01,
            LodMask = g.LodMask,
            U02 = g.U02
        })];
        if (repairLods)
        {
            // The native flag family supplies four distance boundaries for its
            // five masks. Keep every rigid visual available at each selected
            // level and let the cloth retain one visual per level. This is an
            // isolated LOD A/B; it does not alter geometry or materials.
            planeMesh.LodMaxDistAtFov90 = [16, 64, 128, 512];
            foreach (var geom in planeMesh.ShadedGeoms.Take(oldVisuals.Length)) geom.LodMask = 31;
        }
        planeMesh.LodMaxDistAtFov90 = null;
        if (repairLods) planeMesh.LodMaxDistAtFov90 = [16, 64, 128, 512];
        // The cloth entity is no longer needed: the aircraft entity and the
        // existing native constraint become the sole transform owner.
        prefab.Ents = [planeEntry, prefab.Ents[2]];

        var bytes = Save(item);
        var check = Parse(bytes);
        var checkedPrefab = FlagPrefab(check.Node);
        Require(checkedPrefab.Ents!.Length == 2 && checkedPrefab.Ents[1].Model is NPlugDyna_SKinematicConstraint, "Single-host graph did not survive save");
        var checkedPlane = (CPlugDynaObjectModel)checkedPrefab.Ents[0].Model!;
        Require(checkedPlane.Mesh!.Visuals!.Count == 17 && checkedPlane.Mesh.ShadedGeoms!.Length == 17, "Merged visual mapping changed");
        Require(NodeHash(checkedPlane.Mesh.Visuals[0]) == NodeHash(planeMesh.Visuals[0]), "Rigid visual changed during serialization");
        Require(checkedPlane.Mesh.ShadedGeoms[12].VisualIndex == 12 && checkedPlane.Mesh.ShadedGeoms[12].MaterialIndex == 9 && checkedPlane.Mesh.ShadedGeoms[16].LodMask == 16, "Cloth material mapping changed");

        Directory.CreateDirectory(output);
        Directory.CreateDirectory(Path.Combine(output, "files"));
        var itemName = "TMIS_Skyward_OneUVSingleHostFlight.Item.Gbx";
        var mapName = "TMIS_Skyward_OneUVSingleHostFlight.Map.Gbx";
        File.WriteAllBytes(Path.Combine(output, "files", itemName), bytes);
        var dependencies = DependencyHashes.ToDictionary(p => p.Key, p => File.ReadAllBytes(Path.Combine(root, "Resources/Flags", p.Key)));
        foreach (var pair in dependencies) File.WriteAllBytes(Path.Combine(output, "files", pair.Key), pair.Value);
        var folder = "TMIS_SkywardSingleHost_" + Guid.NewGuid().ToString("N")[..16];
        var map = Map(sourceMapBytes, bytes, itemName, dependencies, folder, "OneUVSingleHostFlight", true);
        File.WriteAllBytes(Path.Combine(output, mapName), map);
        var manifest = new
        {
            operation = "offline-textured-flight-single-transform-owner",
            sourceItemPath,
            sourceItemSha256 = Hash(sourceItemBytes),
            sourceMapPath,
            sourceMapSha256 = Hash(sourceMapBytes),
            itemName,
            itemSha256 = Hash(bytes),
            mapName,
            mapSha256 = Hash(map),
            embeddedFolder = folder,
            rigidVisualCount = 12,
            clothVisualCount = 5,
            mergedVisualCount = 17,
            nativeConstraintCount = 1,
            clothMaterialSlot = 9,
            nativeWarpFlagMaterial = !useItemFlagMaterial,
            itemFlagMaterial = useItemFlagMaterial,
            repairedLodDistances = repairLods,
            rigidTexturesPreserved = true,
            rigidSourceBodyHashBeforeMerge = rigidHash,
            clothRigidRotationRemovedBeforeMerge = true,
            sharedTransformOwner = true,
            runtimeTested = false,
            offlineChecksPassed = true,
            limitation = "Mixed stream and VertexTween visual compatibility must be verified by a native load and close flight recording."
        };
        File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(manifest, Json));
        File.WriteAllText(Path.Combine(output, "README.md"), "# Textured Skyward single-transform-owner flight\n\nThis fixture merges the twelve textured rigid aircraft visuals and five animated cloth LOD visuals into one CPlugDynaObjectModel mesh. The original aircraft material slots are retained; the unused banner slot is assigned to " + (useItemFlagMaterial ? "the proven Xertrov ItemFlag material" : "native WarpFlag") + " for the cloth visuals. The cloth's baked rigid rotation is removed so the single source kinematic constraint supplies the transform to both rigid and cloth geometry. This is the direct test of whether one native transform owner can preserve textures and keep the waving banner attached. " + (repairLods ? "This A/B also uses the native flag distance table [16,64,128,512] and mask 31 for rigid visuals so each LOD level has a valid rigid owner." : "The baseline leaves the source rigid LOD layout unchanged.") + "\n");
        Console.WriteLine(JsonSerializer.Serialize(manifest, Json));
    }
}
