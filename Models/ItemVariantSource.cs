using GBX.NET;
using GBX.NET.Engines.Game;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.MetaNotPersistent;
using GBX.NET.Engines.MwFoundations;
using GBX.NET.Engines.Plug;
using TmEssentials;

namespace TM_Item_Studio.Models;

/// <summary>A view into a document. Selecting a view never replaces the document's root.</summary>
public sealed class ItemVariantSource
{
    public readonly record struct MergeRootTransform(Vec3 Position, Quat Rotation);
    public readonly record struct GroupAnimKey(NPlugDyna_SKinematicConstraint.AnimEase Ease, bool Reverse, int DurationMs);
    public readonly record struct GroupKinematicMotion(
        NPlugDyna_SKinematicConstraint.EAxis TransAxis,
        float TransMin,
        float TransMax,
        IReadOnlyList<GroupAnimKey> TransKeys,
        NPlugDyna_SKinematicConstraint.EAxis RotAxis,
        float RotMinDeg,
        float RotMaxDeg,
        IReadOnlyList<GroupAnimKey> RotKeys);
    public readonly record struct GroupMergeDefinition(
        int GroupId,
        IReadOnlyList<int> SourceIndices,
        MergeRootTransform Transform,
        GroupKinematicMotion? Motion);

    public string Name { get; }
    public string FileName { get; }
    public int VariantNumber { get; }
    public Gbx<CGameItemModel> GbxFile { get; }
    public CGameItemModel Model => GbxFile.Node;
    public NPlugItem_SVariant? Variant { get; }
    public CMwNod? PreviewRoot => Variant is null ? Model : Variant.EntityModel;
    /// <summary>Explains when saving re-encodes an imported archive instead of preserving its bytes.</summary>
    public string? SerializationWarning { get; }
    public bool RequiresReencodingAcknowledgement => SerializationWarning is not null;

    private ItemVariantSource(string name, Gbx<CGameItemModel> file, NPlugItem_SVariant? variant, int? variantNumber, string? serializationWarning)
    {
        FileName = name;
        VariantNumber = variantNumber ?? 1;
        Name = variantNumber.HasValue ? $"{name} {variantNumber}" : name;
        GbxFile = file;
        Variant = variant;
        SerializationWarning = serializationWarning;
    }

    public static IReadOnlyList<ItemVariantSource> FromFile(string name, Gbx<CGameItemModel> file, byte[]? originalBytes = null)
    {
        var warning = GetSerializationWarning(file, originalBytes);
        if (file.Node.EntityModel is NPlugItem_SVariantList { Variants.Length: > 0 } list)
        {
            // Keep unresolved and null entity references: they are still authored variants.
            return list.Variants.Select((variant, index) =>
                new ItemVariantSource(name, file, variant, index + 1, warning)).ToArray();
        }

        // An empty variant list is still an editable/exportable document.
        return new[] { new ItemVariantSource(name, file, null, null, warning) };
    }

    public void Save(Stream destination)
    {
        GbxFile.Save(destination);
    }

    /// <summary>Combines documents using the first file's item-level metadata.</summary>
    public static void SaveCombined(IReadOnlyList<ItemVariantSource> sources, Stream destination)
    {
        if (sources.Select(source => source.GbxFile).Distinct().Take(2).Count() < 2)
            throw new InvalidOperationException("Choose at least two item files to combine.");

        var documents = sources.Select(source => source.GbxFile).Distinct().ToArray();
        if (documents.Any(file => file.RefTable?.Files.Count > 0 || file.RefTable?.Resources.Count > 0))
            throw new InvalidOperationException("Combining files with external references is not supported. Export each file separately to preserve its dependencies.");

        var variants = new List<NPlugItem_SVariant>();
        foreach (var source in sources)
        {
            if (source.Model.EntityModel is NPlugItem_SVariantList { Variants.Length: 0 })
                throw new InvalidOperationException($"{source.Name} has an empty variant list. Export it separately.");

            if (source.Variant is { } variant)
            {
                variants.Add(new NPlugItem_SVariant
                {
                    Tags = new Dictionary<string, string>(variant.Tags),
                    HiddenInManualCycle = variant.HiddenInManualCycle,
                    EntityModel = variant.EntityModel,
                    EntityModelFile = variant.EntityModelFile
                });
            }
            else if (source.Model.EntityModel is { } entity)
            {
                variants.Add(new NPlugItem_SVariant { EntityModel = entity });
            }
            else
            {
                throw new InvalidOperationException($"{source.Name} has no entity model to combine. Export it separately.");
            }

        }

        var first = sources[0];
        var originalRoot = first.Model.EntityModel;
        try
        {
            // Version 1 is required to serialize HiddenInManualCycle.
            first.Model.EntityModel = new NPlugItem_SVariantList { Version = 1, Variants = variants.ToArray() };
            first.Save(destination);
        }
        finally
        {
            first.Model.EntityModel = originalRoot;
        }
    }

