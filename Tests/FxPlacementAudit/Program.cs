using System.Reflection;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.Game;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Plug;
using TM_Item_Studio.Models;

Gbx.LZO = new GBX.NET.LZO.MiniLZO();
if (args.Length > 1 && args[0] == "--schema-types")
{
    Console.WriteLine(JsonSerializer.Serialize(args.Skip(1).Select(name => {
        var type = typeof(CPlugPrefab).Assembly.GetTypes().Single(type => type.Name == name || type.FullName == name);
        return new { type = type.FullName, baseType = type.BaseType?.FullName, properties = type.GetProperties(BindingFlags.Public|BindingFlags.Instance).Select(property => new {property.Name, type=property.PropertyType.FullName, property.CanWrite}), fields = type.GetFields(BindingFlags.Public|BindingFlags.Instance).Select(field => new {field.Name,type=field.FieldType.FullName}), chunks = type.GetNestedTypes().Select(type=>type.Name) };
    }),new JsonSerializerOptions {WriteIndented=true}));
    return;
}
if (args.Length == 3 && args[0] == "--audit-primer")
{
    var bytes = File.ReadAllBytes(args[1]);
    var archive = new FxEmissionPoint.Archive(bytes);
    var fx = FxEmissionPoint.Read(bytes);
    using var manifest = JsonDocument.Parse(File.ReadAllText(args[2]));
    var graphBytes = manifest.RootElement.GetProperty("sourceFxGraphBytes").GetInt32();
    var textureReference = manifest.RootElement.GetProperty("sourceNextEntityInsertOffset").GetInt32() + 1561 - 4 - 28;
    if (BitConverter.ToUInt32(archive.Body, textureReference) != 14) throw new InvalidDataException("Expected shared bitmap reference14.");
    var previewBody = archive.Body.ToArray();
    Array.Fill(previewBody,(byte)255,textureReference,4);
    var start = fx.FxGraphOffset - 4;
    var stripped = previewBody[..start].Concat(new byte[] {255,255,255,255}).Concat(previewBody[(start+graphBytes)..]).ToArray();
    var copy = archive.Prefix.Concat(stripped).ToArray();
    copy[7] = (byte)'U';
    var item = Gbx.Parse<CGameItemModel>(new MemoryStream(copy),new GbxReadSettings { SafeSkippableChunks = true }).Node;
    var root = (CPlugPrefab)item.EntityModel!;
    if (root.Ents.Length != 3 || root.Ents[1].Model is not null || root.Ents[2].Model is not CPlugStaticObjectModel model)
        throw new InvalidDataException("Primer prefab topology differs.");
    var material = model.Mesh!.Materials!.Single().Node!;
    var texture = material.CustomMaterial!.Textures!.Single();
    Console.WriteLine(JsonSerializer.Serialize(new { source=Path.GetFullPath(args[1]), fx, entityCount=root.Ents.Length, primerPosition=Values(root.Ents[2].Position), meshVertices=model.Mesh.Visuals?.OfType<CPlugVisualIndexedTriangles>().Select(v=>v.VertexStreams?.Sum(s=>s.Positions?.Length??0)), sampler=texture.Name, texture.U01,texture.U02,texture.U03, materialParentFile=material.ShaderFile?.FilePath, sourceFxGraphBytes=graphBytes, sharedBitmapNodeIndex=14, textureReferenceBodyOffset=textureReference, previewBitmapNote="FX archive and its bitmap definition14 were temporarily omitted; shared sampler reference14 temporarily null only for typed topology audit", originalBytesNeverReserialized=true },new JsonSerializerOptions {WriteIndented=true}));
    return;
}
if (args.Length == 3 && args[0] == "--material-primer-donor")
{
    var sourceBytes = File.ReadAllBytes(args[1]);
    var preview = FxMeshPreview.Read(sourceBytes);
    var sourceArchive = new FxEmissionPoint.Archive(sourceBytes);
    var sourceFx = FxEmissionPoint.Read(sourceBytes);
    var sourcePrefabAt = sourceArchive.Body.AsSpan().IndexOf(new byte[] { 1,0,0,0,0,0x50,0x14,9 });
    if (sourcePrefabAt < 0) throw new InvalidDataException("Source prefab signature missing.");
    var prefab = preview.PreviewRoot;
    var entry = prefab.Ents[0];
    entry.Position = new Vec3(5, 0, 0);
    var model = (CPlugStaticObjectModel)entry.Model!;
    var mesh = model.Mesh!;
    var material = Gbx.Parse<CPlugMaterial>(@"C:\Users\PC\OpenplanetNext\Extract\GameData\Stadium\Media\Material_BlockCustom\CustomModAddSelfIllum.Material.Gbx").Node;
    var table = new GbxRefTable { AncestorLevel = 0 };
    material.ShaderFile = new GbxRefTableFile(table, 1, false, @"GameData\Techno3\Media\Material\Tech3 Block TAddModCV.Material.gbx");
    var texture = material.CustomMaterial!.Textures!.Single();
    if (texture.Name != "SelfIllum") throw new InvalidDataException("Unexpected native material sampler.");
    texture.Texture = new CPlugBitmap();
    texture.TextureFile = null;
    mesh.Materials = [new External<CPlugMaterial>(material, null)];
    mesh.CustomMaterials = null; mesh.MaterialInsts = null; mesh.MaterialIds = [];
    prefab.Ents = [entry];
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);
    using (var output = File.Create(args[2])) new PrefabDocument(prefab, table).Save(output);
    var report = new { output = Path.GetFullPath(args[2]), source = Path.GetFullPath(args[1]), sourceSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(sourceBytes)), sourcePrefabEntCountOffset = sourcePrefabAt + 28, sourceNextEntityInsertOffset = sourceFx.FxGraphOffset - 4 + preview.Proof.RemovedFxBytes + 36, sourceFxGraphOffset = sourceFx.FxGraphOffset, sourceFxGraphBytes = preview.Proof.RemovedFxBytes, sampler = texture.Name, placeholder = "empty CPlugBitmap definition must be replaced with existing FX bitmap reference" };
    var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(args[2] + ".positions.json", json);
    Console.WriteLine(json);
    return;
}
if (args.Length > 0 && args[0] == "--maps")
{
    var maps = new List<object>();
    foreach (var path in args.Skip(1))
    {
        var bytes = File.ReadAllBytes(path);
        var document = Gbx.Parse<CGameCtnChallenge>(new MemoryStream(bytes, writable: false), new GbxReadSettings { SafeSkippableChunks = true });
        var map = document.Node ?? throw new InvalidDataException("Map node missing.");
        maps.Add(new {
            source = Path.GetFullPath(path), sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)),
            map.MapName, map.MapType, map.Mode,
            metadata = Snapshot(map.ScriptMetadata, 3),
            anchors = map.AnchoredObjects?.Select(anchor => Snapshot(anchor, 3)),
            clips = new {
                intro = Snapshot(map.ClipIntro, 5), inGame = Snapshot(map.ClipGroupInGame, 5),
                endRace = Snapshot(map.ClipGroupEndRace, 5), ambiance = Snapshot(map.ClipAmbiance, 5), podium = Snapshot(map.ClipPodium, 5)
            },
            mapProperties = Snapshot(map, 0),
            externalReferences = Snapshot(document.RefTable, 4),
            embeddedZipBytes = map.EmbeddedZipData?.Length ?? 0
        });
    }
    Console.WriteLine(JsonSerializer.Serialize(maps, new JsonSerializerOptions { WriteIndented = true }));
    return;
}
var reports = new List<object>();
foreach (var path in args)
{
    var bytes = File.ReadAllBytes(path);
    var preview = FxMeshPreview.Read(bytes);
    var fx = FxEmissionPoint.Read(bytes);
    var meshOwner = preview.PreviewRoot.Ents[0];
    var model = (CPlugStaticObjectModel)meshOwner.Model!;
    var mesh = model.Mesh!;
    var scene = ItemScene.Build(preview.PreviewRoot, 0).Preview;
    reports.Add(new {
        source = Path.GetFullPath(path), sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)),
        preview.Proof,
        fxGraph = fx,
        prefab = new { properties = Values(preview.PreviewRoot), owners = preview.PreviewRoot.Ents.Select(owner => new { position = Values(owner.Position), rotation = Values(owner.Rotation), owner.U01, parameters = Snapshot(owner.Params, 3), modelType = owner.Model?.GetType().Name }) },
        meshOwner = new { position = Values(meshOwner.Position), rotation = Values(meshOwner.Rotation), meshOwner.U01, parameters = Values(meshOwner.Params) },
        staticModel = Values(model),
        mesh = new { mesh.Flags, mesh.VisCstType, mesh.DamageZone, mesh.MaterialsFolderName,
            lodDistances = mesh.LodMaxDistAtFov90, materialIds = mesh.MaterialIds,
            shadedGeoms = (mesh.ShadedGeoms ?? []).Select(Values),
            materials = (mesh.CustomMaterials ?? []).Select(material => new {
                material.MaterialName, instance = Values(material.MaterialUserInst) }),
            visuals = (mesh.Visuals ?? []).Select(Values),
            authoredVisualBounds = (mesh.Visuals ?? []).Select(visual => Values(visual.BoundingBox)) },
        scene.Materials, scene.Mappings, scene.Solids, scene.Diagnostics,
        sceneNodes = scene.Nodes.Where(node => node.Kind is ItemSceneKind.Entity or ItemSceneKind.StaticObject or ItemSceneKind.Solid),
        geometry = scene.Geometry.Where(geometry => !geometry.IsCollision).Select(geometry => new {
            vertices = geometry.Positions.Length / 3, triangles = geometry.Indices.Length / 3,
            geometry.BoundsMin, geometry.BoundsMax, geometry.WorldTransform })
    });
}
Console.WriteLine(JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));

