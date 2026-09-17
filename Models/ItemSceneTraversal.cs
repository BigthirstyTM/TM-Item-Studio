using System.Numerics;
using GBX.NET.Components;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.MetaNotPersistent;
using GBX.NET.Engines.MwFoundations;
using GBX.NET.Engines.Plug;

namespace TM_Item_Studio.Models;

public static partial class ItemScene
{
    private sealed partial class Builder(int? variantOrdinal, ItemSceneGeometryCache? geometryCache)
    {
        private readonly List<ItemSceneNode> nodes = new();
        private readonly List<ItemSceneGeometry> geometry = new();
        private readonly List<ItemSceneMapping> mappings = new();
        private readonly List<ItemSceneMaterial> materials = new();
        private readonly List<ItemSceneSolid> solids = new();
        private readonly List<ItemSceneLight> lights = new();
        private readonly List<ItemSceneCollision> collisions = new();
        private readonly List<ItemSceneDiagnostic> diagnostics = new();
        private readonly ItemSceneHandles handles = new();
        private readonly Dictionary<object, int> sourceIds = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<object> ancestors = new(ReferenceEqualityComparer.Instance);

        internal ItemSceneResult Result() => new(new(nodes, geometry, mappings, materials, solids, lights, collisions, diagnostics), handles);
        private int Id(object source)
        {
            if (sourceIds.TryGetValue(source, out var id)) return id;
            id = sourceIds.Count;
            sourceIds.Add(source, id);
            handles.Sources.Add(id, source);
            return id;
        }
        private void Issue(string path, string code, ItemSceneState state, string message) => diagnostics.Add(new(path, code, state, message));
        private void Node(string path, string? parent, object? source, ItemSceneKind kind, ItemSceneState state,
            Matrix4x4? local, Matrix4x4? world) => nodes.Add(new(path, parent, source is null ? null : Id(source), kind,
                state, source?.GetType().Name ?? "null", local.HasValue ? Pack(local.Value) : null, world.HasValue ? Pack(world.Value) : null));

        private void Edge(GbxRefTableFile? file, Func<CMwNod?> readInline, string path, string parent,
            Matrix4x4? world, ItemSceneEntryHandle? entry, int depth,
            ItemSceneCollisionSource collisionSource = ItemSceneCollisionSource.SurfaceSlot)
        {
            if (file is not null)
            {
                // FilePath is metadata; never call GetFullPath/GetNode or the resolving property.
                Node(path, parent, file, ItemSceneKind.Other, ItemSceneState.Unresolved, Matrix4x4.Identity, world);
                Issue(path, "external-reference", ItemSceneState.Unresolved, $"External reference: {file.FilePath}");
                return;
            }
            Visit(readInline(), path, parent, Matrix4x4.Identity, world, entry, depth + 1, collisionSource);
        }

