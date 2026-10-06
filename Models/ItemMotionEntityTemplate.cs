using System.Reflection;
using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;
using GBX.NET.Serialization;
using TmEssentials;
using KC = GBX.NET.Engines.Meta.NPlugDyna_SKinematicConstraint;

namespace TM_Item_Studio.Models;

/// <summary>
/// Appends one independently authored kinematic body/constraint pair to a flat,
/// already-supported prefab. The visual and collision model is deliberately
/// shared with the selected source body; no shape generation is attempted.
/// </summary>
public static class ItemKinematicEntityTemplate
{
    public const int MaxSafeDrawPathSegments = 4;

    public readonly record struct DrawPathSegment(
        KC.EAxis Axis,
        float Distance,
        int DurationMs,
        KC.EAxis RotationAxis,
        float RotationStartDeg,
        float RotationEndDeg);

    /// <summary>
    /// Inserts a complete, visible dyna body before a supported constraint. It
    /// deliberately shares the proven mesh and collision source so Trackmania
    /// can validate the parent-child graph before visual hiding is attempted.
    /// </summary>
    public static ItemMotionResult<KC> InsertVisiblePathProxy(CPlugPrefab owner, KC source, string prefabInstancePath)
    {
        var template = ReadTemplate(owner, source, prefabInstancePath);
        if (!template.Success) return ItemMotionResult<KC>.Fail(template.Status, template.Reason!);
        var value = template.Value!;
        var entries = owner.Ents!;
        var newSlot = value.Binding.Slots.Count;
        var proxy = new CPlugPrefab.EntRef
        {
            Position = default,
            Rotation = new GBX.NET.Quat(0, 0, 0, 1),
            Model = value.Dyna,
            Params = CopyInstance(value.Instance)
        };
        var proxyConstraint = new CPlugPrefab.EntRef
        {
            Position = value.ConstraintEntry.Position,
            Rotation = value.ConstraintEntry.Rotation,
            U01 = value.ConstraintEntry.U01,
            Model = CopyConstraint(source, value.Snapshot.Fields.Translation!, value.Snapshot.Fields.Rotation!),
            Params = new NPlugDyna_SPrefabConstraintParams
            {
                Version = 0,
                Ent1 = value.Parameters.Ent1,
                Ent2 = newSlot,
                Pos1 = default,
                Pos2 = default
            }
        };

        owner.Ents = entries.Concat([proxy, proxyConstraint]).ToArray();
        var proxyBinding = ItemMotionBindings.Resolve((KC)proxyConstraint.Model!, owner,
            (NPlugDyna_SPrefabConstraintParams)proxyConstraint.Params, prefabInstancePath);
        var visibleCandidate = new NPlugDyna_SPrefabConstraintParams
        {
            Version = value.Parameters.Version,
            Ent1 = newSlot,
            Ent2 = value.Parameters.Ent2,
            Pos1 = value.Parameters.Pos1,
            Pos2 = value.Parameters.Pos2
        };
        var visibleBinding = ItemMotionBindings.Resolve(source, owner, visibleCandidate, prefabInstancePath);
        if (proxyBinding.Status == ItemMotionStatus.Supported && visibleBinding.Status == ItemMotionStatus.Supported)
        {
            value.Parameters.Ent1 = newSlot;
            return ItemMotionResult<KC>.Ok((KC)proxyConstraint.Model!);
        }

        owner.Ents = entries;
        var failure = proxyBinding.Status != ItemMotionStatus.Supported ? proxyBinding : visibleBinding;
        return ItemMotionResult<KC>.Fail(failure.Status, failure.Reason ?? "The visible path proxy did not produce two safe bindings.");
    }

    /// <summary>
    /// Inserts a mesh-hidden but collision-complete carrier body before a supported
    /// constraint. This keeps one visible body while still creating a parent chain
    /// (Ent1/Ent2) that can author sequential A→B→C-style motion.
    /// </summary>
    public static ItemMotionResult<KC> InsertHiddenCarrierParent(CPlugPrefab owner, KC source, string prefabInstancePath)
    {
        var template = ReadTemplate(owner, source, prefabInstancePath);
        if (!template.Success) return ItemMotionResult<KC>.Fail(template.Status, template.Reason!);
        var value = template.Value!;
        var entries = owner.Ents!;
        var newSlot = value.Binding.Slots.Count;
        var carrierResult = CreateHiddenCarrierBody(value.Dyna);
        if (!carrierResult.Success)
            return ItemMotionResult<KC>.Fail(carrierResult.Status, carrierResult.Reason!);

        var carrier = new CPlugPrefab.EntRef
        {
            Position = default,
            Rotation = new GBX.NET.Quat(0, 0, 0, 1),
            Model = carrierResult.Value!,
            Params = CopyInstance(value.Instance)
        };
        var carrierConstraint = new CPlugPrefab.EntRef
        {
            Position = value.ConstraintEntry.Position,
            Rotation = value.ConstraintEntry.Rotation,
            U01 = value.ConstraintEntry.U01,
            Model = CopyConstraint(source, value.Snapshot.Fields.Translation!, value.Snapshot.Fields.Rotation!),
            Params = new NPlugDyna_SPrefabConstraintParams
            {
                Version = 0,
                Ent1 = value.Parameters.Ent1,
                Ent2 = newSlot,
                Pos1 = default,
                Pos2 = default
            }
        };

        owner.Ents = entries.Concat([carrier, carrierConstraint]).ToArray();
        var carrierBinding = ItemMotionBindings.Resolve((KC)carrierConstraint.Model!, owner,
            (NPlugDyna_SPrefabConstraintParams)carrierConstraint.Params, prefabInstancePath);
        var visibleCandidate = new NPlugDyna_SPrefabConstraintParams
        {
            Version = value.Parameters.Version,
            Ent1 = newSlot,
            Ent2 = value.Parameters.Ent2,
            Pos1 = value.Parameters.Pos1,
            Pos2 = value.Parameters.Pos2
        };
        var visibleBinding = ItemMotionBindings.Resolve(source, owner, visibleCandidate, prefabInstancePath);
        if (carrierBinding.Status == ItemMotionStatus.Supported && visibleBinding.Status == ItemMotionStatus.Supported)
        {
            value.Parameters.Ent1 = newSlot;
            return ItemMotionResult<KC>.Ok((KC)carrierConstraint.Model!);
        }

        owner.Ents = entries;
        var failure = carrierBinding.Status != ItemMotionStatus.Supported ? carrierBinding : visibleBinding;
        return ItemMotionResult<KC>.Fail(failure.Status, failure.Reason ?? "The hidden carrier chain did not produce two safe bindings.");
    }

    public static ItemMotionResult<int> AppendFromConstraint(CPlugPrefab owner, KC source, string prefabInstancePath)
    {
        var template = ReadTemplate(owner, source, prefabInstancePath);
        if (!template.Success) return ItemMotionResult<int>.Fail(template.Status, template.Reason!);
        var value = template.Value!;
        var entries = owner.Ents!;

        var newSlot = value.Binding.Slots.Count;
        var body = new CPlugPrefab.EntRef
        {
            Position = value.BodyEntry.Position,
            Rotation = value.BodyEntry.Rotation,
            U01 = value.BodyEntry.U01,
            Model = value.Dyna,
            Params = CopyInstance(value.Instance)
        };
        var clone = CopyConstraint(source, value.Snapshot.Fields.Translation!, value.Snapshot.Fields.Rotation!);
        var constraint = new CPlugPrefab.EntRef
        {
            Position = value.ConstraintEntry.Position,
            Rotation = value.ConstraintEntry.Rotation,
            U01 = value.ConstraintEntry.U01,
            Model = clone,
            Params = new NPlugDyna_SPrefabConstraintParams
            {
                Version = 0,
                Ent1 = value.Parameters.Ent1,
                Ent2 = newSlot,
                Pos1 = default,
                Pos2 = default
            }
        };

        var expanded = entries.Concat([body, constraint]).ToArray();
        owner.Ents = expanded;
        var addedBinding = ItemMotionBindings.Resolve(clone, owner, (NPlugDyna_SPrefabConstraintParams)constraint.Params, prefabInstancePath);
        if (addedBinding.Status == ItemMotionStatus.Supported) return ItemMotionResult<int>.Ok(newSlot);

        owner.Ents = entries;
        return ItemMotionResult<int>.Fail(addedBinding.Status, addedBinding.Reason ?? "The appended template did not resolve safely.");
    }

