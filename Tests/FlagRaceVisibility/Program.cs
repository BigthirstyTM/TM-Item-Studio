using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.Game;

// Bounded offline XYZ-only anchor patch. No map reserialization, staging or process access.
Gbx.LZO = new GBX.NET.LZO.MiniLZO();
if (args.Length != 2 || args[0] != "--generate") throw new ArgumentException("Usage: --generate <unused-output-directory>");
var root = FindRoot(); var output = Path.GetFullPath(args[1]);
Check.That(!Directory.Exists(output) && !File.Exists(output), "Preserve evidence: choose an unused output directory");
var source = Path.Combine(root, "artifacts/flag-selfcontained-20261022/gravity-native-save/TMIS_Gravity_EmbeddedFlags_ShadowBaked_Repaired.Map.Gbx");
var sourceBytes = File.ReadAllBytes(source); var original = new MapArchive(sourceBytes); var map = original.Map;
Check.That(Check.Hash(sourceBytes) == "CA573B32C9894E73EC7462B525E5F8A5C83EC7B36E8F3A6A58578BE148BCFB19", "Unexpected final validated gravity source");
Check.That(map.Blocks?.Count == 5 && map.AnchoredObjects?.Count == 4 && map.AnchoredObjects[0].ItemModel?.Id == "Flag16m" && map.AnchoredObjects[0].ItemModel?.Author == "Nadeo", "Unexpected native roads or primer");
Check.That(map.AuthorTime?.TotalMilliseconds == 8060 && map.ChallengeParameters?.IsValidatedForScriptModes == true && map.ChallengeParameters.RaceValidateGhost is not null, "Expected actual source validation and ghost");
var start = map.Blocks!.Single(b => b.Name == "RoadTechStart");
Check.That(start.AbsolutePositionInMap == new Vec3(768,64,384) && start.YawPitchRoll == new Vec3(0,MathF.PI/12,0), "Unexpected final start geometry");
var anchorChunk = BoundedChunk(original.Body, 0x03043040);
var patched = original.Body.ToArray(); var words = new List<int>(); var rows = new List<object>();
var positions = new Dictionary<string,Vec3> {
    ["TMIS_MatRemap_Swallowtail.Item.Gbx"] = new(758,RoadOriginY(430)-7.5f,430),
    ["TMIS_MatRemap_Long250.Item.Gbx"] = new(758,RoadOriginY(446)-7.5f,446),
    ["TMIS_MatRemap_Round.Item.Gbx"] = new(802,RoadOriginY(430)-7.5f,430) };