        internal void Visit(CMwNod? source, string path, string? parent, Matrix4x4? local,
            Matrix4x4? world, ItemSceneEntryHandle? entry, int depth,
            ItemSceneCollisionSource collisionSource = ItemSceneCollisionSource.SurfaceSlot)
        {
            if (source is null)
            {
                Node(path, parent, null, ItemSceneKind.Other, ItemSceneState.Absent, local, world);
                return;
            }
            if (depth > 128 || nodes.Count >= 100000)
            {
                Node(path, parent, source, Kind(source), ItemSceneState.Unsupported, local, world);
                Issue(path, "traversal-limit", ItemSceneState.Unsupported, "Traversal budget reached; subtree not inspected.");
                return;
            }
            if (!ancestors.Add(source))
            {
                Node(path, parent, source, Kind(source), ItemSceneState.Cycle, local, world);
                Issue(path, "cycle", ItemSceneState.Cycle, "Reference returns to an ancestor; other occurrences remain traversable.");
                return;
            }
            try
            {
                if (source is CPlugTree { Location: { } location })
                {
                    var treeLocal = FromIso4(location);
                    local = local.HasValue ? treeLocal * local.Value : null;
                    world = world.HasValue ? treeLocal * world.Value : null;
                    if (!local.HasValue || !ValidTransform(local.Value) || !world.HasValue || !ValidTransform(world.Value))
                    {
                        local = null;
                        world = null;
                        Issue(path, "invalid-tree-transform", ItemSceneState.Invalid, "Tree location is nonfinite, singular or overflows its parent transform; geometry omitted.");
                    }
                }
                Node(path, parent, source, Kind(source), Kind(source) == ItemSceneKind.Other ? ItemSceneState.Unsupported
                    : world.HasValue ? ItemSceneState.Present : ItemSceneState.Invalid, local, world);
                switch (source)
                {
                    case CGameItemModel item:
                        Visit(item.EntityModel, path + "/entityModel", path, Matrix4x4.Identity, world, entry, depth + 1);
                        Edge(item.VisModelCustomFile, () => item.VisModelCustom, path + "/visModelCustom", path, world, entry, depth);
                        if (item.PhyModelCustomFile is not null)
                            collisions.Add(new(path + "/phyModelCustom", entry?.Path, "Item PhyModel reference", ItemSceneState.Unresolved, ItemSceneCollisionSource.ItemPhyModel));
                        Edge(item.PhyModelCustomFile, () => item.PhyModelCustom, path + "/phyModelCustom", path, world, entry, depth);
                        // An edition model is source-authoring data, not a second renderable copy.
                        if (item.EntityModelEditionFile is not null)
                            Edge(item.EntityModelEditionFile, () => null, path + "/entityModelEdition", path, world, entry, depth);
                        else if (item.EntityModelEdition is not null)
                        {
                            Node(path + "/entityModelEdition", path, item.EntityModelEdition, ItemSceneKind.Other, ItemSceneState.Unsupported, Matrix4x4.Identity, world);
                            Issue(path + "/entityModelEdition", "edition-model", ItemSceneState.Unsupported, "Authoring representation retained; not rendered as runtime geometry.");
                        }
                        break;
                    case NPlugItem_SVariantList variants:
                        if (!variantOrdinal.HasValue || variants.Variants is null || variantOrdinal.Value >= variants.Variants.Length)
                            Issue(path, "variant-selection", ItemSceneState.Invalid, "Select an existing original variant slot.");
                        else
                            Visit(variants.Variants[variantOrdinal.Value], path + $"/variant:{variantOrdinal.Value}", path, Matrix4x4.Identity, world, entry, depth + 1);
                        break;
                    case NPlugItem_SVariant variant:
                        Edge(variant.EntityModelFile, () => variant.EntityModel, path + "/entityModel", path, world, entry, depth);
                        break;
                    case CPlugPrefab prefab:
                        handles.Prefabs.Add(new(path, prefab, world));
                        var entries = prefab.Ents ?? Array.Empty<CPlugPrefab.EntRef>();
                        for (var i = 0; i < entries.Length; i++)
                        {
                            if (nodes.Count >= 100000)
                            { Issue(path, "traversal-limit", ItemSceneState.Unsupported, "Occurrence budget reached; remaining entries not inspected."); break; }
                            var entPath = path + $"/ent:{i}";
                            var ent = entries[i];
                            if (ent is null) { Node(entPath, path, null, ItemSceneKind.Entity, ItemSceneState.Absent, null, null); continue; }
                            Matrix4x4? entLocal = TryLocalTransform(ent.Position, ent.Rotation, out var transform) ? transform : null;
                            Matrix4x4? entWorld = entLocal.HasValue && world.HasValue ? entLocal.Value * world.Value : null;
                            if (entWorld.HasValue && !ValidTransform(entWorld.Value)) entWorld = null;
                            var owner = new ItemSceneEntryHandle(entPath, path, prefab, i, ent, entLocal, entWorld);
                            handles.Entries.Add(owner);
                            if (!entLocal.HasValue || !entWorld.HasValue)
                                Issue(entPath, "invalid-transform", ItemSceneState.Invalid, "Nonfinite, singular or non-unit transform; geometry omitted.");
                            if (ent.ModelFile is not null)
                            {
                                Node(entPath, path, ent, ItemSceneKind.Entity, ItemSceneState.Unresolved, entLocal, entWorld);
                                Issue(entPath, "external-reference", ItemSceneState.Unresolved, $"External entity reference: {ent.ModelFile.FilePath}");
                            }
                            else Visit(ent.Model, entPath, path, entLocal, entWorld, owner, depth + 1);
                        }
                        break;
                    case CGameCommonItemEntityModel common:
                        Visit(common.StaticObject, path + "/staticObject", path, Matrix4x4.Identity, world, entry, depth + 1);
                        Visit(common.VisModel, path + "/visModel", path, Matrix4x4.Identity, world, entry, depth + 1);
                        Visit(common.PhyModel, path + "/phyModel", path, Matrix4x4.Identity, world, entry, depth + 1);
                        if (common.TriggerShape is { } trigger)
                        {
                            Node(path + "/triggerShape", path, trigger, ItemSceneKind.Collision, ItemSceneState.Unsupported, null, null);
                            collisions.Add(new(path + "/triggerShape", entry?.Path, "Common-item trigger",
                                trigger is CPlugSurface ? ItemSceneState.Present : ItemSceneState.Unsupported, ItemSceneCollisionSource.CommonItemTrigger));
                            Issue(path + "/triggerShape", "trigger-transform", ItemSceneState.Unsupported, "Trigger source retained; its chunk-specific transform is not interpreted.");
                        }
                        else collisions.Add(new(path + "/triggerShape", entry?.Path, "No trigger shape", ItemSceneState.Absent, ItemSceneCollisionSource.CommonItemTrigger));
                        break;
                    case CPlugStaticObjectModel model:
                        Edge(model.MeshFile, () => model.Mesh, path + "/mesh", path, world, entry, depth);
                        if (model.IsMeshCollidable)
                        {
                            collisions.Add(new(path + "/shape", entry?.Path, "Mesh-collidable flag", ItemSceneState.Unsupported, ItemSceneCollisionSource.GeneratedMeshCollision));
                            Issue(path + "/shape", "generated-collision", ItemSceneState.Unsupported, "Mesh collision is requested; no generated collision shape is available to inspect.");
                        }
                        else CollisionEdge(model.ShapeFile, () => model.Shape, path + "/shape", path, world, entry, depth, ItemSceneCollisionSource.StaticObjectShape);
                        break;
                    case CPlugDynaObjectModel model:
                        Edge(model.MeshFile, () => model.Mesh, path + "/mesh", path, world, entry, depth);
                        CollisionEdge(model.DynaShapeFile, () => model.DynaShape, path + "/dynaShape", path, world, entry, depth, ItemSceneCollisionSource.DynamicObjectShape);
                        CollisionEdge(model.StaticShapeFile, () => model.StaticShape, path + "/staticShape", path, world, entry, depth, ItemSceneCollisionSource.DynamicObjectStaticShape);
                        if (model.LocAnimFile is not null)
                            Edge(model.LocAnimFile, () => null, path + "/locAnim", path, world, entry, depth);
                        else if (model.LocAnim is not null)
                            Issue(path + "/locAnim", "local-animation", ItemSceneState.Unsupported, "Local animation is retained but not evaluated by scene traversal.");
                        break;
                    case CPlugSolid2Model solid:
                        Solid(solid, path, world, entry, depth);
                        break;
                    case CPlugSolid solid:
                        Edge(solid.TreeFile, () => solid.Tree, path + "/tree", path, world, entry, depth);
                        break;
                    case CPlugTree tree:
                        // Derived tree LOD/animation semantics are not inferred from ordinary child order.
                        // Their supported base graph remains visible with an explicit partial-support diagnostic.
                        if (tree.GetType() != typeof(CPlugTree))
                            Issue(path, "tree-subclass", ItemSceneState.Unsupported, $"Only base Visual/Children/Location are interpreted for {tree.GetType().Name}; subclass LOD/animation semantics are unsupported.");
                        Visit(tree.Visual, path + "/visual", path, Matrix4x4.Identity, world, entry, depth + 1);
                        Visit(tree.Surface, path + "/surface", path, Matrix4x4.Identity, world, entry, depth + 1);
                        TreeAuxiliary(tree.ShaderFile, () => tree.Shader, path + "/shader", path, world);
                        TreeAuxiliary(tree.FuncTreeFile, () => tree.FuncTree, path + "/funcTree", path, world);
                        if (tree.Generator is not null)
                            Issue(path + "/generator", "tree-generator", ItemSceneState.Unsupported, "Tree generator is retained but not evaluated.");
                        var children = tree.Children;
                        for (var i = 0; children is not null && i < children.Count; i++)
                        {
                            if (nodes.Count >= 100000)
                            { Issue(path, "traversal-limit", ItemSceneState.Unsupported, "Occurrence budget reached; remaining tree children not inspected."); break; }
                            Visit(children[i], path + $"/children:{i}", path, Matrix4x4.Identity, world, entry, depth + 1);
                        }
                        break;
                    case CPlugVisual visual:
                        Visual(visual, path, world, entry, null, null);
                        break;
                    case CPlugSurface surface:
                        Surface(surface, path, world, entry, collisionSource);
                        break;
                    case CPlugLightUserModel light:
                        Light(light, path, world, entry, null, null);
                        break;
                    case NPlugDyna_SKinematicConstraint:
                        // Resolution belongs to ItemMotion, using handles.Prefabs and original entry arrays.
                        break;
                    case CGameObjectPhyModel phy:
                        PhyShapes(phy, path, world, entry);
                        break;
                    default:
                        Issue(path, "unsupported-node", ItemSceneState.Unsupported, $"No typed scene adapter for {source.GetType().Name}.");
                        break;
                }
            }
            finally { ancestors.Remove(source); }
        }