    /// <summary>Builds one composite item whose root prefab contains one nested prefab entry per source,
    /// similar to DeathPit-style multipart assemblies.</summary>
    public static void SaveMergedPrefab(IReadOnlyList<ItemVariantSource> sources, Stream destination,
        IReadOnlyList<MergeRootTransform>? rootOffsets = null,
        IReadOnlyList<GroupMergeDefinition>? groups = null)
    {
        if (sources.Count < 2)
            throw new InvalidOperationException("Choose at least two item files to merge.");
        if (rootOffsets is not null && rootOffsets.Count != sources.Count)
            throw new InvalidOperationException("Merged export offsets must match the selected source count.");

        var documents = sources.Select(source => source.GbxFile).Distinct().ToArray();
        if (documents.Length < 2)
            throw new InvalidOperationException("Choose at least two different item files to merge.");
        if (documents.Any(file => file.RefTable?.Files.Count > 0 || file.RefTable?.Resources.Count > 0))
            throw new InvalidOperationException("Merging files with external references is not supported. Export each file separately to preserve dependencies.");

        var groupedSourceIndex = (groups ?? Array.Empty<GroupMergeDefinition>())
            .SelectMany(group => group.SourceIndices.Select(sourceIndex => (group.GroupId, sourceIndex)))
            .ToDictionary(entry => entry.sourceIndex, entry => entry.GroupId);
        var groupById = (groups ?? Array.Empty<GroupMergeDefinition>())
            .Where(group => group.SourceIndices.Count >= 2)
            .ToDictionary(group => group.GroupId, group => group);

        var sourcePrefabs = new CPlugPrefab[sources.Count];
        var localSlotsBySource = new List<ItemMotionSlot>[sources.Count];
        for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
        {
            sourcePrefabs[sourceIndex] = EnsureMergePrefab(sources[sourceIndex]);
            localSlotsBySource[sourceIndex] = ItemMotionBindings.CollectSlots(sourcePrefabs[sourceIndex], $"source:{sourceIndex}").Slots;
        }

        var groupMembers = groupById.Keys.ToDictionary(groupId => groupId, _ => new List<(int SourceIndex, CPlugPrefab Prefab)>());
        for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
        {
            if (groupedSourceIndex.TryGetValue(sourceIndex, out var groupId)
                && groupMembers.TryGetValue(groupId, out var grouped))
            {
                grouped.Add((sourceIndex, sourcePrefabs[sourceIndex]));
            }
        }

        var rootEntries = new List<CPlugPrefab.EntRef>(sources.Count + groupById.Count * 2);
        var groupCarrierBodies = new Dictionary<int, CPlugDynaObjectModel>();
        var groupConstraintRefs = new Dictionary<int, (CPlugPrefab.EntRef EntRef, NPlugDyna_SPrefabConstraintParams Params)>();
        var groupCollisionProxyBodies = new Dictionary<int, List<CPlugDynaObjectModel>>();
        var groupCollisionProxyConstraintParams = new Dictionary<int, List<NPlugDyna_SPrefabConstraintParams>>();
        var groupPrefabEntriesMap = new Dictionary<int, List<CPlugPrefab.EntRef>>();
        var groupPrefabsMap = new Dictionary<int, CPlugPrefab>();

        // 1. Add ungrouped sources to rootEntries
        for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
        {
            if (groupedSourceIndex.TryGetValue(sourceIndex, out var groupId)
                && groupMembers.TryGetValue(groupId, out var members)
                && members.Count >= 2)
            {
                continue;
            }

            rootEntries.Add(new CPlugPrefab.EntRef
            {
                Position = rootOffsets is null ? default : rootOffsets[sourceIndex].Position,
                Rotation = rootOffsets is null ? new Quat(0, 0, 0, 1) : rootOffsets[sourceIndex].Rotation,
                U01 = "",
                Model = sourcePrefabs[sourceIndex],
                Params = new NPlugDynaObjectModel_SInstanceParams { Version = 2 }
            });
        }

        // 2. Add groups to rootEntries
        foreach (var group in groupById.Values.OrderBy(group => group.GroupId))
        {
            if (!groupMembers.TryGetValue(group.GroupId, out var members) || members.Count < 2)
                continue;

            var orderedMembers = members.OrderBy(m => m.SourceIndex).ToArray();
            var groupPrefabEntries = new List<CPlugPrefab.EntRef>(orderedMembers.Length + 4);
            groupPrefabEntriesMap[group.GroupId] = groupPrefabEntries;
            groupCollisionProxyBodies[group.GroupId] = new List<CPlugDynaObjectModel>();
            groupCollisionProxyConstraintParams[group.GroupId] = new List<NPlugDyna_SPrefabConstraintParams>();

            if (group.Motion is { } motion)
            {
                var carrierBody = ItemKinematicEntityTemplate.CreateCarrierBody();
                groupCarrierBodies[group.GroupId] = carrierBody;

                var carrierRef = new CPlugPrefab.EntRef
                {
                    Position = default,
                    Rotation = new Quat(0, 0, 0, 1),
                    U01 = "",
                    Model = carrierBody,
                    Params = new NPlugDynaObjectModel_SInstanceParams
                    {
                        Version = 2,
                        PeriodSc = 1,
                        PeriodScMax = -1,
                        Phase01 = -1,
                        Phase01Max = -1,
                        TextureId = 0,
                        IsKinematic = true,
                        CastStaticShadow = false
                    }
                };
                groupPrefabEntries.Add(carrierRef);

                var groupConstraintParams = new NPlugDyna_SPrefabConstraintParams
                {
                    Version = 0,
                    Ent1 = -1,
                    Ent2 = -1,
                    Pos1 = default,
                    Pos2 = default
                };
                var groupConstraintRef = new CPlugPrefab.EntRef
                {
                    Position = default,
                    Rotation = new Quat(0, 0, 0, 1),
                    U01 = "",
                    Model = BuildGroupConstraint(motion),
                    Params = groupConstraintParams
                };
                groupPrefabEntries.Add(groupConstraintRef);
                groupConstraintRefs[group.GroupId] = (groupConstraintRef, groupConstraintParams);
            }

            foreach (var member in orderedMembers)
            {
                groupPrefabEntries.Add(new CPlugPrefab.EntRef
                {
                    Position = rootOffsets is null ? default : rootOffsets[member.SourceIndex].Position,
                    Rotation = rootOffsets is null ? new Quat(0, 0, 0, 1) : rootOffsets[member.SourceIndex].Rotation,
                    U01 = "",
                    Model = member.Prefab,
                    Params = new NPlugDynaObjectModel_SInstanceParams { Version = 2 }
                });
            }

            var groupPrefab = new CPlugPrefab
            {
                Version = 11,
                Url = "",
                Ents = groupPrefabEntries.ToArray()
            };
            groupPrefabsMap[group.GroupId] = groupPrefab;

            rootEntries.Add(new CPlugPrefab.EntRef
            {
                Position = group.Transform.Position,
                Rotation = group.Transform.Rotation,
                U01 = "",
                Model = groupPrefab,
                Params = new NPlugDynaObjectModel_SInstanceParams { Version = 2 }
            });
        }

        var mergedRoot = new CPlugPrefab
        {
            Version = 11,
            Url = "",
            Ents = rootEntries.ToArray()
        };

        // 3. Traverse the merged prefab to collect global slots
        var (mergedSlots, _, _) = ItemMotionBindings.CollectSlots(mergedRoot, "merge/root");

        // Map each source's local slots to the new global slots
        var localToGlobal = new Dictionary<(int SourceIndex, int LocalSlot), int>();
        for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
        {
            var localSlots = localSlotsBySource[sourceIndex];
            foreach (var localSlot in localSlots)
            {
                var dynaModel = localSlot.SourceEntry?.Model;
                if (dynaModel is null) continue;

                var globalMatch = mergedSlots.FirstOrDefault(s => ReferenceEquals(s.SourceEntry?.Model, dynaModel));
                if (globalMatch is not null)
                {
                    localToGlobal[(sourceIndex, localSlot.Slot)] = globalMatch.Slot;
                }
            }
        }

        // Map group carrier bodies to their global slot
        var groupCarrierSlots = new Dictionary<int, int>();
        foreach (var (groupId, carrierBody) in groupCarrierBodies)
        {
            var globalMatch = mergedSlots.FirstOrDefault(s => ReferenceEquals(s.SourceEntry?.Model, carrierBody));
            if (globalMatch is not null)
            {
                groupCarrierSlots[groupId] = globalMatch.Slot;
            }
        }

        // 4. Wire constraints and rebase slots
        var rebased = new List<(NPlugDyna_SPrefabConstraintParams Constraint, int Ent1, int Ent2)>();

        // Rebase member constraints
        for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
        {
            var prefab = sourcePrefabs[sourceIndex];
            var groupId = -1;
            GroupKinematicMotion? groupMotion = null;
            var isGroupedWithMotion = groupedSourceIndex.TryGetValue(sourceIndex, out groupId)
                && groupById.TryGetValue(groupId, out var resolvedGroup)
                && resolvedGroup.Motion is { } resolvedMotion
                && groupCarrierSlots.TryGetValue(groupId, out _);
            if (isGroupedWithMotion)
                groupMotion = groupById[groupId].Motion;

            var carrierSlot = isGroupedWithMotion ? groupCarrierSlots[groupId] : -1;
            var drivenGlobalSlots = new HashSet<int>();
            var proxiedGlobalSlots = new HashSet<int>();

            void AddGroupCollisionProxy(CPlugDynaObjectModel proxyBody)
            {
                if (!isGroupedWithMotion || !groupPrefabEntriesMap.TryGetValue(groupId, out var groupEntries))
                    return;
                var proxyBodyEntry = new CPlugPrefab.EntRef
                {
                    Position = default,
                    Rotation = new Quat(0, 0, 0, 1),
                    U01 = "",
                    Model = proxyBody,
                    Params = new NPlugDynaObjectModel_SInstanceParams
                    {
                        Version = 2,
                        PeriodSc = 1,
                        PeriodScMax = -1,
                        Phase01 = -1,
                        Phase01Max = -1,
                        TextureId = 0,
                        IsKinematic = true,
                        CastStaticShadow = false
                    }
                };
                var proxyParams = new NPlugDyna_SPrefabConstraintParams
                {
                    Version = 0,
                    Ent1 = -1,
                    Ent2 = -1,
                    Pos1 = default,
                    Pos2 = default
                };
                var proxyConstraintEntry = new CPlugPrefab.EntRef
                {
                    Position = default,
                    Rotation = new Quat(0, 0, 0, 1),
                    U01 = "",
                    Model = BuildGroupConstraint(groupMotion!.Value),
                    Params = proxyParams
                };
                groupEntries.Add(proxyBodyEntry);
                groupEntries.Add(proxyConstraintEntry);
                groupCollisionProxyBodies[groupId].Add(proxyBody);
                groupCollisionProxyConstraintParams[groupId].Add(proxyParams);
            }

            void WalkConstraints(CPlugPrefab p)
            {
                if (p.Ents is null) return;
                foreach (var ent in p.Ents)
                {
                    if (ent?.Params is NPlugDyna_SPrefabConstraintParams constraint)
                    {
                        var origEnt1 = constraint.Ent1;
                        var origEnt2 = constraint.Ent2;
                        rebased.Add((constraint, origEnt1, origEnt2));

                        if (localToGlobal.TryGetValue((sourceIndex, origEnt2), out var globalChild))
                        {
                            constraint.Ent2 = globalChild;
                            drivenGlobalSlots.Add(globalChild);
                        }

                        if (origEnt1 == -1)
                        {
                            constraint.Ent1 = isGroupedWithMotion ? carrierSlot : -1;
                        }
                        else if (localToGlobal.TryGetValue((sourceIndex, origEnt1), out var globalParent))
                        {
                            constraint.Ent1 = globalParent;
                        }

                        if (isGroupedWithMotion
                            && ent.Model is NPlugDyna_SKinematicConstraint constraintModel
                            && localSlotsBySource[sourceIndex].FirstOrDefault(slot => slot.Slot == origEnt2)?.SourceEntry?.Model is CPlugDynaObjectModel drivenBody
                            && localToGlobal.TryGetValue((sourceIndex, origEnt2), out var drivenGlobalSlot)
                            && proxiedGlobalSlots.Add(drivenGlobalSlot))
                        {
                            var proxyBody = CreateCollisionProxyBody(drivenBody, constraintModel);
                            AddGroupCollisionProxy(proxyBody);
                        }
                    }

                    if (ent?.Model is CPlugPrefab nested)
                        WalkConstraints(nested);
                }
            }
            WalkConstraints(prefab);

            // If in a group with motion, any member dyna body without an authored constraint gets a rigid lock constraint
            if (isGroupedWithMotion && groupPrefabEntriesMap.TryGetValue(groupId, out var groupEntries))
            {
                var localSlots = localSlotsBySource[sourceIndex];
                foreach (var localSlot in localSlots)
                {
                    if (localToGlobal.TryGetValue((sourceIndex, localSlot.Slot), out var globalSlot)
                        && !drivenGlobalSlots.Contains(globalSlot))
                    {
                        var lockParams = new NPlugDyna_SPrefabConstraintParams
                        {
                            Version = 0,
                            Ent1 = carrierSlot,
                            Ent2 = globalSlot,
                            Pos1 = default,
                            Pos2 = default
                        };
                        groupEntries.Add(new CPlugPrefab.EntRef
                        {
                            Position = default,
                            Rotation = new Quat(0, 0, 0, 1),
                            U01 = "",
                            Model = BuildRigidLockConstraint(),
                            Params = lockParams
                        });
                        drivenGlobalSlots.Add(globalSlot);
                    }

                    if (localToGlobal.TryGetValue((sourceIndex, localSlot.Slot), out var proxyGlobalSlot)
                        && proxiedGlobalSlots.Add(proxyGlobalSlot)
                        && localSlot.SourceEntry?.Model is CPlugDynaObjectModel slotBody)
                    {
                        var proxyBody = ItemKinematicEntityTemplate.CreateCarrierBody(slotBody);
                        var proxyShape = slotBody.StaticShape ?? slotBody.DynaShape;
                        if (proxyShape is not null)
                        {
                            proxyBody.StaticShape = proxyShape;
                            proxyBody.DynaShape = proxyShape;
                        }
                        AddGroupCollisionProxy(proxyBody);
                    }
                }
            }
        }

        // Update groupPrefab.Ents for any groups that had lock constraints added
        foreach (var (groupId, groupEntries) in groupPrefabEntriesMap)
        {
            if (groupPrefabsMap.TryGetValue(groupId, out var groupPrefab))
            {
                groupPrefab.Ents = groupEntries.ToArray();
            }
        }

        // Re-resolve each group prefab after all injected proxy/lock entries exist.
        // Group-internal constraints must use group-local slot indices; global flattened
        // slots can point outside the nested table and detach collision in-game.
        foreach (var (groupId, groupPrefab) in groupPrefabsMap)
        {
            var (groupSlots, _, _) = ItemMotionBindings.CollectSlots(groupPrefab, $"merge/group:{groupId}");
            var groupSlotByBody = new Dictionary<CPlugDynaObjectModel, int>(ReferenceEqualityComparer.Instance);
            foreach (var slot in groupSlots)
            {
                if (slot.SourceEntry?.Model is CPlugDynaObjectModel body && !groupSlotByBody.ContainsKey(body))
                    groupSlotByBody[body] = slot.Slot;
            }

            if (groupConstraintRefs.TryGetValue(groupId, out var rootConstraint)
                && groupCarrierBodies.TryGetValue(groupId, out var carrierBody)
                && groupSlotByBody.TryGetValue(carrierBody, out var localCarrierSlot))
            {
                rootConstraint.Params.Ent1 = -1;
                rootConstraint.Params.Ent2 = localCarrierSlot;
            }

            if (groupCollisionProxyConstraintParams.TryGetValue(groupId, out var proxyParams)
                && groupCollisionProxyBodies.TryGetValue(groupId, out var proxyBodies))
            {
                for (var i = 0; i < proxyParams.Count && i < proxyBodies.Count; i++)
                {
                    if (!groupSlotByBody.TryGetValue(proxyBodies[i], out var localProxySlot))
                        continue;
                    proxyParams[i].Ent1 = -1;
                    proxyParams[i].Ent2 = localProxySlot;
                }
            }
        }

        var first = sources[0];
        var originalRoot = first.Model.EntityModel;
        try
        {
            first.Model.EntityModel = mergedRoot;
            first.Save(destination);
        }
        finally
        {
            foreach (var entry in rebased)
            {
                entry.Constraint.Ent1 = entry.Ent1;
                entry.Constraint.Ent2 = entry.Ent2;
            }
            first.Model.EntityModel = originalRoot;
        }
    }

