using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.Game;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Plug;
using TM_Item_Studio.Models;

// Offline visual-isolation fixture only: no staging, editor action or process access.
Gbx.LZO = new GBX.NET.LZO.MiniLZO();
if (args.Length == 5 && args[0] == "pair") { BuildPair(args[1], args[2], args[3], args[4]); return; }
if (args.Length == 4 && args[0] == "audit-pair") { AuditPair(args[1], args[2], args[3]); return; }
if (args.Length != 3) throw new ArgumentException("Usage: FxVisualMap <native-start-map> <exact-item> <unused-output-directory>");
var source = Path.GetFullPath(args[0]);
var itemSource = Path.GetFullPath(args[1]);
var output = Path.GetFullPath(args[2]);
if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Preserve previous evidence: output must not exist.");
var sourceBytes = File.ReadAllBytes(source);
var itemBytes = File.ReadAllBytes(itemSource);
var itemHash=Hash(itemBytes);
var customSparkler=itemHash=="903E73BB38C0BD34B3447A9A0678255EBD696739D8FE3EF72AED477B6ED062B5";
var customFoggerVolume=itemHash=="82E1540DF19C9E3B226375BA176A1B5A80DEDAA483D2A9E1B5FA851BB660A079";
var customFoggerSprite=itemHash=="A97153015E365273EE380EAC72FCAD94C3498B46A9DE862AB1B8E63798B2A195";
var customFoggerThickFrame=itemHash=="391C764E18CCC5C16F2D25ABFE7ADB95877BB42D3BCD9862706EB27988C0B372";
var studioFogger=itemHash=="4AC27D570261D644DAC11CA598987673F9A6208EEAA60991691C57B349E41363";
var customFoggerUniqueThickFrame=itemHash=="0140B2B6E40780BE4F1016A9B98D858BF1F60C44CFB59CA5CAAEC3588B72AC07";
var customFoggerNoCollision=itemHash=="A2D0BA1B1E471E470FFD9F59967342AE847EA5BBA398E5EF21E30B6966E60133";
var mergedFoggerBell=itemHash=="4960E49D3D666F3FCEA8C6502481BA8E0D821F6EC73DBB2870F413C6D7ACB479";
var browserSparkler=itemHash=="E942E5FC4344D894F1DF8DBB21F90D62653045CA265709645B8930022C0F42A0";
var browserFoggerBell=itemHash=="B48FF6190840B3A5738F017D1492DB4D40C5E4DBEAC420F2B25B24302691B07C";
var sparkler=itemHash=="3C1F8FA89642AD33E918BAD8AC323CA8CE0A3EB2A40204B767C8AC8541863B44"||customSparkler||browserSparkler;
var fogger=itemHash=="0D11EB45C8A9F61F582D690A7D394042095D0C3E954E8B99EA42795BF58EAF61"||customFoggerVolume||customFoggerSprite||customFoggerThickFrame||studioFogger||customFoggerUniqueThickFrame||customFoggerNoCollision||mergedFoggerBell||browserFoggerBell;
Require(sparkler||fogger, "Unexpected resolved FX item bytes.");
var item = Gbx.ParseHeader<CGameItemModel>(new MemoryStream(itemBytes)).Node;
var ident = item.Ident ?? throw new InvalidDataException("Item header identity missing.");
Require(sparkler||mergedFoggerBell||browserFoggerBell ? string.IsNullOrEmpty(ident.Id)&&ident.Author=="5X7FYPqoTyWnW8fpyvCv5Q"
    : ident.Id==(studioFogger?"":"TMIS_FX_SequentialProof\\")+Path.GetFileName(itemSource)&&ident.Author=="-oTBhm4_S1-UxnlnBizUDA", "Unexpected native identity/author.");
var settings = new GbxReadSettings { SafeSkippableChunks = true, SkipChunkIds = [0x0304305B] };
var document = Gbx.Parse<CGameCtnChallenge>(new MemoryStream(sourceBytes), settings);
var map = document.Node;
var blocksBefore = Blocks(map);
var startsBefore = map.Blocks?.Where(block => block.Name.Contains("Start",StringComparison.OrdinalIgnoreCase)).Select(block => new {block.Name,block.AbsolutePositionInMap,block.YawPitchRoll}).ToArray() ?? [];
Require(startsBefore.Length > 0 && map.Blocks?.Count >= 1, "Source map must contain a real native start block.");
var oldAnchors = map.AnchoredObjects?.Count ?? 0;
var itemRelative = sparkler||browserFoggerBell ? "TMIS_FX_ResolvedVisual_20261025\\"+Path.GetFileName(itemSource)
    : mergedFoggerBell ? "TMIS_FX_SequentialProof\\"+Path.GetFileName(itemSource) : ident.Id;