        private static ItemSceneKind Kind(CMwNod node) => node switch
        {
            CGameItemModel => ItemSceneKind.Item, NPlugItem_SVariant or NPlugItem_SVariantList => ItemSceneKind.Variant,
            CPlugPrefab => ItemSceneKind.Prefab, CPlugStaticObjectModel => ItemSceneKind.StaticObject,
            CPlugDynaObjectModel => ItemSceneKind.DynamicObject, CPlugSolid2Model or CPlugSolid => ItemSceneKind.Solid,
            CPlugTree => ItemSceneKind.Tree,
            CPlugVisual => ItemSceneKind.Visual, CPlugSurface => ItemSceneKind.Collision,
            CPlugLightUserModel => ItemSceneKind.Light, NPlugDyna_SKinematicConstraint => ItemSceneKind.Constraint,
            CGameCommonItemEntityModel => ItemSceneKind.Entity, _ => ItemSceneKind.Other
        };

        private void CollisionEdge(GbxRefTableFile? file, Func<CMwNod?> inline, string path, string parent,
            Matrix4x4? world, ItemSceneEntryHandle? entry, int depth, ItemSceneCollisionSource source)
        {
            if (file is not null) collisions.Add(new(path, entry?.Path, "External shape", ItemSceneState.Unresolved, source));
            else if (inline() is null) collisions.Add(new(path, entry?.Path, "No shape", ItemSceneState.Absent, source));
            Edge(file, inline, path, parent, world, entry, depth, source);
        }

