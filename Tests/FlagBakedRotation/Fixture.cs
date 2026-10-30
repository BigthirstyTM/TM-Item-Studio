using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.Game;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.MwFoundations;
using GBX.NET.Engines.Plug;

// Offline research only. This executable never installs or stages files or controls a process.
internal static class Fixture
{
    const int Frames = 86;
    const float PeriodSeconds = 12;
    const string ItemName = "TMIS_v6_Long_BakedRotateWave.Item.Gbx";
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, IncludeFields = true };

    public static void Generate(string output)
    {
        output = Path.GetFullPath(output);
        Require(!Directory.Exists(output) && !File.Exists(output), "Preserve evidence: choose an unused output directory");
        var root = FindRoot();
        var sourceMap = Path.Combine(root, "artifacts/flag-selfcontained-20261022/material-remap-shapes-v6-near-native-primer/TMIS_Embedded_Flag_Shapes_v6_Near_NativePrimerFirst.Map.Gbx");
        var sourceItem = Path.Combine(root, "artifacts/flag-selfcontained-20261022/material-remap-shapes-v6/files/TMIS_MatRemap_Long250.Item.Gbx");
        var package = Path.Combine(root, "artifacts/flag-selfcontained-20261022/material-remap-v6-audited");
        var mapBytes = File.ReadAllBytes(sourceMap);
        var itemBytes = File.ReadAllBytes(sourceItem);
        Require(Hash(itemBytes) == "D4F44EB23EE07B0C59CC0E9A976EAE05405B4DDE139BB2673C429F327EAFE751", "Unexpected proven v6 Long250 seed");
        var dependencyHashes = new Dictionary<string, string> {
            ["TMIS_MatRemap_Placeholder.Material.Gbx"] = "CB3B703C2B5BED9F359919C59B24A08730EE833FC5E422C40062DA061CDA588A",
            ["TMIS_MatRemap.GameSkin.Gbx"] = "DC6118300D7C4FDBA9DAA737130B45249A5E46F1AA56AC17A6D7F658CFA6859E",
            ["TMIS_MatRemap.TerrainModifier.Gbx"] = "D276FA3E545347EC01533401B9A44A8EC2254BCDE9C2C7BFA4FAFC7CE20D942B" };
        var files = dependencyHashes.ToDictionary(p => p.Key, p => File.ReadAllBytes(Path.Combine(package, p.Key)));
        foreach (var pair in files) Require(Hash(pair.Value) == dependencyHashes[pair.Key], "Unexpected v6 dependency " + pair.Key);

        var original = ParseItem(itemBytes);
        var originalCanonical = Serialize(original);
        ValidateItem(original);
        var originalPrefab = Prefab(original.Node);
        var sourceBody = (CPlugDynaObjectModel)originalPrefab.Ents![1].Model!;
        var sourceMesh = sourceBody.Mesh!;
        var originalPoleHash = NodeHash(originalPrefab.Ents[0].Model!);
        var sourceVisuals = sourceMesh.Visuals!.Cast<CPlugVisualIndexedTriangles>().ToArray();
        var sourceParams = (NPlugDynaObjectModel_SInstanceParams)originalPrefab.Ents[1].Params!;
        Require(!sourceParams.IsKinematic && sourceParams.PeriodSc == 8 && sourceParams.PeriodScMax == 16
            && sourceParams.Phase01 == 0 && sourceParams.Phase01Max == 1, "Unexpected source timing");
        Require(sourceMesh.Boxes is null, "Unknown Solid2 bounds would require an additional explicit transform");
        foreach (var v in sourceVisuals) ValidateVisual(v);

        var candidate = ParseItem(itemBytes);
        var prefab = Prefab(candidate.Node);
        var entry = prefab.Ents![1];
        var body = (CPlugDynaObjectModel)entry.Model!;
        var mesh = body.Mesh!;
        candidate.Node.Name = "TMIS v6 Long Baked Rotate and Wave";
        var parameters = (NPlugDynaObjectModel_SInstanceParams)entry.Params!;
        parameters.PeriodSc = PeriodSeconds; parameters.PeriodScMax = PeriodSeconds;
        parameters.Phase01 = 0; parameters.Phase01Max = 0;
        var geometry = new List<object>();
        for (var lod = 0; lod < sourceVisuals.Length; lod++)
        {
            var v = (CPlugVisualIndexedTriangles)mesh.Visuals![lod];
            var count = v.Vertices.Length / Frames;
            // Preserve the authored entity transform and rotate in item space about Y through (0,0,0).
            // Frames 0 and 85 are the same source pose. Keep both mathematically and bitwise closed.
            v.Vertices = v.Vertices.Select((p, i) => {
                var frame = i / count;
                // Source endpoint positions coincide, but some source endpoint normals differ.
                // Close only that lighting seam explicitly; retain every other source normal.
                var sourceNormal = frame == Frames-1 ? v.Vertices[i % count].Normal : p.Normal;
                return p with { Position = Position(p.Position, entry, frame), Normal = sourceNormal is {} n ? Normal(n, entry, frame) : null };
            }).ToArray();
            v.BoundingBox = Bounds(v.Vertices.Select(p => p.Position));
            geometry.Add(ValidateTransformed(sourceVisuals[lod], v, entry, lod));
        }
        var expectedVisualHashes = mesh.Visuals!.Select(NodeHash).ToArray();
        var candidateBytes = Serialize(candidate);
        files[ItemName] = candidateBytes;

        var parsed = ParseItem(candidateBytes);
        ValidateItem(parsed);
        var parsedPrefab = Prefab(parsed.Node);
        var parsedMesh = ((CPlugDynaObjectModel)parsedPrefab.Ents![1].Model!).Mesh!;
        Require(parsedMesh.Visuals!.Select(NodeHash).SequenceEqual(expectedVisualHashes), "A complete transformed visual changed during serialization");
        Require(NodeHash(parsedPrefab.Ents[0].Model!) == originalPoleHash, "Static pole changed");
        for (var i = 0; i < 2; i++)
            Require(parsedPrefab.Ents[i].Position == originalPrefab.Ents![i].Position && parsedPrefab.Ents[i].Rotation == originalPrefab.Ents[i].Rotation, "Original entity rest transform changed");
        for (var lod = 0; lod < sourceVisuals.Length; lod++) ValidateTransformed(sourceVisuals[lod], (CPlugVisualIndexedTriangles)parsedMesh.Visuals![lod], parsedPrefab.Ents[1], lod);
        // Strong negative-difference proof: restoring only positions, normals, visual bounds, name,
        // and the four documented timing values restores the complete original serialized Item.
        parsed.Node.Name = original.Node.Name;
        var pp = (NPlugDynaObjectModel_SInstanceParams)parsedPrefab.Ents[1].Params!;
        pp.PeriodSc = sourceParams.PeriodSc; pp.PeriodScMax = sourceParams.PeriodScMax;
        pp.Phase01 = sourceParams.Phase01; pp.Phase01Max = sourceParams.Phase01Max;
        for (var lod = 0; lod < sourceVisuals.Length; lod++) {
            var v = (CPlugVisualIndexedTriangles)parsedMesh.Visuals![lod];
            v.Vertices = sourceVisuals[lod].Vertices; v.BoundingBox = sourceVisuals[lod].BoundingBox;
        }
        Require(Serialize(parsed).AsSpan().SequenceEqual(originalCanonical), "Undocumented Item graph/material/topology/property changes");

        var token = Guid.NewGuid().ToString("N")[..20];
        var folder = "TMIS_BakedRotateWave_" + token;
        var id = folder + "\\" + ItemName;
        var mapDoc = Gbx.Parse<CGameCtnChallenge>(new MemoryStream(mapBytes), MapSettings());
        var map = mapDoc.Node;
        var native = map.AnchoredObjects!.Single(a => a.ItemModel?.Id == "Flag16m" && a.ItemModel.Author == "Nadeo");
        var nativeSnapshot = NativeSnapshot(native);
        map.AnchoredObjects!.Clear(); map.AnchoredObjects.Add(native);
        var anchor = new CGameCtnAnchoredObject { ItemModel = new Ident(id, original.Node.Ident!.Collection, original.Node.Ident.Author), AnchorTreeId = id,
            AbsolutePositionInMap = new(768,128,450), YawPitchRoll = new(MathF.PI/2,0,0), PivotPosition = default, Scale = 1 };
        anchor.CreateChunk<CGameCtnAnchoredObject.Chunk03101002>().Version = 8;
        anchor.CreateChunk<CGameCtnAnchoredObject.Chunk03101004>().Version = 0;
        map.AnchoredObjects.Add(anchor);
        map.MapName = "TMIS Embedded v6 Baked Rotate Wave " + token[..7];
        map.MapUid = "TMISBR_" + token;
        map.EmbeddedZipData = null;
        map.UpdateEmbeddedZipData(zip => { foreach (var pair in files) { using var stream = zip.CreateEntry("Items/"+folder+"/"+pair.Key, CompressionLevel.Optimal).Open(); stream.Write(pair.Value); } });
        using var mapMemory = new MemoryStream(); mapDoc.Save(mapMemory);
        var outputMapBytes = mapMemory.ToArray();
        var check = Gbx.Parse<CGameCtnChallenge>(new MemoryStream(outputMapBytes), MapSettings()).Node;
        Require(check.AnchoredObjects!.Count == 2 && NativeSnapshot(check.AnchoredObjects[0]) == nativeSnapshot, "Genuine native anchor or anchor count changed");
        Require(check.AnchoredObjects[1].ItemModel!.Id == id && check.AnchoredObjects[1].AnchorTreeId == id, "Custom anchor identity changed");
        Require(check.ExpectedEmbeddedItemModels!.Select(i => i.Id).SequenceEqual(new[] {id}), "Expected embedded Item identities changed");
        using (var zip = check.OpenReadEmbeddedZipData()) {
            Require(zip.Entries.Count == 4, "Expected exactly one custom Item and three dependencies");
            foreach (var pair in files) {
                using var stream = zip.GetEntry("Items/"+folder+"/"+pair.Key)!.Open(); using var memory = new MemoryStream(); stream.CopyTo(memory);
                Require(memory.ToArray().AsSpan().SequenceEqual(pair.Value), "Embedded bytes changed: " + pair.Key);
            }
        }
        Require(File.ReadAllBytes(sourceItem).AsSpan().SequenceEqual(itemBytes) && File.ReadAllBytes(sourceMap).AsSpan().SequenceEqual(mapBytes), "A fixture source changed");
        foreach (var pair in dependencyHashes) Require(Hash(File.ReadAllBytes(Path.Combine(package, pair.Key))) == pair.Value, "A dependency source changed");

        var mapName = "TMIS_Embedded_v6_Baked_Rotate_Wave.Map.Gbx";
        var mapPath = Path.Combine(output, mapName);
        var embeddedFiles = files.Select(p => new { zipPath = "Items/"+folder+"/"+p.Key, bytes = p.Value.Length, sha256 = Hash(p.Value) }).ToArray();
        var manifest = new {
            manifestVersion = 1, operation = "offline-v6-vertex-tween-baked-rotation-and-wave",
            scope = "Offline authoring, geometry and serialization checks only; no runtime loading, waving, rotation, restart, culling, lighting or gameplay verdict.",
            sourceMap, sourceMapSha256 = Hash(mapBytes), sourceItem, sourceItemSha256 = Hash(itemBytes), dependencySource = package, dependencyHashes,
            outputMap = mapPath, outputSha256 = Hash(outputMapBytes), item = new { file = ItemName, sha256 = Hash(candidateBytes), bytes = candidateBytes.Length },
            map.MapName, map.MapUid, embeddedFolder = folder, embeddedFiles, expectedEmbeddedItemIds = new[] {id},
            nativePrimerPresent = true, nativePrimerIndex = 0, nativeFlagEmbedded = false, anchoredObjectCount = 2, embeddedZipSha256 = Hash(check.EmbeddedZipData!),
            transform = new { axis = "item-space Y through the static pole shaft (0,0,0)", degrees = 360, frames = Frames,
                angleRadians = "2*pi*frame/85 for frames 0..85; frame 85 uses exact identity for a bitwise closed endpoint",
                position = "Q^-1 * (R * (Q*p + t) - t)", normal = "Q^-1 * R * Q * n", restPosition = entry.Position, restQuaternion = entry.Rotation,
                timingSeconds = PeriodSeconds, phase01 = 0, baseFoldAnimationRetained = true,
                endpointNormalCorrection = "Frame 85 normals copied from frame 0 to close the pre-existing source lighting seam; every other normal rigidly transformed",
                staticPoleUnmodified = true, isKinematic = false, locAnimAdded = false },
            checks = new { allFiveLodsAnd86Frames = true, allOriginalIndicesUvsFrameTablesAndMaterialsPreserved = true,
                completePoleSerializationByteExact = true, restTransformsExact = true, candidateVisualsSerializationByteExact = true,
                inverseRigidPositionsMatchSource = true, inverseRigidNormalsMatchSourceExceptExplicitEndpointClosure = true, triangleEdgeLengthsPreservedWithinTolerance = true,
                firstLastPositionsAndNormalsBitwiseClosed = true, allFramesInsideConservativeBounds = true,
                onlyDocumentedGeometryNameAndTimingChanges = true, fourEmbeddedPayloadsByteExact = true, sourceMapItemAndDependenciesUnchanged = true },
            lodGeometry = geometry,
            limitations = new[] {
                "Only the cloth rotates and waves. The pole is an unchanged static mesh with Technics/TechnicsTrims materials; this is not whole-assembly rotation.",
                "The rigid path and folds share a single baked VertexTween timeline. There are no independently steerable flight/wind controls or runtime wind simulation.",
                "No kinematic constraint or LocAnim is used. Collision shapes and physics remain those of the source; no moving-collision or vanilla-collision-parity claim.",
                "Linear interpolation between angular keyframes follows chords and can slightly shrink the silhouette between keys; all authored keys retain the exact source folds within float tolerance.",
                "The source has bitwise equal endpoint positions but different endpoint normals at some LODs. Frame 85 uses frame 0 normals for explicit loop lighting closure; the per-LOD correction is recorded below.",
                "The common 12-second period and zero phase are requested through the source's ordinary non-kinematic Dyna parameters. Native timing and visible loop smoothness require an in-game test.",
                "A genuine native Flag16m anchor is first and is not embedded. This fixture does not prove a custom-only shader initialization route.",
                "The map is serialized from the original embedded editor-map template. It is not a validated drivable track or a native-save persistence test." }
        };
        var sidecar = new { manifestVersion = 1, operation = "offline-v6-baked-rotate-wave-embedded-map-sidecar", scope = "Offline exact package records; no game verdict.",
            outputMap = mapPath, outputSha256 = Hash(outputMapBytes), map.MapName, map.MapUid, anchoredObjectCount = 2,
            embeddedFolder = folder, embeddedFiles, expectedEmbeddedItemIds = new[] {id}, nativePrimerPresent = true, nativePrimerIndex = 0,
            nativeFlagEmbedded = false, embeddedZipSha256 = Hash(check.EmbeddedZipData!) };
        Directory.CreateDirectory(output); Directory.CreateDirectory(Path.Combine(output, "files"));
        foreach (var pair in files) File.WriteAllBytes(Path.Combine(output, "files", pair.Key), pair.Value);
        File.WriteAllBytes(mapPath, outputMapBytes);
        File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(manifest, Json));
        File.WriteAllText(mapPath + ".manifest.json", JsonSerializer.Serialize(sidecar, Json));
        File.WriteAllText(Path.Combine(output, "findings.md"), Report(mapPath, candidateBytes, outputMapBytes));
        Console.WriteLine(JsonSerializer.Serialize(new { output, mapPath, mapSha256 = Hash(outputMapBytes), itemSha256 = Hash(candidateBytes), embeddedFolder = folder,
            files = files.Count, anchors = 2, clothRotationAndWaveBaked = true, staticPole = true, offlineChecksPassed = true, runtimeTested = false }, Json));
    }

    static object ValidateTransformed(CPlugVisualIndexedTriangles source, CPlugVisualIndexedTriangles target, CPlugPrefab.EntRef entry, int lod)
    {
        ValidateVisual(target);
        Require(source.SubVisuals!.SequenceEqual(target.SubVisuals!) && source.IndexBuffer!.Indices.SequenceEqual(target.IndexBuffer!.Indices), "Topology or frame table changed");
        var count = source.Vertices.Length / Frames;
        double maxInversePositionError = 0, maxInverseNormalError = 0, maxEdgeLengthError = 0, maxDisplacement = 0;
        double minSourceTriangleArea = double.PositiveInfinity, minTargetTriangleArea = double.PositiveInfinity, maxSourceEndpointNormalCorrection = 0;
        for (var frame = 0; frame < Frames; frame++) {
            for (var i = 0; i < count; i++) {
                var index = frame * count + i; var a = source.Vertices[index]; var b = target.Vertices[index];
                var referenceNormal = frame == Frames-1 ? source.Vertices[i].Normal : a.Normal;
                Require(b.Position == Position(a.Position, entry, frame) && b.Normal == (referenceNormal is {} n ? Normal(n, entry, frame) : null), "Transformed geometry not exactly reproducible");
                maxInversePositionError = Math.Max(maxInversePositionError, Distance(Position(b.Position, entry, frame, true), a.Position));
                if (referenceNormal is {} an && b.Normal is {} bn) maxInverseNormalError = Math.Max(maxInverseNormalError, Distance(Normal(bn, entry, frame, true), an));
                if (frame == Frames-1 && a.Normal is {} originalN && referenceNormal is {} closedN) maxSourceEndpointNormalCorrection = Math.Max(maxSourceEndpointNormalCorrection,Distance(originalN,closedN));
                maxDisplacement = Math.Max(maxDisplacement, Distance(a.Position, b.Position));
                Require(Inside(target.BoundingBox, b.Position), "A transformed frame escapes its visual bounds");
            }
            var indices = source.IndexBuffer!.Indices;
            for (var i = 0; i < indices.Length; i += 3) {
                var a = indices[i] + frame * count; var b = indices[i+1] + frame * count; var c = indices[i+2] + frame * count;
                foreach (var edge in new[] {(a,b),(b,c),(c,a)})
                    maxEdgeLengthError = Math.Max(maxEdgeLengthError, Math.Abs(Distance(source.Vertices[edge.Item1].Position, source.Vertices[edge.Item2].Position) - Distance(target.Vertices[edge.Item1].Position, target.Vertices[edge.Item2].Position)));
                minSourceTriangleArea = Math.Min(minSourceTriangleArea, Area(source.Vertices[a].Position,source.Vertices[b].Position,source.Vertices[c].Position));
                minTargetTriangleArea = Math.Min(minTargetTriangleArea, Area(target.Vertices[a].Position,target.Vertices[b].Position,target.Vertices[c].Position));
            }
        }
        Require(maxInversePositionError < 0.00001 && maxInverseNormalError < 0.000001 && maxEdgeLengthError < 0.00001, "Rigid geometry tolerance exceeded");
        for (var i = 0; i < count; i++) Require(target.Vertices[i].Position == target.Vertices[(Frames-1)*count+i].Position && target.Vertices[i].Normal == target.Vertices[(Frames-1)*count+i].Normal, "Loop endpoint is not bitwise closed");
        Require(maxDisplacement > 15, "Rotation did not substantially move the long cloth");
        return new { lod, frames = Frames, verticesPerFrame = count, trianglesPerFrame = source.IndexBuffer!.Indices.Length/3,
            maxInversePositionError, maxInverseNormalError, maxTriangleEdgeLengthError = maxEdgeLengthError, maxLocalVertexDisplacement = maxDisplacement,
            minSourceTriangleArea, minTransformedTriangleArea = minTargetTriangleArea, maxSourceEndpointNormalCorrection, target.BoundingBox,
            firstLastPositionsAndNormalsBitwiseEqual = true, allFrameBoundsVerified = true };
    }

    static void ValidateItem(Gbx<CGameItemModel> doc)
    {
        Require(doc.Node.Ident?.Id == "" && doc.Node.ArchetypeRef == "", "Expected ordinary v6 user identity");
        Require(doc.RefTable!.AncestorLevel == 0 && doc.RefTable.Files.Select(f => f.FilePath).SequenceEqual(new[] {"TMIS_MatRemap_Placeholder.Material.Gbx", "TMIS_MatRemap.TerrainModifier.Gbx"}) && doc.RefTable.Files.All(f => f.Flags == 1 && !f.UseFile), "v6 sibling reference table changed");
        Require(doc.Node.References.MaterialModifier.File?.FilePath == "TMIS_MatRemap.TerrainModifier.Gbx", "v6 modifier changed");
        var p = Prefab(doc.Node);
        Require(p.Ents is {Length:2} && p.Ents[0].Model is CPlugStaticObjectModel && p.Ents[1].Model is CPlugDynaObjectModel, "Unknown source graph");
        var d = (CPlugDynaObjectModel)p.Ents![1].Model!;
        Require(d.LocAnim is null && d.References.LocAnim.File is null && d.DynaShape is null && d.StaticShape is null && !((NPlugDynaObjectModel_SInstanceParams)p.Ents[1].Params!).IsKinematic, "Unexpected motion or physics graph");
        Require(d.Mesh?.Visuals?.Count == 5 && d.Mesh.Materials is {Count:1} && d.Mesh.Materials.Get(0).File?.FilePath == "TMIS_MatRemap_Placeholder.Material.Gbx", "Unknown material/visual route");
    }
    static void ValidateVisual(CPlugVisualIndexedTriangles v)
    {
        Require(v.SubVisuals is {Length:Frames} && v.Vertices.Length % Frames == 0 && v.VertexStreams.Count == 0, "Expected 86 complete inline frames");
        Require((v.Tangents?.Length ?? 0) == 0 && (v.BiTangents?.Length ?? 0) == 0 && v.Chunks.OfType<CPlugVisual3D.Chunk0902C004>().All(c => c.Tangents1Count == 0 && c.Tangents2Count == 0), "Unknown tangent data requires explicit rotation");
        var count = v.Vertices.Length / Frames;
        Require(v.IndexBuffer!.Indices.Length % 3 == 0 && v.IndexBuffer.Indices.All(i => i >= 0 && i < count), "Indices outside a frame");
        for (var frame = 0; frame < Frames; frame++) Require(v.SubVisuals![frame] == new Int3(frame * count,0,v.IndexBuffer.Indices.Length), "Unexpected native frame table");
        Require(v.Vertices.All(p => Finite(p.Position) && p.Normal is {} n && Finite(n)), "Nonfinite/missing position or normal");
    }
    static Vec3 Position(Vec3 p, CPlugPrefab.EntRef e, int frame, bool inverse = false)
    {
        if (frame is 0 or Frames-1) return p;
        var q = e.Rotation; var t = e.Position;
        var world = Add(Rotate(p, q, false), t);
        return Rotate(Sub(Yaw(world, Angle(frame, inverse)), t), q, true);
    }
    static Vec3 Normal(Vec3 n, CPlugPrefab.EntRef e, int frame, bool inverse = false)
    {
        if (frame is 0 or Frames-1) return n;
        return Rotate(Yaw(Rotate(n,e.Rotation,false),Angle(frame,inverse)),e.Rotation,true);
    }
    static double Angle(int frame, bool inverse) => (inverse ? -1 : 1) * (2 * Math.PI * frame / (Frames-1));
    static Vec3 Yaw(Vec3 p, double a) { var c=Math.Cos(a);var s=Math.Sin(a);return new((float)(c*p.X+s*p.Z),p.Y,(float)(-s*p.X+c*p.Z)); }
    static Vec3 Rotate(Vec3 p, Quat q, bool inverse)
    {
        // Normalize in double precision and conjugate for the inverse; preserve the stored quaternion itself.
        double x=q.X,y=q.Y,z=q.Z,w=q.W, len=Math.Sqrt(x*x+y*y+z*z+w*w);
        x/=len;y/=len;z/=len;w/=len;if(inverse){x=-x;y=-y;z=-z;}
        double tx=2*(y*p.Z-z*p.Y),ty=2*(z*p.X-x*p.Z),tz=2*(x*p.Y-y*p.X);
        return new((float)(p.X+w*tx+y*tz-z*ty),(float)(p.Y+w*ty+z*tx-x*tz),(float)(p.Z+w*tz+x*ty-y*tx));
    }
    static BoxAligned Bounds(IEnumerable<Vec3> values) {
        var p=values.ToArray();var min=new Vec3(p.Min(v=>v.X),p.Min(v=>v.Y),p.Min(v=>v.Z));var max=new Vec3(p.Max(v=>v.X),p.Max(v=>v.Y),p.Max(v=>v.Z));
        const float margin=0.0001f;
        return new((min.X+max.X)/2,(min.Y+max.Y)/2,(min.Z+max.Z)/2,(max.X-min.X)/2+margin,(max.Y-min.Y)/2+margin,(max.Z-min.Z)/2+margin);
    }
    static bool Inside(BoxAligned b,Vec3 p)=>Math.Abs(p.X-b.X)<=b.X2 && Math.Abs(p.Y-b.Y)<=b.Y2 && Math.Abs(p.Z-b.Z)<=b.Z2;
    static bool Finite(Vec3 p)=>float.IsFinite(p.X)&&float.IsFinite(p.Y)&&float.IsFinite(p.Z);
    static Vec3 Add(Vec3 a,Vec3 b)=>new(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
    static Vec3 Sub(Vec3 a,Vec3 b)=>new(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
    static double Distance(Vec3 a,Vec3 b)=>Math.Sqrt(Math.Pow((double)a.X-b.X,2)+Math.Pow((double)a.Y-b.Y,2)+Math.Pow((double)a.Z-b.Z,2));
    static double Area(Vec3 a,Vec3 b,Vec3 c) { double x1=b.X-a.X,y1=b.Y-a.Y,z1=b.Z-a.Z,x2=c.X-a.X,y2=c.Y-a.Y,z2=c.Z-a.Z;return .5*Math.Sqrt(Math.Pow(y1*z2-z1*y2,2)+Math.Pow(z1*x2-x1*z2,2)+Math.Pow(x1*y2-y1*x2,2)); }
    static string Report(string mapPath,byte[] item,byte[] map)=>$"""
        # Baked cloth rotation plus native fold-animation control

        This is an offline fixture, not an in-game success report. It combines a 360-degree cloth yaw with the accepted Long250 fold animation in the same five complete VertexTween visuals. It uses the unchanged non-kinematic Dyna host and material route. No plugin, runtime write, LocAnim or kinematic constraint is added by this generator.

        The pole remains fixed and byte-exact. Its standard Technics/TechnicsTrims materials do not establish a VertexTween path. This control therefore does not claim whole-assembly motion, independently steerable flight, moving collision or vanilla physics parity.

        The native source has 86 frames and exactly matching first/last positions, with a pre-existing normal seam at some LODs. Each original frame receives one rigid Y rotation about the item-space pole shaft, conjugated through the original cloth entity transform. Normals receive the same rigid rotation; frame 85 copies frame 0 normals to close that lighting seam explicitly. Conservative visual bounds include every transformed frame. Frames 0 and 85 are bitwise equal in both position and normal after this documented correction. The requested period is 12 seconds with zero phase.

        Offline checks preserve all index/UV/frame tables and materials; check all five LODs and all vertices/normals; bound inverse-transform and triangle-edge error; verify complete visual serialization; and prove that restoring only geometry, name and the four timing values restores the complete original serialized Item. The map contains one custom Item, its three byte-exact v6 dependencies, and the original genuine native Flag16m anchor first. It is generated from the original embedded editor-map template, not a validated race map.

        Map: `{mapPath}`  
        Map SHA-256: `{Hash(map)}`  
        Item SHA-256: `{Hash(item)}`

        Required next test: load this one map in a fresh game process without Openplanet or loose custom assets; observe cloth yaw and changing folds through at least two full loops; inspect attachment, loop seam, culling and LODs; and distinguish the unchanged pole from the rotating cloth. The common timeline makes this a visual baking route for future aircraft/banner work, not yet a moving aircraft test.
        """;
    static GbxReadSettings MapSettings()=>new(){SkipChunkIds=[0x0304305B]};
    static string NativeSnapshot(CGameCtnAnchoredObject a)=>JsonSerializer.Serialize(new{a.ItemModel,a.AbsolutePositionInMap,a.YawPitchRoll,a.PivotPosition,a.Scale,a.AnchorTreeId},Json);
    static Gbx<CGameItemModel> ParseItem(byte[] b)=>Gbx.Parse<CGameItemModel>(new MemoryStream(b));
    static CPlugPrefab Prefab(CGameItemModel i)=>(CPlugPrefab)((NPlugItem_SVariantList)i.EntityModel!).Variants![0].EntityModel!;
    static byte[] Serialize(Gbx<CGameItemModel> doc){using var m=new MemoryStream();new ReferenceTableDocument(doc.Node,doc.RefTable!).Save(m);return m.ToArray();}
    static string NodeHash<T>(T node) where T:CMwNod {using var m=new MemoryStream();new Gbx<T>(node).Save(m);return Hash(m.ToArray());}
    static string Hash(byte[] b)=>Convert.ToHexString(SHA256.HashData(b));
    static void Require(bool ok,string message){if(!ok)throw new InvalidDataException(message);}
    static string FindRoot(){for(var d=new DirectoryInfo(Environment.CurrentDirectory);d is not null;d=d.Parent)if(File.Exists(Path.Combine(d.FullName,"TM-Item-Studio.csproj")))return d.FullName;throw new DirectoryNotFoundException("Run within repository");}
    sealed class ReferenceTableDocument:Gbx<CGameItemModel>{public ReferenceTableDocument(CGameItemModel i,GbxRefTable t):base(i)=>RefTable=t;}
}
