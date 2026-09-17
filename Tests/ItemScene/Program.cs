using System.Numerics;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.MetaNotPersistent;
using GBX.NET.Engines.MwFoundations;
using GBX.NET.Engines.Plug;
using TM_Item_Studio.Models;

var failed = 0;
var passed = 0;
Check("document cache preserves shared occurrences, fresh paths and explicit edit invalidation", () =>
{
    var visual = Visual();
    var solid = new CPlugSolid2Model { Visuals = new[] { visual } };
    var first = new CPlugPrefab { Ents = new[] { Ent(new CPlugStaticObjectModel { Mesh = solid }, new Vec3(10, 0, 0)) } };
    var second = new CPlugPrefab { Ents = new[] { Ent(new CPlugStaticObjectModel { Mesh = solid }, new Vec3(20, 0, 0)) } };
    var cache = new ItemSceneGeometryCache();
    var a = ItemScene.Build(first, 0, 0, cache).Preview.Geometry.Single();
    var b = ItemScene.Build(second, 0, 1, cache).Preview.Geometry.Single();
    var again = ItemScene.Build(first, 0, 2, cache).Preview.Geometry.Single();
    Near(a.Positions.Take(3), 11, 0, 0); Near(b.Positions.Take(3), 21, 0, 0);
    Require(a.GeometryId.HasValue && a.GeometryId == b.GeometryId, "Shared parsed visual identity lost across variants.");
    Require(ReferenceEquals(a.LocalPositions, b.LocalPositions) && ReferenceEquals(a.Positions, again.Positions), "Geometry was rebuilt.");
    Require(again.Path.Contains("variant:2") && again.EntityPath!.Contains("variant:2"), "Cache retained stale occurrence paths.");
    var unrelated = ItemScene.Build(Visual(), 1, null, cache).Preview.Geometry.Single();
    Require(unrelated.GeometryId != a.GeometryId, "Snapshot source ID aliased an unrelated visual.");
    visual.VertexStreams[0].Positions![0] = new Vec3(5, 0, 0);
    var epoch = cache.Epoch;
    cache.Clear();
    var edited = ItemScene.Build(second, 0, 1, cache).Preview.Geometry.Single();
    Near(edited.Positions.Take(3), 25, 0, 0); Near(a.Positions.Take(3), 11, 0, 0);
    Require(cache.Epoch != epoch && !ReferenceEquals(edited.LocalPositions, a.LocalPositions), "In-place edit did not invalidate buffers.");
});
Check("nested rotations, repeated prefab and original entry ownership", () =>
{
    var solid = Solid();
    var nested = new CPlugPrefab { Ents = new[] {
        Ent(new CPlugStaticObjectModel { Mesh = Solid() }, new Vec3(0, 5, 0)),
        Ent(new CPlugDynaObjectModel { Mesh = solid }, new Vec3(2, 0, 0), Z90()),
        Ent(new CPlugDynaObjectModel { Mesh = solid }, new Vec3(0, 3, 0)),
        Ent(new NPlugDyna_SKinematicConstraint()) } };
    var root = new CPlugPrefab { Ents = new[] {
        Ent(nested, new Vec3(10, 0, 0), Z90()), Ent(nested, new Vec3(20, 0, 0), Z90()) } };
    var result = ItemScene.Build(root, 7, 2);
    var scene = result.Preview;
    Require(scene.Geometry.Count == 6, "Every shared occurrence must render.");
    var a = scene.Geometry.Single(x => x.EntityPath == "doc:7/variant:2/root/ent:0/ent:1");
    var b = scene.Geometry.Single(x => x.EntityPath == "doc:7/variant:2/root/ent:1/ent:1");
    Near(a.Positions.Take(3), 9, 2, 0);
    Near(b.Positions.Take(3), 19, 2, 0);
    Near(a.Normals!.Take(3), -1, 0, 0);
    Require(a.SourceId == b.SourceId && a.Path != b.Path, "Shared identity must differ from occurrence path.");
    Require(result.Handles.Prefabs.Count == 3, "Repeated nested prefab lost.");
    var owners = result.Handles.Entries.Where(x => ReferenceEquals(x.Prefab, nested)).ToArray();
    Require(owners.Length == 8 && owners.Select(x => x.OriginalIndex).SequenceEqual(new[] { 0, 1, 2, 3, 0, 1, 2, 3 }), "Original array slots changed.");
    Require(owners[3].Entry.Model is NPlugDyna_SKinematicConstraint, "Constraint handle missing.");
    Require(scene.Nodes.Count(x => x.Kind == ItemSceneKind.Constraint) == 2, "Constraint occurrences collapsed.");
    Require(ReferenceEquals(result.Handles.Prefabs[1].Source, result.Handles.Prefabs[2].Source), "Source graph cloned.");
    Require(!scene.Diagnostics.Any(x => x.State == ItemSceneState.Invalid), "Valid fixture rejected.");
});