var anchor = new CGameCtnAnchoredObject {
    ItemModel = new Ident(itemRelative, ident.Collection, ident.Author),
    AnchorTreeId = itemRelative,
    AbsolutePositionInMap = new Vec3(720,8,720),
    YawPitchRoll = default, PivotPosition = default, Scale = 1
};
anchor.CreateChunk<CGameCtnAnchoredObject.Chunk03101002>().Version = 8;
anchor.CreateChunk<CGameCtnAnchoredObject.Chunk03101004>().Version = 0;
map.AnchoredObjects = [anchor];
CGameCtnAnchoredObject? nativeComparison=null;
string? nativeComparisonHash=null;
if(customFoggerVolume||customFoggerSprite) {
    var nativeBytes=File.ReadAllBytes("artifacts/fx-clones/final-root/a/ShowFogger8M.Item.Gbx");
    var nativeIdent=Gbx.ParseHeader<CGameItemModel>(new MemoryStream(nativeBytes)).Node.Ident!;
    Require(nativeIdent.Author=="Nadeo"&&nativeIdent.Id.Equals("ShowFogger8m",StringComparison.OrdinalIgnoreCase),"Unexpected native Fogger comparison identity.");
    nativeComparison=new CGameCtnAnchoredObject {ItemModel=nativeIdent,AnchorTreeId=nativeIdent.Id,AbsolutePositionInMap=new Vec3(740,8,720),Scale=1};
    nativeComparison.CreateChunk<CGameCtnAnchoredObject.Chunk03101002>().Version=8;
    nativeComparison.CreateChunk<CGameCtnAnchoredObject.Chunk03101004>().Version=0;
    map.AnchoredObjects.Add(nativeComparison);
    nativeComparisonHash=Hash(nativeBytes);
}
map.MacroblockInstances?.Clear();
map.MapName = "TMIS Resolved "+(sparkler?"Sparkler":"Fogger")+" Visual Isolation";
map.MapUid = "TMISFXVIS_" + Guid.NewGuid().ToString("N")[..18];
map.EmbeddedZipData = null;
map.UpdateEmbeddedZipData(zip => {
    using var entry = zip.CreateEntry("Items/" + itemRelative.Replace('\\','/'),CompressionLevel.Optimal).Open();
    entry.Write(itemBytes);
});
using var stream = new MemoryStream();
document.Save(stream);
var bytes = stream.ToArray();
var reopened = Gbx.Parse<CGameCtnChallenge>(new MemoryStream(bytes),settings).Node;
Require(Blocks(reopened) == blocksBefore, "Native start/road block data changed.");
Require(reopened.AnchoredObjects?.Count == (nativeComparison is null?1:2), "Output anchor count differs.");
if(nativeComparison is not null) Require(reopened.AnchoredObjects![1].ItemModel==nativeComparison.ItemModel&&reopened.AnchoredObjects[1].AbsolutePositionInMap==nativeComparison.AbsolutePositionInMap,"Native comparison changed.");
var stored = reopened.AnchoredObjects![0];
Require(stored.ItemModel == anchor.ItemModel && stored.AbsolutePositionInMap == anchor.AbsolutePositionInMap && stored.PivotPosition == default && stored.Scale == 1, "Anchor identity or transform changed.");
var entries = new List<object>();
using (var zip = reopened.OpenReadEmbeddedZipData()) {
    Require(zip.Entries.Count == 1, "ZIP must contain only the exact item.");
    var entry = zip.Entries.Single();
    using var input = entry.Open(); using var memory = new MemoryStream(); input.CopyTo(memory);
    Require(memory.ToArray().AsSpan().SequenceEqual(itemBytes), "Embedded item bytes differ.");
    Require(entry.FullName == "Items/"+itemRelative.Replace('\\','/'), "ZIP item path differs from anchor identity.");
    entries.Add(new {path=entry.FullName,bytes=entry.Length,sha256=Hash(memory.ToArray())});
}
Require(File.ReadAllBytes(source).AsSpan().SequenceEqual(sourceBytes) && File.ReadAllBytes(itemSource).AsSpan().SequenceEqual(itemBytes),"Input source files changed.");
Directory.CreateDirectory(output);
var destination = Path.Combine(output,"TMIS_"+(customSparkler?"CustomSparklerDecimalPoint":customFoggerVolume?"CustomFoggerVolumeVisible":customFoggerSprite?"CustomFoggerSpriteVisible":customFoggerThickFrame?"CustomFoggerThickFrame":studioFogger?"StudioFoggerRootId":customFoggerUniqueThickFrame?"CustomFoggerUniqueThickFrame":customFoggerNoCollision?"CustomFoggerThickNoCollision":mergedFoggerBell?"FoggerVisibleCarrierBell":browserSparkler?"BrowserSparklerFlare":browserFoggerBell?"BrowserFoggerBell":sparkler?"ResolvedSparkler":"ResolvedFogger")+"_OneItem_ValidStart_20261025.Map.Gbx");
File.WriteAllBytes(destination,bytes);
var report = new {
    operation="offline-resolved-"+(sparkler?"sparkler":"fogger")+"-visual-isolation",
    sourceMap=source,sourceMapSha256=Hash(sourceBytes),sourceItem=itemSource,sourceItemSha256=Hash(itemBytes),
    outputMap=destination,outputSha256=Hash(bytes),outputBytes=bytes.Length,
    skippedCopyChunkIds=new[]{"0x0304305B (lightmap cache)"},
    sourceAnchorsRemoved=oldAnchors,outputAnchors=reopened.AnchoredObjects?.Count,
    nativeComparison=nativeComparison is null?null:new {nativeComparison.ItemModel,nativeComparison.AbsolutePositionInMap,sourceHeaderSha256=nativeComparisonHash,nativeAssetEmbedded=false},
    nativeBlocksPreserved=true,nativeBlockCount=reopened.Blocks?.Count,startBlocks=startsBefore,
    anchor=new {stored.ItemModel,stored.AnchorTreeId,stored.AbsolutePositionInMap,stored.PivotPosition,stored.Scale,stored.Flags},
    embeddedFiles=entries,onlyOneExactItemEmbedded=true,noTextureAssetsEmbedded=true,
    expectedEmbeddedItemModels=reopened.ExpectedEmbeddedItemModels?.Select(model=>new {model.Id,model.Collection,model.Author}),
    sourceAuthorTimeMilliseconds=map.AuthorTime?.TotalMilliseconds,
    sourceNativeValidationMetadataRetained=map.ChallengeParameters?.IsValidatedForScriptModes,
    scope="Offline engine-load/gameplay visual isolation using exact string-FID item bytes in a map with a native start. Not editor SaveMapAs success, fresh validation, fresh shadow bake or standalone particle-render proof.",
    noStagingOrGameActions=true,inputsUnchanged=true
};
var json=JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented=true,IncludeFields=true});
File.WriteAllText(destination+".manifest.json",json);
Console.WriteLine(json);

