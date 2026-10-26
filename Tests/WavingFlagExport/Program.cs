using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;
using TM_Item_Studio.Models;

Gbx.LZO = new GBX.NET.LZO.MiniLZO();
var checks = 0;
var fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
Directory.CreateDirectory(fixtures);
Check("capability and snapshot isolation", () => {
    var source = Source(); var before = Save(source); var original = source.Node.EntityModel;
    var c = ItemWavingFlagExport.GetCapability(source);
    Req(c.Supported && c.VertexCount == 12 && c.TriangleCount == 12 && c.MeshCount == 1, c.Summary);
    Req(before.SequenceEqual(Save(source)) && ReferenceEquals(original, source.Node.EntityModel), "Capability mutated source.");
});
foreach (var mode in new[] { ItemWavingFlagExport.Mode.Swallowtail, ItemWavingFlagExport.Mode.Long250, ItemWavingFlagExport.Mode.Round })
Check("byte-exact preset " + mode, () => {
    var source = Source(); var before = Save(source);
    var r = ItemWavingFlagExport.Export(source, new() { Mode = mode, OutputName = "Preset" + mode });
    var files = Unzip(r.Bytes); var item = files.Single(p => p.Key.EndsWith(".Item.Gbx")).Value;
    Req(Hash(item) == Hash(Resource("TMIS_MatRemap_" + mode + ".Item.Gbx")), "Preset bytes changed.");
    VerifyPackage(r, files); Req(before.SequenceEqual(Save(source)), "Preset mutated source.");
    File.WriteAllBytes(Path.Combine(fixtures, r.FileName), r.Bytes);
});
foreach (var edge in Enum.GetValues<ItemWavingFlagExport.AttachmentEdge>())
Check("86-frame topology/UV/normal/pin round-trip " + edge, () => {
    bool z = edge is ItemWavingFlagExport.AttachmentEdge.MinimumZ or ItemWavingFlagExport.AttachmentEdge.MaximumZ;
    var source = Source(z); var before = Save(source); var input = Visual(source);
    var r = ItemWavingFlagExport.Export(source, new() { OutputName = "Offline" + edge, Edge = edge, Amplitude = .4f, Wavelength = 3, PeriodSeconds = 7 });
    var files = Unzip(r.Bytes); VerifyPackage(r, files);
    var output = Gbx.Parse<CGameItemModel>(new MemoryStream(files.Single(p => p.Key.EndsWith(".Item.Gbx")).Value));
    var prefab = Prefab(output.Node); Req(prefab.Ents!.Length == 1, "Seed pole retained.");
    var e = prefab.Ents[0]; var body = (CPlugDynaObjectModel)e.Model!; var t = (NPlugDynaObjectModel_SInstanceParams)e.Params!;
    Req(e.Position == default && e.Rotation == Quat.Identity && !t.IsKinematic && t.PeriodSc == 7 && t.PeriodScMax == 7 && t.Phase01 == 0 && t.Phase01Max == 0, "Wrong host transform/timing.");
    Req(body.LocAnim is null && body.DynaShape is null && body.StaticShape is null && body.Mesh!.Visuals!.Length == 5, "Wrong motion/physics graph.");
    bool max = edge is ItemWavingFlagExport.AttachmentEdge.MaximumX or ItemWavingFlagExport.AttachmentEdge.MaximumZ;
    foreach (var v in body.Mesh!.Visuals!.Cast<CPlugVisualIndexedTriangles>()) {
        Req(v.Vertices.Length == 12 * 86 && v.SubVisuals!.Length == 86 && v.IndexBuffer!.Indices.SequenceEqual(input.IndexBuffer!.Indices), "Wrong topology.");
        Req(v.TexCoords.Length == 2, "Lost UV channel.");
        for (int f = 0; f < 86; f++) for (int i = 0; i < 12; i++) {
            var p = v.Vertices[f * 12 + i]; var a = input.Vertices[i].Position;
            Req(p.Position.Y == a.Y && (z ? p.Position.Z == a.Z : p.Position.X == a.X), "Source longitudinal/vertical extent changed.");
            if ((z ? a.Z : a.X) == (max ? 4.5f : 0)) Req(SameBits(p.Position, a) && SameBits(p.Normal!.Value, input.Vertices[i].Normal!.Value), "Pinned edge changed bits.");
            Req(p.Normal.HasValue && Math.Abs(Length(p.Normal.Value) - 1) < .00001, "Nonunit normal.");
            for (int c = 0; c < 2; c++) Req(v.TexCoords[c].TexCoords[f * 12 + i].UV == input.TexCoords[c].TexCoords[i].UV, "UV changed.");
            Req(Inside(v.BoundingBox, p.Position), "Bad bounds."); if (f == 85) Req(p == v.Vertices[i], "Unclosed endpoint.");
        }
        Req(v.Vertices.Take(12).Where((p, i) => p.Position != v.Vertices[21 * 12 + i].Position).Any(), "No authored deformation.");
    }
    Req(before.SequenceEqual(Save(source)), "Source-mesh export mutated input.");
    File.WriteAllBytes(Path.Combine(fixtures, r.FileName), r.Bytes);
    if (!z && edge == ItemWavingFlagExport.AttachmentEdge.MinimumX) File.WriteAllBytes(Path.Combine(fixtures, "InlineStaticClothSource.Item.Gbx"), before);
});
Check("missing normals reconstruction", () => {
    var source = Source(); var visual = Visual(source); visual.Vertices = visual.Vertices.Select(p => p with { Normal = null }).ToArray();
    var before = Save(source); var r = ItemWavingFlagExport.Export(source);
    Req(r.Warnings.Any(w => w.Contains("reconstructed")) && before.SequenceEqual(Save(source)), "Reconstruction/isolation failed.");
});
Check("rigid static transform baked once", () => {
    var source = Source(); var root = (CPlugPrefab)source.Node.EntityModel!; root.Ents![0].Position = new(11, 13, 17); root.Ents[0].Rotation = new(0, 0, MathF.Sqrt(.5f), MathF.Sqrt(.5f));
    var before = Save(source); var r = ItemWavingFlagExport.Export(source, new() { Amplitude = 0 });
    var output = Gbx.Parse<CGameItemModel>(new MemoryStream(Unzip(r.Bytes).Single(p => p.Key.EndsWith(".Item.Gbx")).Value));
    var v = (CPlugVisualIndexedTriangles)((CPlugDynaObjectModel)Prefab(output.Node).Ents![0].Model!).Mesh!.Visuals![0];
    Req(Math.Abs(v.Vertices[0].Position.X - 11) < .00001 && Math.Abs(v.Vertices[0].Position.Y - 13) < .00001 && Math.Abs(v.Vertices[0].Position.Z - 17) < .00001, "Transform lost.");
    Req(before.SequenceEqual(Save(source)), "Transform export mutated input.");
});
Check("deterministic package", () => { var source = Source(); Req(ItemWavingFlagExport.Export(source).Bytes.SequenceEqual(ItemWavingFlagExport.Export(source).Bytes), "Nondeterministic output."); });
Check("preset default filenames are distinct and source-independent", () => {
    var source = Source();
    var names = new[] { ItemWavingFlagExport.Mode.Swallowtail, ItemWavingFlagExport.Mode.Long250, ItemWavingFlagExport.Mode.Round }
        .Select(mode => ItemWavingFlagExport.Export(source, new() { Mode = mode }, "PreviousSourceName").FileName).ToArray();
    Req(names.Distinct().Count() == 3 && names.All(n => n.StartsWith("TMIS_", StringComparison.Ordinal) && !n.Contains("PreviousSourceName")), "Preset output can overwrite another preset or inherits the opened item name.");
    Req(ItemWavingFlagExport.Export(source, new() { Mode = ItemWavingFlagExport.Mode.Round, OutputName = "ExplicitPreset" }).FileName == "ExplicitPreset_WavingFlag.zip", "Explicit preset name was ignored.");
});
Refuse("dynamic graph", s => s.Node.EntityModel = new CPlugDynaObjectModel { Mesh = Mesh(s) });
Refuse("source vertex budget", s => Visual(s).Vertices = Enumerable.Repeat(Visual(s).Vertices[0], ItemWavingFlagExport.MaximumSourceVertices + 1).ToArray());
Refuse("source triangle budget", s => Visual(s).IndexBuffer!.Indices = Enumerable.Range(0, ItemWavingFlagExport.MaximumSourceTriangles + 1).SelectMany(_ => new[] { 0, 1, 4 }).ToArray());
Refuse("parameterized entity", s => ((CPlugPrefab)s.Node.EntityModel!).Ents![0].Params = new NPlugDynaObjectModel_SInstanceParams { IsKinematic = true });
Refuse("external mesh", s => ((CPlugStaticObjectModel)((CPlugPrefab)s.Node.EntityModel!).Ents![0].Model!).MeshFile = new(new GbxRefTable(), 1, false, "missing.Mesh.Gbx"));
Refuse("structured stream", s => Visual(s).VertexStreams = [new CPlugVertexStream { Positions = [new(1, 2, 3)] }]);
Refuse("existing frames", s => Visual(s).SubVisuals = [new(0, 0, 36)]);
Refuse("morph", s => Visual(s).MorphCount = 1);
Refuse("missing UV", s => Visual(s).TexCoords = []);
Refuse("partial normals", s => Visual(s).Vertices[0] = Visual(s).Vertices[0] with { Normal = null });
Refuse("zero normals", s => Visual(s).Vertices[0] = Visual(s).Vertices[0] with { Normal = default(Vec3) });
Refuse("nonfinite position", s => Visual(s).Vertices[0] = Visual(s).Vertices[0] with { Position = new(float.NaN, 0, 0) });
Refuse("bad index", s => Visual(s).IndexBuffer!.Indices[0] = 1000);
Refuse("degenerate triangle", s => Visual(s).IndexBuffer!.Indices[1] = Visual(s).IndexBuffer!.Indices[0]);
Refuse("ambiguous LOD", s => Mesh(s).LodMaxDistAtFov90 = [10, 20]);
Refuse("multiple variants", s => { var root = s.Node.EntityModel; s.Node.EntityModel = new NPlugItem_SVariantList { Variants = [new() { EntityModel = root }, new() { EntityModel = root }] }; });
Refuse("tangents", s => Visual(s).Tangents = [new(1, 0, 0)]);
Refuse("nonunit transform", s => ((CPlugPrefab)s.Node.EntityModel!).Ents![0].Rotation = new(0, 0, 0, 0));
Check("cyclic source refused without mutation", () => {
    var source = Source(); var prefab = (CPlugPrefab)source.Node.EntityModel!; prefab.Ents![0].Model = prefab;
    Req(!ItemWavingFlagExport.GetCapability(source).Supported, "Cycle accepted."); Reject(() => ItemWavingFlagExport.Export(source)); Req(ReferenceEquals(prefab.Ents[0].Model, prefab), "Cycle mutated.");
});
Check("invalid controls and zero extent refuse atomically", () => {
    var source = Source(); var before = Save(source);
    Reject(() => ItemWavingFlagExport.Export(source, new() { Edge = ItemWavingFlagExport.AttachmentEdge.MinimumZ }));
    foreach (var o in new[] { new ItemWavingFlagExport.Options { Amplitude = float.NaN }, new() { Wavelength = 0 }, new() { PeriodSeconds = -1 }, new() { Edge = (ItemWavingFlagExport.AttachmentEdge)99 } }) Reject(() => ItemWavingFlagExport.Export(source, o));
    Req(before.SequenceEqual(Save(source)), "Refusal mutated source.");
});
Console.WriteLine($"{checks} offline checks passed; no game test was run. Fixtures: {fixtures}");