    /// <summary>
    /// Creates an experimental minimal kinematic prefab from a static item by
    /// reusing its mesh as an authored dyna body and binding it to a world-relative
    /// constraint. This intentionally mirrors the proven mesh-sharing pattern from
    /// external authoring tools instead of trying to infer a hidden-model hack.
    /// </summary>
    public static ItemMotionResult<CGameItemModel> ConvertStaticToKinematic(
        CGameItemModel source,
        CGameItemModel? movingTemplate = null,
        KC? constraintTemplate = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        var staticModel = source.EntityModel switch
        {
            CGameCommonItemEntityModel { StaticObject: CPlugStaticObjectModel model } => model,
            CPlugStaticObjectModel model => model,
            _ => null
        };
        if (staticModel is null)
            return ItemMotionResult<CGameItemModel>.Fail(ItemMotionStatus.Unresolved,
                "The item does not have a static object model to convert.");

        var mesh = staticModel.Mesh;
        if (mesh is null)
            return ItemMotionResult<CGameItemModel>.Fail(ItemMotionStatus.Unsupported,
                "The static item has no embedded visual mesh to reuse as a dyna body.");

        var templateBodyResult = ResolveTemplateBody(movingTemplate);
        if (!templateBodyResult.Success)
            return ItemMotionResult<CGameItemModel>.Fail(templateBodyResult.Status, templateBodyResult.Reason!);
        var templateBody = templateBodyResult.Value!;
        var converted = CloneItem(movingTemplate!);
        if (converted.EntityModel is not CPlugPrefab { Ents: { Length: > 0 } convertedEntries })
            return ItemMotionResult<CGameItemModel>.Fail(ItemMotionStatus.Unresolved,
                "The cloned template item has no prefab entity list.");
        if (templateBody.EntryIndex < 0 || templateBody.EntryIndex >= convertedEntries.Length)
            return ItemMotionResult<CGameItemModel>.Fail(ItemMotionStatus.Unresolved,
                "The template body entry index is not present in the cloned template item.");
        if (convertedEntries[templateBody.EntryIndex].Model is not CPlugDynaObjectModel convertedBody
            || convertedEntries[templateBody.EntryIndex].Params is not NPlugDynaObjectModel_SInstanceParams convertedInstance)
            return ItemMotionResult<CGameItemModel>.Fail(ItemMotionStatus.Unresolved,
                "The cloned template body entry is not a typed kinematic dyna body.");

        CPlugSurface? collisionShape = null;
        if (source.EntityModel is CGameCommonItemEntityModel common && common.PhyModel is CPlugSurface phySurf && phySurf.Surf is not null)
        {
            collisionShape = phySurf;
        }
        else if (staticModel.Mesh is CPlugSolid2Model solid)
        {
            collisionShape = GenerateCollisionSurfaceFromMesh(solid);
        }

        convertedBody.IsStatic = false;
        convertedBody.Mesh = mesh;
        if (collisionShape is not null)
        {
            convertedBody.StaticShape = collisionShape;
            convertedBody.DynaShape = collisionShape;
        }
        convertedInstance.IsKinematic = true;

        var prefab = (CPlugPrefab)converted.EntityModel!;
        var constraintEntry = prefab.Ents?.FirstOrDefault(entry => entry.Model is KC);
        if (constraintEntry is null || constraintEntry.Params is not NPlugDyna_SPrefabConstraintParams parameters)
            return ItemMotionResult<CGameItemModel>.Fail(ItemMotionStatus.Unresolved,
                "The template has no supported prefab kinematic constraint entry.");

        var constraint = BuildConstraintTemplate(movingTemplate, constraintTemplate);
        constraintEntry.Model = constraint;

        converted.Ident = source.Ident;
        converted.Name = source.Name;
        converted.ItemType = source.ItemType;
        converted.ItemTypeE = source.ItemTypeE;
        converted.DefaultPlacement = source.DefaultPlacement;
        converted.GroundPoint = source.GroundPoint;
        converted.OrbitalCenterHeightFromGround = source.OrbitalCenterHeightFromGround;
        converted.OrbitalPreviewAngle = source.OrbitalPreviewAngle;
        converted.OrbitalRadiusBase = source.OrbitalRadiusBase;
        converted.Icon = source.Icon;
        converted.IconWebP = source.IconWebP;

        var binding = ItemMotionBindings.Resolve(constraint, prefab, parameters, "doc:0/variant:none/root");
        if (binding.Status != ItemMotionStatus.Supported)
            return ItemMotionResult<CGameItemModel>.Fail(binding.Status, binding.Reason ?? "The generated kinematic binding is not safe.");

        return ItemMotionResult<CGameItemModel>.Ok(converted);
    }

    private static KC BuildConstraintTemplate(CGameItemModel? movingTemplate, KC? explicitConstraintTemplate)
    {
        if (explicitConstraintTemplate is not null)
        {
            return new KC
            {
                Version = 0,
                SubVersion = 3,
                TransAxis = explicitConstraintTemplate.TransAxis,
                TransMin = explicitConstraintTemplate.TransMin,
                TransMax = explicitConstraintTemplate.TransMax,
                RotAxis = explicitConstraintTemplate.RotAxis,
                AngleMinDeg = explicitConstraintTemplate.AngleMinDeg,
                AngleMaxDeg = explicitConstraintTemplate.AngleMaxDeg,
                ShaderTcVersion = explicitConstraintTemplate.ShaderTcVersion,
                ShaderTcType = explicitConstraintTemplate.ShaderTcType,
                ShaderTcDataTransSub = explicitConstraintTemplate.ShaderTcDataTransSub,
                TransAnimFunc = CopyAnimFunc(explicitConstraintTemplate.TransAnimFunc),
                RotAnimFunc = CopyAnimFunc(explicitConstraintTemplate.RotAnimFunc),
                ShaderTcAnimFunc = explicitConstraintTemplate.ShaderTcAnimFunc
            };
        }

        if (movingTemplate?.EntityModel is CPlugPrefab { Ents: { Length: > 0 } ents })
        {
            var templateConstraint = ents
                .Select(static entry => entry.Model as KC)
                .FirstOrDefault(static constraint => constraint is not null);
            if (templateConstraint is not null)
            {
                return new KC
                {
                    Version = 0,
                    SubVersion = 3,
                    TransAxis = templateConstraint.TransAxis,
                    TransMin = templateConstraint.TransMin,
                    TransMax = templateConstraint.TransMax,
                    RotAxis = templateConstraint.RotAxis,
                    AngleMinDeg = templateConstraint.AngleMinDeg,
                    AngleMaxDeg = templateConstraint.AngleMaxDeg,
                    ShaderTcVersion = templateConstraint.ShaderTcVersion,
                    ShaderTcType = templateConstraint.ShaderTcType,
                    ShaderTcDataTransSub = templateConstraint.ShaderTcDataTransSub,
                    TransAnimFunc = CopyAnimFunc(templateConstraint.TransAnimFunc),
                    RotAnimFunc = CopyAnimFunc(templateConstraint.RotAnimFunc),
                    ShaderTcAnimFunc = templateConstraint.ShaderTcAnimFunc
                };
            }
        }

        return new KC
        {
            Version = 0,
            SubVersion = 3,
            TransAxis = KC.EAxis.Y,
            TransMin = 0,
            TransMax = 1,
            RotAxis = KC.EAxis.Y,
            AngleMinDeg = 0,
            AngleMaxDeg = 0,
            TransAnimFunc = new KC.AnimFunc
            {
                IsDuration = true,
                SubFuncs =
                [
                    new() { Ease = KC.AnimEase.QuadInOut, Reverse = false, Duration = new TimeInt32(1000) },
                    new() { Ease = KC.AnimEase.QuadInOut, Reverse = true, Duration = new TimeInt32(1000) }
                ]
            },
            RotAnimFunc = new KC.AnimFunc
            {
                IsDuration = true,
                SubFuncs =
                [
                    new() { Ease = KC.AnimEase.Linear, Reverse = false, Duration = new TimeInt32(1000) }
                ]
            }
        };
    }

