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

    private static ItemMotionResult<CPlugDynaObjectModel> CreateHiddenCarrierBody(CPlugDynaObjectModel source)
    {
        var carrierMesh = CreateCarrierMesh();
        var carrierShape = CreateCarrierShape();

        return ItemMotionResult<CPlugDynaObjectModel>.Ok(new CPlugDynaObjectModel
        {
            Version = source.Version,
            IsStatic = false,
            DynamizeOnSpawn = source.DynamizeOnSpawn,
            Mass = source.Mass,
            BreakSpeedKmh = source.BreakSpeedKmh,
            Mesh = carrierMesh,
            StaticShape = carrierShape,
            DynaShape = carrierShape,
            LocAnim = source.LocAnim
        });
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
            CustomMaterials = [],
            ShadedGeoms = []
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
                    new() { Ease = KC.AnimEase.Constant, Reverse = false, Duration = new TimeInt32(dur2Ms * 2) },
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

    private sealed record Template(CPlugPrefab.EntRef ConstraintEntry, CPlugPrefab.EntRef BodyEntry,
        NPlugDyna_SPrefabConstraintParams Parameters, CPlugDynaObjectModel Dyna,
        NPlugDynaObjectModel_SInstanceParams Instance, ItemMotionBinding Binding, ItemMotionSnapshot Snapshot);
    private sealed record TemplateBody(int EntryIndex, CPlugDynaObjectModel Body, NPlugDynaObjectModel_SInstanceParams Instance);
}