Check("path-local cycles retain independent instances and serializable preview", () =>
{
    var nested = new CPlugPrefab();
    nested.Ents = new[] { Ent(nested), Ent(new CPlugStaticObjectModel { Mesh = Solid() }) };
    var result = ItemScene.Build(new CPlugPrefab { Ents = new[] { Ent(nested), Ent(nested) } }, 0);
    Require(result.Preview.Geometry.Count == 2, "Cycle guard must not globally deduplicate.");
    Require(result.Preview.Nodes.Count(x => x.State == ItemSceneState.Cycle) == 2, "Missing cycle diagnostics.");
    var json = JsonSerializer.Serialize(result.Preview);
    Require(json.Contains("Positions") && !json.Contains("Ents"), "Preview leaks GBX graph.");
});

Check("save/reparse keeps shared prefab topology and entry transforms", () =>
{
    var nested = new CPlugPrefab { Ents = new[] {
        Ent(new CPlugStaticObjectModel(), new Vec3(0, 5, 0)),
        Ent(new CPlugDynaObjectModel(), new Vec3(2, 0, 0), Z90()),
        Ent(new CPlugDynaObjectModel(), new Vec3(0, 3, 0)) } };
    var item = new CGameItemModel { EntityModel = new CPlugPrefab { Ents = new[] {
        Ent(nested, new Vec3(10, 0, 0), Z90()), Ent(nested, new Vec3(20, 0, 0), Z90()) } },
        ItemType = CGameItemModel.EItemType.Ornament };
    item.CreateChunk<CGameItemModel.HeaderChunk2E002000>();
    item.CreateChunk<CGameItemModel.Chunk2E002019>().Version = 15;
    var file = new Gbx<CGameItemModel>(item) { BodyCompression = GbxCompression.Uncompressed };
    using var before = new MemoryStream(); file.Save(before);
    ItemScene.Build(item, 0);
    using var after = new MemoryStream(); file.Save(after);
    Require(before.ToArray().SequenceEqual(after.ToArray()), "Traversal changed serialized source bytes.");
    after.Position = 0;
    var reopened = Gbx.Parse<CGameItemModel>(after);
    var root = (CPlugPrefab)reopened.Node.EntityModel!;
    Require(ReferenceEquals(root.Ents[0].Model, root.Ents[1].Model), "Reopened instances lost shared topology.");
    var result = ItemScene.Build(reopened.Node, 0);
    Require(result.Handles.Prefabs.Count == 3 && result.Handles.Entries.Count == 8, "Reopened inventory differs.");
    var child = result.Handles.Entries.Single(x => x.Path.EndsWith("/ent:0/ent:1"));
    var p = Vector3.Transform(Vector3.UnitX, child.WorldTransform!.Value);
    Near(new[] { p.X, p.Y, p.Z }, 9, 2, 0);
});

Check("attribute streams join by semantic, retaining UV0 and UV1", () =>
{
    var visual = Visual();
    var positions = visual.VertexStreams[0];
    positions.UVs.Clear();
    var attributes = new CPlugVertexStream { UVs = new() {
        [0] = new[] { new Vec2(.1f, .2f), new Vec2(.3f, .4f), new Vec2(.5f, .6f) },
        [1] = new[] { new Vec2(.7f, .8f), new Vec2(.9f, 1), new Vec2(1.1f, 1.2f) } } };
    visual.VertexStreams.Add(attributes);
    var result = ItemScene.Build(visual, 0);
    var mesh = result.Preview.Geometry.Single();
    Require(mesh.Positions.Length == 9 && mesh.Normals!.Length == 9, "Separate attributes concatenated as vertices.");
    Near(mesh.UVs[0].Take(2), .1f, .2f);
    Near(mesh.UVs[1].Take(2), .7f, .8f);
    mesh.Positions[0] = 99;
    mesh.UVs[1][0] = 99;
    Require(positions.Positions![0].X == 1 && attributes.UVs[1][0].X == .7f, "Snapshot mutates authored arrays.");
    Require(result.Handles.Streams.Count == 2, "Source stream ownership missing.");
});

Check("duplicate position streams and mixed CPU data are unsupported", () =>
{
    var visual = Visual();
    visual.VertexStreams.Add(Visual().VertexStreams[0]);
    var result = ItemScene.Build(visual, 0).Preview;
    Require(result.Geometry.Count == 0 && Has(result, "ambiguous-attributes"), "Ambiguous position buffers accepted.");
    visual.VertexStreams.RemoveAt(1);
    visual.Vertices = new[] { Vertex(new Vec3(1, 0, 0)) };
    Require(Has(ItemScene.Build(visual, 0).Preview, "ambiguous-attributes"), "Mixed CPU and stream data accepted.");
    visual.Vertices = Array.Empty<CPlugVisual3D.Vertex>();
    visual.VertexStreams.Clear();
    visual.VertexStreams.Add(new CPlugVertexStream());
    Require(Has(ItemScene.Build(visual, 0).Preview, "opaque-stream"), "Opaque stream reported as absent.");
});

Check("CPU indexed attributes retain normals and multiple UV sets", () =>
{
    var visual = new CPlugVisualIndexedTriangles {
        Vertices = new[] { Vertex(new Vec3(1, 0, 0)), Vertex(new Vec3(0, 1, 0)), Vertex(new Vec3(0, 0, 0)) },
        IndexBuffer = new CPlugIndexBuffer { Indices = new[] { 0, 1, 2 } },
        TexCoords = new[] { TexCoords(.1f), TexCoords(.6f) } };
    var mesh = ItemScene.Build(visual, 0).Preview.Geometry.Single();
    Near(mesh.Normals!.Take(3), 1, 0, 0);
    Near(mesh.UVs[0].Take(2), .1f, 0);
    Near(mesh.UVs[1].Take(2), .6f, 0);
});