    private static NPlugDyna_SKinematicConstraint BuildRigidLockConstraint()
    {
        return new NPlugDyna_SKinematicConstraint
        {
            Version = 0,
            SubVersion = 3,
            TransAxis = NPlugDyna_SKinematicConstraint.EAxis.Y,
            TransMin = 0,
            TransMax = 0,
            RotAxis = NPlugDyna_SKinematicConstraint.EAxis.Y,
            AngleMinDeg = 0,
            AngleMaxDeg = 0,
            TransAnimFunc = new NPlugDyna_SKinematicConstraint.AnimFunc
            {
                IsDuration = true,
                SubFuncs =
                [
                    new NPlugDyna_SKinematicConstraint.SubAnimFunc
                    {
                        Ease = NPlugDyna_SKinematicConstraint.AnimEase.Constant,
                        Reverse = false,
                        Duration = new TimeInt32(1000)
                    }
                ]
            },
            RotAnimFunc = new NPlugDyna_SKinematicConstraint.AnimFunc
            {
                IsDuration = true,
                SubFuncs =
                [
                    new NPlugDyna_SKinematicConstraint.SubAnimFunc
                    {
                        Ease = NPlugDyna_SKinematicConstraint.AnimEase.Constant,
                        Reverse = false,
                        Duration = new TimeInt32(1000)
                    }
                ]
            }
        };
    }