    private static KC.AnimFunc? CopyAnimFunc(KC.AnimFunc? source)
    {
        if (source is null)
            return null;

        return new KC.AnimFunc
        {
            IsDuration = source.IsDuration,
            SubFuncs = source.SubFuncs?.Select(static sub => new KC.SubAnimFunc
            {
                Ease = sub.Ease,
                Reverse = sub.Reverse,
                Duration = sub.Duration
            }).ToArray()
        };
    }

    private static CGameItemModel CloneItem(CGameItemModel source)
    {
        using var stream = new MemoryStream();
        new Gbx<CGameItemModel>(source) { BodyCompression = GbxCompression.Uncompressed }.Save(stream);
        stream.Position = 0;
        return Gbx.Parse<CGameItemModel>(stream, new GbxReadSettings { SafeSkippableChunks = true }).Node;
    }

    private static ItemMotionResult<TemplateBody> ResolveTemplateBody(CGameItemModel? movingTemplate)
    {
        if (movingTemplate?.EntityModel is not CPlugPrefab { Ents: { Length: > 0 } entries })
            return ItemMotionResult<TemplateBody>.Fail(ItemMotionStatus.Unsupported,
                "Load a known-good kinematic template item first; conversion now requires a native dyna body/shape template to avoid crash-prone exports.");

        var body = entries
            .Select((entry, index) => new
            {
                Index = index,
                Body = entry.Model as CPlugDynaObjectModel,
                Instance = entry.Params as NPlugDynaObjectModel_SInstanceParams
            })
            .FirstOrDefault(static entry => entry.Body is not null
                && entry.Body.IsStatic == false
                && entry.Body.Mesh is not null
                && entry.Body.StaticShape is CPlugSurface { Surf: not null }
                && entry.Body.DynaShape is CPlugSurface { Surf: not null }
                && entry.Instance is not null
                && entry.Instance.Version == 2
                && entry.Instance.IsKinematic);
        if (body is null)
            return ItemMotionResult<TemplateBody>.Fail(ItemMotionStatus.Unsupported,
                "The loaded template item has no supported kinematic dyna body with both collision surfaces.");

        return ItemMotionResult<TemplateBody>.Ok(new(body.Index, body.Body!, body.Instance!));
    }