Check("material/visual/LOD mappings preserve original order and mask", () =>
{
    var solid = Solid();
    solid.Visuals = new CPlugVisual[] { Visual(), Visual() };
    solid.CustomMaterials = new[] { new CPlugSolid2Model.Material { MaterialName = "first" }, new CPlugSolid2Model.Material { MaterialName = "second" } };
    solid.ShadedGeoms = new[] {
        new CPlugSolid2Model.ShadedGeom { VisualIndex = 1, MaterialIndex = 0, LodMask = 6 },
        new CPlugSolid2Model.ShadedGeom { VisualIndex = 0, MaterialIndex = 1, LodMask = 1 },
        new CPlugSolid2Model.ShadedGeom { VisualIndex = 1, MaterialIndex = 1, LodMask = 8 } };
    var result = ItemScene.Build(solid, 0).Preview;
    Require(result.Geometry.Count == 2 && result.Mappings.Count == 3, "Mappings incorrectly duplicate geometry.");
    Require(result.Mappings[0].VisualIndex == 1 && result.Mappings[0].MaterialIndex == 0 && result.Mappings[0].LodMask == 6, "Shaded mapping guessed from visual order.");
    solid.ShadedGeoms[0].MaterialIndex = 5;
    Require(Has(ItemScene.Build(solid, 0).Preview, "mapping-index"), "Invalid material index accepted.");
    solid.ShadedGeoms[0].VisualIndex = -1;
    Require(ItemScene.Build(solid, 0).Preview.Mappings[0].State == ItemSceneState.Invalid, "Invalid visual index accepted.");
});

Check("bad indices and nonfinite attributes produce diagnostics without mutation", () =>
{
    foreach (var indices in new[] { new[] { 0, 1, 99 }, new[] { -1, 1, 2 }, new[] { 0, 1 } })
    {
        var visual = Visual();
        visual.IndexBuffer!.Indices = indices;
        var result = ItemScene.Build(visual, 0).Preview;
        Require(result.Geometry.Count == 0 && Has(result, "invalid-indices"), "Bad indices accepted.");
        Require(ReferenceEquals(visual.IndexBuffer.Indices, indices), "Input replaced.");
    }
    var invalid = Visual();
    invalid.VertexStreams[0].Positions![0] = new Vec3(float.NaN, 0, 0);
    Require(Has(ItemScene.Build(invalid, 0).Preview, "nonfinite-position"), "NaN accepted.");
    invalid = Visual();
    invalid.VertexStreams[0].Normals![0] = new Vec3(0, 0, 0);
    invalid.VertexStreams[0].UVs[0][0] = new Vec2(float.PositiveInfinity, 0);
    var channels = ItemScene.Build(invalid, 0).Preview;
    Require(channels.Geometry.Single().Normals is null && !channels.Geometry.Single().UVs.ContainsKey(0), "Invalid channels leaked.");
    Require(Has(channels, "invalid-normals") && Has(channels, "invalid-uv"), "Missing channel diagnostics.");
    JsonSerializer.Serialize(channels);
});

Check("invalid quaternions and overflow suppress geometry", () =>
{
    foreach (var q in new[] { new Quat(), new Quat(float.NaN, 0, 0, 1), new Quat(0, 0, 0, 2) })
    {
        var root = new CPlugPrefab { Ents = new[] { Ent(Solid(), default, q) } };
        var result = ItemScene.Build(root, 0).Preview;
        Require(result.Geometry.Count == 0 && Has(result, "invalid-transform"), "Invalid rotation accepted.");
        JsonSerializer.Serialize(result);
    }
    var overflow = new CPlugPrefab { Ents = new[] { Ent(new CPlugPrefab { Ents = new[] { Ent(Solid(), new Vec3(float.MaxValue, 0, 0)) } }, new Vec3(float.MaxValue, 0, 0)) } };
    Require(ItemScene.Build(overflow, 0).Preview.Geometry.Count == 0, "Overflow transform accepted.");
});

Check("inverse-transpose normals and finite source bounds", () =>
{
    Require(ItemScene.TryTransformNormal(new Vector3(1, 1, 0), Matrix4x4.CreateScale(2, 1, 1), out var normal), "Valid affine normal rejected.");
    Near(new[] { normal.X, normal.Y, normal.Z }, 1 / MathF.Sqrt(5), 2 / MathF.Sqrt(5), 0);
    Require(!ItemScene.TryTransformNormal(Vector3.UnitX, Matrix4x4.CreateScale(0, 1, 1), out _), "Singular matrix accepted.");
    var visual = Visual();
    visual.BoundingBox = new BoxAligned(float.NaN, 0, 0, 1, 1, 1);
    var result = ItemScene.Build(visual, 0).Preview;
    Require(Has(result, "nonfinite-source-bounds"), "Invalid authored bounds ignored.");
    Near(result.Geometry.Single().BoundsMin, 0, 0, 0);
    Near(result.Geometry.Single().BoundsMax, 1, 1, 0);
});