    private static NPlugDyna_SKinematicConstraint BuildGroupConstraint(GroupKinematicMotion motion)
    {
        var transKeys = (motion.TransKeys?.Count > 0 ? motion.TransKeys : new[]
        {
            new GroupAnimKey(NPlugDyna_SKinematicConstraint.AnimEase.Linear, false, 1000),
            new GroupAnimKey(NPlugDyna_SKinematicConstraint.AnimEase.Linear, true, 1000)
        })
        .Take(10)
        .Select(key => new NPlugDyna_SKinematicConstraint.SubAnimFunc
        {
            Ease = key.Ease,
            Reverse = key.Reverse,
            Duration = new TimeInt32(Math.Max(50, key.DurationMs))
        })
        .ToArray();
        var rotKeys = (motion.RotKeys?.Count > 0 ? motion.RotKeys : new[]
        {
            new GroupAnimKey(NPlugDyna_SKinematicConstraint.AnimEase.Linear, false, 1000),
            new GroupAnimKey(NPlugDyna_SKinematicConstraint.AnimEase.QuadInOut, true, 1000)
        })
        .Take(10)
        .Select(key => new NPlugDyna_SKinematicConstraint.SubAnimFunc
        {
            Ease = key.Ease,
            Reverse = key.Reverse,
            Duration = new TimeInt32(Math.Max(50, key.DurationMs))
        })
        .ToArray();

        return new NPlugDyna_SKinematicConstraint
        {
            Version = 0,
            SubVersion = 3,
            TransAxis = motion.TransAxis,
            TransMin = motion.TransMin,
            TransMax = motion.TransMax,
            RotAxis = motion.RotAxis,
            AngleMinDeg = motion.RotMinDeg,
            AngleMaxDeg = motion.RotMaxDeg,
            TransAnimFunc = new NPlugDyna_SKinematicConstraint.AnimFunc
            {
                IsDuration = true,
                SubFuncs = transKeys
            },
            RotAnimFunc = new NPlugDyna_SKinematicConstraint.AnimFunc
            {
                IsDuration = true,
                SubFuncs = rotKeys
            }
        };
    }