        /// <summary>
        /// Inventory the hit/move/trigger shape references of a game-object phy model. The fid getters resolve
        /// external files, so the node is only read through the inline path when no file reference is set.
        /// </summary>
        private void PhyShapes(CGameObjectPhyModel phy, string path, Matrix4x4? world, ItemSceneEntryHandle? entry)
        {
            PhyShape(phy.HitShapeFidFile, () => phy.HitShapeFid, path + "/hitShapeFid", path, world, entry, ItemSceneCollisionSource.GameObjectHitShape);
            PhyShape(phy.MoveShapeFidFile, () => phy.MoveShapeFid, path + "/moveShapeFid", path, world, entry, ItemSceneCollisionSource.GameObjectMoveShape);
            PhyShape(phy.TriggerShapeFidFile, () => phy.TriggerShapeFid, path + "/triggerShapeFid", path, world, entry, ItemSceneCollisionSource.GameObjectTriggerShape);
            if (phy.Triggers is { Length: > 0 })
                Issue(path + "/triggers", "phy-trigger-actions", ItemSceneState.Unsupported,
                    $"{phy.Triggers.Length} trigger action records retained; their semantics are not interpreted.");
        }

        private void PhyShape(GbxRefTableFile? file, Func<CPlugSurface?> inline, string path, string parent,
            Matrix4x4? world, ItemSceneEntryHandle? entry, ItemSceneCollisionSource source)
        {
            if (file is not null)
            {
                Node(path, parent, file, ItemSceneKind.Collision, ItemSceneState.Unresolved, Matrix4x4.Identity, world);
                collisions.Add(new(path, entry?.Path, "External shape reference", ItemSceneState.Unresolved, source));
                return;
            }
            var surface = inline();
            if (surface is null)
            {
                Node(path, parent, null, ItemSceneKind.Collision, ItemSceneState.Absent, Matrix4x4.Identity, world);
                collisions.Add(new(path, entry?.Path, "No shape", ItemSceneState.Absent, source));
                return;
            }
            Node(path, parent, surface, ItemSceneKind.Collision, world.HasValue ? ItemSceneState.Present : ItemSceneState.Invalid, Matrix4x4.Identity, world);
            Surface(surface, path, world, entry, source);
        }