Check("external entity, mesh, shapes, variants and materials never resolve", () =>
{
    var calls = 0;
    var table = new GbxRefTable();
    table.ExternalNodes["external.Gbx"] = () => { calls++; throw new Exception("Unexpected external resolution"); };
    var file = new GbxRefTableFile(table, 0, false, "external.Gbx");
    var staticModel = new CPlugStaticObjectModel { MeshFile = file, ShapeFile = file };
    var dynamicModel = new CPlugDynaObjectModel { MeshFile = file, DynaShapeFile = file, StaticShapeFile = file, LocAnimFile = file };
    var externalEntity = Ent(null); externalEntity.ModelFile = file;
    var root = new CPlugPrefab { Ents = new[] { externalEntity, Ent(staticModel), Ent(dynamicModel) } };
    var result = ItemScene.Build(root, 0).Preview;
    Require(result.Diagnostics.Count(x => x.Code == "external-reference") == 7, "External slots not inventoried.");
    Require(result.Collisions.Count(x => x.State == ItemSceneState.Unresolved) == 3, "Unresolved collision reported absent.");
    var solid = Solid(); solid.CustomMaterials = null; solid.Materials = new[] { new External<CPlugMaterial>(null, file) };
    Require(ItemScene.Build(solid, 0).Preview.Materials.Single().State == ItemSceneState.Unresolved, "Material reference not inventoried.");
    ItemScene.Build(new NPlugItem_SVariant { EntityModelFile = file }, 0, 0);
    ItemScene.Build(new CGameItemModel { VisModelCustomFile = file, PhyModelCustomFile = file, EntityModelEditionFile = file }, 0);
    Require(calls == 0, "A resolving getter was invoked.");
    // Prove the fixture trap is live; only this explicit control is allowed to invoke the loader.
    try { _ = staticModel.Mesh; } catch (Exception) { }
    Require(calls == 1, "External resolver trap was not active.");
});

Check("unsupported nodes stay visible in inventory", () =>
{
    var result = ItemScene.Build(new CPlugCrystal(), 0).Preview;
    Require(result.Nodes.Single().State == ItemSceneState.Unsupported && Has(result, "unsupported-node"), "Unsupported root silently absent.");
    var primitive = ItemScene.Build(new CPlugSurface { Surf = new CPlugSurface.Sphere { Size = 2 } }, 0).Preview;
    Require(primitive.Collisions.Single().State == ItemSceneState.Unsupported && primitive.Geometry.Count == 0, "Unsupported primitive fabricated triangles.");
});

Check("selected variant scopes are explicit and null roots are absent", () =>
{
    var item = new CGameItemModel { EntityModel = new NPlugItem_SVariantList {
        Variants = new[] { new NPlugItem_SVariant { EntityModel = Solid() }, new NPlugItem_SVariant { EntityModel = Solid(), HiddenInManualCycle = true } } } };
    Require(ItemScene.Build(item, 3, 1).Preview.Geometry.Single().Path.StartsWith("doc:3/variant:1/root/"), "Variant scope missing.");
    Require(Has(ItemScene.Build(item, 3).Preview, "variant-selection"), "Missing selection guessed.");
    Require(Has(ItemScene.Build(item, 3, 9).Preview, "variant-selection"), "Out of range selection guessed.");
    Require(ItemScene.Build(null, 0).Preview.Nodes.Single().State == ItemSceneState.Absent, "Null root unsupported instead of absent.");
});

Check("light zero values and explicit socket ownership", () =>
{
    var light = new CPlugLightUserModel { Color = new Vec3(0, .5f, 0), Intensity = 0, Distance = 0 };
    var direct = ItemScene.Build(new CPlugPrefab { Ents = new[] { Ent(light, new Vec3(1, 2, 3)) } }, 0);
    var dto = direct.Preview.Lights.Single();
    Require(dto.Intensity == 0 && dto.Distance == 0 && dto.Color[0] == 0, "Zero value replaced.");
    Near(dto.Position!, 1, 2, 3);
    Require(direct.Handles.Lights.Single().Entry!.OriginalIndex == 0, "Position owner missing.");
    var solid = Solid(); solid.LightUserModels = new[] { light };
    var instance = new CPlugSolid2Model.LightInst { ModelIndex = 0, SocketIndex = 7 };
    solid.LightInsts = new[] { instance };
    var owned = ItemScene.Build(solid, 0);
    Require(owned.Preview.Lights.Single().Position is null && owned.Preview.Lights.Single().SocketIndex == 7, "Socket position guessed.");
    Require(ReferenceEquals(owned.Handles.Lights.Single().Instance, instance), "Light instance owner missing.");
    instance.ModelIndex = 99;
    Require(Has(ItemScene.Build(solid, 0).Preview, "light-model-index"), "Invalid model index accepted.");
});

