using System.Security.Cryptography;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.MwFoundations;
using GBX.NET.Engines.Plug;

namespace TM_Item_Studio.Models.FlagPackaging;

/// <summary>Conservative decoded-graph match, independent of native Item identity and raw node numbering.</summary>
internal sealed record FlagItemGraph(string StructureHash, string[] VisualHashes, int[] VerticesPerFrame, int[] TrianglesPerFrame)
{
    internal static FlagItemGraph Inspect(Gbx<CGameItemModel> document)
    {
        var item = document.Node;
        PackagingGuard.That(FlagItemArchive.IsStadium(item), "Flag Item must belong to Stadium");
        PackagingGuard.That(item.VisModelCustom is null && item.VisModelCustomFile is null
            && item.PhyModelCustom is null && item.PhyModelCustomFile is null && item.ArchetypeRef == "",
            "Alternate root visual/physics models and archetype routing are unsupported");
        PackagingGuard.That(item.EntityModel is NPlugItem_SVariantList { Variants.Length: 1 },
            "Expected one inline flag variant");
        var variants = (NPlugItem_SVariantList)item.EntityModel!;
        var variant = variants.Variants![0];
        PackagingGuard.That(variant.EntityModelFile is null && variant.EntityModel is CPlugPrefab { Ents.Length: 1 or 2 }
            && variant.Tags?.GetValueOrDefault("Type") == "Flag", "Expected the supported inline non-kinematic flag prefab");
        var prefab = (CPlugPrefab)variant.EntityModel!;
        var entities = prefab.Ents!;
        PackagingGuard.That(entities.All(e => e.ModelFile is null)
            && entities[^1].Model is CPlugDynaObjectModel
            && (entities.Length == 1 || entities[0].Model is CPlugStaticObjectModel && entities[0].Params is null),
            "Only a cloth Dyna, optionally preceded by the static pole, is supported");
        var cloth = (CPlugDynaObjectModel)entities[^1].Model!;
        PackagingGuard.That(entities[^1].Params is NPlugDynaObjectModel_SInstanceParams { IsKinematic: false },
            "Cloth instance must be non-kinematic");
        var parameters = (NPlugDynaObjectModel_SInstanceParams)entities[^1].Params!;
        PackagingGuard.That(float.IsFinite(parameters.PeriodSc) && parameters.PeriodSc is >= .25f and <= 600
            && float.IsFinite(parameters.PeriodScMax) && parameters.PeriodScMax >= parameters.PeriodSc && parameters.PeriodScMax <= 600
            && float.IsFinite(parameters.Phase01) && parameters.Phase01 is >= 0 and <= 1
            && float.IsFinite(parameters.Phase01Max) && parameters.Phase01Max >= parameters.Phase01 && parameters.Phase01Max <= 1,
            "Cloth timing is outside the supported template range");
        PackagingGuard.That(!cloth.IsStatic && !cloth.DynamizeOnSpawn && cloth.LocAnim is null && cloth.LocAnimFile is null
            && cloth.DynaShape is null && cloth.DynaShapeFile is null && cloth.StaticShape is null && cloth.StaticShapeFile is null
            && cloth.WaterModel is null && cloth.MeshFile is null && cloth.Mesh?.Visuals?.Length == 5,
            "Cloth must retain the inline five-LOD non-kinematic template without shapes or local animation");
        var mesh = cloth.Mesh!;
        PackagingGuard.That(mesh.Skel is null && mesh.PreLightGenerator is null && (mesh.Joints?.Length ?? 0) == 0
            && (mesh.Lights?.Length ?? 0) == 0 && (mesh.LightUserModels?.Length ?? 0) == 0
            && (mesh.LightInsts?.Length ?? 0) == 0 && (mesh.FakeOccProjs?.Length ?? 0) == 0,
            "Skinned, generated, lit or effect-bearing cloth meshes are unsupported");
        PackagingGuard.That(mesh.Materials is { Length: 1 } && mesh.Materials[0].File is {} material
            && !material.UseFile && BaseName(material.FilePath) == FlagItemArchive.Placeholder
            && item.MaterialModifier is null && item.MaterialModifierFile is {} modifier
            && !modifier.UseFile && BaseName(modifier.FilePath) == FlagItemArchive.Modifier
            && (mesh.CustomMaterials?.Length ?? 0) == 0 && (mesh.MaterialInsts?.Length ?? 0) == 0,
            "Flag Item does not use the supported placeholder and modifier route");

        var hashes = new List<string>(); var counts = new List<int>(); var triangles = new List<int>();
        long vertexTotal = 0;
        foreach (var visual in mesh.Visuals!)
        {
            PackagingGuard.That(visual is CPlugVisualIndexedTriangles, "Unsupported cloth visual type");
            var v = (CPlugVisualIndexedTriangles)visual;
            PackagingGuard.That(v.SubVisuals is { Length: 86 } && v.Vertices.Length is > 0 and <= 1048576
                && v.Vertices.Length % 86 == 0 && v.VertexStreams.Count == 0,
                "Expected 86 complete inline frames in every cloth LOD");
            vertexTotal += v.Vertices.Length;
            PackagingGuard.That(vertexTotal <= 1048576, "Cloth vertex count exceeds the supported limit");
            var count = v.Vertices.Length / 86;
            PackagingGuard.That(v.IndexBuffer?.Indices is { Length: > 0 } indices && indices.Length <= 24576
                && indices.Length % 3 == 0 && indices.All(i => i >= 0 && i < count), "Cloth frame indices are invalid or exceed the supported limit");
            for (var frame = 0; frame < 86; frame++)
                PackagingGuard.That(v.SubVisuals![frame] == new Int3(frame * count, 0, v.IndexBuffer!.Indices.Length),
                    "Cloth frame table does not match its complete baked geometry");
            PackagingGuard.That(v.Vertices.All(p => Finite(p.Position) && p.Normal is {} n && Finite(n)),
                "Cloth has missing or non-finite geometry");
            for (var i = 0; i < count; i++) PackagingGuard.That(v.Vertices[i].Position == v.Vertices[85 * count + i].Position,
                "Cloth loop endpoint positions do not close");
            hashes.Add(NodeHash(v)); counts.Add(count); triangles.Add(v.IndexBuffer!.Indices.Length / 3);
        }
        // No root Item is serialized here. Canonical visuals contain every decoded frame,
        // index/UV/normal/bounds/chunk field; source and native writer may number nodes differently.
        var structure = JsonSerializer.SerializeToUtf8Bytes(new {
            variantVersion = variants.Version, variant.HiddenInManualCycle,
            // Avoid KeyValuePair constructor metadata: it is trimmed in published WASM.
            tags = variant.Tags!.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new { p.Key, p.Value }),
            prefabVersion = prefab.Version, prefab.U01, prefab.U02,
            // Both inputs have already had only their external material paths normalized
            // to siblings. Hash the entire decoded prefab as well, including auxiliary
            // graph fields and entity parameters that the bounded shape checks omit.
            completePrefabHash = NodeHash(prefab),
            transforms = entities.Select(e => new { e.Position, e.Rotation, e.U01 }),
            staticPole = entities.Length == 2 ? NodeHash((CPlugStaticObjectModel)entities[0].Model!) : null,
            parametersVersion = parameters.Version, parameters.PeriodSc, parameters.PeriodScMax, parameters.Phase01, parameters.Phase01Max,
            parameters.TextureId, parameters.IsKinematic, parameters.CastStaticShadow,
            clothVersion = cloth.Version, cloth.IsStatic, cloth.DynamizeOnSpawn, cloth.BreakSpeedKmh, cloth.Mass,
            cloth.LightAliveDurationScMin, cloth.LightAliveDurationScMax,
            dyna = new { cloth.U01, cloth.U02, cloth.U03, cloth.U04, cloth.U05, cloth.U06, cloth.U07, cloth.U08, cloth.U09, cloth.U10, cloth.LocAnimIsPhysical },
            mesh.ShadedGeoms, mesh.LodMaxDistAtFov90, mesh.Boxes, mesh.Flags, mesh.MaterialsFolderName,
            visualHashes = hashes
        }, new JsonSerializerOptions { IncludeFields = true });
        return new(Convert.ToHexString(SHA256.HashData(structure)), hashes.ToArray(), counts.ToArray(), triangles.ToArray());
    }

    internal void RequireMatch(FlagItemGraph saved)
    {
        PackagingGuard.That(StructureHash == saved.StructureHash && VisualHashes.SequenceEqual(saved.VisualHashes),
            "Embedded flag geometry, topology, transforms or timing does not match the source package; refusing repair");
    }
    static bool Finite(Vec3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    static string BaseName(string path) => path.Replace('\\', '/').Split('/').Last();
    static string NodeHash<T>(T node) where T : CMwNod
    {
        using var stream = new MemoryStream(); new Gbx<T>(node).Save(stream);
        return PackagingGuard.Hash(stream.ToArray());
    }
}