        private void TreeAuxiliary(GbxRefTableFile? file, Func<CMwNod?> inline, string path, string parent, Matrix4x4? world)
        {
            if (file is not null)
            {
                Node(path, parent, file, ItemSceneKind.Other, ItemSceneState.Unresolved, Matrix4x4.Identity, world);
                Issue(path, "external-reference", ItemSceneState.Unresolved, $"External tree auxiliary reference: {file.FilePath}");
            }
            else if (inline() is { } source)
            {
                Node(path, parent, source, ItemSceneKind.Other, ItemSceneState.Unsupported, Matrix4x4.Identity, world);
                Issue(path, "tree-auxiliary", ItemSceneState.Unsupported, "Tree shader/function data retained; rendering, material mapping and animation semantics are not interpreted.");
            }
        }

        private void Solid(CPlugSolid2Model solid, string path, Matrix4x4? world, ItemSceneEntryHandle? entry, int depth)
        {
            var visuals = solid.Visuals ?? Array.Empty<CPlugVisual>();
            var custom = solid.CustomMaterials;
            var ordinary = solid.Materials;
            var materialCount = custom is { Length: > 0 } ? custom.Length : ordinary?.Length ?? 0;
            var materialOffset = materials.Count;
            var lodDistances = solid.LodMaxDistAtFov90;
            var validLod = lodDistances?.All(x => float.IsFinite(x) && x >= 0) != false;
            solids.Add(new(path, Id(solid), validLod ? lodDistances?.ToArray() : null, visuals.Length, materialCount,
                solid.Skel is not null, solid.PreLightGenerator is not null));
            for (var i = 0; i < materialCount; i++)
            {
                var matPath = path + $"/material:{i}";
                if (custom is { Length: > 0 })
                {
                    var material = custom[i];
                    var instance = material?.MaterialUserInst;
                    var name = instance?.MaterialName ?? material?.MaterialName;
                    materials.Add(new(matPath, i, material is null ? null : Id(material), name,
                        material is null ? ItemSceneState.Absent : ItemSceneState.Present, "CustomMaterial",
                        instance?.MaterialName, instance?.Link));
                }
                else
                {
                    var material = ordinary![i];
                    var state = material?.File is not null ? ItemSceneState.Unresolved : material?.Node is null ? ItemSceneState.Absent : ItemSceneState.Present;
                    materials.Add(new(matPath, i, material is null ? null : Id(material), solid.MaterialIds?.ElementAtOrDefault(i), state, "Material"));
                    if (state == ItemSceneState.Unresolved)
                        Issue(matPath, "external-reference", state, $"External material reference: {material!.File!.FilePath}");
                }
            }
            if (materialCount == 0 && solid.MaterialInsts is { Length: > 0 })
                Issue(path, "legacy-material-table", ItemSceneState.Unsupported, "Legacy MaterialInsts retained; no verified shaded-index mapping.");
            var shaded = solid.ShadedGeoms ?? Array.Empty<CPlugSolid2Model.ShadedGeom>();
            for (var i = 0; i < shaded.Length; i++)
            {
                var map = shaded[i];
                var mapPath = path + $"/shadedGeom:{i}";
                if (map is null) { Issue(mapPath, "null-mapping", ItemSceneState.Invalid, "Null shaded geometry record."); continue; }
                var valid = map.VisualIndex >= 0 && map.VisualIndex < visuals.Length && visuals[map.VisualIndex] is not null
                    && map.MaterialIndex >= 0 && map.MaterialIndex < materialCount;
                if (valid && materials[materialOffset + map.MaterialIndex].State == ItemSceneState.Absent) valid = false;
                mappings.Add(new(mapPath, path, map.VisualIndex, map.MaterialIndex, map.LodMask,
                    valid ? materials[materialOffset + map.MaterialIndex].State : ItemSceneState.Invalid));
                if (!valid) Issue(mapPath, "mapping-index", ItemSceneState.Invalid, "Visual or material index lies outside its authored table.");
            }
            for (var i = 0; i < visuals.Length; i++)
            {
                var visPath = path + $"/visual:{i}";
                if (visuals[i] is null) { Node(visPath, path, null, ItemSceneKind.Visual, ItemSceneState.Absent, Matrix4x4.Identity, world); continue; }
                Node(visPath, path, visuals[i], ItemSceneKind.Visual, world.HasValue ? ItemSceneState.Present : ItemSceneState.Invalid, Matrix4x4.Identity, world);
                Visual(visuals[i], visPath, world, entry, solid, i);
                if (!shaded.Any(x => x?.VisualIndex == i)) Issue(visPath, "unmapped-visual", ItemSceneState.Unsupported, "Visual has no shaded material/LOD mapping.");
            }
            if (!validLod)
                Issue(path, "lod-distance", ItemSceneState.Invalid, "Nonfinite or negative LOD distance.");
            var models = solid.LightUserModels ?? Array.Empty<CPlugLightUserModel>();
            var instances = solid.LightInsts ?? Array.Empty<CPlugSolid2Model.LightInst>();
            for (var i = 0; i < instances.Length; i++)
            {
                var instance = instances[i];
                var lightPath = path + $"/lightInst:{i}";
                if (instance is null || instance.ModelIndex < 0 || instance.ModelIndex >= models.Length || models[instance.ModelIndex] is null)
                { Issue(lightPath, "light-model-index", ItemSceneState.Invalid, "Light instance has no valid model slot."); continue; }
                Light(models[instance.ModelIndex], lightPath, world, entry, solid, instance);
            }
            for (var i = 0; i < models.Length; i++)
                if (models[i] is not null && !instances.Any(x => x?.ModelIndex == i))
                {
                    var modelPath = path + $"/lightModel:{i}";
                    handles.Lights.Add(new(modelPath, models[i], solid, null, null));
                    Node(modelPath, path, models[i], ItemSceneKind.Light, ItemSceneState.Present, null, null);
                    Issue(modelPath, "uninstanced-light", ItemSceneState.Unsupported, "Authored light model has no instance; no position is inferred and the model stays diagnostic-only.");
                }
            if (solid.Lights is not null)
                for (var i = 0; i < solid.Lights.Length; i++)
                {
                    var light = solid.Lights[i];
                    var lightPath = path + $"/light:{i}";
                    Node(lightPath, path, light, ItemSceneKind.Light, ItemSceneState.Unsupported, null, world);
                    Issue(lightPath, "legacy-light", ItemSceneState.Unsupported, "Legacy Solid2 light representation retained; it is legacy/diagnostic-only and its unknown fields are not interpreted.");
                    if (light?.U03File is not null)
                        Issue(lightPath + "/model", "external-reference", ItemSceneState.Unresolved, $"External light reference: {light.U03File.FilePath}");
                }
        }