    private static CPlugDynaObjectModel CreateCollisionProxyBody(CPlugDynaObjectModel sourceBody, NPlugDyna_SKinematicConstraint sourceConstraint)
    {
        var proxy = ItemKinematicEntityTemplate.CreateCarrierBody(sourceBody);
        var proxyShape = CreateSweptCollisionShape(sourceBody, sourceConstraint)
            ?? sourceBody.StaticShape
            ?? sourceBody.DynaShape;
        if (proxyShape is not null)
        {
            proxy.StaticShape = proxyShape;
            proxy.DynaShape = proxyShape;
        }
        return proxy;
    }

    private static CPlugSurface? CreateSweptCollisionShape(CPlugDynaObjectModel sourceBody, NPlugDyna_SKinematicConstraint sourceConstraint)
    {
        var mesh = (sourceBody.StaticShape?.Surf as CPlugSurface.Mesh)
            ?? (sourceBody.DynaShape?.Surf as CPlugSurface.Mesh);
        if (mesh?.Vertices is null || mesh.Vertices.Length < 3 || mesh.Triangles is null || mesh.Triangles.Length == 0)
            return null;

        var axis = sourceConstraint.TransAxis switch
        {
            NPlugDyna_SKinematicConstraint.EAxis.X => new Vec3(1, 0, 0),
            NPlugDyna_SKinematicConstraint.EAxis.Y => new Vec3(0, 1, 0),
            NPlugDyna_SKinematicConstraint.EAxis.Z => new Vec3(0, 0, 1),
            _ => new Vec3(0, 0, 0)
        };
        var minOffset = new Vec3(axis.X * sourceConstraint.TransMin, axis.Y * sourceConstraint.TransMin, axis.Z * sourceConstraint.TransMin);
        var maxOffset = new Vec3(axis.X * sourceConstraint.TransMax, axis.Y * sourceConstraint.TransMax, axis.Z * sourceConstraint.TransMax);

        var vertices = new Vec3[mesh.Vertices.Length * 2];
        for (var i = 0; i < mesh.Vertices.Length; i++)
        {
            var v = mesh.Vertices[i];
            vertices[i] = new Vec3(v.X + minOffset.X, v.Y + minOffset.Y, v.Z + minOffset.Z);
            vertices[i + mesh.Vertices.Length] = new Vec3(v.X + maxOffset.X, v.Y + maxOffset.Y, v.Z + maxOffset.Z);
        }

        var triangles = new CPlugSurface.Mesh.Triangle[mesh.Triangles.Length * 2];
        for (var i = 0; i < mesh.Triangles.Length; i++)
        {
            var tri = mesh.Triangles[i];
            triangles[i] = new CPlugSurface.Mesh.Triangle(new Int3(tri.Indices.X, tri.Indices.Y, tri.Indices.Z), 0, 0, 0);
            triangles[i + mesh.Triangles.Length] = new CPlugSurface.Mesh.Triangle(
                new Int3(
                    tri.Indices.X + mesh.Vertices.Length,
                    tri.Indices.Y + mesh.Vertices.Length,
                    tri.Indices.Z + mesh.Vertices.Length),
                0, 0, 0);
        }

        var swept = new CPlugSurface.Mesh
        {
            Version = 6,
            Vertices = vertices,
            Triangles = triangles
        };
        var surface = new CPlugSurface { Surf = swept };
        surface.CreateChunk<CPlugSurface.Chunk0900C003>().Version = 2;
        return surface;
    }

