using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.Game;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;
using TM_Item_Studio.Models;
using TM_Item_Studio.Models.FlagPackaging;

Gbx.LZO = new GBX.NET.LZO.MiniLZO();
if (args.Length == 6 && args[0] == "--verify-output") {
    VerifyBrowserOutput(args[1], args[2], args[3], args[4], args[5]); return;
}
if (args.Length != 0 && (args.Length != 2 || args[0] != "--fixtures"))
    throw new ArgumentException("Usage: dotnet run --project Tests/WavingFlagMapPackaging [-- --fixtures <new-directory> | --verify-output <browser-map> <expected-map> <native-map> <package-zip> <report-output>]");
var native = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures/NativeSaved.Map.Gbx"));
Req(Hash(native) == "431C9F7B40652DE7941E68C0005175443C9E80E8FAEF2BAEC918904A7C157E5C", "Tracked native fixture hash changed.");
var originalHash = Hash(native); var document = new MapArchive(native);
var packages = new[] { "Swallowtail", "Long250", "Round" }.ToDictionary(n => n, n => Package("TMIS_MatRemap_" + n + ".Item.Gbx"));
var package = packages["Long250"]; var checks = 0;
var result = ItemWavingFlagMapExport.Repair(native, package, "NativeSaved.Map.Gbx");
Check("genuine native preset repair and exact known output", () => {
    Req(Hash(result.Bytes) == "C043DCB2A0925DC1503D73446209D96B230BFE8E756C8940F47B6DED9A664A59", "Single selected repair differs from established proof hash.");
    using var json = JsonDocument.Parse(result.ReportJson); var r = json.RootElement;
    Req(r.GetProperty("repairedItemCount").GetInt32() == 1 && r.GetProperty("restoredDependencyCount").GetInt32() == 3, "Wrong selected repair counts.");
    Req(r.GetProperty("nativeFlag16mCount").GetInt32() == 1 && !r.GetProperty("nativeFlagAdded").GetBoolean(), "Native flag prerequisite changed.");
    Req(r.GetProperty("exactDecodedGeometryAndGraphMatched").GetBoolean() && !r.GetProperty("preservation").GetProperty("nativeRenderingTested").GetBoolean(), "Evidence claim mismatch.");
});
Check("header, validation, roads, anchors and input buffers preserved", () => {
    var output = new MapArchive(result.Bytes);
    Req(output.Header.SequenceEqual(document.Header), "Header changed.");
    Req(output.Map.Blocks!.Count == 5 && output.Map.AnchoredObjects!.Count == 4, "Map graph changed.");
    Req(output.Map.AuthorTime?.TotalMilliseconds == 8060 && output.Map.ChallengeParameters?.IsValidatedForScriptModes == true
        && output.Map.ChallengeParameters.RaceValidateGhost is not null, "Native validation fields changed.");
    Req(Hash(native) == originalHash, "Source map mutated.");
});
Check("byte-identical repeat repair", () => Req(ItemWavingFlagMapExport.Repair(result.Bytes, package).Bytes.SequenceEqual(result.Bytes), "Repair is not byte-idempotent."));
Check("three packages sequentially reproduce accepted native proof", () => {
    var output = native;
    foreach (var p in packages.Values) output = ItemWavingFlagMapExport.Repair(output, p).Bytes;
    Req(Hash(output) == "F4899504479FEED65073C29D0DA7D16D0F3F703AE743B5A2706CA2E171C7869E", "Sequential map differs from accepted full proof hash.");
});
var noNative = MutateMap(native, m => m.AnchoredObjects!.RemoveAll(a => a.ItemModel?.Id == "Flag16m" && a.ItemModel.Author == "Nadeo"));
Refuse("no native Flag16m", noNative, package);
Refuse("truncated map", native[..20], package);
Refuse("truncated package", native, package[..20]);
var packageFiles = Unzip(package); var itemPath = packageFiles.Keys.Single(p => p.EndsWith(".Item.Gbx"));
RefusePackage("wrong pinned dependency", f => f["Flag/TMIS_MatRemap.GameSkin.Gbx"][^1] ^= 1);
RefusePackage("missing pinned dependency", f => f.Remove("Flag/TMIS_MatRemap.GameSkin.Gbx"));
RefusePackage("path traversal", f => f.Add("../outside.txt", [1]));
RefusePackage("case-colliding path", f => f.Add("flag/TMIS_MatRemap.GameSkin.Gbx", [1]));
RefusePackage("multiple source Items", f => f.Add("Flag/Other.Item.Gbx", f[itemPath]));
RefusePackage("unexpected GBX dependency", f => f.Add("Flag/Other.Mesh.Gbx", [1]));
RefusePackage("unmatched Item basename", f => { f["Flag/Unknown.Item.Gbx"] = f[itemPath]; f.Remove(itemPath); });
RefusePackageItem("changed geometry", i => Cloth(i).Mesh!.Visuals!.Cast<CPlugVisualIndexedTriangles>().First().Vertices[1] = Cloth(i).Mesh!.Visuals!.Cast<CPlugVisualIndexedTriangles>().First().Vertices[1] with { Position = new(123, 456, 789) });
RefusePackageItem("changed timing", i => ((NPlugDynaObjectModel_SInstanceParams)Prefab(i).Ents![^1].Params!).PeriodSc = 7);
RefusePackageItem("kinematic cloth", i => ((NPlugDynaObjectModel_SInstanceParams)Prefab(i).Ents![^1].Params!).IsKinematic = true);
RefusePackageItem("unclosed loop", i => { var v = (CPlugVisualIndexedTriangles)Cloth(i).Mesh!.Visuals![0]; v.Vertices[^1] = v.Vertices[^1] with { Position = new(123, 456, 789) }; });
RefusePackageItem("changed auxiliary cloth field", i => Cloth(i).LightAliveDurationScMin += 1);
RefusePackageItem("unsupported water graph", i => Cloth(i).WaterModel = new CPlugDynaWaterModel());
RefusePackageItem("changed auxiliary mesh field", i => Cloth(i).Mesh!.VisCstType++);
RefusePackageItem("changed static pole parameters", i => Prefab(i).Ents![0].Params = new NPlugDynaObjectModel_SInstanceParams());
RefusePackageItem("alternate root visual", i => i.VisModelCustom = Prefab(i));
RefusePackageItem("alternate root physics", i => i.PhyModelCustom = Prefab(i));
RefusePackageItem("alternate archetype", i => i.ArchetypeRef = "Other.Item.Gbx");
Check("generic SourceMesh compressed Item and variable node count accepted against synthetic native reference layout", () => {
    // This is an offline serialization control, deliberately not native runtime evidence.
    var exported = ItemWavingFlagExport.Export(Source(), new() { OutputName = "OfflineCustom", PeriodSeconds = 7 });
    var f = Unzip(exported.Bytes); var sourceBytes = f.Single(p => p.Key.EndsWith(".Item.Gbx")).Value;
    var item = Gbx.Parse<CGameItemModel>(new MemoryStream(sourceBytes)).Node;
    Req(Prefab(item).Ents!.Length == 1, "SourceMesh retained pole.");
    const string folder = "OfflineSynthetic"; var id = folder + "\\" + exported.ItemFileName;
    item.Ident = item.Ident! with { Id = id };
    var table = new GbxRefTable { AncestorLevel = 3 };
    Cloth(item).Mesh!.Materials = new External<CPlugMaterial>[] { new External<CPlugMaterial>(null, new GbxRefTableFile(table, 1, false, "ContentLoaded\\Items\\" + folder + "\\TMIS_MatRemap_Placeholder.Material.Gbx")) };
    item.References.MaterialModifier.Descriptor = new GbxRefTableFile(table, 1, false, "ContentLoaded\\Items\\" + folder + "\\TMIS_MatRemap.TerrainModifier.Gbx");
    using var saved = new MemoryStream(); new ReferenceDocument(item, table).Save(saved); var savedBytes = saved.ToArray();
    // Compress the body to exercise the saved Item C mode without changing its graph.
    savedBytes = CompressItem(savedBytes);
    var mapBytes = MutateMap(native, m => {
        var custom = m.AnchoredObjects!.First(a => a.ItemModel?.Author != "Nadeo");
        var nativeAnchor = m.AnchoredObjects!.Single(a => a.ItemModel?.Id == "Flag16m" && a.ItemModel.Author == "Nadeo");
        custom.ItemModel = item.Ident; custom.AnchorTreeId = id;
        m.AnchoredObjects!.Clear(); m.AnchoredObjects.Add(nativeAnchor); m.AnchoredObjects.Add(custom);
        m.EmbeddedZipData = null; m.UpdateEmbeddedZipData(z => { using var stream = z.CreateEntry("Items/" + folder + "/" + exported.ItemFileName).Open(); stream.Write(savedBytes); });
    });
    var repaired = ItemWavingFlagMapExport.Repair(mapBytes, exported.Bytes);
    using var report = JsonDocument.Parse(repaired.ReportJson); var r = report.RootElement;
    Req(r.GetProperty("VerticesPerFrame").EnumerateArray().All(v => v.GetInt32() == 12), "Generic topology not accepted.");
    Req(r.GetProperty("itemArchiveSplice").GetProperty("originalNodeCount").GetInt32() != 29 && savedBytes[7] == (byte)'C', "Synthetic control failed to vary native node count/body mode.");
    Req(ItemWavingFlagMapExport.Repair(repaired.Bytes, exported.Bytes).Bytes.SequenceEqual(repaired.Bytes), "SourceMesh repeat is not byte exact.");
});
Check("uncompressed map mode retained", () => {
    var header = document.Header.ToArray(); header[7] = (byte)'U'; var bytes = header.Concat(document.Body).ToArray();
    var output = ItemWavingFlagMapExport.Repair(bytes, package).Bytes; Req(output[7] == (byte)'U', "Uncompressed mode changed.");
    Req(new MapArchive(output).Map.AnchoredObjects!.Count == 4, "Uncompressed output malformed.");
});
Check("conflicting dependency refused atomically", () => {
    var f = Unzip(document.Map.EmbeddedZipData!); var path = f.Keys.Single(n => n.EndsWith("TMIS_MatRemap_Long250.Item.Gbx")); var folder = path[..path.LastIndexOf('/')];
    f.Add(folder + "/TMIS_MatRemap.GameSkin.Gbx", [1, 2, 3]); Refuse("conflicting dependency", document.ReplaceZip(Zip(f)).Output, package);
});
Check("ambiguous embedded basename refused atomically", () => {
    var f = Unzip(document.Map.EmbeddedZipData!); var pair = f.Single(p => p.Key.EndsWith("TMIS_MatRemap_Long250.Item.Gbx")); f.Add("Items/Duplicate/TMIS_MatRemap_Long250.Item.Gbx", pair.Value);
    Refuse("ambiguous basename", document.ReplaceZip(Zip(f)).Output, package);
});
Check("unrelated compressed ZIP records, extras, attributes and comments preserved", () => {
    const string sentinel = "Research/unrelated.bin";
    var f = Unzip(document.Map.EmbeddedZipData!); f.Add(sentinel, Enumerable.Range(0, 4096).Select(i => (byte)(i * 37)).ToArray());
    var decorated = DecoratedZip(f, sentinel); var fixture = document.ReplaceZip(decorated).Output;
    var output = new MapArchive(ItemWavingFlagMapExport.Repair(fixture, package).Bytes).Map.EmbeddedZipData!;
    var a = new RawZip(decorated).Entries.Single(e => e.Name == sentinel); var b = new RawZip(output).Entries.Single(e => e.Name == sentinel);
    Req(a.Local.SequenceEqual(b.Local), "Unrelated local payload/extras changed.");
    var before = a.Central.ToArray(); var after = b.Central.ToArray(); before.AsSpan(42, 4).Clear(); after.AsSpan(42, 4).Clear();
    Req(before.SequenceEqual(after) && ZipComment(decorated).SequenceEqual(ZipComment(output)), "Unrelated central metadata/comments changed.");
});
if (args.Length == 2) {
    var target = Path.GetFullPath(args[1]); Req(!Directory.Exists(target) && !File.Exists(target), "Fixture output directory must not already exist."); Directory.CreateDirectory(target);
    File.WriteAllBytes(Path.Combine(target, "source-package.zip"), package);
    File.WriteAllBytes(Path.Combine(target, "native-saved.Map.Gbx"), native);
    File.WriteAllBytes(Path.Combine(target, "no-native.Map.Gbx"), noNative);
    File.WriteAllBytes(Path.Combine(target, "expected-repaired.Map.Gbx"), result.Bytes);
    File.WriteAllText(Path.Combine(target, "README.txt"), "Offline browser controls. native-saved is the immutable tracked native game save. no-native is a synthetic refusal control, produced offline by removing the native anchor; it is not game evidence. Expected output is the bounded in-memory repair.\n");
    Console.WriteLine("Browser fixtures: " + target);
}
Console.WriteLine($"{checks} offline map packaging checks passed; no game operation was performed.");

