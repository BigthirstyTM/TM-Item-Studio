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
using TmEssentials;
using KC = GBX.NET.Engines.Meta.NPlugDyna_SKinematicConstraint;

// Offline bounded research fixture. Does not install, stage, load or control the game.
internal static class Fixture
{
static readonly JsonSerializerOptions Json = new() {WriteIndented=true,IncludeFields=true};
public static void Generate(string output)
{
output = Path.GetFullPath(output);
Require(!Directory.Exists(output) && !File.Exists(output), "Choose an unused output directory");
var root = FindRoot();
var sourceMap = Path.Combine(root, "artifacts/flag-selfcontained-20261022/material-remap-shapes-v6-near-native-primer/TMIS_Embedded_Flag_Shapes_v6_Near_NativePrimerFirst.Map.Gbx");
var sourceItem = Path.Combine(root, "artifacts/flag-selfcontained-20261022/material-remap-shapes-v6/files/TMIS_MatRemap_Long250.Item.Gbx");
var package = Path.Combine(root, "artifacts/flag-selfcontained-20261022/material-remap-v6-audited");
var mapBytes = File.ReadAllBytes(sourceMap); var itemBytes = File.ReadAllBytes(sourceItem);
Require(Hash(itemBytes) == "D4F44EB23EE07B0C59CC0E9A976EAE05405B4DDE139BB2673C429F327EAFE751", "Unexpected v6 Long250 seed");
var dependencyHashes = new Dictionary<string,string> {
    ["TMIS_MatRemap_Placeholder.Material.Gbx"] = "CB3B703C2B5BED9F359919C59B24A08730EE833FC5E422C40062DA061CDA588A",
    ["TMIS_MatRemap.GameSkin.Gbx"] = "DC6118300D7C4FDBA9DAA737130B45249A5E46F1AA56AC17A6D7F658CFA6859E",
    ["TMIS_MatRemap.TerrainModifier.Gbx"] = "D276FA3E545347EC01533401B9A44A8EC2254BCDE9C2C7BFA4FAFC7CE20D942B" };
var files = dependencyHashes.ToDictionary(p => p.Key, p => File.ReadAllBytes(Path.Combine(package,p.Key)));
foreach (var p in files) Require(Hash(p.Value) == dependencyHashes[p.Key], "Unexpected v6 dependency: " + p.Key);
var original = ParseItem(itemBytes); var originalPrefab = Prefab(original.Node);
Require(originalPrefab.Ents is {Length:2} && originalPrefab.Ents[0].Model is CPlugStaticObjectModel && originalPrefab.Ents[1].Model is CPlugDynaObjectModel, "Unknown baseline prefab");
var sourcePole = (CPlugStaticObjectModel)originalPrefab.Ents![0].Model!;
var sourceCloth = (CPlugDynaObjectModel)originalPrefab.Ents[1].Model!;
var originalMeshes = new[] { NodeHash(sourcePole.Mesh!), NodeHash(sourceCloth.Mesh!) };
var visualHashes = VisualHashes(sourceCloth.Mesh!); var sourceBodyHash = NodeHash(sourceCloth);
Require(visualHashes.Length == 5 && sourceCloth.Mesh!.Visuals!.All(v => v.SubVisuals is { Length:86 }), "Expected all five/86 animated visuals");
Require(original.Node.Ident?.Id == "" && original.Node.ArchetypeRef == "", "Expected ordinary v6 user identity");
ValidateReferences(original);
var sourceParams = (NPlugDynaObjectModel_SInstanceParams)originalPrefab.Ents[1].Params!;
Require(!sourceParams.IsKinematic && sourceParams.PeriodSc == 8 && sourceParams.PeriodScMax == 16 && sourceParams.Phase01 == 0 && sourceParams.Phase01Max == 1, "Unexpected source animation timing");
var token = Guid.NewGuid().ToString("N")[..20]; var folder = "TMIS_KinRemap_" + token;
var names = new[] { "TMIS_v6_Long_Baseline.Item.Gbx", "TMIS_v6_Long_KinematicStationary.Item.Gbx", "TMIS_v6_Long_KinematicRotate.Item.Gbx" };
files[names[0]] = itemBytes; var itemRows = new List<object>();
foreach (var moving in new[] { false, true })
{
    var doc = ParseItem(itemBytes); var item = doc.Node; var prefab = Prefab(item);
    item.Name = moving ? "TMIS v6 Long Kinematic Rotate" : "TMIS v6 Long Kinematic Stationary";
    var oldPole = prefab.Ents![0]; var clothEntry = prefab.Ents[1];
    var poleMesh = ((CPlugStaticObjectModel)oldPole.Model!).Mesh!;
    var clothBody = (CPlugDynaObjectModel)clothEntry.Model!;
    Require(NodeHash(clothBody) == sourceBodyHash, "Cloth body changed before conversion");
    var poleBody = (CPlugDynaObjectModel)Prefab(ParseItem(itemBytes).Node).Ents![1].Model!;
    poleBody.Mesh = poleMesh; if (poleBody.References.Mesh.Descriptor is not null) poleBody.References.Mesh.Clear();
    foreach (var body in new[] { poleBody, clothBody })
    {
        var shape = new CPlugSurface { Surf = new CPlugSurface.Mesh { Version = 6,
            Vertices = [new(0,0,0),new(.02f,0,0),new(0,.02f,0)], Triangles = [new(new Int3(0,1,2),0,0,0)] } };
        shape.CreateChunk<CPlugSurface.Chunk0900C003>().Version = 2;
        body.StaticShape = shape; body.DynaShape = shape; if (body.References.StaticShape.Descriptor is not null) body.References.StaticShape.Clear(); if (body.References.DynaShape.Descriptor is not null) body.References.DynaShape.Clear();
    }
    // Retain source rest positions and quaternions. Never bake transforms into vertices.
    oldPole.Model = poleBody; oldPole.Params = Parameters(sourceParams, true); clothEntry.Params = Parameters(sourceParams, true);
    CPlugPrefab.EntRef Constraint(int slot) => new() { Rotation = new(0,0,0,1),
        Params = new NPlugDyna_SPrefabConstraintParams { Version = 0, Ent1 = -1, Ent2 = slot, Pos1 = default, Pos2 = default },
        Model = new KC { Version = 0, SubVersion = 3, TransAxis = KC.EAxis.Y, TransMin = 0, TransMax = 0,
            RotAxis = KC.EAxis.Y, AngleMinDeg = 0, AngleMaxDeg = moving ? 360 : 0,
            TransAnimFunc = Timeline(), RotAnimFunc = Timeline() } };
    prefab.Ents = [oldPole,clothEntry,Constraint(0),Constraint(1)];
    using var memory = new MemoryStream(); new ReferenceTableDocument(item,doc.RefTable!).Save(memory); var bytes = memory.ToArray();
    var parsed = ParseItem(bytes); var p = Prefab(parsed.Node); ValidateReferences(parsed);
    Require(p.Ents is {Length:4} && p.Ents.Count(e => e.Model is KC) == 2, "Kinematic graph changed");
    var meshes = p.Ents!.Take(2).Select(e => ((CPlugDynaObjectModel)e.Model!).Mesh!).ToArray();
    Require(meshes.Select(NodeHash).SequenceEqual(originalMeshes), "A whole Solid2 mesh changed");
    Require(VisualHashes(meshes[1]).SequenceEqual(visualHashes), "A complete cloth visual/frame table changed");
    for (var i = 0; i < 2; i++) {
        Require(p.Ents[i].Position == originalPrefab.Ents[i].Position && p.Ents[i].Rotation == originalPrefab.Ents[i].Rotation, "Source rest transform changed");
        Require(ParamsJson((NPlugDynaObjectModel_SInstanceParams)p.Ents[i].Params!) == ParamsJson(Parameters(sourceParams,true)), "Instance timing differs from source");
    }
    var reopenedBody = (CPlugDynaObjectModel)p.Ents[1].Model!; reopenedBody.StaticShape = null; reopenedBody.DynaShape = null;
    Require(NodeHash(reopenedBody) == sourceBodyHash, "Original cloth body changed beyond added diagnostic collision shapes");
    var name = names[moving ? 2 : 1]; files[name] = bytes;
    itemRows.Add(new { file = name, moving, sha256 = Hash(bytes), bytes = bytes.Length, requestedRotationDegrees = moving ? 360 : 0,
        requestedRotationPeriodMs = 12000, clothBodyExceptDiagnosticCollisionByteEquivalent = true,
        wholePoleAndClothSolid2SerializationByteExact = true, allFiveCompleteClothVisualsByteExact = true,
        originalRestPositionsAndQuaternionsExact = true, sourceInstanceTimingExactExceptIsKinematic = true, noVertexRebake = true, noLocAnimAdded = true });
}
// Prove stationary/moving differ only names and the two rotation upper bounds.
var stationary = ParseItem(files[names[1]]); var movingDoc = ParseItem(files[names[2]]); movingDoc.Node.Name = stationary.Node.Name;
foreach (var entry in Prefab(movingDoc.Node).Ents!.Where(e => e.Model is KC)) ((KC)entry.Model!).AngleMaxDeg = 0;
using (var compare = new MemoryStream()) { new ReferenceTableDocument(movingDoc.Node,movingDoc.RefTable!).Save(compare); Require(compare.ToArray().AsSpan().SequenceEqual(files[names[1]]), "Stationary/rotating pair has extra differences"); }
var mapRows = new List<object>(); var mapOutputBytes = new Dictionary<string,byte[]>();
foreach (var selected in new[] { -1, 0, 1, 2 })
{
    var mapDoc = Gbx.Parse<CGameCtnChallenge>(new MemoryStream(mapBytes), new GbxReadSettings { SkipChunkIds = [0x0304305B] }); var map = mapDoc.Node;
    var native = map.AnchoredObjects!.Single(a => a.ItemModel?.Id == "Flag16m" && a.ItemModel.Author == "Nadeo"); var nativeSnapshot = NativeSnapshot(native);
    map.AnchoredObjects!.Clear(); map.AnchoredObjects.Add(native);
    var chosen = selected < 0 ? new[] {0,1,2} : new[] {selected};
    foreach (var index in chosen) {
        var id = folder + "\\" + names[index];
        var anchor = new CGameCtnAnchoredObject { ItemModel = new Ident(id,original.Node.Ident!.Collection,original.Node.Ident.Author), AnchorTreeId = id,
            AbsolutePositionInMap = new(selected < 0 ? 718 + index * 50 : 768,128,450), YawPitchRoll = new(MathF.PI/2,0,0), PivotPosition = default, Scale = 1 };
        anchor.CreateChunk<CGameCtnAnchoredObject.Chunk03101002>().Version = 8; anchor.CreateChunk<CGameCtnAnchoredObject.Chunk03101004>().Version = 0; map.AnchoredObjects.Add(anchor);
    }
    var label = selected < 0 ? "Overview" : selected == 0 ? "Baseline" : selected == 1 ? "KinematicStationary" : "KinematicRotate";
    map.MapName = "TMIS Embedded v6 Motion " + label + " " + token[..7]; map.MapUid = "TMISK_" + token + (selected < 0 ? "O" : selected.ToString());
    var mapFiles = files.Where(p => dependencyHashes.ContainsKey(p.Key) || chosen.Any(i => names[i] == p.Key)).ToDictionary(p => p.Key,p => p.Value);
    map.EmbeddedZipData = null; map.UpdateEmbeddedZipData(zip => { foreach (var pair in mapFiles) { using var entry = zip.CreateEntry("Items/"+folder+"/"+pair.Key,CompressionLevel.Optimal).Open(); entry.Write(pair.Value); } });
    using var memory = new MemoryStream(); mapDoc.Save(memory); var bytes = memory.ToArray(); var check = Gbx.Parse<CGameCtnChallenge>(new MemoryStream(bytes), new GbxReadSettings { SkipChunkIds = [0x0304305B] }).Node;
    Require(check.AnchoredObjects!.Count == chosen.Length+1, "Anchor count mismatch"); Require(NativeSnapshot(check.AnchoredObjects[0]) == nativeSnapshot, "Native anchor changed");
    using (var zip = check.OpenReadEmbeddedZipData()) { Require(zip.Entries.Count == mapFiles.Count, "Embedded count mismatch");
        foreach(var pair in mapFiles) { using var entry = zip.GetEntry("Items/"+folder+"/"+pair.Key)!.Open(); using var extracted = new MemoryStream(); entry.CopyTo(extracted); Require(extracted.ToArray().AsSpan().SequenceEqual(pair.Value), "Embedded payload changed"); } }
    Require(check.ExpectedEmbeddedItemModels!.Select(i => i.Id).Order().SequenceEqual(chosen.Select(i => folder+"\\"+names[i]).Order()), "Expected embedded identities mismatch");
    var file = "TMIS_Embedded_v6_Motion_" + label + ".Map.Gbx"; mapOutputBytes[file] = bytes;
    mapRows.Add(new { file, sha256 = Hash(bytes), bytes = bytes.Length, label, check.MapName,check.MapUid, nativeAnchorIndex = 0,nativeEmbedded = false,embeddedFileCount = mapFiles.Count,
        embeddedZipSha256 = Hash(check.EmbeddedZipData!), anchors = check.AnchoredObjects.Select(a => new {a.ItemModel,a.AbsolutePositionInMap,a.YawPitchRoll,a.PivotPosition,a.Scale,a.AnchorTreeId}),
        embeddedFiles = mapFiles.Select(p => new {zipPath="Items/"+folder+"/"+p.Key,bytes=p.Value.Length,sha256=Hash(p.Value)}) });
}
Require(File.ReadAllBytes(sourceItem).AsSpan().SequenceEqual(itemBytes) && File.ReadAllBytes(sourceMap).AsSpan().SequenceEqual(mapBytes), "A source changed");
foreach(var pair in dependencyHashes) Require(Hash(File.ReadAllBytes(Path.Combine(package,pair.Key))) == pair.Value,"Dependency source changed");
Directory.CreateDirectory(output); Directory.CreateDirectory(Path.Combine(output,"files"));
foreach(var pair in files) File.WriteAllBytes(Path.Combine(output,"files",pair.Key),pair.Value);
foreach(var pair in mapOutputBytes) File.WriteAllBytes(Path.Combine(output,pair.Key),pair.Value);
var manifest = new { manifestVersion=1, operation="offline-embedded-v6-long-kinematic-repeat", scope="Offline authoring and equivalence checks only; no native loading, simultaneous rotation/waving, restart, gameplay or collision-parity result is claimed.",
    sourceMap,sourceMapSha256=Hash(mapBytes),sourceItem,sourceItemSha256=Hash(itemBytes),dependencySource=package,dependencyHashes, embeddedFolder=folder, baselineItemByteExact=true,
    sourceClothBodySerializationSha256=sourceBodyHash, originalWholeMeshSerializationSha256=originalMeshes,originalFiveClothVisualSerializationSha256=visualHashes,
    sourceClothRestPosition=originalPrefab.Ents[1].Position,sourceClothRestQuaternion=originalPrefab.Ents[1].Rotation, sourceClothTiming=JsonSerializer.Deserialize<JsonElement>(ParamsJson(sourceParams)),
    stationaryVsRotatingOnlyNameAndTwoAngleMaximaDiffer=true,items=itemRows,maps=mapRows,
    limitations=new[] { "Pole converted from Static to Dyna; both kinematic bodies have tiny diagnostic triangular collision shapes. No vanilla collision/physics parity.",
        "Original cloth body, five full meshes/86 frame tables, attachment position/quaternion and 8-16s/0-1 timing retained. No vertex transforms baked.",
        "Rotating graph requests world-relative Y rotation at authored rest transforms. Actual pivot/attachment behavior must be checked in game; this is not a six-metre orbit or aircraft fixture.",
        "Individual maps contain only their selected Item plus the three exact dependencies, to avoid loading sibling motion controls. Every map has a genuine native Flag16m anchor first.",
        "Generated research maps use GBX.NET serialization; no native validation preservation or drivable track claim. Test baseline, stationary kinematic and rotating in separate fresh starts for strict comparison." } };
File.WriteAllText(Path.Combine(output,"manifest.json"),JsonSerializer.Serialize(manifest,Json));
foreach (var row in JsonSerializer.SerializeToElement(mapRows,Json).EnumerateArray())
{
    var file = row.GetProperty("file").GetString()!; var mapPath = Path.Combine(output,file);
    var ids = row.GetProperty("anchors").EnumerateArray().Select(a=>a.GetProperty("ItemModel").GetProperty("Id").GetString()!).Where(id=>id!="Flag16m").ToArray();
    var sidecar = new {manifestVersion=1,operation="offline-embedded-v6-kinematic-map-sidecar",scope="Offline exact package records; no game verdict.",
        outputMap=mapPath,outputSha256=Hash(File.ReadAllBytes(mapPath)),MapName=row.GetProperty("MapName").GetString(),MapUid=row.GetProperty("MapUid").GetString(),
        anchoredObjectCount=row.GetProperty("anchors").GetArrayLength(),embeddedFolder=folder,embeddedFiles=row.GetProperty("embeddedFiles"),expectedEmbeddedItemIds=ids,
        nativePrimerPresent=true,nativePrimerIndex=0,nativeFlagEmbedded=false,embeddedZipSha256=row.GetProperty("embeddedZipSha256").GetString()};
    File.WriteAllText(mapPath+".manifest.json",JsonSerializer.Serialize(sidecar,Json));
}
Console.WriteLine(JsonSerializer.Serialize(new {output,embeddedFolder=folder,mapCount=mapRows.Count,files=files.Count,offlineChecksPassed=true,simultaneousWaveAndRotationTested=false},Json));
}
static string NativeSnapshot(CGameCtnAnchoredObject a)=>JsonSerializer.Serialize(new{a.ItemModel,a.AbsolutePositionInMap,a.YawPitchRoll,a.PivotPosition,a.Scale,a.AnchorTreeId},Json);
static Gbx<CGameItemModel> ParseItem(byte[] bytes)=>Gbx.Parse<CGameItemModel>(new MemoryStream(bytes));
static CPlugPrefab Prefab(CGameItemModel item)=>(CPlugPrefab)((NPlugItem_SVariantList)item.EntityModel!).Variants![0].EntityModel!;
static NPlugDynaObjectModel_SInstanceParams Parameters(NPlugDynaObjectModel_SInstanceParams p,bool kinematic)=>new(){Version=p.Version,PeriodSc=p.PeriodSc,PeriodScMax=p.PeriodScMax,Phase01=p.Phase01,Phase01Max=p.Phase01Max,IsKinematic=kinematic,CastStaticShadow=p.CastStaticShadow,TextureId=p.TextureId};
static string ParamsJson(NPlugDynaObjectModel_SInstanceParams p)=>JsonSerializer.Serialize(new {p.Version,p.PeriodSc,p.PeriodScMax,p.Phase01,p.Phase01Max,p.IsKinematic,p.CastStaticShadow,p.TextureId},Json);
static KC.AnimFunc Timeline()=>new(){IsDuration=true,SubFuncs=[new(){Ease=KC.AnimEase.Linear,Reverse=false,Duration=new TimeInt32(12000)}]};
static string[] VisualHashes(CPlugSolid2Model mesh)=>mesh.Visuals!.Select(NodeHash).ToArray();
static string NodeHash<T>(T node) where T:CMwNod {using var memory=new MemoryStream();new Gbx<T>(node).Save(memory);return Hash(memory.ToArray());}
static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
static void Require(bool condition,string message){if(!condition)throw new InvalidDataException(message);}
static void ValidateReferences(Gbx<CGameItemModel> doc){Require(doc.RefTable!.AncestorLevel==0&&doc.RefTable.Files.Select(f=>f.FilePath).SequenceEqual(new[]{"TMIS_MatRemap_Placeholder.Material.Gbx","TMIS_MatRemap.TerrainModifier.Gbx"})&&doc.RefTable.Files.All(f=>f.Flags==1&&!f.UseFile),"v6 Item references changed");Require(doc.Node.References.MaterialModifier.File?.FilePath=="TMIS_MatRemap.TerrainModifier.Gbx", "v6 modifier lost");}
static string FindRoot(){for(var dir=new DirectoryInfo(Environment.CurrentDirectory);dir is not null;dir=dir.Parent)if(File.Exists(Path.Combine(dir.FullName,"TM-Item-Studio.csproj")))return dir.FullName;throw new DirectoryNotFoundException("Run within repository");}
sealed class ReferenceTableDocument: Gbx<CGameItemModel>{public ReferenceTableDocument(CGameItemModel item,GbxRefTable table):base(item)=>RefTable=table;}
}