    private static CPlugPrefab EnsureMergePrefab(ItemVariantSource source)
    {
        var node = source.PreviewRoot;
        if (node is CGameItemModel itemRoot)
            node = itemRoot.EntityModel;
        if (node is CPlugPrefab prefab) return prefab;

        if (node is CGameCommonItemEntityModel or CPlugStaticObjectModel)
        {
            var template = ItemKinematicEntityTemplate.GetDefaultMovingTemplate();
            if (template is null)
                throw new InvalidOperationException("Built-in moving template is unavailable; cannot convert static sources for merge.");
            var conversion = ItemKinematicEntityTemplate.ConvertStaticToKinematic(source.Model, template);
            if (!conversion.Success || conversion.Value?.EntityModel is not CPlugPrefab convertedPrefab)
                throw new InvalidOperationException($"{source.Name} could not be converted to a prefab for merge: {conversion.Reason ?? "unknown reason"}");
            return convertedPrefab;
        }

        throw new InvalidOperationException($"{source.Name} has unsupported entity model type {node?.GetType().Name ?? "null"} for merge.");
    }

    private static string? GetSerializationWarning(Gbx<CGameItemModel> file, byte[]? originalBytes)
    {
        if (originalBytes is null) return null;

        using var output = new MemoryStream();
        file.Save(output);
        return output.ToArray().SequenceEqual(originalBytes)
            ? null
            : "Saving this item re-encodes native GBX data before edits. Some items are valid after re-encoding, but Trackmania compatibility must be verified for this source.";
    }
}