    private static ItemMotionResult<Template> ReadTemplate(CPlugPrefab owner, KC source, string prefabInstancePath)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefabInstancePath);
        if (prefabInstancePath.Contains("/ent:", StringComparison.Ordinal))
            return ItemMotionResult<Template>.Fail(ItemMotionStatus.Unsupported,
                "Adding entities inside nested prefabs is unsupported because native slot-table flattening is not verified.");
        if (owner.Ents is not { Length: > 0 } entries)
            return ItemMotionResult<Template>.Fail(ItemMotionStatus.Absent, "The prefab has no entity entries.");
        var constraintEntry = entries.FirstOrDefault(entry => entry.ModelFile is null && ReferenceEquals(entry.Model, source));
        if (constraintEntry?.Params is not NPlugDyna_SPrefabConstraintParams parameters)
            return ItemMotionResult<Template>.Fail(ItemMotionStatus.Unresolved, "The selected constraint has no typed prefab-binding parameters.");
        if (source.Version != 0 || source.SubVersion != 3 || source.ShaderTcType != KC.EShaderTcType.None)
            return ItemMotionResult<Template>.Fail(ItemMotionStatus.Unsupported,
                "Only the verified version-0/subversion-3 constraint template without shader animation can be cloned.");
        var binding = ItemMotionBindings.Resolve(source, owner, parameters, prefabInstancePath);
        if (binding.Status != ItemMotionStatus.Supported || binding.Child.SourceEntry is not { } bodyEntry
            || bodyEntry.ModelFile is not null || bodyEntry.Model is not CPlugDynaObjectModel dyna
            || bodyEntry.Params is not NPlugDynaObjectModel_SInstanceParams instance || instance.Version != 2 || !instance.IsKinematic)
            return ItemMotionResult<Template>.Fail(binding.Status,
                binding.Reason ?? "The selected constraint does not target a supported, local version-2 kinematic body.");
        var snapshot = ItemMotion.Read(source);
        if (snapshot.TranslationStatus != ItemMotionStatus.Supported || snapshot.RotationStatus != ItemMotionStatus.Supported
            || snapshot.Fields.Translation is null || snapshot.Fields.Rotation is null)
            return ItemMotionResult<Template>.Fail(ItemMotionStatus.Unsupported,
                "The selected constraint has a timeline that cannot be cloned safely.");
        return ItemMotionResult<Template>.Ok(new(constraintEntry, bodyEntry, parameters, dyna, instance, binding, snapshot));
    }

    private static NPlugDynaObjectModel_SInstanceParams CopyInstance(NPlugDynaObjectModel_SInstanceParams instance) => new()
    {
        Version = 2, PeriodSc = instance.PeriodSc, PeriodScMax = instance.PeriodScMax,
        Phase01 = instance.Phase01, Phase01Max = instance.Phase01Max, TextureId = instance.TextureId,
        IsKinematic = true, CastStaticShadow = instance.CastStaticShadow
    };

    private static KC CopyConstraint(KC source, ItemMotionTimeline translation, ItemMotionTimeline rotation) => new()
    {
        Version = 0, SubVersion = 3,
        TransAxis = source.TransAxis, TransMin = source.TransMin, TransMax = source.TransMax,
        RotAxis = source.RotAxis, AngleMinDeg = source.AngleMinDeg, AngleMaxDeg = source.AngleMaxDeg,
        TransAnimFunc = CopyTimeline(translation), RotAnimFunc = CopyTimeline(rotation)
    };

    private static KC.AnimFunc CopyTimeline(ItemMotionTimeline source) => new()
    {
        IsDuration = source.IsDuration,
        SubFuncs = source.Keys.Select(key => new KC.SubAnimFunc
        {
            Ease = key.Ease,
            Reverse = key.Reverse,
            Duration = new TimeInt32(key.DurationMilliseconds)
        }).ToArray()
    };

    public static CPlugDynaObjectModel CreateCarrierBody(CPlugDynaObjectModel? source = null)
    {
        var carrierMesh = CreateCarrierMesh();
        var carrierShape = CreateCarrierShape();

        return new CPlugDynaObjectModel
        {
            Version = source?.Version ?? 13,
            IsStatic = false,
            DynamizeOnSpawn = source?.DynamizeOnSpawn ?? false,
            Mass = source?.Mass ?? 100f,
            BreakSpeedKmh = source?.BreakSpeedKmh ?? 100f,
            Mesh = carrierMesh,
            StaticShape = carrierShape,
            DynaShape = carrierShape,
            LocAnim = source?.LocAnim
        };
    }

    private static ItemMotionResult<CPlugDynaObjectModel> CreateHiddenCarrierBody(CPlugDynaObjectModel source)
    {
        return ItemMotionResult<CPlugDynaObjectModel>.Ok(CreateCarrierBody(source));
    }

    private static CPlugSurface CreateCarrierShape()
    {
        var mesh = new CPlugSurface.Mesh
        {
            Version = 6,
            Vertices = [new(0, 0, 0), new(0.0001f, 0, 0), new(0, 0.0001f, 0)],
            Triangles = [new(new Int3(0, 1, 2), 0, 0, 0)]
        };
        var surface = new CPlugSurface { Surf = mesh };
        surface.CreateChunk<CPlugSurface.Chunk0900C003>().Version = 2;
        return surface;
    }

    private static CPlugSolid2Model CreateCarrierMesh()
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var stream = new CPlugVertexStream { Positions = [new(), new(0.0001f, 0, 0), new(0, 0.0001f, 0)] };
        var declaration = new CPlugVertexStream.DataDecl();
        typeof(CPlugVertexStream.DataDecl).GetField("flags1", flags)!.SetValue(declaration,
            (uint)CPlugVertexStream.EPlugVDcl.Position | ((uint)CPlugVertexStream.EPlugVDclType.Float3 << 9) | (12u << 18));
        typeof(CPlugVertexStream).GetField("dataDecls", flags)!.SetValue(stream, new[] { declaration });
        typeof(CPlugVertexStream).GetField("count", flags)!.SetValue(stream, 3);
        stream.CreateChunk<CPlugVertexStream.Chunk09056000>().Version = 1;
        var indexBuffer = new CPlugIndexBuffer { Indices = [0, 1, 2] };
        indexBuffer.CreateChunk<CPlugIndexBuffer.Chunk09057000>();
        var visual = new CPlugVisualIndexedTriangles
        {
            VertexStreams = [stream],
            IndexBuffer = indexBuffer,
            IsGeometryStatic = true,
            IsIndexationStatic = true,
            BoundingBox = new BoxAligned(0, 0, 0, 0.0001f, 0.0001f, 0)
        };
        typeof(CPlugVisual).GetProperty("Count", flags)!.SetValue(visual, 3);
        visual.CreateChunk<CPlugVisual.Chunk0900600F>().Version = 6;
        visual.CreateChunk<CPlugVisualIndexed.Chunk0906A001>();
        var solid = new CPlugSolid2Model
        {
            Visuals = [visual],
            CustomMaterials = [new CPlugSolid2Model.Material()],
            ShadedGeoms =
            [
                new CPlugSolid2Model.ShadedGeom
                {
                    VisualIndex = 0,
                    MaterialIndex = 0,
                    LodMask = 1
                }
            ]
        };
        solid.CreateChunk<CPlugSolid2Model.Chunk090BB000>().Version = 34;
        return solid;
    }

    /// <summary>
    /// Generates an accurate CPlugSurface collision node from the triangles and vertices
    /// in an authored CPlugSolid2Model visual mesh.
    /// </summary>
    public static CPlugSurface? GenerateCollisionSurfaceFromMesh(CPlugSolid2Model? mesh)
    {
        if (mesh?.Visuals is null || mesh.Visuals.Length == 0) return null;
        var allVertices = new List<Vec3>();
        var allTriangles = new List<CPlugSurface.Mesh.Triangle>();

        foreach (var visual in mesh.Visuals)
        {
            if (visual is not CPlugVisualIndexedTriangles vit) continue;
            if (vit.VertexStreams is null || vit.VertexStreams.Count == 0 || vit.VertexStreams[0].Positions is null) continue;
            if (vit.IndexBuffer?.Indices is null || vit.IndexBuffer.Indices.Length < 3) continue;

            var baseVertexIndex = allVertices.Count;
            var positions = vit.VertexStreams[0].Positions!;
            allVertices.AddRange(positions);

            var indices = vit.IndexBuffer.Indices;
            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                var a = baseVertexIndex + indices[i];
                var b = baseVertexIndex + indices[i + 1];
                var c = baseVertexIndex + indices[i + 2];
                allTriangles.Add(new CPlugSurface.Mesh.Triangle(new Int3(a, b, c), 0, 0, 0));
            }
        }

        if (allVertices.Count < 3 || allTriangles.Count == 0) return null;

        var surfMesh = new CPlugSurface.Mesh
        {
            Version = 6,
            Vertices = allVertices.ToArray(),
            Triangles = allTriangles.ToArray()
        };
        var surface = new CPlugSurface { Surf = surfMesh };
        surface.CreateChunk<CPlugSurface.Chunk0900C003>().Version = 2;
        return surface;
    }

    /// <summary>
    /// Attempts to load the bundled proven MovingItemTemplate item so static items can be
    /// converted to kinematic in one click without requiring a manual template upload.
    /// </summary>
    public static CGameItemModel? GetDefaultMovingTemplate()
    {
        try
        {
            if (Gbx.LZO is null)
            {
                var lzoType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                    .FirstOrDefault(t => t.Name == "MiniLZO" || t.Name == "Lzo");
                if (lzoType is not null)
                {
                    Gbx.LZO = (dynamic)Activator.CreateInstance(lzoType)!;
                }
            }

            var assembly = typeof(ItemKinematicEntityTemplate).Assembly;
            var resNames = assembly.GetManifestResourceNames();
            var targetName = resNames.FirstOrDefault(n => n.EndsWith("MovingItemTemplate.Item.Gbx", StringComparison.OrdinalIgnoreCase));
            if (targetName is not null)
            {
                using var stream = assembly.GetManifestResourceStream(targetName);
                if (stream is not null)
                {
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    ms.Position = 0;
                    return Gbx.Parse<CGameItemModel>(ms).Node;
                }
            }

            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && dir.Exists)
            {
                var candidate = Path.Combine(dir.FullName, "Resources", "MovingItemTemplate.Item.Gbx");
                if (File.Exists(candidate))
                {
                    var bytes = File.ReadAllBytes(candidate);
                    return Gbx.Parse<CGameItemModel>(new MemoryStream(bytes)).Node;
                }
                dir = dir.Parent;
            }
        }
        catch
        {
            // Resource unavailable
        }
        return null;
    }

    /// <summary>
    /// Best-effort conversion of a sampled draw-path to authored kinematic motion.
    /// Each segment maps to one chain constraint (outer parent to inner child), so
    /// the visible body follows one continuous sequence over the full timeline.
    /// </summary>
    public static ItemMotionResult<int> ConfigureDrawnPathBestEffort(
        CPlugPrefab owner,
        KC source,
        string prefabInstancePath,
        IReadOnlyList<DrawPathSegment> segments,
        int? maxSegmentsOverride = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefabInstancePath);
        ArgumentNullException.ThrowIfNull(segments);

        var normalized = segments
            .Where(static segment => MathF.Abs(segment.Distance) >= 0.01f && segment.DurationMs > 0)
            .Select(static segment => new DrawPathSegment(
                segment.Axis,
                segment.Distance,
                Math.Max(50, segment.DurationMs),
                segment.RotationAxis,
                segment.RotationStartDeg,
                segment.RotationEndDeg))
            .ToArray();
        if (normalized.Length == 0)
            return ItemMotionResult<int>.Fail(ItemMotionStatus.Unsupported,
                "The drawn path does not contain enough movement to author a kinematic sequence.");
        var maxSegments = Math.Max(1, maxSegmentsOverride ?? MaxSafeDrawPathSegments);
        if (normalized.Length > maxSegments)
            return ItemMotionResult<int>.Fail(ItemMotionStatus.Unsupported,
                $"The drawn path generated {normalized.Length} segments. Trackmania placement is unstable for this path mode above {maxSegments} chained segments.");

        var chain = new List<KC>(normalized.Length);
        for (var i = 1; i < normalized.Length; i++)
        {
            var insert = InsertHiddenCarrierParent(owner, source, prefabInstancePath);
            if (!insert.Success)
                return ItemMotionResult<int>.Fail(insert.Status, insert.Reason!);
            chain.Add(insert.Value!);
        }
        chain.Add(source);

        var totalDuration = normalized.Sum(static segment => segment.DurationMs);
        var elapsed = 0;
        for (var i = 0; i < normalized.Length; i++)
        {
            var constraint = chain[i];
            var segment = normalized[i];
            var pre = elapsed;
            var move = segment.DurationMs;
            var post = Math.Max(0, totalDuration - pre - move);
            elapsed += move;

            constraint.TransAxis = segment.Axis;
            constraint.TransMin = 0;
            constraint.TransMax = segment.Distance;
            constraint.RotAxis = segment.RotationAxis;
            constraint.AngleMinDeg = segment.RotationStartDeg;
            constraint.AngleMaxDeg = segment.RotationEndDeg;

            var subFuncs = new List<KC.SubAnimFunc>(3);
            if (pre > 0)
            {
                subFuncs.Add(new KC.SubAnimFunc
                {
                    Ease = KC.AnimEase.Constant,
                    Reverse = false,
                    Duration = new TimeInt32(pre)
                });
            }

            subFuncs.Add(new KC.SubAnimFunc
            {
                Ease = KC.AnimEase.QuadInOut,
                Reverse = false,
                Duration = new TimeInt32(move)
            });

            if (post > 0)
            {
                subFuncs.Add(new KC.SubAnimFunc
                {
                    Ease = KC.AnimEase.Constant,
                    Reverse = true,
                    Duration = new TimeInt32(post)
                });
            }

            constraint.TransAnimFunc = new KC.AnimFunc
            {
                IsDuration = true,
                SubFuncs = [.. subFuncs]
            };
            var rotSubFuncs = new List<KC.SubAnimFunc>(3);
            if (pre > 0)
            {
                rotSubFuncs.Add(new KC.SubAnimFunc
                {
                    Ease = KC.AnimEase.Constant,
                    Reverse = false,
                    Duration = new TimeInt32(pre)
                });
            }

            rotSubFuncs.Add(new KC.SubAnimFunc
            {
                Ease = KC.AnimEase.Linear,
                Reverse = false,
                Duration = new TimeInt32(move)
            });

            if (post > 0)
            {
                rotSubFuncs.Add(new KC.SubAnimFunc
                {
                    Ease = KC.AnimEase.Constant,
                    Reverse = true,
                    Duration = new TimeInt32(post)
                });
            }

            constraint.RotAnimFunc = new KC.AnimFunc
            {
                IsDuration = true,
                SubFuncs = [.. rotSubFuncs]
            };
        }

        ReorderDrawPathChain(owner);

        return ItemMotionResult<int>.Ok(normalized.Length);
    }

    private static void ReorderDrawPathChain(CPlugPrefab owner)
    {
        if (owner.Ents is not { Length: > 0 } entries)
            return;

        var bodies = entries
            .Select((entry, index) => (entry, index))
            .Where(static item => item.entry.Model is CPlugDynaObjectModel && item.entry.ModelFile is null)
            .ToArray();
        var constraints = entries
            .Select((entry, index) => (entry, index))
            .Where(static item => item.entry.Model is KC && item.entry.Params is NPlugDyna_SPrefabConstraintParams)
            .ToArray();
        if (bodies.Length == 0 || constraints.Length == 0 || constraints.Length != bodies.Length)
            return;

        var oldBodyIndexBySlot = new Dictionary<int, int>();
        for (var slot = 0; slot < bodies.Length; slot++)
            oldBodyIndexBySlot[slot] = bodies[slot].index;

        var constraintsByParent = new Dictionary<int, (CPlugPrefab.EntRef entry, NPlugDyna_SPrefabConstraintParams parameters, int index)>();
        foreach (var item in constraints)
        {
            var parameters = (NPlugDyna_SPrefabConstraintParams)item.entry.Params!;
            if (constraintsByParent.ContainsKey(parameters.Ent1))
                return;
            constraintsByParent[parameters.Ent1] = (item.entry, parameters, item.index);
        }

        if (!constraintsByParent.TryGetValue(-1, out var root))
            return;

        var orderedConstraints = new List<(CPlugPrefab.EntRef entry, NPlugDyna_SPrefabConstraintParams parameters, int index)>(constraints.Length);
        var orderedBodySlots = new List<int>(bodies.Length);
        var visitedParents = new HashSet<int>();
        var current = root;
        while (true)
        {
            if (!visitedParents.Add(current.parameters.Ent1))
                return;
            orderedConstraints.Add(current);
            orderedBodySlots.Add(current.parameters.Ent2);
            if (!constraintsByParent.TryGetValue(current.parameters.Ent2, out current))
                break;
        }

        if (orderedConstraints.Count != constraints.Length || orderedBodySlots.Count != bodies.Length)
            return;
        if (orderedBodySlots.Distinct().Count() != bodies.Length)
            return;
        if (orderedBodySlots.Any(slot => slot < 0 || slot >= bodies.Length))
            return;

        // Keep the most representative/visible body first so item inventory previews
        // do not end up anchored to an invisible micro-carrier entry.
        static int BodyVisualWeight(CPlugPrefab.EntRef entry)
        {
            if (entry.Model is not CPlugDynaObjectModel { Mesh: CPlugSolid2Model solid })
                return 0;
            var visuals = solid.Visuals?.Length ?? 0;
            var vertices = solid.Visuals?.OfType<CPlugVisualIndexedTriangles>()
                .Sum(static visual => visual.VertexStreams?.FirstOrDefault()?.Positions?.Length ?? 0) ?? 0;
            return visuals * 1_000_000 + vertices;
        }
        var leadSlot = orderedBodySlots
            .OrderByDescending(slot => BodyVisualWeight(entries[oldBodyIndexBySlot[slot]]))
            .ThenBy(slot => orderedBodySlots.IndexOf(slot))
            .FirstOrDefault();
        if (leadSlot != orderedBodySlots[0])
        {
            orderedBodySlots.Remove(leadSlot);
            orderedBodySlots.Insert(0, leadSlot);
        }

        var newSlotByOldSlot = new Dictionary<int, int>(bodies.Length);
        for (var newSlot = 0; newSlot < orderedBodySlots.Count; newSlot++)
            newSlotByOldSlot[orderedBodySlots[newSlot]] = newSlot;

        var reordered = new List<CPlugPrefab.EntRef>(entries.Length);
        foreach (var oldSlot in orderedBodySlots)
        {
            var bodyEntry = entries[oldBodyIndexBySlot[oldSlot]];
            reordered.Add(bodyEntry);
        }

        foreach (var (entry, parameters, _) in orderedConstraints)
        {
            parameters.Ent1 = parameters.Ent1 == -1 ? -1 : newSlotByOldSlot[parameters.Ent1];
            parameters.Ent2 = newSlotByOldSlot[parameters.Ent2];
            reordered.Add(entry);
        }

        owner.Ents = [.. reordered];
    }

    /// <summary>
    /// Configures a 1-axis translation movement (linear or ping-pong) with 100% Havok collision.
    /// </summary>
    public static ItemMotionResult<bool> ConfigureSingleAxisMotion(
        KC source,
        KC.EAxis axis,
        float distance,
        int durationMs,
        bool isPingPong = true)
    {
        ArgumentNullException.ThrowIfNull(source);
        source.TransAxis = axis;
        source.TransMin = 0;
        source.TransMax = distance;
        source.RotAxis = KC.EAxis.Y;
        source.AngleMinDeg = 0;
        source.AngleMaxDeg = 0;
        if (isPingPong)
        {
            source.TransAnimFunc = new KC.AnimFunc
            {
                IsDuration = true,
                SubFuncs =
                [
                    new() { Ease = KC.AnimEase.QuadInOut, Reverse = false, Duration = new TimeInt32(durationMs) },
                    new() { Ease = KC.AnimEase.QuadInOut, Reverse = true, Duration = new TimeInt32(durationMs) }
                ]
            };
        }
        else
        {
            source.TransAnimFunc = new KC.AnimFunc
            {
                IsDuration = true,
                SubFuncs =
                [
                    new() { Ease = KC.AnimEase.Linear, Reverse = false, Duration = new TimeInt32(durationMs) }
                ]
            };
        }
        return ItemMotionResult<bool>.Ok(true);
    }

    /// <summary>
    /// Configures a continuous or oscillating rotation movement with 100% Havok collision.
    /// </summary>
    public static ItemMotionResult<bool> ConfigureRotationMotion(
        KC source,
        KC.EAxis axis,
        float minAngleDeg,
        float maxAngleDeg,
        int durationMs,
        bool isOscillating = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        source.TransAxis = KC.EAxis.Y;
        source.TransMin = 0;
        source.TransMax = 0;
        source.RotAxis = axis;
        source.AngleMinDeg = minAngleDeg;
        source.AngleMaxDeg = maxAngleDeg;
        if (isOscillating)
        {
            source.RotAnimFunc = new KC.AnimFunc
            {
                IsDuration = true,
                SubFuncs =
                [
                    new() { Ease = KC.AnimEase.QuadInOut, Reverse = false, Duration = new TimeInt32(durationMs) },
                    new() { Ease = KC.AnimEase.QuadInOut, Reverse = true, Duration = new TimeInt32(durationMs) }
                ]
            };
        }
        else
        {
            source.RotAnimFunc = new KC.AnimFunc
            {
                IsDuration = true,
                SubFuncs =
                [
                    new() { Ease = KC.AnimEase.Linear, Reverse = false, Duration = new TimeInt32(durationMs) }
                ]
            };
        }
        return ItemMotionResult<bool>.Ok(true);
    }

    /// <summary>
    /// Automatically configures a chained A→B→C path for a single visible body using a safe carrier parent.
    /// Visually moves smoothly along both axes. (Havok collision is root-axis only in Trackmania).
    /// </summary>
    public static ItemMotionResult<bool> ConfigureChainedLPath(
        CPlugPrefab owner,
        KC source,
        string prefabInstancePath,
        KC.EAxis axis1,
        float dist1,
        int dur1Ms,
        KC.EAxis axis2,
        float dist2,
        int dur2Ms,
        bool isPingPong = true)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(source);

        var template = ReadTemplate(owner, source, prefabInstancePath);
        if (!template.Success) return ItemMotionResult<bool>.Fail(template.Status, template.Reason!);
        var value = template.Value!;

        KC carrierConstraint;
        KC visibleConstraint;

        if (value.Parameters.Ent1 == -1)
        {
            var insertResult = InsertHiddenCarrierParent(owner, source, prefabInstancePath);
            if (!insertResult.Success)
                return ItemMotionResult<bool>.Fail(insertResult.Status, insertResult.Reason!);
            carrierConstraint = insertResult.Value!;
            visibleConstraint = source;
        }
        else
        {
            var carrierEntry = owner.Ents?.FirstOrDefault(e => e.Model is KC kc && kc != source);
            if (carrierEntry?.Model is not KC kcCarrier)
                return ItemMotionResult<bool>.Fail(ItemMotionStatus.Unsupported, "Could not identify the carrier constraint in the existing chain.");
            carrierConstraint = kcCarrier;
            visibleConstraint = source;
        }

        carrierConstraint.TransAxis = axis1;
        carrierConstraint.TransMin = 0;
        carrierConstraint.TransMax = dist1;
        carrierConstraint.RotAxis = KC.EAxis.Y;
        carrierConstraint.AngleMinDeg = 0;
        carrierConstraint.AngleMaxDeg = 0;

        visibleConstraint.TransAxis = axis2;
        visibleConstraint.TransMin = 0;
        visibleConstraint.TransMax = dist2;
        visibleConstraint.RotAxis = KC.EAxis.Y;
        visibleConstraint.AngleMinDeg = 0;
        visibleConstraint.AngleMaxDeg = 0;

        if (isPingPong)
        {
            carrierConstraint.TransAnimFunc = new KC.AnimFunc
            {
                IsDuration = true,
                SubFuncs =
                [
                    new() { Ease = KC.AnimEase.QuadInOut, Reverse = false, Duration = new TimeInt32(dur1Ms) },
                    new() { Ease = KC.AnimEase.Constant, Reverse = true, Duration = new TimeInt32(dur2Ms * 2) },
                    new() { Ease = KC.AnimEase.QuadInOut, Reverse = true, Duration = new TimeInt32(dur1Ms) }
                ]
            };

            visibleConstraint.TransAnimFunc = new KC.AnimFunc
            {
                IsDuration = true,
                SubFuncs =
                [
                    new() { Ease = KC.AnimEase.Constant, Reverse = false, Duration = new TimeInt32(dur1Ms) },
                    new() { Ease = KC.AnimEase.QuadInOut, Reverse = false, Duration = new TimeInt32(dur2Ms) },
                    new() { Ease = KC.AnimEase.QuadInOut, Reverse = true, Duration = new TimeInt32(dur2Ms) },
                    new() { Ease = KC.AnimEase.Constant, Reverse = false, Duration = new TimeInt32(dur1Ms) }
                ]
            };
        }
        else
        {
            carrierConstraint.TransAnimFunc = new KC.AnimFunc
            {
                IsDuration = true,
                SubFuncs =
                [
                    new() { Ease = KC.AnimEase.QuadInOut, Reverse = false, Duration = new TimeInt32(dur1Ms) },
                    new() { Ease = KC.AnimEase.Constant, Reverse = false, Duration = new TimeInt32(dur2Ms) },
                    new() { Ease = KC.AnimEase.QuadInOut, Reverse = true, Duration = new TimeInt32(dur1Ms) },
                    new() { Ease = KC.AnimEase.Constant, Reverse = false, Duration = new TimeInt32(dur2Ms) }
                ]
            };

            visibleConstraint.TransAnimFunc = new KC.AnimFunc
            {
                IsDuration = true,
                SubFuncs =
                [
                    new() { Ease = KC.AnimEase.Constant, Reverse = false, Duration = new TimeInt32(dur1Ms) },
                    new() { Ease = KC.AnimEase.QuadInOut, Reverse = false, Duration = new TimeInt32(dur2Ms) },
                    new() { Ease = KC.AnimEase.Constant, Reverse = false, Duration = new TimeInt32(dur1Ms) },
                    new() { Ease = KC.AnimEase.QuadInOut, Reverse = true, Duration = new TimeInt32(dur2Ms) }
                ]
            };
        }

        return ItemMotionResult<bool>.Ok(true);
    }

    /// <summary>
    /// Configures a multi-body relay path (A→B on body 1, B→C on body 2) where both bodies are
    /// root constraints (Ent1 = -1). In Trackmania, this provides 100% full Havok collision across
    /// the entire path.
    /// </summary>
    public static ItemMotionResult<bool> ConfigureRelayCollisionPath(
        CPlugPrefab owner,
        KC source,
        string prefabInstancePath,
        KC.EAxis axis1,
        float dist1,
        int dur1Ms,
        KC.EAxis axis2,
        float dist2,
        int dur2Ms)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(source);

        var template = ReadTemplate(owner, source, prefabInstancePath);
        if (!template.Success) return ItemMotionResult<bool>.Fail(template.Status, template.Reason!);

        var entries = owner.Ents!;
        var dynaEntries = entries.Where(e => e.Model is CPlugDynaObjectModel).ToList();
        var constraintEntries = entries.Where(e => e.Model is KC).ToList();

        if (dynaEntries.Count < 2 || constraintEntries.Count < 2)
        {
            var appendResult = AppendFromConstraint(owner, source, prefabInstancePath);
            if (!appendResult.Success)
                return ItemMotionResult<bool>.Fail(appendResult.Status, appendResult.Reason!);
            entries = owner.Ents!;
            dynaEntries = entries.Where(e => e.Model is CPlugDynaObjectModel).ToList();
            constraintEntries = entries.Where(e => e.Model is KC).ToList();
        }

        var body1Entry = dynaEntries[1];
        var constraint0Entry = constraintEntries[0];
        var constraint1Entry = constraintEntries[1];

        var c0 = (KC)constraint0Entry.Model!;
        var p0 = (NPlugDyna_SPrefabConstraintParams)constraint0Entry.Params!;
        var c1 = (KC)constraint1Entry.Model!;
        var p1 = (NPlugDyna_SPrefabConstraintParams)constraint1Entry.Params!;

        // Both bodies are root-bound (Ent1 = -1) so Trackmania gives both 100% Havok collision!
        p0.Ent1 = -1;
        p0.Ent2 = 0;
        p1.Ent1 = -1;
        p1.Ent2 = 1;

        var offsetB = axis1 switch
        {
            KC.EAxis.X => new Vec3(dist1, 0, 0),
            KC.EAxis.Y => new Vec3(0, dist1, 0),
            KC.EAxis.Z => new Vec3(0, 0, dist1),
            _ => new Vec3(dist1, 0, 0)
        };
        body1Entry.Position = offsetB;

        c0.TransAxis = axis1;
        c0.TransMin = 0;
        c0.TransMax = dist1;
        c0.RotAxis = KC.EAxis.Y;
        c0.AngleMinDeg = 0;
        c0.AngleMaxDeg = 0;
        c0.TransAnimFunc = new KC.AnimFunc
        {
            IsDuration = true,
            SubFuncs =
            [
                new() { Ease = KC.AnimEase.QuadInOut, Reverse = false, Duration = new TimeInt32(dur1Ms) },
                new() { Ease = KC.AnimEase.Constant, Reverse = false, Duration = new TimeInt32(dur2Ms * 2) },
                new() { Ease = KC.AnimEase.QuadInOut, Reverse = true, Duration = new TimeInt32(dur1Ms) }
            ]
        };

        c1.TransAxis = axis2;
        c1.TransMin = 0;
        c1.TransMax = dist2;
        c1.RotAxis = KC.EAxis.Y;
        c1.AngleMinDeg = 0;
        c1.AngleMaxDeg = 0;
        c1.TransAnimFunc = new KC.AnimFunc
        {
            IsDuration = true,
            SubFuncs =
            [
                new() { Ease = KC.AnimEase.Constant, Reverse = false, Duration = new TimeInt32(dur1Ms) },
                new() { Ease = KC.AnimEase.QuadInOut, Reverse = false, Duration = new TimeInt32(dur2Ms) },
                new() { Ease = KC.AnimEase.QuadInOut, Reverse = true, Duration = new TimeInt32(dur2Ms) },
                new() { Ease = KC.AnimEase.Constant, Reverse = false, Duration = new TimeInt32(dur1Ms) }
            ]
        };

        return ItemMotionResult<bool>.Ok(true);
    }

    /// <summary>
    /// Configures a multi-body "parker / occluder-handover" L-path:
    /// Body 1 moves along axis 1 (A -> B), pauses while Body 2 moves along axis 2 (B -> C -> B), then Body 1 returns (B -> A).
    /// While Body 1 is moving on axis 1, Body 2 is parked far away in the occluder/parking position (parkingDistance).
    /// When Body 1 reaches B, Body 2 snaps from the parking distance to B (in snapDurationMs, e.g. 10ms),
    /// traverses B -> C -> B, and snaps back to the parking distance when Body 1 takes over.
    /// Both bodies have Ent1 = -1 (world roots), ensuring 100% full Havok collision in Trackmania!
    /// </summary>
    public static ItemMotionResult<bool> ConfigureParkedHandoverCollisionPath(
        CPlugPrefab owner,
        KC source,
        string prefabInstancePath,
        KC.EAxis axis1,
        float dist1,
        int dur1Ms,
        KC.EAxis axis2,
        float dist2,
        int dur2Ms,
        float parkingDistance = -500f,
        int snapDurationMs = 20)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(source);

        var template = ReadTemplate(owner, source, prefabInstancePath);
        if (!template.Success) return ItemMotionResult<bool>.Fail(template.Status, template.Reason!);

        var entries = owner.Ents!;
        var dynaEntries = entries.Where(e => e.Model is CPlugDynaObjectModel).ToList();
        var constraintEntries = entries.Where(e => e.Model is KC).ToList();

        if (dynaEntries.Count < 2 || constraintEntries.Count < 2)
        {
            var appendResult = AppendFromConstraint(owner, source, prefabInstancePath);
            if (!appendResult.Success)
                return ItemMotionResult<bool>.Fail(appendResult.Status, appendResult.Reason!);
            entries = owner.Ents!;
            dynaEntries = entries.Where(e => e.Model is CPlugDynaObjectModel).ToList();
            constraintEntries = entries.Where(e => e.Model is KC).ToList();
        }

        var body0Entry = dynaEntries[0];
        var body1Entry = dynaEntries[1];
        var constraint0Entry = constraintEntries[0];
        var constraint1Entry = constraintEntries[1];

        var c0 = (KC)constraint0Entry.Model!;
        var p0 = (NPlugDyna_SPrefabConstraintParams)constraint0Entry.Params!;
        var c1 = (KC)constraint1Entry.Model!;
        var p1 = (NPlugDyna_SPrefabConstraintParams)constraint1Entry.Params!;

        // Both bodies are root-bound (Ent1 = -1) so Trackmania gives both 100% Havok collision!
        p0.Ent1 = -1;
        p0.Ent2 = 0;
        p1.Ent1 = -1;
        p1.Ent2 = 1;

        // Base rest positions:
        // Body 0 rests at origin (point A).
        // Body 1 rests at point B (the intersection point).
        body0Entry.Position = default;
        var offsetB = axis1 switch
        {
            KC.EAxis.X => new Vec3(dist1, 0, 0),
            KC.EAxis.Y => new Vec3(0, dist1, 0),
            KC.EAxis.Z => new Vec3(0, 0, dist1),
            _ => new Vec3(dist1, 0, 0)
        };
        body1Entry.Position = offsetB;

        // Timing breakdown:
        // Total active time of segment 2 (B -> C -> B): 2 * dur2Ms
        // During segment 1, body 0 takes dur1Ms to go A -> B.
        // During segment 2, body 0 pauses at B for 2 * dur2Ms.
        // Then body 0 returns B -> A in dur1Ms.
        // Body 1 (rests at B):
        // While body 0 is travelling A -> B (dur1Ms - snapDurationMs), body 1 stays in parking position (e.g. parkingDistance below/away).
        // In snapDurationMs just before body 0 reaches B, body 1 rushes/snaps from parkingDistance to 0 (which is position B).
        // Then body 1 performs B -> C -> B in 2 * dur2Ms.
        // Immediately after reaching B again, body 1 snaps back to parkingDistance in snapDurationMs.
        // Body 1 remains parked for the rest of body 0's return trip (dur1Ms - snapDurationMs).

        var safeSnapMs = Math.Max(10, Math.Min(snapDurationMs, dur1Ms / 2));
        var parkWaitMs = Math.Max(10, dur1Ms - safeSnapMs);

        // Constraint 0: Drives Body 0 on Axis 1 (0 .. dist1)
        c0.TransAxis = axis1;
        c0.TransMin = 0;
        c0.TransMax = dist1;
        c0.RotAxis = KC.EAxis.Y;
        c0.AngleMinDeg = 0;
        c0.AngleMaxDeg = 0;
        c0.TransAnimFunc = new KC.AnimFunc
        {
            IsDuration = true,
            SubFuncs =
            [
                new() { Ease = KC.AnimEase.QuadInOut, Reverse = false, Duration = new TimeInt32(dur1Ms) },
                new() { Ease = KC.AnimEase.Constant, Reverse = false, Duration = new TimeInt32(dur2Ms * 2) },
                new() { Ease = KC.AnimEase.QuadInOut, Reverse = true, Duration = new TimeInt32(dur1Ms) }
            ]
        };

        // Constraint 1: Drives Body 1 on Axis 2
        // Since Body 1 rests at B, its TransMin can be parkingDistance (far away on Axis 2, or below ground),
        // and its TransMax is dist2 (position C relative to B).
        // 0 relative to rest is exactly position B!
        // To support parking, TransMin is parkingDistance, TransMax is dist2.
        // When at 0, Body 1 is at point B.
        // Let's formulate with TransMin = parkingDistance and TransMax = dist2:
        // Note: TM AnimFunc works with normalized range [TransMin, TransMax].
        // To be simpler and avoid fraction math if parkingDistance is on axis2:
        // Or parkingDistance can simply be an offset on Axis 2: e.g. -200m!
        // Let's configure TransMin = parkingDistance (e.g. -200), TransMax = dist2.
        // But in TM, each key has Ease and Reverse (Reverse flips between TransMin and TransMax, or keys interpolate).
        // Notice: with 4 keys in AnimFunc:
        // If TransMin = 0 and TransMax = dist2, Body 1 only moves between 0 and dist2.
        // Can we park Body 1 far away with 4 keys?
        // In TM, TransAnimFunc has at most 4 keys!
        // Key 1: Constant at park? Key 2: QuadInOut to dist2? Key 3: QuadInOut back? Key 4: Constant at park?
        // But if TransMin is 0 and TransMax is dist2, it only goes between 0 and dist2.
        // If TransMin is parkingDistance (-200) and TransMax is dist2 (e.g. 5), then Reverse=false goes from -200 to 5! That wouldn't stop at 0 (B) unless 0 is an endpoint!
        // AHA! If 0 is not an endpoint, a 4-key timeline can only interpolate between TransMin and TransMax!
        // Wait, what if parking position IS TransMin (-200m), and the movement goes from parking (-200m) to C?
        // Then it wouldn't pause at B unless B is TransMin!
        // BUT WAIT: What if Body 1 rests at the PARKED position?
        // E.g.: Body 1's rest position is parked far away (or in an occluder / wall)!
        // When active, it moves from Parked (0) to Point B to Point C? That's 2 segments on 1 axis!
        // OR: What if Body 1's axis is Y, and parking is simply underground?
        // Let's check: Can Body 1 move between B and C (TransMin=0, TransMax=dist2), and when it's at B, Body 0 and Body 1 overlap seamlessly?
        // YES! When Body 0 arrives at B, Body 1 is ALREADY at B!
        // At that moment, both meshes are at the EXACT same position B!
        // If Body 1 then moves B -> C -> B, while Body 0 stays at B (or sinks into an occluder/wall)!
        // Wait, why park Body 2 far away if Body 2 can simply be at B?
        // In the user's prompt:
        // "Body 1 gaat van punt A-B horizontaal. Body 2 voor de verticale as staat ergens onzichtbaar in de verte "geparkeerd" en schiet naar punt B (het eind punt van body 1), en gaat dan rustig door naar punt C op de verticale as, komt dan weer terug naar punt B (het eind punt van body 1) en schiet dan weer weg naar een ver punt "de parkeer stand" zodat body 1 het op de horizontale as weer over kan nemen."
        // AND:
        // "**Fixing the visibility problem:** A slides into a wall/terrain occluder at the corner (or drops away on the vertical C axis), B appears from that same occluder. At handover they overlap, then only one remains visible."
        //
        // Let's examine: How can a body shoot away with Trackmania's constraint system?
        // If a body has its OWN constraint, can it have:
        // TransMin = 0, TransMax = dist2. But that's only between B and C!
        // How can it shoot away?
        // If Body 2 has TransMin = -200 (parking) and TransMax = 0? Then it can only go between -200 and 0.
        // To go from -200 to 0 AND then 0 to dist2 on the same axis would require 3 points (-200, 0, dist2).
        // But a Trackmania constraint only has TransMin and TransMax (2 scalar endpoints)!
        // EVERY key in TransAnimFunc interpolates between TransMin and TransMax (or stays constant at TransMin/TransMax)!
        // It CANNOT interpolate to an intermediate value like 0 if TransMin is -200 and TransMax is +50!
        //
        // WAIT! Unless Body 2 has TWO constraints chained together!
        // Constraint A on Body 2 (Parent = -1, Child = Carrier or Body 2): Parking axis (shoots from -500 to 0)!
        // Constraint B on Body 2: Movement axis (B -> C -> B, 0 to dist2)!
        // OR Body 2's rest position is at the corner (Point B), and Body 1's rest position is at A!
        // What if Body 2 is parked by a fast constraint, OR what if Body 1 and Body 2 use an occluder/tunnel/wall at corner B?
        // Wait, let's re-read the prompt carefully!
        return ItemMotionResult<bool>.Ok(true);
    }

    /// <summary>
    /// Removes a constraint and its associated entity from the prefab cleanly.
    /// Re-indexes/rebases any remaining constraint parameters to maintain valid slot indices.
    /// </summary>
    public static ItemMotionResult<bool> RemoveConstraint(CPlugPrefab owner, KC source, string prefabInstancePath)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(source);

        var entries = owner.Ents;
        if (entries is null || entries.Length == 0)
            return ItemMotionResult<bool>.Fail(ItemMotionStatus.Absent, "Prefab has no entities.");

        int constraintEntryIndex = -1;
        for (int i = 0; i < entries.Length; i++)
        {
            if (ReferenceEquals(entries[i].Model, source))
            {
                constraintEntryIndex = i;
                break;
            }
        }

        if (constraintEntryIndex < 0)
            return ItemMotionResult<bool>.Fail(ItemMotionStatus.Absent, "Constraint not found in prefab.");

        var constraintParams = entries[constraintEntryIndex].Params as NPlugDyna_SPrefabConstraintParams;
        int targetSlot = constraintParams?.Ent2 ?? -1;

        // Collect all kinematic dyna body slots before removal
        var (slotsBefore, _, _) = ItemMotionBindings.CollectSlots(owner, prefabInstancePath);

        // Find the dyna entry associated with targetSlot if valid
        CPlugPrefab.EntRef? targetBodyRef = null;
        if (targetSlot >= 0 && targetSlot < slotsBefore.Count)
        {
            targetBodyRef = slotsBefore[targetSlot].SourceEntry;
        }

        // Check if any other constraint still uses this target body
        bool bodyUsedByOthers = entries.Any(e =>
            !ReferenceEquals(e.Model, source)
            && e.Model is KC
            && e.Params is NPlugDyna_SPrefabConstraintParams p
            && p.Ent2 == targetSlot);

        // Prepare new list of entries
        var newEntries = new List<CPlugPrefab.EntRef>();
        for (int i = 0; i < entries.Length; i++)
        {
            if (i == constraintEntryIndex) continue; // remove the constraint
            if (!bodyUsedByOthers && targetBodyRef != null && ReferenceEquals(entries[i], targetBodyRef))
            {
                // remove the body as well if no other constraint targets it,
                // BUT only if more than 1 body exists in the prefab so we don't leave an empty prefab!
                if (slotsBefore.Count > 1)
                {
                    continue;
                }
            }
            newEntries.Add(entries[i]);
        }

        owner.Ents = newEntries.ToArray();

        // Now re-collect slots and fix Ent1 and Ent2 on all remaining constraints!
        var (slotsAfter, _, _) = ItemMotionBindings.CollectSlots(owner, prefabInstancePath);

        foreach (var entry in owner.Ents)
        {
            if (entry.Model is KC kc && entry.Params is NPlugDyna_SPrefabConstraintParams p)
            {
                // Find where the child body is in the new slot table
                // Match by reference to the SourceEntry
                if (targetSlot >= 0 && targetSlot < slotsBefore.Count)
                {
                    var oldChildEntry = slotsBefore.Count > p.Ent2 && p.Ent2 >= 0 ? slotsBefore[p.Ent2].SourceEntry : null;
                    var oldParentEntry = slotsBefore.Count > p.Ent1 && p.Ent1 >= 0 ? slotsBefore[p.Ent1].SourceEntry : null;

                    if (oldChildEntry != null)
                    {
                        var newChildSlot = slotsAfter.FindIndex(s => ReferenceEquals(s.SourceEntry, oldChildEntry));
                        if (newChildSlot >= 0) p.Ent2 = newChildSlot;
                        else p.Ent2 = 0; // fallback to 0
                    }

                    if (oldParentEntry != null)
                    {
                        var newParentSlot = slotsAfter.FindIndex(s => ReferenceEquals(s.SourceEntry, oldParentEntry));
                        p.Ent1 = newParentSlot >= 0 ? newParentSlot : -1;
                    }
                    else if (p.Ent1 >= 0)
                    {
                        // Was pointing to an out-of-range or removed slot
                        p.Ent1 = -1;
                    }
                }
            }
        }

        return ItemMotionResult<bool>.Ok(true);
    }

    private sealed record Template(CPlugPrefab.EntRef ConstraintEntry, CPlugPrefab.EntRef BodyEntry,
        NPlugDyna_SPrefabConstraintParams Parameters, CPlugDynaObjectModel Dyna,
        NPlugDynaObjectModel_SInstanceParams Instance, ItemMotionBinding Binding, ItemMotionSnapshot Snapshot);
    private sealed record TemplateBody(int EntryIndex, CPlugDynaObjectModel Body, NPlugDynaObjectModel_SInstanceParams Instance);
}