Check("light ownership classes and value persistence capability", () =>
{
    var light = new CPlugLightUserModel { Color = new Vec3(0, .5f, 0), Intensity = 2, Distance = 5 };
    light.CreateChunk<CPlugLightUserModel.Chunk090F9000>();
    var direct = ItemScene.Build(new CPlugPrefab { Ents = new[] { Ent(light, new Vec3(1, 2, 3)) } }, 0).Preview;
    var dto = direct.Lights.Single();
    Require(dto.Ownership == ItemSceneLightOwnership.PrefabEntry && dto.ValuesPersist, "Entry ownership or persistence misclassified.");
    Require(dto.EntityPath!.EndsWith("/ent:0"), "Entry owner path missing.");
    Near(dto.Position!, 1, 2, 3);
    var owner = direct.Diagnostics.Single(x => x.Code == "light-owner-entry");
    Require(owner.State == ItemSceneState.Present && owner.Message.Contains("/ent:0"), "Entry ownership diagnostic missing the entry path.");
    Require(!Has(direct, "light-socket-transform") && !Has(direct, "light-values-not-persisted"), "Entry light misreported as blocked.");

    var bare = ItemScene.Build(light, 0).Preview;
    Require(bare.Lights.Single().Ownership == ItemSceneLightOwnership.SceneTransform && Has(bare, "light-owner-scene")
        && !Has(bare, "light-owner-entry"), "Scene-transform ownership misclassified.");

    var socketLight = new CPlugLightUserModel { Color = new Vec3(.2f, .4f, .6f), Intensity = 3, Distance = 8 };
    socketLight.CreateChunk<CPlugLightUserModel.Chunk090F9000>();
    var solid = Solid();
    solid.LightUserModels = new[] { socketLight, new CPlugLightUserModel { Color = new Vec3(1, 0, 0), Intensity = 5, Distance = 9 },
        new CPlugLightUserModel { Color = new Vec3(0, 0, 1), Intensity = 1, Distance = 2 } };
    solid.LightInsts = new[] { new CPlugSolid2Model.LightInst { ModelIndex = 0, SocketIndex = 7 },
        new CPlugSolid2Model.LightInst { ModelIndex = 1, SocketIndex = 3 } };
    var owned = ItemScene.Build(solid, 0).Preview;
    var instanced = owned.Lights.Single(x => x.SocketIndex == 7);
    Require(instanced.Ownership == ItemSceneLightOwnership.SolidInstance, "Instance ownership misclassified.");
    Require(instanced.Position is null && instanced.ModelIndex == 0, "Socket data not retained as typed ownership.");
    var blocked = owned.Diagnostics.Single(x => x.Code == "light-socket-transform" && x.Path.EndsWith("lightInst:0"));
    Require(blocked.Message.Contains("090BA000") && blocked.Message.Contains("not an array index into positions"), "Socket diagnostic does not state the exact blocker.");
    Require(owned.Lights.Count(x => x.Ownership == ItemSceneLightOwnership.SolidInstance) == 2
        && Has(owned, "uninstanced-light"), "Uninstanced model leaked into the editable inventory.");
    Require(owned.Diagnostics.Any(x => x.Code == "uninstanced-light" && x.Path.EndsWith("lightModel:2")), "Uninstanced light not reported at its slot.");

    var unpersisted = owned.Lights.Single(x => !x.ValuesPersist);
    Require(unpersisted.Path.EndsWith("lightInst:1") && unpersisted.SocketIndex == 3 && Has(owned, "light-values-not-persisted"), "Missing chunk not surfaced as capability info.");
    var legacy = Solid();
    legacy.Lights = new[] { new CPlugSolid2Model.Light() };
    var legacyScene = ItemScene.Build(legacy, 0).Preview;
    Require(legacyScene.Lights.Count == 0 && Has(legacyScene, "legacy-light"), "Legacy array must stay diagnostic-only.");
    Require(legacyScene.Nodes.Any(x => x.Kind == ItemSceneKind.Light && x.State == ItemSceneState.Unsupported), "Legacy light node missing.");
});