void Check(string name, Action body) { body(); checks++; Console.WriteLine("PASS " + name); }
void Refuse(string name, Action<Gbx<CGameItemModel>> change) => Check("refuses " + name, () => { var s = Source(); change(s); var root = s.Node.EntityModel; Req(!ItemWavingFlagExport.GetCapability(s).Supported, "Capability accepted " + name); Reject(() => ItemWavingFlagExport.Export(s)); Req(ReferenceEquals(root, s.Node.EntityModel), "Refusal changed source graph."); });
static void VerifyPackage(ItemWavingFlagExport.Result r, Dictionary<string, byte[]> files) {
    Req(files.Count == 6 && files.Keys.All(k => !k.Contains("..") && k.Count(c => c == '/') == 1) && files.Keys.Count(k => k.EndsWith(".Gbx")) == 4, "Wrong ZIP layout.");
    var json = JsonDocument.Parse(r.ManifestJson).RootElement;
    Req(json.GetProperty("requiresNativeFlag16m").GetBoolean() && !json.GetProperty("standaloneSingleItem").GetBoolean() && json.GetProperty("nativeSavedMapRepairRequired").GetBoolean(), "Portability qualification missing.");
    foreach (var e in json.GetProperty("files").EnumerateArray()) { var b = files.Single(f => f.Key.EndsWith('/' + e.GetProperty("file").GetString(), StringComparison.Ordinal)).Value; Req(b.Length == e.GetProperty("bytes").GetInt32() && Hash(b) == e.GetProperty("sha256").GetString(), "Hash mismatch."); }
    var item = Gbx.Parse<CGameItemModel>(new MemoryStream(files.Single(p => p.Key.EndsWith(".Item.Gbx")).Value));
    Req(item.Node.Ident!.Id == "" && item.RefTable!.AncestorLevel == 0 && item.RefTable.Files.Count == 2 && item.RefTable.Files.All(f => !f.UseFile && f.Flags == 1), "Item identity/reference layout mismatch.");
    var skin = Gbx.ParseHeader(new MemoryStream(files.Single(p => p.Key.EndsWith(".GameSkin.Gbx")).Value));
    Req(skin.RefTable!.AncestorLevel == 0 && skin.RefTable.Files.Single().UseFile && skin.RefTable.Files.Single().FilePath == "TMIS_MatRemap_Placeholder.Material.Gbx", "GameSkin FID lost.");
    var mod = Gbx.ParseHeader(new MemoryStream(files.Single(p => p.Key.EndsWith(".TerrainModifier.Gbx")).Value));
    Req(mod.RefTable!.AncestorLevel == 0 && !mod.RefTable.Files.Single().UseFile && mod.RefTable.Files.Single().FilePath == "TMIS_MatRemap.GameSkin.Gbx", "Modifier NodeRef lost.");
    Req(files.Single(p => p.Key.EndsWith("/README.md")).Value.Length > 1000, "Instructions missing.");
}
static Gbx<CGameItemModel> Source(bool z = false) {
    var seed = Gbx.Parse<CGameItemModel>(new MemoryStream(Resource("TMIS_MatRemap_Long250.Item.Gbx"))); var p = Prefab(seed.Node); var mesh = ((CPlugDynaObjectModel)p.Ents![1].Model!).Mesh!;
    var v = (CPlugVisualIndexedTriangles)mesh.Visuals![0]; var prototype = v.Vertices[0]; var vertices = new List<CPlugVisual3D.Vertex>(); var u0 = new List<CPlugVisual.TexCoord>(); var u1 = new List<CPlugVisual.TexCoord>();
    for (int y = 0; y < 3; y++) for (int x = 0; x < 4; x++) { vertices.Add(prototype with { Position = z ? new(0, y, x * 1.5f) : new(x * 1.5f, y, 0), Normal = z ? new(1, 0, 0) : new(0, 0, 1) }); u0.Add(new(new(x / 3f, y / 2f), null, null)); u1.Add(new(new(.1f + x / 6f, .2f + y / 5f), null, null)); }
    var ix = new List<int>(); for (int y = 0; y < 2; y++) for (int x = 0; x < 3; x++) { int a = y * 4 + x; ix.AddRange([a, a + 1, a + 4, a + 1, a + 5, a + 4]); }
    v.Vertices = vertices.ToArray(); v.SubVisuals = []; v.IndexBuffer!.Indices = ix.ToArray(); typeof(CPlugVisual).GetProperty("Count", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(v, vertices.Count);
    v.TexCoords = [new() { Version = 3, Flags = 256, U01 = [], TexCoords = u0.ToArray() }, new() { Version = 3, Flags = 256, U01 = [], TexCoords = u1.ToArray() }];
    mesh.Visuals = [v]; mesh.LodMaxDistAtFov90 = [100]; mesh.Materials = null; mesh.CustomMaterials = [new() { MaterialName = "source" }]; mesh.ShadedGeoms = [new() { VisualIndex = 0, MaterialIndex = 0, LodMask = 1 }];
    var s = (CPlugStaticObjectModel)p.Ents[0].Model!; s.Mesh = mesh; s.Shape = null; s.ShapeFile = null; s.IsMeshCollidable = false;
    seed.Node.EntityModel = new CPlugPrefab { Version = 11, Ents = [new() { Model = s, Rotation = Quat.Identity, U01 = "" }] }; seed.Node.MaterialModifier = null; seed.Node.MaterialModifierFile = null; seed.Node.Name = "Inline static cloth source";
    using var output = new MemoryStream(); new Gbx<CGameItemModel>(seed.Node).Save(output); output.Position = 0; return Gbx.Parse<CGameItemModel>(output);
}
static CPlugVisualIndexedTriangles Visual(Gbx<CGameItemModel> s) => (CPlugVisualIndexedTriangles)Mesh(s).Visuals![0];
static CPlugSolid2Model Mesh(Gbx<CGameItemModel> s) => ((CPlugStaticObjectModel)((CPlugPrefab)s.Node.EntityModel!).Ents![0].Model!).Mesh!;
static CPlugPrefab Prefab(CGameItemModel i) => (CPlugPrefab)((NPlugItem_SVariantList)i.EntityModel!).Variants![0].EntityModel!;
static Dictionary<string, byte[]> Unzip(byte[] b) { using var z = new ZipArchive(new MemoryStream(b)); return z.Entries.ToDictionary(e => e.FullName, e => { using var s = e.Open(); using var m = new MemoryStream(); s.CopyTo(m); return m.ToArray(); }); }
static byte[] Resource(string n) { using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("TM_Item_Studio.Resources.Flags." + n)!; using var m = new MemoryStream(); s.CopyTo(m); return m.ToArray(); }
static byte[] Save(Gbx<CGameItemModel> d) { using var s = new MemoryStream(); d.Save(s); return s.ToArray(); }
static string Hash(byte[] b) => Convert.ToHexString(SHA256.HashData(b));
static double Length(Vec3 p) => Math.Sqrt((double)p.X * p.X + (double)p.Y * p.Y + (double)p.Z * p.Z);
static bool SameBits(Vec3 a, Vec3 b) => BitConverter.SingleToInt32Bits(a.X) == BitConverter.SingleToInt32Bits(b.X) && BitConverter.SingleToInt32Bits(a.Y) == BitConverter.SingleToInt32Bits(b.Y) && BitConverter.SingleToInt32Bits(a.Z) == BitConverter.SingleToInt32Bits(b.Z);
static bool Inside(BoxAligned b, Vec3 p) => Math.Abs(p.X - b.X) <= b.X2 && Math.Abs(p.Y - b.Y) <= b.Y2 && Math.Abs(p.Z - b.Z) <= b.Z2;
static void Req(bool b, string m) { if (!b) throw new Exception(m); }
static void Reject(Action a) { try { a(); } catch (Exception e) when (e is NotSupportedException or ArgumentException or InvalidDataException) { return; } throw new Exception("Expected safe refusal."); }