foreach (var pair in positions)
{
    var a = map.AnchoredObjects!.Single(a => BaseName(a.ItemModel!.Id) == pair.Key);
    Check.That(a.AbsolutePositionInMap.X == 744 && a.AbsolutePositionInMap.Y == 8 && a.YawPitchRoll == new Vec3(MathF.PI/2,0,0) && a.PivotPosition == default && a.Scale == 1, "Unexpected source custom transform");
    var needle = VectorBytes(a.AbsolutePositionInMap); var matches = new List<int>();
    for (var scan=anchorChunk.Start+12; scan<=anchorChunk.End-needle.Length;) {
        var relative=original.Body.AsSpan(scan,anchorChunk.End-scan).IndexOf(needle);if(relative<0)break;
        var found=scan+relative;matches.Add(found);scan=found+1;
    }
    Check.That(matches.Count == 1, "Anchor position is not unique inside the bounded native anchor chunk: " + pair.Key);
    var offset = matches.Single(); VectorBytes(pair.Value).CopyTo(patched,offset);
    words.AddRange(new[]{offset,offset+4,offset+8});
    rows.Add(new {itemId=a.ItemModel!.Id, shape=pair.Key, originalPosition=a.AbsolutePositionInMap, outputPosition=pair.Value,
        yawPitchRoll=a.YawPitchRoll, positionOffsetInUncompressedBody=offset,roadOriginHeightAtFlagZ=RoadOriginY(pair.Value.Z),
        approximateClothHeightAboveRoadOrigin=new[]{0.7f,5.3f}, poleLateralClearanceFromNominalRoadEdge=pair.Value.X<768?768-pair.Value.X:pair.Value.X-800});
}
var restored=patched.ToArray();foreach(var offset in words)original.Body.AsSpan(offset,4).CopyTo(restored.AsSpan(offset,4));
Check.That(restored.AsSpan().SequenceEqual(original.Body), "Non-position map bytes changed");
using var memory=new MemoryStream();memory.Write(original.Header);
if(sourceBytes[7]==(byte)'C') {var compressed=Gbx.LZO.Compress(patched);memory.Write(BitConverter.GetBytes(patched.Length));memory.Write(BitConverter.GetBytes(compressed.Length));memory.Write(compressed);}
else memory.Write(patched);
var outputBytes=memory.ToArray();var result=new MapArchive(outputBytes);
Check.That(result.Header.AsSpan().SequenceEqual(original.Header) && result.Body.AsSpan().SequenceEqual(patched), "Raw body/header round-trip mismatch");
Check.That(result.Map.EmbeddedZipData!.AsSpan().SequenceEqual(map.EmbeddedZipData!), "Embedded ZIP changed");
Check.That(result.Map.ExpectedEmbeddedItemModels!.SequenceEqual(map.ExpectedEmbeddedItemModels!), "Expected Item identities changed");
Check.That(AnchorState(result.Map.AnchoredObjects![0])==AnchorState(map.AnchoredObjects![0]), "Native primer changed");
for(var i=1;i<4;i++) {
    var expected=positions[BaseName(map.AnchoredObjects![i].ItemModel!.Id)];var a=result.Map.AnchoredObjects![i];
    Check.That(a.AbsolutePositionInMap==expected, "Output placement was not parsed at the requested position");
    a.AbsolutePositionInMap=map.AnchoredObjects[i].AbsolutePositionInMap;
    Check.That(AnchorState(a)==AnchorState(map.AnchoredObjects[i]),"An unrequested anchor field changed");
}
Check.That(File.ReadAllBytes(source).AsSpan().SequenceEqual(sourceBytes), "Source changed");
var embeddedFiles=new List<object>();
using(var zip=map.OpenReadEmbeddedZipData()) foreach(var e in zip.Entries) {using var s=e.Open();using var m=new MemoryStream();s.CopyTo(m);var b=m.ToArray();embeddedFiles.Add(new{zipPath=e.FullName,bytes=b.Length,sha256=Check.Hash(b)});}
var json=new JsonSerializerOptions{WriteIndented=true,IncludeFields=true};
var outputMap=Path.Combine(output,"TMIS_Gravity_Three_Shapes_Start_Visibility_NeedsNativeSave.Map.Gbx");
var manifest=new {
    manifestVersion=1,operation="offline-raw-three-shape-start-race-visibility-placement",
    scope="Offline coordinate candidate only. Requires native revalidation, shadow bake, save and bounded dependency/reference repair before a fresh no-Openplanet Local race. No current visibility, waving, playability or valid-lightmap claim.",
    sourceMap=source,sourceSha256=Check.Hash(sourceBytes),outputMap,outputSha256=Check.Hash(outputBytes),outputBytes=outputBytes.Length,
    map.MapName,map.MapUid,anchoredObjectCount=4,nativePrimerPresent=true,nativePrimerIndex=0,nativeFlagEmbedded=false,
    embeddedFolder="TMIS_MatRemap_Shapes_66c295272549484d8580",embeddedFiles,expectedEmbeddedItemIds=map.ExpectedEmbeddedItemModels!.Select(i=>i.Id),embeddedZipSha256=Check.Hash(map.EmbeddedZipData!),
    nativeValidationDataPreservedFromSource=true,currentNativeValidationAcceptanceClaimed=false,currentShadowBakeValidClaimed=false,
    requestedChange="Only three custom anchors' XYZ float words. Every orientation, scale, pivot, Item/dependency byte, road, native primer and unrelated serialized byte is preserved.",
    road=new{startOrigin=start.AbsolutePositionInMap,startYawPitchRoll=start.YawPitchRoll,nominalRoadXRange=new[]{768,800},laneCenterX=784,routeDirection="+Z/down Y",heightFormula="64-(z-384)*tan(pi/12)",heightMeaning="model-origin sloped plane; actual road surface and car camera are not established by this formula"},
    placement=rows,changedFloatWordOffsetsInUncompressedBody=words,
    preservation=new{originalHeaderBytes=original.Header.Length,originalHeaderSha256=Check.Hash(original.Header),originalUncompressedBodySha256=Check.Hash(original.Body),outputUncompressedBodySha256=Check.Hash(patched),
        headerAndReferenceTableByteExact=true,onlyNinePositionWordsChanged=true,entireOriginalUncompressedBodyRestorableExactly=true,embeddedZipByteExact=true,
        allNativeBlocksValidationGhostTimesAndUnknownBytesExact=true,nativePrimerExact=true,noMapOrItemSerialization=true,noStagingOrGameAccess=true,sourceUnchanged=true},
    cameraEvidence=new[]{"gravity-shadowbaked-localrace-cold-no-openplanet/analysis/repeat-run-3s-contact.jpg: normal car view; old custom cloth not identifiable",
        "native-gravity-route/fixture/TMIS_Embedded_Flags_Race_Downhill15_NativeFirst.Map.Gbx.manifest.json: five native free-road origins, original custom Y8 placements"},
    limitations=new[]{"Source map name/UID are deliberately unchanged to preserve raw header bytes; isolate the new filename in the native browser and use native Save As for a unique final identity.",
        "Old author time/ghost and old shadow data remain byte-exact provenance. Moving scenery can stale baked lighting; they do not validate this candidate. Native revalidate, compute shadows, Save As, then repair that new export.",
        "The recommended coordinates are inferred from parsed road geometry and existing ordinary-car footage. Native car camera/FOV, actual road surface clearance and cloth visibility require in-game checks.",
        "All three cloths remain on ordinary upright static poles. Their Y placement puts the cloth near the driving eye line; the pole bases float beside/below the elevated road. No collision parity claim.",
        "The flags retain yaw pi/2, the proven broadside orientation. No test of any changed material, model, embedded dependency or shader initialization order is introduced."}
};
Directory.CreateDirectory(output);
File.WriteAllBytes(outputMap,outputBytes);File.WriteAllText(outputMap+".manifest.json",JsonSerializer.Serialize(manifest,json));
File.WriteAllText(Path.Combine(output,"findings.md"),$"""
    # Ordinary-race camera placement candidate

    This is an offline coordinate candidate requiring native revalidation, shadow computation, Save As and embedded reference/dependency repair before a cold Local race. It is not a new runtime success result. The accepted final gravity map is the pinned source (`{Check.Hash(sourceBytes)}`).

    The three proven embedded shapes remain byte-exact. Only their nine XYZ float words are patched inside the uniquely bounded native anchor chunk. Header/reference table, five native roads, validation ghost/times, baked blocks, native primer, the complete embedded ZIP and every unrelated raw map byte remain exact. Reversing these nine words restores the complete original uncompressed body. No GBX map or Item is reserialized.

    The start road origin is (768,64,384), pitched downhill 15 degrees along +Z; the nominal road spans X768..800, centered at X784. The old custom poles were at Y8, well below the elevated start. The new pole placements are Swallowtail (758,{positions["TMIS_MatRemap_Swallowtail.Item.Gbx"].Y:R},430), Long250 (758,{positions["TMIS_MatRemap_Long250.Item.Gbx"].Y:R},446), Round (802,{positions["TMIS_MatRemap_Round.Item.Gbx"].Y:R},430). All retain yaw pi/2 and scale1. Pole bases sit 7.5m below the sloped road model-origin plane, placing the cloth roughly 0.7..5.3m above that plane. This is a planning estimate, not a measured camera/surface clearance.

    Existing ordinary-car footage is available in `gravity-shadowbaked-localrace-cold-no-openplanet/analysis/repeat-run-3s-contact.jpg`; its forward view motivates close broadside cloths to either side of the road. Swallowtail and Long250 sit 10m left of the nominal road edge, with Long250 farther forward; Round sits 2m right of the edge. Neither perspective projection nor visible waving is proven offline.

    Source map name/UID and validation/shadow bytes are preserved as provenance. Their presence does not validate moved scenery or lighting. The native final Save As must supply a unique identity and newly computed shadows. The final native export then needs the existing bounded flag repair, followed by a fresh game-process Local race with no Openplanet/loose custom files and identifiable changing cloth poses in the ordinary car view.

    Output map SHA-256: `{Check.Hash(outputBytes)}`.
    """);