void Check(string name, Action body) { body(); checks++; Console.WriteLine("PASS " + name); }
void Refuse(string name, byte[] map, byte[] source) {
    var a = Hash(map); var b = Hash(source); var refused = false;
    try { ItemWavingFlagMapExport.Repair(map, source); } catch (InvalidDataException) { refused = true; }
    Req(refused && a == Hash(map) && b == Hash(source), "Expected unchanged-input refusal: " + name); checks++; Console.WriteLine("PASS refuses " + name + " without input mutation");
}
void RefusePackage(string name, Action<Dictionary<string, byte[]>> change) { var f = Unzip(package); change(f); Refuse(name, native, Zip(f)); }
void RefusePackageItem(string name, Action<CGameItemModel> change) => RefusePackage(name, f => {
    var d = Gbx.Parse<CGameItemModel>(new MemoryStream(f[itemPath])); change(d.Node); using var m = new MemoryStream(); new Gbx<CGameItemModel>(d.Node).Save(m); f[itemPath] = m.ToArray();
});
static byte[] Package(string item) {
    var f = new Dictionary<string, byte[]> { ["Flag/" + item] = Resource(item) };
    foreach (var n in new[] { "TMIS_MatRemap_Placeholder.Material.Gbx", "TMIS_MatRemap.GameSkin.Gbx", "TMIS_MatRemap.TerrainModifier.Gbx" }) f.Add("Flag/" + n, Resource(n));
    return Zip(f);
}
static byte[] MutateMap(byte[] bytes, Action<CGameCtnChallenge> action) {
    var d = Gbx.Parse<CGameCtnChallenge>(new MemoryStream(bytes), new GbxReadSettings { SkipChunkIds = [0x0304305B] }); action(d.Node);
    using var m = new MemoryStream(); d.Save(m); return m.ToArray();
}
static CPlugPrefab Prefab(CGameItemModel i) => (CPlugPrefab)((NPlugItem_SVariantList)i.EntityModel!).Variants![0].EntityModel!;
static CPlugDynaObjectModel Cloth(CGameItemModel i) => (CPlugDynaObjectModel)Prefab(i).Ents![^1].Model!;
static Gbx<CGameItemModel> Source() {
    var seed = Gbx.Parse<CGameItemModel>(new MemoryStream(Resource("TMIS_MatRemap_Long250.Item.Gbx"))); var p = Prefab(seed.Node); var mesh = Cloth(seed.Node).Mesh!;
    var v = (CPlugVisualIndexedTriangles)mesh.Visuals![0]; var prototype = v.Vertices[0]; var vertices = new List<CPlugVisual3D.Vertex>(); var uv = new List<CPlugVisual.TexCoord>();
    for (int y = 0; y < 3; y++) for (int x = 0; x < 4; x++) { vertices.Add(prototype with { Position = new(x * 1.5f, y, 0), Normal = new(0, 0, 1) }); uv.Add(new(new(x / 3f, y / 2f), null, null)); }
    var indices = new List<int>(); for (int y = 0; y < 2; y++) for (int x = 0; x < 3; x++) { int a = y * 4 + x; indices.AddRange([a, a + 1, a + 4, a + 1, a + 5, a + 4]); }
    v.Vertices = vertices.ToArray(); v.SubVisuals = []; v.IndexBuffer!.Indices = indices.ToArray(); typeof(CPlugVisual).GetProperty("Count", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(v, vertices.Count);
    v.TexCoords = [new() { Version = 3, Flags = 256, U01 = [], TexCoords = uv.ToArray() }];
    mesh.Visuals = [v]; mesh.LodMaxDistAtFov90 = [100]; mesh.Materials = null; mesh.CustomMaterials = [new() { MaterialName = "source" }]; mesh.ShadedGeoms = [new() { VisualIndex = 0, MaterialIndex = 0, LodMask = 1 }];
    var pole = (CPlugStaticObjectModel)p.Ents![0].Model!; pole.Mesh = mesh; pole.Shape = null; if (pole.References.Shape.Descriptor is not null) pole.References.Shape.Clear(); pole.IsMeshCollidable = false;
    seed.Node.EntityModel = new CPlugPrefab { Version = 11, Ents = [new() { Model = pole, Rotation = Quat.Identity, U01 = "" }] }; seed.Node.MaterialModifier = null; if (seed.Node.References.MaterialModifier.Descriptor is not null) seed.Node.References.MaterialModifier.Clear();
    using var output = new MemoryStream(); new Gbx<CGameItemModel>(seed.Node).Save(output); output.Position = 0; return Gbx.Parse<CGameItemModel>(output);
}
static byte[] CompressItem(byte[] bytes) {
    if (bytes[7] == (byte)'C') return bytes;
    using var input = new MemoryStream(bytes); _ = Gbx.ParseHeader<CGameItemModel>(input); var offset = checked((int)input.Position);
    var header = bytes[..offset]; header[7] = (byte)'C'; var body = bytes[offset..]; var compressed = Gbx.LZO!.Compress(body);
    return header.Concat(BitConverter.GetBytes(body.Length)).Concat(BitConverter.GetBytes(compressed.Length)).Concat(compressed).ToArray();
}
static byte[] Resource(string n) { using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("TM_Item_Studio.Resources.Flags." + n)!; using var m = new MemoryStream(); s.CopyTo(m); return m.ToArray(); }
static byte[] Zip(IReadOnlyDictionary<string, byte[]> entries) {
    using var memory = new MemoryStream(); using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true)) foreach (var e in entries) {
        var created = zip.CreateEntry(e.Key); created.LastWriteTime = new DateTimeOffset(2026, 10, 22, 0, 0, 0, TimeSpan.Zero); using var stream = created.Open(); stream.Write(e.Value);
    } return memory.ToArray();
}
static Dictionary<string, byte[]> Unzip(byte[] b) { using var z = new ZipArchive(new MemoryStream(b)); return z.Entries.ToDictionary(e => e.FullName, e => { using var s = e.Open(); using var m = new MemoryStream(); s.CopyTo(m); return m.ToArray(); }); }
static byte[] DecoratedZip(Dictionary<string, byte[]> payloads, string sentinel) {
    var archive = new RawZip(Zip(payloads)); using var output = new MemoryStream(); var offsets = new Dictionary<string, uint>(); var centrals = new List<byte[]>();
    byte[] extra = [0xFE, 0xCA, 3, 0, 0x11, 0x22, 0x33]; var comment = Encoding.UTF8.GetBytes("unrelated entry metadata must survive");
    foreach (var entry in archive.Entries) {
        offsets[entry.Name] = checked((uint)output.Position); var local = entry.Local.ToArray(); var central = entry.Central.ToArray();
        if (entry.Name == sentinel) {
            var start = 30 + BinaryPrimitives.ReadUInt16LittleEndian(local.AsSpan(26)); local = local[..start].Concat(extra).Concat(local[start..]).ToArray();
            BinaryPrimitives.WriteUInt16LittleEndian(local.AsSpan(28), (ushort)extra.Length);
            central = central.Concat(extra).Concat(comment).ToArray(); BinaryPrimitives.WriteUInt16LittleEndian(central.AsSpan(30), (ushort)extra.Length); BinaryPrimitives.WriteUInt16LittleEndian(central.AsSpan(32), (ushort)comment.Length);
        }
        output.Write(local); centrals.Add(central);
    }
    var cdOffset = checked((uint)output.Position);
    for (int i = 0; i < centrals.Count; i++) { BinaryPrimitives.WriteUInt32LittleEndian(centrals[i].AsSpan(42), offsets[archive.Entries[i].Name]); output.Write(centrals[i]); }
    var cdSize = checked((uint)output.Position - cdOffset); var archiveComment = Encoding.UTF8.GetBytes("controlled offline preservation fixture");
    using (var writer = new BinaryWriter(output, Encoding.UTF8, true)) { writer.Write(0x06054B50u); writer.Write(0u); writer.Write((ushort)centrals.Count); writer.Write((ushort)centrals.Count); writer.Write(cdSize); writer.Write(cdOffset); writer.Write((ushort)archiveComment.Length); writer.Write(archiveComment); }
    return output.ToArray();
}
static byte[] ZipComment(byte[] zip) {
    for (var i = zip.Length - 22; i >= Math.Max(0, zip.Length - 65557); i--)
        if (BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(i)) == 0x06054B50u && i + 22 + BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(i + 20)) == zip.Length) return zip[(i + 22)..];
    throw new InvalidDataException("ZIP comment missing");
}
static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
static void Req(bool b, string message) { if (!b) throw new Exception(message); }
static void VerifyBrowserOutput(string browserPath, string expectedPath, string nativePath, string packagePath, string reportPath) {
    var browserBytes = File.ReadAllBytes(browserPath); var expectedBytes = File.ReadAllBytes(expectedPath); var nativeBytes = File.ReadAllBytes(nativePath); var package = File.ReadAllBytes(packagePath);
    var originalHashes = new[] { Hash(browserBytes), Hash(expectedBytes), Hash(nativeBytes), Hash(package) };
    var source = new MapArchive(nativeBytes); var expected = new MapArchive(expectedBytes); var browser = new MapArchive(browserBytes);
    Req(ItemWavingFlagMapExport.Repair(nativeBytes, package).Bytes.SequenceEqual(expectedBytes), "Expected map is not the independently generated desktop repair of these inputs.");
    Req(browser.Header.SequenceEqual(source.Header) && browser.Header.SequenceEqual(expected.Header), "Browser map header/reference-table bytes changed.");
    var sourceZip = source.Map.EmbeddedZipData!; var expectedZip = expected.Map.EmbeddedZipData!; var browserZip = browser.Map.EmbeddedZipData!;
    var sourceFiles = Unzip(sourceZip); var expectedFiles = Unzip(expectedZip); var browserFiles = Unzip(browserZip);
    Req(browserFiles.Keys.Order(StringComparer.Ordinal).SequenceEqual(expectedFiles.Keys.Order(StringComparer.Ordinal)), "Browser embedded entry set differs from expected.");
    foreach (var f in expectedFiles) Req(browserFiles[f.Key].SequenceEqual(f.Value), "Browser embedded payload differs: " + f.Key);
    // Restoring the source ZIP through the bounded raw splice proves all original map
    // body bytes survived, including native validation and unknown chunks. The splice
    // changes only the ZIP and its three enclosing length words.
    var restored = new MapArchive(browser.ReplaceZip(sourceZip).Output);
    Req(restored.Body.SequenceEqual(source.Body) && restored.Header.SequenceEqual(source.Header), "Browser non-ZIP map body bytes changed.");
    var sourceRecords = new RawZip(sourceZip); var browserRecords = new RawZip(browserZip); var expectedRecords = new RawZip(expectedZip);
    var untouched = new List<string>(); var changed = new List<object>();
    foreach (var entry in sourceRecords.Entries) {
        if (!sourceFiles[entry.Name].SequenceEqual(expectedFiles[entry.Name])) continue;
        var actual = browserRecords.Entries.Single(e => e.Name == entry.Name);
        Req(entry.Local.SequenceEqual(actual.Local), "Browser changed unrelated ZIP local record/compressed payload/extras: " + entry.Name);
        var a = entry.Central.ToArray(); var b = actual.Central.ToArray(); a.AsSpan(42, 4).Clear(); b.AsSpan(42, 4).Clear();
        Req(a.SequenceEqual(b), "Browser changed unrelated ZIP central metadata/comment: " + entry.Name); untouched.Add(entry.Name);
    }
    Req(ZipComment(sourceZip).SequenceEqual(ZipComment(browserZip)), "Browser changed source ZIP archive comment.");
    foreach (var entry in expectedRecords.Entries) {
        var actual = browserRecords.Entries.Single(e => e.Name == entry.Name);
        if (!entry.Local.SequenceEqual(actual.Local)) {
            var a = entry.Central.ToArray(); var b = actual.Central.ToArray();
            // Compressed sizes, writer host-platform byte and relocated offsets may differ between runtimes;
            // every other central property, including CRC, raw length, name, extras
            // and entry comment, must remain equal.
            var desktopCreatorPlatform = a[5]; var browserCreatorPlatform = b[5]; a[5] = b[5] = 0;
            Req(desktopCreatorPlatform == browserCreatorPlatform || desktopCreatorPlatform == 0 && browserCreatorPlatform == 3,
                "Unexpected ZIP creator platform transition: " + entry.Name);
            var newlyAdded = !sourceFiles.ContainsKey(entry.Name);
            var desktopAttributes = BinaryPrimitives.ReadUInt32LittleEndian(a.AsSpan(38)); var browserAttributes = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(38));
            // Only brand-new dependency records inherit the host writer's default
            // permission attributes. Replaced original entries retain original attrs.
            if (newlyAdded && desktopCreatorPlatform == 0 && browserCreatorPlatform == 3
                && desktopAttributes == 0 && browserAttributes == 0x81A40000) { a.AsSpan(38, 4).Clear(); b.AsSpan(38, 4).Clear(); }
            a.AsSpan(20, 4).Clear(); b.AsSpan(20, 4).Clear(); a.AsSpan(42, 4).Clear(); b.AsSpan(42, 4).Clear();
            Req(a.SequenceEqual(b), "Changed browser ZIP record differs beyond compressed size/offset/creator platform: " + entry.Name + "; central byte differences "
                + string.Join(",", Enumerable.Range(0, Math.Min(a.Length, b.Length)).Where(i => a[i] != b[i]).Select(i => $"{i}:{a[i]:X2}->{b[i]:X2}")));
            var desktopHeaderSize = 30 + BinaryPrimitives.ReadUInt16LittleEndian(entry.Local.AsSpan(26)) + BinaryPrimitives.ReadUInt16LittleEndian(entry.Local.AsSpan(28));
            var browserHeaderSize = 30 + BinaryPrimitives.ReadUInt16LittleEndian(actual.Local.AsSpan(26)) + BinaryPrimitives.ReadUInt16LittleEndian(actual.Local.AsSpan(28));
            var desktopLocalHeader = entry.Local[..desktopHeaderSize]; var browserLocalHeader = actual.Local[..browserHeaderSize];
            desktopLocalHeader.AsSpan(18, 4).Clear(); browserLocalHeader.AsSpan(18, 4).Clear();
            Req(desktopLocalHeader.SequenceEqual(browserLocalHeader), "Changed ZIP local header/name/extra differs beyond compressed size: " + entry.Name);
            changed.Add(new { entry.Name, desktopLocalRecordBytes = entry.Local.Length, browserLocalRecordBytes = actual.Local.Length,
                payloadSha256 = Hash(browserFiles[entry.Name]), identicalUncompressedPayload = true, desktopCreatorPlatform, browserCreatorPlatform,
                newlyAdded, desktopAttributes, browserAttributes });
        }
    }
    var selectedName = Unzip(package).Keys.Single(p => p.EndsWith(".Item.Gbx", StringComparison.OrdinalIgnoreCase)).Replace('\\', '/').Split('/').Last();
    var selected = sourceFiles.Single(p => p.Key.Replace('\\', '/').Split('/').Last().Equals(selectedName, StringComparison.OrdinalIgnoreCase));
    var normalized = FlagItemArchive.Normalize(selected.Key, selected.Value);
    Req(browserFiles[selected.Key].SequenceEqual(normalized.Bytes), "Browser Item differs from exact header/raw-body-preserving reference normalization.");
    var repairedAgain = ItemWavingFlagMapExport.Repair(browserBytes, package);
    Req(repairedAgain.Bytes.SequenceEqual(browserBytes), "Browser output is not byte-idempotent under desktop repair.");
    Req(new[] { Hash(browserBytes), Hash(expectedBytes), Hash(nativeBytes), Hash(package) }.SequenceEqual(originalHashes), "Verifier mutated input buffers.");
    var report = new {
        operation = "offline-cross-runtime-browser-map-repair-verification", allChecksPassed = true,
        scope = "Independent desktop parsing and raw binary preservation checks of browser output; no native game loading, rendering or validation acceptance was tested.",
        browser = new { path = Path.GetFullPath(browserPath), bytes = browserBytes.Length, sha256 = Hash(browserBytes) },
        desktop = new { path = Path.GetFullPath(expectedPath), bytes = expectedBytes.Length, sha256 = Hash(expectedBytes) },
        sourceMapSha256 = Hash(nativeBytes), sourcePackageSha256 = Hash(package),
        entireCompressedMapByteExactAcrossRuntimes = browserBytes.SequenceEqual(expectedBytes),
        decompressedMapBodyByteExactAcrossRuntimes = browser.Body.SequenceEqual(expected.Body),
        embeddedZipByteExactAcrossRuntimes = browserZip.SequenceEqual(expectedZip),
        exactOriginalHeaderAndMapReferenceTable = true, exactSourceRawBodyRestoredByReplacingOnlyZipAndThreeLengthWords = true,
        everyEmbeddedEntryNameAndUncompressedPayloadMatchesDesktop = true, exactNormalizedSelectedItemHeaderAndRawBody = true,
        selectedItem = selected.Key, unrelatedOriginalZipLocalRecordsCompressedPayloadsExtrasExact = untouched,
        unrelatedOriginalZipCentralMetadataExactExceptRelocatedOffsets = true, archiveCommentExact = true,
        browserChangedZipRecordsDifferOnlyInCompressedRepresentationSizeOffsetsCreatorPlatformAndNewEntryDefaultPermissions = changed,
        desktopRepairOfBrowserOutputIsByteIdenticalNoOp = true, inputBuffersUnchanged = true,
        compressionExplanation = "Deflate and outer LZO encodings need not be byte-identical across desktop and WASM runtimes. Newly written ZIP entries use creator platform DOS=0 or Unix=3; only brand-new dependencies may have host default permissions (DOS=0, Unix=0x81A40000). Original unrelated entry metadata remains exact. Acceptance is based on exact payloads and raw preservation, not compressed output hash equality."
    };
    var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    Req(!File.Exists(reportPath) && !Directory.Exists(reportPath), "Verification report output must not exist.");
    File.WriteAllText(reportPath, json); Console.WriteLine(json);
}
sealed class ReferenceDocument : Gbx<CGameItemModel> { public ReferenceDocument(CGameItemModel item, GbxRefTable table) : base(item) => RefTable = table; }