Check("light save/reparse keeps ownership, sockets and persisted values", () =>
{
    var persisted = new CPlugLightUserModel { Color = new Vec3(.2f, .4f, .6f), Intensity = 3, Distance = 8 };
    persisted.CreateChunk<CPlugLightUserModel.Chunk090F9000>().Version = 1;
    var volatileLight = new CPlugLightUserModel { Color = new Vec3(1, 0, 0), Intensity = 5, Distance = 9 };
    var solid = new CPlugSolid2Model { Visuals = Array.Empty<CPlugVisual>(), CustomMaterials = Array.Empty<CPlugSolid2Model.Material>(), ShadedGeoms = Array.Empty<CPlugSolid2Model.ShadedGeom>() };
    solid.CreateChunk<CPlugSolid2Model.Chunk090BB000>().Version = 34;
    solid.LightUserModels = new[] { persisted, volatileLight };
    solid.LightInsts = new[] { new CPlugSolid2Model.LightInst { ModelIndex = 0, SocketIndex = 7 },
        new CPlugSolid2Model.LightInst { ModelIndex = 1, SocketIndex = 3 } };
    var file = new Gbx<CPlugSolid2Model>(solid) { BodyCompression = GbxCompression.Uncompressed };
    using var before = new MemoryStream(); file.Save(before);
    ItemScene.Build(solid, 0);
    using var after = new MemoryStream(); file.Save(after);
    Require(before.ToArray().SequenceEqual(after.ToArray()), "Traversal changed serialized source bytes.");
    after.Position = 0;
    var reopened = Gbx.Parse<CPlugSolid2Model>(after).Node;
    var result = ItemScene.Build(reopened, 0).Preview;
    var instanced = result.Lights.Single(x => x.SocketIndex == 7);
    Require(instanced.Ownership == ItemSceneLightOwnership.SolidInstance, "Instance ownership lost on reparse.");
    Require(instanced.SocketIndex == 7 && instanced.ModelIndex == 0, "Socket/model indices did not round-trip.");
    Require(instanced.ValuesPersist && instanced.Intensity == 3 && instanced.Distance == 8, "Persisted light levels lost.");
    Near(instanced.Color, .2f, .4f, .6f);
    var lost = result.Lights.Single(x => !x.ValuesPersist);
    Require(Has(result, "light-values-not-persisted") && !(lost.Color[0] == 1 && lost.Intensity == 5), "Values without chunk 090F9000 falsely persisted.");
    Require(!Has(result, "light-model-index"), "Valid instance rejected after reparse.");

    var item = new CGameItemModel { EntityModel = new CPlugPrefab { Ents = new[] { Ent(persisted, new Vec3(10, 0, 0)) } },
        ItemType = CGameItemModel.EItemType.Ornament };
    item.CreateChunk<CGameItemModel.HeaderChunk2E002000>();
    item.CreateChunk<CGameItemModel.Chunk2E002019>().Version = 15;
    var itemFile = new Gbx<CGameItemModel>(item) { BodyCompression = GbxCompression.Uncompressed };
    using var itemStream = new MemoryStream(); itemFile.Save(itemStream);
    itemStream.Position = 0;
    var reparsed = ItemScene.Build(Gbx.Parse<CGameItemModel>(itemStream).Node, 0).Preview;
    var entryLight = reparsed.Lights.Single();
    Require(entryLight.Ownership == ItemSceneLightOwnership.PrefabEntry, "Entry ownership lost on reparse.");
    Near(entryLight.Position!, 10, 0, 0);
    Require(Has(reparsed, "light-owner-entry") && entryLight.ValuesPersist, "Entry capability lost on reparse.");
});

Check("collision mesh, compound transform, cycle and generated status", () =>
{
    var mesh = new CPlugSurface.Mesh { Version = 6, Vertices = new[] { new Vec3(1, 0, 0), new Vec3(0, 1, 0), new Vec3(0, 0, 0) },
        Triangles = new[] { new CPlugSurface.Mesh.Triangle(new Int3(0, 1, 2), 0, 0, 0) } };
    var compound = new CPlugSurface.Compound { Surfs = new CPlugSurface.ISurf[] { mesh },
        SurfLocs = new[] { new Iso4(0, -1, 0, 1, 0, 0, 0, 0, 1, 2, 0, 0) } };
    var result = ItemScene.Build(new CPlugSurface { Surf = compound }, 0).Preview;
    Near(result.Geometry.Single().Positions.Take(3), 2, 1, 0);
    Require(result.Geometry.Single().IsCollision, "Collision tagged visual.");
    compound.Surfs = new CPlugSurface.ISurf[] { compound };
    Require(Has(ItemScene.Build(new CPlugSurface { Surf = compound }, 0).Preview, "collision-cycle-or-limit"), "Collision cycle not handled.");
    var generated = ItemScene.Build(new CPlugStaticObjectModel { Mesh = Solid(), IsMeshCollidable = true }, 0).Preview;
    Require(generated.Collisions.Single().State == ItemSceneState.Unsupported && Has(generated, "generated-collision"), "Generated collision claimed absent or verified.");
});

Check("tree roots, solid wrappers and nested affine locations", () =>
{
    var visual = CpuVisual();
    var tree = new CPlugTree { Visual = visual, Location = Iso4.Identity with { TX = 10 } };
    Near(ItemScene.Build(tree, 0).Preview.Geometry.Single().Positions.Take(3), 11, 0, 0);
    Near(ItemScene.Build(new CPlugSolid { Tree = tree }, 0).Preview.Geometry.Single().Positions.Take(3), 11, 0, 0);
    var child = new CPlugTree { Visual = visual, Location = new Iso4(0, -1, 0, 1, 0, 0, 0, 0, 1, 2, 0, 0) };
    var parent = new CPlugTree { Children = [child], Location = new Iso4(0, -1, 0, 1, 0, 0, 0, 0, 1, 10, 0, 0) };
    var result = ItemScene.Build(new CPlugPrefab { Ents = [Ent(new CPlugSolid { Tree = parent }, new(20, 0, 0))] }, 0);
    var mesh = result.Preview.Geometry.Single();
    Near(mesh.Positions, 29, 2, 0, 30, 1, 0, 30, 2, 0);
    Near(mesh.Normals!.Take(3), -1, 0, 0);
    Require(mesh.Path.EndsWith("/tree/children:0/visual") && mesh.EntityPath!.EndsWith("/ent:0"), "Tree visual occurrence/entry ownership lost.");
    Require(ReferenceEquals(result.Handles.Visuals.Single().Source, visual), "Tree traversal cloned visual.");
    parent.Location = Iso4.Identity with { XX = 2, YY = 1 };
    child.Location = null;
    visual.Vertices = [new(new(1, 0, 0), new Vec3(1, 1, 0), null, null, null, null, null), Vertex(new(0, 1, 0)), Vertex(Vec3.Zero)];
    Near(ItemScene.Build(parent, 0).Preview.Geometry.Single().Normals!.Take(3), 1 / MathF.Sqrt(5), 2 / MathF.Sqrt(5), 0);
    parent.Location = Iso4.Zero;
    var invalid = ItemScene.Build(parent, 0).Preview;
    Require(invalid.Geometry.Count == 0 && Has(invalid, "invalid-tree-transform"), "Singular tree location accepted.");
});

