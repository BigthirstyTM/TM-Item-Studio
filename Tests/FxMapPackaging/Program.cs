using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.Game;
using GBX.NET.Engines.GameData;
using TM_Item_Studio.Models;
using TM_Item_Studio.Models.FlagPackaging;
using TM_Item_Studio.Models.FxPackaging;

Gbx.LZO = new GBX.NET.LZO.MiniLZO();
if (args.Length > 0)
{
    if (args.Length != 5 || args[0] != "repair")
        throw new ArgumentException("Usage: FxMapPackaging repair <saved-map> <exact-original-item> <Items-relative-path> <unused-output.Map.Gbx>");
    var savedMapPath = Path.GetFullPath(args[1]); var originalItemPath = Path.GetFullPath(args[2]); var destination = Path.GetFullPath(args[4]);
    if (!destination.EndsWith(".Map.Gbx", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Output must end in .Map.Gbx");
    var reportPath = destination + ".packaging-report.json";
    if (File.Exists(destination) || File.Exists(reportPath)) throw new IOException("Preserve existing evidence: output and report must not exist");
    var savedMapBytes = File.ReadAllBytes(savedMapPath); var originalItemBytes = File.ReadAllBytes(originalItemPath);
    var savedMapHash = PackagingGuard.Hash(savedMapBytes); var originalItemHash = PackagingGuard.Hash(originalItemBytes);
    var result = FxMapPackaging.Repair(savedMapBytes, originalItemBytes, args[3], Path.GetFileName(savedMapPath));
    if (PackagingGuard.Hash(File.ReadAllBytes(savedMapPath)) != savedMapHash || PackagingGuard.Hash(File.ReadAllBytes(originalItemPath)) != originalItemHash)
        throw new IOException("Input file changed during packaging");
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.WriteAllBytes(destination, result.Bytes); File.WriteAllText(reportPath, result.ReportJson, new UTF8Encoding(false));
    Console.WriteLine(JsonSerializer.Serialize(new { savedMap = savedMapPath, savedMapSha256 = savedMapHash, originalItem = originalItemPath,
        originalItemSha256 = originalItemHash, itemRelativePath = args[3], outputMap = destination,
        outputSha256 = PackagingGuard.Hash(result.Bytes), outputBytes = result.Bytes.Length, report = reportPath, result.Warnings,
        nativeOrStagingActionsPerformed = false, inputFilesUnchanged = true }, new JsonSerializerOptions { WriteIndented = true }));
    return;
}
var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures");
var map = File.ReadAllBytes(Path.Combine(fixture, "NativeSaveAnyway.Map.Gbx"));
var item = File.ReadAllBytes(Path.Combine(fixture, "TMIS_Sparkler_FidStadiumBackslash_20261025.Item.Gbx"));
const string relative = "TMIS_FX_SequentialProof/TMIS_Sparkler_FidStadiumBackslash_20261025.Item.Gbx";
const string author = "5X7FYPqoTyWnW8fpyvCv5Q";
var checks = new List<string>();
Check(PackagingGuard.Hash(map) == "633F22C8DB7376EE859D523CFC2F6B82C3BA265BBF3C5EAB84181929E66812F0"
    && PackagingGuard.Hash(item) == "3C1F8FA89642AD33E918BAD8AC323CA8CE0A3EB2A40204B767C8AC8541863B44", "Pinned native map and resolved Sparkler fixtures match");
var sourceMapHash = PackagingGuard.Hash(map); var sourceItemHash = PackagingGuard.Hash(item);
var targets = FxMapPackaging.FindTargets(map, item, Path.GetFileName(relative));
Check(targets.Count == 1 && targets[0] == new FxMapPackagingTarget(relative, author, 1), "FindTargets matches exact filename, author, collection and existing path");
Check(FxMapPackaging.FindTargets(map, item, "Different.Item.Gbx").Count == 0, "FindTargets does not guess alternate basenames");
var repaired = FxMapPackaging.Repair(map, item, relative, "NativeSaveAnyway.Map.Gbx");
var before = new FxMapArchive(map); var after = new FxMapArchive(repaired.Bytes);
Check(before.Embedded.Zip.Length == 0 && before.Embedded.Identities.Count == 0 && after.Embedded.Identities.Count == 1,
    "Native SaveAnyway empty chunk is bootstrapped with one exact identity");
Check(after.Embedded.Identities[0] == new FxEmbeddedFiles.Identity(relative.Replace('/', '\\'), 26, null, author), "Independent GBX.NET reparse accepts restored identity");
Check(PackagingGuard.ReadZip(after.Embedded.Zip).Single().Value.AsSpan().SequenceEqual(item), "Restored embedded item is exact original bytes");
Check(before.Header.AsSpan().SequenceEqual(after.Header) && before.Body.AsSpan(0, before.ChunkStart).SequenceEqual(after.Body.AsSpan(0, after.ChunkStart))
    && before.Body.AsSpan(before.ChunkEnd).SequenceEqual(after.Body.AsSpan(after.ChunkEnd)), "Entire header/ref table and all uncompressed bytes outside chunk054 remain exact");
Check(after.Map.AnchoredObjects!.Single().ItemModel == before.Map.AnchoredObjects!.Single().ItemModel
    && after.Map.AnchoredObjects!.Single().AbsolutePositionInMap == before.Map.AnchoredObjects!.Single().AbsolutePositionInMap,
    "Existing anchor identity and placement remain unchanged");
Check(FxMapPackaging.Repair(repaired.Bytes, item, relative).Bytes.AsSpan().SequenceEqual(repaired.Bytes), "Repeated repair is entire-map byte-exact idempotent");
Check(repaired.FileName == "NativeSaveAnyway_EmbeddedFX.Map.Gbx" && repaired.Warnings.Any(w => w.Contains("every native editor save")), "Output naming and repeat-after-save diagnostic are explicit");

var otherName = "Items/Other/Keep.Item.Gbx";
var unrelated = new Dictionary<string, byte[]> { [otherName] = Encoding.UTF8.GetBytes("Unrelated item bytes"), ["Textures/Keep.bin"] = Enumerable.Range(0, 257).Select(i => (byte)i).ToArray() };
var unrelatedZip = Zip(unrelated);
var unrelatedPayload = before.Embedded.Replace(unrelatedZip, new("Other\\Keep.Item.Gbx", 26, null, "OtherAuthor"));
var unrelatedMap = before.Replace(unrelatedPayload).Bytes;
var withUnrelated = new FxMapArchive(FxMapPackaging.Repair(unrelatedMap, item, relative).Bytes);
var oldRaw = new RawZip(unrelatedZip); var newRaw = new RawZip(withUnrelated.Embedded.Zip);
foreach (var old in oldRaw.Entries)
{
    var next = newRaw.Entries.Single(e => e.Name == old.Name);
    var oldCentral = old.Central.ToArray(); var nextCentral = next.Central.ToArray(); oldCentral.AsSpan(42, 4).Clear(); nextCentral.AsSpan(42, 4).Clear();
    Check(old.Local.AsSpan().SequenceEqual(next.Local) && oldCentral.AsSpan().SequenceEqual(nextCentral), "Unrelated ZIP raw records and central metadata preserved: " + old.Name);
}
Check(withUnrelated.Embedded.Identities.Count == 2 && withUnrelated.Embedded.Identities[0] == new FxEmbeddedFiles.Identity("Other\\Keep.Item.Gbx", 26, null, "OtherAuthor"), "Unrelated metadata retained while FX identity appended");
Check(withUnrelated.Embedded.Payload.AsSpan(16, FindMetaEnd(unrelatedPayload) - 16)
    .SequenceEqual(unrelatedPayload.AsSpan(16, FindMetaEnd(unrelatedPayload) - 16)), "Existing scoped lookback bytes copied verbatim");
var aliasZip = Zip(new() { ["Items/Other/First.Item.Gbx"] = [1], ["Items/Other/Second.Item.Gbx"] = [2] });
var aliasPayload = MetadataAliasPayload(aliasZip);
var aliasSource = before.Replace(aliasPayload).Bytes;
var aliasResult = new FxMapArchive(FxMapPackaging.Repair(aliasSource, item, relative).Bytes);
var aliasSourceChunk = new FxEmbeddedFiles(aliasPayload);
Check(aliasResult.Embedded.Identities.Take(2).SequenceEqual(aliasSourceChunk.Identities)
    && aliasResult.Embedded.Payload.AsSpan(16, aliasPayload.Length - aliasZip.Length - 16 - 4 - TextureSuffix().Length)
        .SequenceEqual(aliasPayload.AsSpan(16, aliasPayload.Length - aliasZip.Length - 16 - 4 - TextureSuffix().Length)),
    "Existing author lookback alias and preceding metadata bytes retain their exact slots");
Check(aliasResult.Embedded.Payload.AsSpan(^TextureSuffix().Length).SequenceEqual(TextureSuffix()), "Original plain texture-string suffix remains byte exact");
var flag2Payload = MetadataAliasPayload(aliasZip, true);
var flag2Result = new FxMapArchive(FxMapPackaging.Repair(before.Replace(flag2Payload).Bytes, item, relative).Bytes);
Check(flag2Result.Embedded.Identities.Take(2).SequenceEqual(new FxEmbeddedFiles(flag2Payload).Identities),
    "Flag2 author definition and exact high-bit alias namespace survive reparse");
var numericPayload = MetadataAliasPayload(aliasZip, false, 0xC000001A);
var numericResult = new FxMapArchive(FxMapPackaging.Repair(before.Replace(numericPayload).Bytes, item, relative).Bytes);
Check(numericResult.Embedded.Identities.Take(2).All(identity => identity.CollectionNumber == unchecked((int)0xC000001A)),
    "Unrelated flag3 numeric collection retains its complete signed 32-bit identity");

var uncompressedMap = before.Header.Concat(before.Body).ToArray(); uncompressedMap[7] = (byte)'U';
var uncompressedResult = FxMapPackaging.Repair(uncompressedMap, item, relative);
Check(uncompressedResult.Bytes[7] == (byte)'U' && new FxMapArchive(uncompressedResult.Bytes).Header.AsSpan().SequenceEqual(uncompressedMap.AsSpan(0, before.Header.Length)),
    "Uncompressed map mode and complete header/ref table are retained");

var fxless = StripFx(item);
var droppedZip = Zip(new() { ["Items/" + relative] = fxless, ["Other/Keep.bin"] = [2, 4, 6] });
var droppedMap = before.Replace(before.Embedded.Replace(droppedZip, new(relative.Replace('/', '\\'), 26, null, author))).Bytes;
var restored = new FxMapArchive(FxMapPackaging.Repair(droppedMap, item, relative).Bytes);
Check(PackagingGuard.ReadZip(restored.Embedded.Zip)["Items/" + relative].AsSpan().SequenceEqual(item), "An existing matching FX-less item is restored from the exact source instead of reserialized");

foreach (var variant in new[] { "TMIS_CustomSparklerFlare_DecimalPoint.Item.Gbx", "TMIS_CustomFoggerTower_VolumeVisible_20261025.Item.Gbx" })
{
    var variantBytes = File.ReadAllBytes(Path.Combine(fixture, variant));
    var identity = Gbx.ParseHeader<CGameItemModel>(new MemoryStream(variantBytes)).Node.Ident!;
    var variantRelative = string.IsNullOrEmpty(identity.Id) ? "Custom/" + variant : identity.Id.Replace('\\', '/');
    var variantMap = MapWithAnchors(map, new Ident(variantRelative.Replace('/', '\\'), identity.Collection, identity.Author));
    var variantTargets = FxMapPackaging.FindTargets(variantMap, variantBytes, variant);
    Check(variantTargets.Count == 1 && variantTargets[0].ItemRelativePath == variantRelative, "Custom mesh source and exact identity accepted: " + variant);
    var variantOutput = FxMapPackaging.Repair(variantMap, variantBytes, variantRelative);
    Check(PackagingGuard.ReadZip(new FxMapArchive(variantOutput.Bytes).Embedded.Zip).Single().Value.AsSpan().SequenceEqual(variantBytes),
        "Custom carrier packaged byte exact: " + variant);
    if (!string.IsNullOrEmpty(identity.Id)) Reject(() => FxMapPackaging.Repair(variantMap, variantBytes, "Different/" + variant), "Stored full source identity cannot be renamed during packaging");
}
var sourceIdent = before.Map.AnchoredObjects!.Single().ItemModel!;
var repeatedAnchors = MapWithAnchors(map, sourceIdent, sourceIdent);
Check(FxMapPackaging.FindTargets(repeatedAnchors, item, Path.GetFileName(relative)).Single().Placements == 2, "Repeated exact anchors group into one target with placement count");
var collidingAnchors = MapWithAnchors(map, sourceIdent, new Ident(sourceIdent.Id, sourceIdent.Collection, "OtherAuthor"));
Reject(() => FxMapPackaging.Repair(collidingAnchors, item, relative), "Same anchor path with another author rejected");
var noAnchors = MapWithAnchors(map);
Reject(() => FxMapPackaging.Repair(noAnchors, item, relative), "Map with no anchors rejected");
foreach (var clubAuthor in new[] { "OtherAuthor", author })
{
    const string clubId = "club:123\\Unrelated.Item.Gbx";
    var clubMap = MapWithAnchors(map, sourceIdent, new Ident(clubId, 26, clubAuthor));
    var clubBefore = new FxMapArchive(clubMap);
    var clubZip = Zip(new() { ["ClubItems/123/Unrelated.Item.Gbx"] = [5, 6, 7] });
    var clubPayload = clubBefore.Embedded.Replace(clubZip, new(clubId, 26, null, clubAuthor));
    clubMap = clubBefore.Replace(clubPayload).Bytes; clubBefore = new FxMapArchive(clubMap);
    Check(FxMapPackaging.FindTargets(clubMap, item, Path.GetFileName(relative)).Count == 1, "Unrelated club anchor is excluded from local target discovery: " + clubAuthor);
    var clubAfter = new FxMapArchive(FxMapPackaging.Repair(clubMap, item, relative).Bytes);
    Check(clubAfter.Map.AnchoredObjects![1].ItemModel!.Id == clubId && clubAfter.Embedded.Identities[0] == clubBefore.Embedded.Identities[0]
        && clubAfter.Embedded.Payload.AsSpan(16, FindMetaEnd(clubPayload) - 16).SequenceEqual(clubPayload.AsSpan(16, FindMetaEnd(clubPayload) - 16))
        && PackagingGuard.ReadZip(clubAfter.Embedded.Zip)["ClubItems/123/Unrelated.Item.Gbx"].AsSpan().SequenceEqual(new byte[] { 5, 6, 7 }),
        "Unrelated club anchor, exact metadata bytes and ZIP payload preserved: " + clubAuthor);
}

Reject(() => FxMapPackaging.Repair(map, item, "../Bad.Item.Gbx"), "Unsafe selected path rejected");
Reject(() => FxMapPackaging.Repair(map, item, "Items/" + relative), "Non-relative Items prefix rejected");
Reject(() => FxMapPackaging.Repair(map, item, "club:123\\Target.Item.Gbx"), "Club identifier cannot become an injected local ZIP item path");
Reject(() => FxMapPackaging.Repair(map, item, relative + "/"), "Selected item path cannot name a directory");
Reject(() => FxMapPackaging.Repair(map, item, relative.ToLowerInvariant()), "Path case mismatch rejected");
Reject(() => FxMapPackaging.Repair(map, item, "Missing.Item.Gbx"), "Absent anchor rejected");
var wrongBodyAuthor = FxIdentity.Export(item, relative.Replace('/', '\\'), "DifferentAuthor").ItemBytes;
Reject(() => FxMapPackaging.Repair(map, wrongBodyAuthor, relative), "Source header/body author mismatch rejected");
var wrongExisting = before.Replace(before.Embedded.Replace(Zip(new() { ["Items/" + relative] = wrongBodyAuthor }), null)).Bytes;
Reject(() => FxMapPackaging.Repair(wrongExisting, item, relative), "Conflicting existing embedded item author rejected");
var wrongMetadata = before.Replace(before.Embedded.Replace([], new(relative.Replace('/', '\\'), 26, null, "DifferentAuthor"))).Bytes;
Reject(() => FxMapPackaging.Repair(wrongMetadata, item, relative), "Conflicting selected metadata author rejected");
var aliasMap = before.Replace(before.Embedded.Replace(Zip(new() { ["Items/" + relative.ToLowerInvariant()] = item }), null)).Bytes;
Reject(() => FxMapPackaging.Repair(aliasMap, item, relative), "Case-colliding selected ZIP path rejected");
var unsafeMap = before.Replace(before.Embedded.Replace(Zip(new() { ["../Unsafe.bin"] = [1] }), null)).Bytes;
Reject(() => FxMapPackaging.Repair(unsafeMap, item, relative), "Unsafe existing ZIP path rejected");
var duplicateMap = before.Replace(before.Embedded.Replace(Zip(new() { ["Other/Test.bin"] = [1], ["Other/test.bin"] = [2] }), null)).Bytes;
Reject(() => FxMapPackaging.Repair(duplicateMap, item, relative), "Case-colliding unrelated ZIP paths rejected");
var body = new FxEmissionPoint.Archive(item); var gpu = Find(body.Body, BitConverter.GetBytes(0x090C6002));
var unsupportedBody = body.Body.ToArray(); BinaryPrimitives.WriteInt32LittleEndian(unsupportedBody.AsSpan(gpu + 12), 0);
Reject(() => FxMapPackaging.Repair(map, body.Write(unsupportedBody), relative), "Unproven inline/non-FID texture branch rejected");
var arbitraryBody = body.Body.ToArray(); arbitraryBody[gpu + 20] = (byte)'X';
Reject(() => FxMapPackaging.Repair(map, body.Write(arbitraryBody), relative), "Arbitrary texture FID rejected");
Reject(() => FxMapPackaging.Repair(map, fxless, relative), "FX-less source rejected");
Reject(() => FxMapPackaging.Repair(map[..^1], item, relative), "Truncated compressed map rejected");
var badChunkBody = before.Body.ToArray(); BinaryPrimitives.WriteInt32LittleEndian(badChunkBody.AsSpan(before.ChunkStart + 20), int.MaxValue);
Reject(() => FxMapPackaging.Repair(WriteMapBody(before, badChunkBody), item, relative), "Malformed chunk enclosure rejected");
Reject(() => new FxEmbeddedFiles(new byte[23]), "Truncated embedded chunk rejected");
var oversizedCount = before.Embedded.Payload.ToArray(); BinaryPrimitives.WriteInt32LittleEndian(oversizedCount.AsSpan(12), 4097);
Reject(() => new FxEmbeddedFiles(oversizedCount), "Excessive metadata count rejected");
var unsupportedVersion = before.Embedded.Payload.ToArray(); BinaryPrimitives.WriteInt32LittleEndian(unsupportedVersion, 0);
Reject(() => new FxEmbeddedFiles(unsupportedVersion), "Version0 chunk explicitly refused rather than parsed with the version1 texture layout");
Reject(() => FxMapPackaging.Repair(WriteMapBody(before, before.Body[..(before.ChunkStart + 12)].Concat(unsupportedVersion).Concat(before.Body[before.ChunkEnd..]).ToArray()), item, relative),
    "Map packaging refuses unsupported version0 embedded-files chunk");
Check(PackagingGuard.Hash(map) == sourceMapHash && PackagingGuard.Hash(item) == sourceItemHash, "Input arrays unchanged after successful and refused operations");
Console.WriteLine(JsonSerializer.Serialize(new { passed = checks.Count, checks }, new JsonSerializerOptions { WriteIndented = true }));

void Check(bool passed, string text) { if (!passed) throw new Exception("FAIL: " + text); checks.Add(text); }
void Reject(Action action, string text) { try { action(); } catch (InvalidDataException) { checks.Add(text); return; } throw new Exception("FAIL (accepted): " + text); }
static byte[] Zip(Dictionary<string, byte[]> files)
{
    using var memory = new MemoryStream(); using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true)) foreach (var pair in files)
    { var entry = zip.CreateEntry(pair.Key); entry.LastWriteTime = new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.Zero); using var stream = entry.Open(); stream.Write(pair.Value); }
    return memory.ToArray();
}
static byte[] MapWithAnchors(byte[] source, params Ident[] identities)
{
    // Typed serialization is only test-fixture setup, never the production packaging path.
    var document = Gbx.Parse<CGameCtnChallenge>(new MemoryStream(source), new GbxReadSettings { SkipChunkIds = [0x0304305B] });
    document.Node.AnchoredObjects = identities.Select(identity => {
        var anchor = new CGameCtnAnchoredObject { ItemModel = identity, AnchorTreeId = identity.Id, AbsolutePositionInMap = new Vec3(720, 8, 720), Scale = 1 };
        anchor.CreateChunk<CGameCtnAnchoredObject.Chunk03101002>().Version = 8;
        anchor.CreateChunk<CGameCtnAnchoredObject.Chunk03101004>().Version = 0; return anchor;
    }).ToList();
    using var output = new MemoryStream(); document.Save(output); return output.ToArray();
}
static int Find(byte[] bytes, byte[] needle) { var at = bytes.AsSpan().IndexOf(needle); if (at < 0) throw new Exception("Signature absent"); return at; }
static byte[] StripFx(byte[] item)
{
    var archive = new FxEmissionPoint.Archive(item); var native = FxEmissionPoint.Read(item); var end = native.Emitters[^1].LengthOffset;
    for (var i = 0; i < 10; i++) end += 4 + BinaryPrimitives.ReadInt32LittleEndian(archive.Body.AsSpan(end));
    end += 4;
    for (var i = 0; i < 2; i++) end += 4 + BinaryPrimitives.ReadInt32LittleEndian(archive.Body.AsSpan(end));
    end += 20; var start = native.FxGraphOffset - 4;
    return archive.Write(archive.Body[..start].Concat(BitConverter.GetBytes(-1)).Concat(archive.Body[end..]).ToArray());
}
static byte[] WriteMapBody(FxMapArchive map, byte[] body)
{
    using var memory = new MemoryStream(); memory.Write(map.Header);
    if (map.Source[7] == (byte)'C') { var zipped = Gbx.LZO!.Compress(body); memory.Write(BitConverter.GetBytes(body.Length)); memory.Write(BitConverter.GetBytes(zipped.Length)); memory.Write(zipped); }
    else memory.Write(body); return memory.ToArray();
}
static int FindMetaEnd(byte[] payload)
{
    // This test setup has one known newly defined ID + numeric collection + new author.
    var p = 24; p += 4 + BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(p)); p += 8;
    p += 4 + BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(p)); return p;
}
static byte[] MetadataAliasPayload(byte[] zip, bool flag2Author = false, uint collection = 26)
{
    using var enclosed = new MemoryStream();
    Put(enclosed, 2); Put(enclosed, 3);
    Def(enclosed, "Other\\First.Item.Gbx"); Put(enclosed, collection); Put(enclosed, flag2Author ? 0x80000000 : 0x40000000); Text(enclosed, "SharedAuthor");
    Def(enclosed, "Other\\Second.Item.Gbx"); Put(enclosed, collection); Put(enclosed, flag2Author ? 0x80000002 : 0x40000002);
    Put(enclosed, (uint)zip.Length); enclosed.Write(zip); enclosed.Write(TextureSuffix());
    using var result = new MemoryStream(); Put(result, 1); Put(result, 0); Put(result, (uint)enclosed.Length); result.Write(enclosed.ToArray()); return result.ToArray();
}
static byte[] TextureSuffix()
{ using var memory = new MemoryStream(); Put(memory, 2); Text(memory, "Textures/Keep.dds"); Text(memory, "Textures/Other.dds"); return memory.ToArray(); }
static void Def(Stream stream, string text) { Put(stream, 0x40000000); Text(stream, text); }
static void Text(Stream stream, string text) { var bytes = Encoding.UTF8.GetBytes(text); Put(stream, (uint)bytes.Length); stream.Write(bytes); }
static void Put(Stream stream, uint value) { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, value); stream.Write(bytes); }