static object? Snapshot(object? value, int depth)
{
    if (value is null || value is string || value.GetType().IsPrimitive || value.GetType().IsEnum) return value;
    if (value is byte[] bytes) return new { type = "Byte[]", bytes = bytes.Length };
    if (value is System.Collections.IEnumerable sequence)
    {
        var items = sequence.Cast<object?>().ToArray();
        return new { type = value.GetType().Name, count = items.Length, items = depth < 0 ? null : items.Take(256).Select(item => Snapshot(item, depth - 1)).ToArray() };
    }
    var result = new Dictionary<string, object?> { ["type"] = value.GetType().Name };
    if (depth < 0) return result;
    foreach (var property in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
    {
        if (!property.CanRead || property.GetIndexParameters().Length != 0 || property.Name is "Parent" or "Chunks") continue;
        try { result[property.Name] = Snapshot(property.GetValue(value), depth - 1); }
        catch (Exception error) { result[property.Name] = new { error = error.GetBaseException().Message }; }
    }
    foreach (var field in value.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        result[field.Name] = Snapshot(field.GetValue(value), depth - 1);
    return result;
}

static Dictionary<string, object?>? Values(object? value)
{
    if (value is null) return null;
    var result = new Dictionary<string, object?> { ["type"] = value.GetType().Name };
    foreach (var property in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
    {
        if (!property.CanRead || property.GetIndexParameters().Length != 0) continue;
        try
        {
            var item = property.GetValue(value);
            if (item is null || item is string || property.PropertyType.IsPrimitive || property.PropertyType.IsEnum)
                result[property.Name] = item;
            else if (item is Array array && (array.GetType().GetElementType()?.IsPrimitive == true || array is string[]))
                result[property.Name] = item;
        }
        catch { }
    }
    foreach (var field in value.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        if (field.FieldType.IsPrimitive || field.FieldType.IsEnum || field.FieldType == typeof(string)) result[field.Name] = field.GetValue(value);
    return result;
}

sealed class PrefabDocument : Gbx<CPlugPrefab>
{
    public PrefabDocument(CPlugPrefab node, GbxRefTable table) : base(node) => RefTable = table;
}