Check("shared trees, cycles and unsupported subclasses preserve base graph", () =>
{
    var shared = new CPlugTree { Visual = CpuVisual() };
    var root = new CPlugTree { Children = [shared, shared] };
    shared.Children.Add(root);
    var result = ItemScene.Build(root, 0).Preview;
    Require(result.Geometry.Count == 2 && result.Nodes.Count(n => n.State == ItemSceneState.Cycle) == 2, "Tree traversal globally deduplicated or recursed through cycle.");
    Require(result.Geometry[0].SourceId == result.Geometry[1].SourceId && result.Geometry[0].Path != result.Geometry[1].Path, "Repeated tree occurrence identity lost.");
    var subclass = ItemScene.Build(new CPlugTreeVisualMip { Visual = CpuVisual(), Children = [new() { Visual = CpuVisual() }] }, 0).Preview;
    Require(subclass.Geometry.Count == 2 && Has(subclass, "tree-subclass"), "Unsupported subclass lost supported base visual/children.");
});

Check("tree external slots never resolve", () =>
{
    var calls = 0;
    var table = new GbxRefTable();
    table.ExternalNodes["external-tree.Gbx"] = () => { calls++; throw new Exception("Unexpected tree resolution"); };
    var file = new GbxRefTableFile(table, 0, false, "external-tree.Gbx");
    var solid = new CPlugSolid { TreeFile = file, Tree = new CPlugTree { Visual = CpuVisual() } };
    var result = ItemScene.Build(solid, 0).Preview;
    Require(result.Geometry.Count == 0 && result.Nodes.Single(n => n.Path.EndsWith("/tree")).State == ItemSceneState.Unresolved, "External TreeFile was followed or lost.");
    var treeResult = ItemScene.Build(new CPlugTree { Visual = CpuVisual(), ShaderFile = file, FuncTreeFile = file }, 0).Preview;
    Require(treeResult.Geometry.Count == 1 && treeResult.Diagnostics.Count(d => d.Code == "external-reference") == 2 && calls == 0, "Tree resolving getter invoked.");
    try { _ = new CPlugSolid { TreeFile = file }.Tree; } catch (Exception) { }
    Require(calls == 1, "External tree trap was not active.");
});

Check("collision versions select serialized cooked or ordinary triangles", () =>
{
    foreach (var version in new[] { 1, 2, 3, 5, 6, 7 })
    {
        var mesh = new CPlugSurface.Mesh { Version = version, Vertices = [new(1, 0, 0), new(0, 1, 0), Vec3.Zero] };
        if (version <= 5) mesh.CookedTriangles = [new(default, new(0, 1, 2), 0, 0, 0)];
        else mesh.Triangles = [new(new(0, 1, 2), 0, 0, 0)];
        var parsed = ReopenSurface(mesh);
        var result = ItemScene.Build(parsed, 0).Preview;
        Require(result.Collisions.Single().State == ItemSceneState.Present, $"Supported mesh version {version} rejected.");
        Near(result.Geometry.Single().Positions, 1, 0, 0, 0, 1, 0, 0, 0, 0);
        Require(result.Geometry.Single().Indices.SequenceEqual(new[] { 0, 1, 2 }), "Wrong triangle layout decoded.");
    }
});