Console.WriteLine(JsonSerializer.Serialize(new{outputMap,outputSha256=Check.Hash(outputBytes),offlineChecksPassed=true,changedFloatWords=words.Count,requiresNativeRevalidateBakeSaveRepair=true,runtimeTested=false},json));

static float RoadOriginY(float z)=>64-(z-384)*MathF.Tan(MathF.PI/12);
static byte[] VectorBytes(Vec3 p)=>BitConverter.GetBytes(p.X).Concat(BitConverter.GetBytes(p.Y)).Concat(BitConverter.GetBytes(p.Z)).ToArray();
static string BaseName(string path)=>path.Replace('\\','/').Split('/').Last();
static string AnchorState(CGameCtnAnchoredObject a)=>JsonSerializer.Serialize(new{a.ItemModel,a.AnchorTreeId,a.AbsolutePositionInMap,a.YawPitchRoll,a.PivotPosition,a.Scale,a.Flags,a.Color,a.AnimPhaseOffset,a.LightmapQuality,a.SnappedOnGroup},new JsonSerializerOptions{IncludeFields=true});
static (int Start,int End) BoundedChunk(byte[] body,uint id) {
    var signature=BitConverter.GetBytes(id).Concat("PIKS"u8.ToArray()).ToArray();var found=new List<(int,int)>();
    for(var scan=0;scan<=body.Length-12;) {var r=body.AsSpan(scan).IndexOf(signature);if(r<0)break;var p=scan+r;scan=p+1;var size=BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(p+8));if(size>=0&&(long)p+12+size<=body.Length)found.Add((p,p+12+size));}
    Check.That(found.Count==1,"Cannot uniquely bound native anchor chunk");return found.Single();
}
static string FindRoot(){for(var d=new DirectoryInfo(Environment.CurrentDirectory);d is not null;d=d.Parent)if(File.Exists(Path.Combine(d.FullName,"TM-Item-Studio.csproj")))return d.FullName;throw new DirectoryNotFoundException("Run within repository");}
internal static class Check {public static void That(bool ok,string text){if(!ok)throw new InvalidDataException(text);}public static string Hash(byte[] b)=>Convert.ToHexString(SHA256.HashData(b));}