        private void Light(CPlugLightUserModel light, string path, Matrix4x4? world, ItemSceneEntryHandle? entry,
            CPlugSolid2Model? solid, CPlugSolid2Model.LightInst? instance)
        {
            handles.Lights.Add(new(path, light, solid, instance, entry));
            var valid = Finite(V(light.Color)) && float.IsFinite(light.Intensity) && float.IsFinite(light.Distance)
                && light.Intensity >= 0 && light.Distance >= 0;
            // Chunk 090F9000 is the only serializer of Color/Intensity/Distance; without it the values exist in memory only.
            var persists = light.GetChunk<CPlugLightUserModel.Chunk090F9000>() is not null;
            var ownership = instance is not null ? ItemSceneLightOwnership.SolidInstance
                : entry is not null ? ItemSceneLightOwnership.PrefabEntry : ItemSceneLightOwnership.SceneTransform;
            var state = !valid ? ItemSceneState.Invalid : instance is not null ? ItemSceneState.Unsupported
                : world.HasValue ? ItemSceneState.Present : ItemSceneState.Invalid;
            lights.Add(new(path, entry?.Path, Id(light), instance?.ModelIndex, instance?.SocketIndex,
                instance is null && world.HasValue ? Pack(world.Value.Translation) : null,
                valid ? Pack(V(light.Color)) : Array.Empty<float>(), valid ? light.Intensity : 0, valid ? light.Distance : 0, state,
                ownership, persists));
            if (instance is not null)
                Issue(path, "light-socket-transform", ItemSceneState.Unsupported, "LightInst.ModelIndex selects LightUserModels; the position needs the skeleton socket transform, and CPlugSkel socket records are serialized by chunk 090BA000 but not exposed by the bundled GBX.NET 2.4.4 public API. SocketIndex is retained as typed data and is not an array index into positions; no position inferred.");
            else if (entry is not null && world.HasValue)
                Issue(path, "light-owner-entry", ItemSceneState.Present, $"Position is owned by the prefab entry transform at {entry.Path}; the light model itself carries no placement.");
            else if (world.HasValue)
                Issue(path, "light-owner-scene", ItemSceneState.Present, "No prefab entry owns this light; its position is the composed scene transform chain applied to the model.");
            if (!persists)
                Issue(path, "light-values-not-persisted", ItemSceneState.Unsupported, "Color, intensity and distance are serialized by CPlugLightUserModel chunk 090F9000, which this node does not carry; value edits would not persist.");
            if (!valid) Issue(path, "light-values", ItemSceneState.Invalid, "Invalid light color, intensity or distance; preview omitted.");
        }
    }
}