static void Require(bool condition,string message) { if(!condition) throw new InvalidDataException(message); }
static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
static string Blocks(CGameCtnChallenge map)=>JsonSerializer.Serialize(map.Blocks?.Select(block=>new {block.Name,block.Coord,block.Direction,block.Flags,block.AbsolutePositionInMap,block.YawPitchRoll}),new JsonSerializerOptions {IncludeFields=true});

static void BuildPair(string sourceName, string sparklerName, string foggerName, string outputName)
{
    var source = Path.GetFullPath(sourceName); var output = Path.GetFullPath(outputName);
    if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Preserve previous evidence: output must not exist.");
    var sourceBytes = File.ReadAllBytes(source);
    Require(Hash(sourceBytes)=="431C9F7B40652DE7941E68C0005175443C9E80E8FAEF2BAEC918904A7C157E5C", "Unexpected native-start map bytes.");
    var paths = new[] { Path.GetFullPath(sparklerName), Path.GetFullPath(foggerName) };
    var inputs = paths.Select(File.ReadAllBytes).ToArray();
    Require(Hash(inputs[0])=="E942E5FC4344D894F1DF8DBB21F90D62653045CA265709645B8930022C0F42A0"
        && Hash(inputs[1])=="B48FF6190840B3A5738F017D1492DB4D40C5E4DBEAC420F2B25B24302691B07C", "Unexpected browser-exported item bytes.");
    var relative = paths.Select(path=>"TMIS_FX_ResolvedVisual_20261025\\"+Path.GetFileName(path)).ToArray();
    var identities = inputs.Select(bytes=>Gbx.ParseHeader<CGameItemModel>(new MemoryStream(bytes)).Node.Ident!).ToArray();
    Require(identities.All(identity=>string.IsNullOrEmpty(identity.Id)&&identity.Author=="5X7FYPqoTyWnW8fpyvCv5Q"&&identity.Collection.Number==26), "Browser item null ID/author/collection changed.");
    var points = inputs.Select(bytes=>FxEmitterTransform.Read(bytes)).ToArray();
    Require(points.All(point=>Math.Abs(point.Primary.X-.5f)<.00002&&Math.Abs(point.Primary.Y-4.25f)<.00002&&Math.Abs(point.Primary.Z+.5f)<.00002), "Browser emission point is not (.5,4.25,-.5) in item space.");
    var settings = new GbxReadSettings { SafeSkippableChunks=true, SkipChunkIds=[0x0304305B] };
    var document = Gbx.Parse<CGameCtnChallenge>(new MemoryStream(sourceBytes),settings); var map=document.Node;
    var blocksBefore=Blocks(map); var oldAnchors=map.AnchoredObjects?.Count??0;
    Require(map.Blocks?.Count==5&&map.Blocks.Any(block=>block.Name.Contains("Start",StringComparison.OrdinalIgnoreCase)), "Native start/road fixture changed.");
    map.AnchoredObjects=identities.Select((identity,index)=>{
        var anchor=new CGameCtnAnchoredObject { ItemModel=new Ident(relative[index],identity.Collection,identity.Author),AnchorTreeId=relative[index],
            AbsolutePositionInMap=new Vec3(index==0?715:725,8,720),Scale=1 };
        anchor.CreateChunk<CGameCtnAnchoredObject.Chunk03101002>().Version=8;
        anchor.CreateChunk<CGameCtnAnchoredObject.Chunk03101004>().Version=0; return anchor;
    }).ToList();
    map.MacroblockInstances?.Clear(); map.MapName="TMIS Browser Custom Sparkler and Fogger"; map.MapUid="TMISFXPAIR_"+Guid.NewGuid().ToString("N")[..17];
    map.EmbeddedZipData=null; map.UpdateEmbeddedZipData(zip=>{
        for(var index=0;index<inputs.Length;index++) { using var entry=zip.CreateEntry("Items/"+relative[index].Replace('\\','/'),CompressionLevel.Optimal).Open(); entry.Write(inputs[index]); }
    });
    using var stream=new MemoryStream(); document.Save(stream); var bytes=stream.ToArray();
    var reopened=Gbx.Parse<CGameCtnChallenge>(new MemoryStream(bytes),settings).Node;
    Require(Blocks(reopened)==blocksBefore&&reopened.AnchoredObjects?.Count==2,"Native blocks or custom-only anchor count changed.");
    for(var index=0;index<inputs.Length;index++) Require(reopened.AnchoredObjects![index].ItemModel==map.AnchoredObjects[index].ItemModel
        &&reopened.AnchoredObjects[index].AbsolutePositionInMap==map.AnchoredObjects[index].AbsolutePositionInMap
        &&reopened.AnchoredObjects[index].Scale==1&&reopened.AnchoredObjects[index].PivotPosition==default,"Pair anchor identity or transform changed.");
    using(var zip=reopened.OpenReadEmbeddedZipData()) {
        Require(zip.Entries.Count==2,"Pair ZIP must contain exactly two exported Items.");
        for(var index=0;index<inputs.Length;index++) {
            var entry=zip.GetEntry("Items/"+relative[index].Replace('\\','/'))??throw new InvalidDataException("Exact embedded browser item missing.");
            using var entryStream=entry.Open();using var memory=new MemoryStream();entryStream.CopyTo(memory);
            Require(memory.ToArray().AsSpan().SequenceEqual(inputs[index]),"Embedded browser item changed.");
        }
    }
    Require(reopened.ExpectedEmbeddedItemModels?.Count==2&&relative.All(path=>reopened.ExpectedEmbeddedItemModels.Any(identity=>identity.Id==path&&identity.Author=="5X7FYPqoTyWnW8fpyvCv5Q")),"Expected item metadata differs from anchors.");
    Require(File.ReadAllBytes(source).AsSpan().SequenceEqual(sourceBytes)&&paths.Select((path,index)=>File.ReadAllBytes(path).AsSpan().SequenceEqual(inputs[index])).All(equal=>equal),"Pair input file changed.");
    Directory.CreateDirectory(output);var destination=Path.Combine(output,"TMIS_Browser_CustomSparkler_And_Fogger_ValidStart_20261025.Map.Gbx");File.WriteAllBytes(destination,bytes);
    var report=new { operation="offline-exact-browser-fx-pair-visual-fixture",sourceMap=source,sourceMapSha256=Hash(sourceBytes),outputMap=destination,outputSha256=Hash(bytes),outputBytes=bytes.Length,
        sourceAnchorsRemoved=oldAnchors,customAnchors=2,nativeFxAnchors=0,nativeBlocksPreserved=true,nativeBlockCount=5,skippedCopyChunkIds=new[]{"0x0304305B (lightmap cache)"},
        items=paths.Select((path,index)=>new {sourceItem=path,sourceItemSha256=Hash(inputs[index]),sourceItemBytes=inputs[index].Length,embeddedPath="Items/"+relative[index].Replace('\\','/'),
            anchor=reopened.AnchoredObjects![index].AbsolutePositionInMap,emissionPointItemSpace=points[index].Primary,owner=points[index].Owner,emitterCount=points[index].Emitters.Count,sourceHeaderAndBodyUntouched=true}),
        collisionScope="Both exact source collision shapes are retained. Bell's display is tapered; its inherited original Sparkler cube collision was not resized to match that display. No collision behavior claim is established by this map generation.",
        nativeRenderingTested=false,nativeEditorSaveSupportTested=false,noStagingOrGameActions=true,inputsUnchanged=true};
    var json=JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented=true,IncludeFields=true});File.WriteAllText(destination+".manifest.json",json);Console.WriteLine(json);
}