Check("collision unknown, ambiguous, missing and invalid layouts are distinguished", () =>
{
    foreach (var version in new[] { 0, 4, 99 })
    {
        var mesh = new CPlugSurface.Mesh { Version = version };
        var result = ItemScene.Build(ReopenSurface(mesh), 0).Preview;
        Require(result.Collisions.Single().State == ItemSceneState.Unsupported && Has(result, "collision-mesh-version"), "Undecoded collision version reported absent.");
        mesh.Vertices = [new(1, 0, 0), new(0, 1, 0), Vec3.Zero]; mesh.Triangles = [new(new(0, 1, 2), 0, 0, 0)];
        result = ItemScene.Build(new CPlugSurface { Surf = mesh }, 0).Preview;
        Require(result.Geometry.Count == 0 && result.Collisions.Single().State == ItemSceneState.Unsupported, "Unknown version arrays accepted.");
    }
    foreach (var version in new[] { 5, 6 })
    {
        var mesh = new CPlugSurface.Mesh { Version = version, Vertices = [new(1, 0, 0), new(0, 1, 0), Vec3.Zero],
            Triangles = [new(new(0, 1, 2), 0, 0, 0)], CookedTriangles = [new(default, new(0, 1, 2), 0, 0, 0)] };
        var result = ItemScene.Build(new CPlugSurface { Surf = mesh }, 0).Preview;
        Require(result.Geometry.Count == 0 && result.Collisions.Single().State == ItemSceneState.Invalid && Has(result, "collision-mesh-layout"), "Ambiguous layout accepted.");
        if (version == 5) mesh.CookedTriangles = null; else mesh.Triangles = null;
        result = ItemScene.Build(new CPlugSurface { Surf = mesh }, 0).Preview;
        Require(result.Geometry.Count == 0 && result.Collisions.Single().State == ItemSceneState.Invalid, "Wrong alternate array substituted.");
    }
    Require(ItemScene.Build(new CPlugSurface { Surf = new CPlugSurface.Mesh { Version = 6 } }, 0).Preview.Collisions.Single().State == ItemSceneState.Invalid, "Missing supported array not distinguished.");
    Require(ItemScene.Build(ReopenSurface(new CPlugSurface.Mesh { Version = 6, Triangles = [] }), 0).Preview.Collisions.Single().State == ItemSceneState.Absent, "Supported empty mesh should be absent.");
    var invalidMesh = new CPlugSurface.Mesh { Version = 6, Vertices = [Vec3.Zero], Triangles = [new(new(0, 1, 2), 0, 0, 0)] };
    var invalid = ItemScene.Build(new CPlugSurface { Surf = invalidMesh }, 0).Preview;
    Require(invalid.Geometry.Count == 0 && invalid.Collisions.Single().State == ItemSceneState.Invalid && Has(invalid, "invalid-indices"), "Invalid triangles labelled supported.");
    invalidMesh.Triangles = []; invalidMesh.Vertices = [new(float.NaN, 0, 0)];
    invalid = ItemScene.Build(new CPlugSurface { Surf = invalidMesh }, 0).Preview;
    Require(invalid.Collisions.Single().State == ItemSceneState.Invalid && Has(invalid, "nonfinite-position"), "Invalid empty mesh labelled absent.");
});

Console.WriteLine($"{passed} passed, {failed} failed.");
return failed == 0 ? 0 : 1;

void Check(string name, Action test)
{
    try { test(); passed++; Console.WriteLine($"PASS: {name}"); }
    catch (Exception ex) { failed++; Console.Error.WriteLine($"FAIL: {name}: {ex}"); }
}
static bool Has(ItemScenePreview scene, string code) => scene.Diagnostics.Any(x => x.Code == code);
static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
static void Near(IEnumerable<float> actual, params float[] expected)
{
    var values = actual.ToArray();
    Require(values.Length == expected.Length && values.Zip(expected).All(x => MathF.Abs(x.First - x.Second) < .0001f),
        $"Expected [{string.Join(',', expected)}], got [{string.Join(',', values)}]");
}
static Quat Z90() => new(0, 0, MathF.Sqrt(.5f), MathF.Sqrt(.5f));
static CPlugVisualIndexedTriangles CpuVisual() => new() { Vertices = [Vertex(new(1, 0, 0)), Vertex(new(0, 1, 0)), Vertex(Vec3.Zero)], IndexBuffer = new() { Indices = [0, 1, 2] } };
static CPlugSurface ReopenSurface(CPlugSurface.Mesh mesh)
{
    var surface = new CPlugSurface { Surf = mesh };
    surface.CreateChunk<CPlugSurface.Chunk0900C003>().Version = 2;
    using var stream = new MemoryStream();
    new Gbx<CPlugSurface>(surface) { BodyCompression = GbxCompression.Uncompressed }.Save(stream);
    stream.Position = 0;
    return Gbx.Parse<CPlugSurface>(stream).Node;
}
static CPlugPrefab.EntRef Ent(CMwNod? model, Vec3 position = default, Quat? rotation = null) =>
    new() { Model = model, Position = position, Rotation = rotation ?? new Quat(0, 0, 0, 1) };
static CPlugVisual3D.Vertex Vertex(Vec3 p) => new(p, new Vec3(1, 0, 0), null, null, null, null, null);
static CPlugVisual.TexCoordSet TexCoords(float x) => new() { TexCoords = new[] {
    new CPlugVisual.TexCoord(new Vec2(x, 0), null, null), new CPlugVisual.TexCoord(new Vec2(0, 1), null, null),
    new CPlugVisual.TexCoord(new Vec2(1, 1), null, null) } };
static CPlugVisualIndexedTriangles Visual() => new() {
    VertexStreams = new() { new CPlugVertexStream {
        Positions = new[] { new Vec3(1, 0, 0), new Vec3(0, 1, 0), new Vec3(0, 0, 0) },
        Normals = new[] { new Vec3(1, 0, 0), new Vec3(1, 0, 0), new Vec3(1, 0, 0) },
        UVs = new() { [0] = new[] { new Vec2(0, 0), new Vec2(1, 0), new Vec2(0, 1) },
            [1] = new[] { new Vec2(.25f, .25f), new Vec2(.5f, .25f), new Vec2(.25f, .5f) } } } },
    IndexBuffer = new CPlugIndexBuffer { Indices = new[] { 0, 1, 2 } } };
static CPlugSolid2Model Solid() => new() { Visuals = new CPlugVisual[] { Visual() },
    CustomMaterials = new[] { new CPlugSolid2Model.Material { MaterialName = "synthetic" } },
    ShadedGeoms = new[] { new CPlugSolid2Model.ShadedGeom { VisualIndex = 0, MaterialIndex = 0, LodMask = 1 } } };