static void AuditPair(string sparklerName, string foggerName, string outputName)
{
    var paths=new[]{Path.GetFullPath(sparklerName),Path.GetFullPath(foggerName)};var inputs=paths.Select(File.ReadAllBytes).ToArray();
    Require(Hash(inputs[0])=="E942E5FC4344D894F1DF8DBB21F90D62653045CA265709645B8930022C0F42A0"
        &&Hash(inputs[1])=="B48FF6190840B3A5738F017D1492DB4D40C5E4DBEAC420F2B25B24302691B07C","Unexpected browser pair for collision audit.");
    var output=Path.GetFullPath(outputName);if(File.Exists(output))throw new IOException("Preserve previous audit: output must not exist.");
    var reports=inputs.Select((bytes,index)=>{
        var root=FxMeshPreview.Read(bytes).PreviewRoot;var entry=root.Ents[0];var model=(CPlugStaticObjectModel)entry.Model!;
        var surface=model.Shape?.Surf as CPlugSurface.Mesh;
        var display=model.Mesh!.Visuals!.OfType<CPlugVisualIndexedTriangles>().SelectMany(visual=>visual.VertexStreams.Count>0
            ?visual.VertexStreams.SelectMany(stream=>stream.Positions??[])
            :((CPlugVisual3D)visual).Vertices.Select(vertex=>vertex.Position)).ToArray();
        Require(surface is not null&&surface.Vertices.Length>0&&display.Length>0,"Expected inline collision and display mesh.");
        var collisionJson=JsonSerializer.Serialize(new {surface!.Vertices,surface.Triangles,surface.CookedTriangles},new JsonSerializerOptions {IncludeFields=true});
        return new {sourceItem=paths[index],sourceSha256=Hash(bytes),model.IsMeshCollidable,collisionSurfaceType=surface.GetType().FullName,
            collisionVertices=surface.Vertices.Length,collisionTriangles=surface.Triangles?.Length,collisionCookedTriangles=surface.CookedTriangles?.Length,
            collisionBoundsLocal=Bounds(surface.Vertices),collisionDecodedGeometrySha256=Hash(System.Text.Encoding.UTF8.GetBytes(collisionJson)),
            displayVertices=display.Length,displayBoundsLocal=Bounds(display),staticOwnerPosition=entry.Position,staticOwnerRotation=entry.Rotation};
    }).ToArray();
    Require(reports[0].collisionDecodedGeometrySha256==reports[1].collisionDecodedGeometrySha256,"Expected inherited original collision geometry differs between source carriers.");
    var report=new {operation="read-only-exact-browser-pair-collision-display-audit",items=reports,
        sameInheritedDecodedCollisionGeometry=true,bellCollisionResizedToDisplay=false,
        caveat="Bounds are local to the static mesh entry. This is a file inspection, not a native collision/driving test. Original collision remains independent of the new display shapes.",
        sourceItemBytesUnchanged=paths.Select((path,index)=>File.ReadAllBytes(path).AsSpan().SequenceEqual(inputs[index])).All(equal=>equal),noRuntimeActions=true};
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);var json=JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented=true,IncludeFields=true});File.WriteAllText(output,json);Console.WriteLine(json);
}
static object Bounds(Vec3[] vertices)=>new {min=new Vec3(vertices.Min(v=>v.X),vertices.Min(v=>v.Y),vertices.Min(v=>v.Z)),max=new Vec3(vertices.Max(v=>v.X),vertices.Max(v=>v.Y),vertices.Max(v=>v.Z))};
