using System.IO.Compression;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.MwFoundations;
using GBX.NET.Engines.Plug;

namespace TM_Item_Studio.Models;

/// <summary>
/// Authors the measured v6 early-material flag graph as a four-file package. The source document
/// is read only. Presets retain the exact tested bytes; source-mesh conversion is an explicitly
/// bounded, offline-verified authoring path, not a claim of native rendering or map portability.
/// </summary>
public static class ItemWavingFlagExport
{
    public enum Mode { SourceMesh, Swallowtail, Long250, Round }
    public enum AttachmentEdge { MinimumX, MaximumX, MinimumZ, MaximumZ }
    public readonly record struct AnchorPoint(float X, float Y, float Z)
    {
        public static AnchorPoint Origin => new(0, 0, 0);
        public Vector3 Vector => new(X, Y, Z);
    }
    public sealed record Options
    {
        public Mode Mode { get; init; } = Mode.SourceMesh;
        public AttachmentEdge Edge { get; init; } = AttachmentEdge.MinimumX;
        public float Amplitude { get; init; } = .25f;
        public float Wavelength { get; init; } = 4;
        public float PeriodSeconds { get; init; } = 12;
        public string? OutputName { get; init; }
        /// <summary>Viewport-selected visual paths. Empty means the complete supported item mesh.</summary>
        public IReadOnlyList<string> SelectionPaths { get; init; } = Array.Empty<string>();
        /// <summary>Item-local transform owner position for the converted cloth.</summary>
        public AnchorPoint? Anchor { get; init; }
    }
    public sealed record Capability(bool Supported, string Summary, int VertexCount, int TriangleCount,
        int MeshCount, IReadOnlyList<string> Warnings);
    public sealed record Result(byte[] Bytes, string FileName, string ItemFileName,
        IReadOnlyList<string> Warnings, string ManifestJson);

    public const int FrameCount = 86;
    public const int MaximumSourceVertices = 2048;
    public const int MaximumSourceTriangles = 8192;
    private const string Placeholder = "TMIS_MatRemap_Placeholder.Material.Gbx";
    private const string Skin = "TMIS_MatRemap.GameSkin.Gbx";
    private const string Modifier = "TMIS_MatRemap.TerrainModifier.Gbx";
    private const string LongSeed = "TMIS_MatRemap_Long250.Item.Gbx";
    // This is the same flag graph after Trackmania's native Item save path. It is
    // intentionally separate from LongSeed: the compact C archive is a good
    // authoring seed, while this uncompressed U archive preserves native
    // node/body and sibling-reference metadata for map embedding.
    private const string NativeSavedLongSeed = "TMIS_MatRemap_NativeSaved_Long250.Item.Gbx";
    private static readonly Dictionary<string, string> ResourceHashes = new(StringComparer.Ordinal)
    {
        [LongSeed] = "D4F44EB23EE07B0C59CC0E9A976EAE05405B4DDE139BB2673C429F327EAFE751",
        [NativeSavedLongSeed] = "021B11E5B775C73E0FA2EE54625F214AA7ECA464BD10E6A3FFFC7BFBB3508E55",
        ["TMIS_MatRemap_Swallowtail.Item.Gbx"] = "81B749218584A9CB399AF27088E049132C2E06AA029A21E24137298C0138376D",
        ["TMIS_MatRemap_Round.Item.Gbx"] = "C55638E2B2DE29A1A4ABED7994AB613A2B7ACFE850FEEFFF1E30243005E2A14B",
        [Placeholder] = "CB3B703C2B5BED9F359919C59B24A08730EE833FC5E422C40062DA061CDA588A",
        [Skin] = "DC6118300D7C4FDBA9DAA737130B45249A5E46F1AA56AC17A6D7F658CFA6859E",
        [Modifier] = "D276FA3E545347EC01533401B9A44A8EC2254BCDE9C2C7BFA4FAFC7CE20D942B"
    };
    private static readonly Lazy<IReadOnlyDictionary<string, byte[]>> Resources = new(LoadResources);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Inspects only decoded inline static geometry; never resolves a file or edits the item.</summary>
    public static Capability GetCapability(Gbx<CGameItemModel> gbx)
    {
        ArgumentNullException.ThrowIfNull(gbx);
        try
        {
            var source = ReadSource(gbx);
            return new(true, "Supported inline static triangle mesh. Native rendering of converted geometry is unverified.",
                source.Positions.Length, source.Indices.Length / 3, source.MeshCount, source.Warnings);
        }
        catch (Exception e) when (e is NotSupportedException or InvalidDataException or ArgumentException)
        {
            return new(false, e.Message, 0, 0, 0, Array.Empty<string>());
        }
    }

    /// <summary>Returns a ZIP containing one Item, three pinned sibling dependencies, manifest and English instructions.</summary>
    public static Result Export(Gbx<CGameItemModel> gbx, Options? options = null, string? preferredId = null)
    {
        ArgumentNullException.ThrowIfNull(gbx);
        options ??= new();
        ValidateOptions(options);
        // Validate the caller's graph before touching resources. ReadSource makes its own arrays;
        // it does not Save, call GetModel, or ask an External<T> for its Node.
        var source = options.Mode == Mode.SourceMesh ? ReadSource(gbx, options.SelectionPaths) : null;
        var resource = Resources.Value;
        var warnings = CommonWarnings().ToList();
        byte[] item;
        object details;
        var baseName = SafeName(options.OutputName ?? (source is null ? "TMIS_" + options.Mode : preferredId ?? gbx.Node.Name));
        var folder = baseName + "_WavingFlag";
        var itemFile = folder + ".Item.Gbx";
        if (source is null)
        {
            var presetFile = "TMIS_MatRemap_" + options.Mode + ".Item.Gbx";
            item = resource[presetFile].ToArray();
            ValidateFlagGraph(Parse(item));
            warnings.Add("Preset geometry, animation timing and bytes are retained exactly. Wave controls apply only to SourceMesh mode.");
            details = new { preset = options.Mode.ToString(), sourceResource = presetFile, exactTestedBytes = true,
                sourceSha256 = ResourceHashes[presetFile], originalPoleRetained = true,
                status = "Previously observed native graph; this package export itself received offline checks only." };
        }
        else
        {
            warnings.AddRange(source.Warnings);
            warnings.Add("Converted source-mesh animation is offline verified only; native loading, waving, LODs and cold-map rendering are unverified.");
            warnings.Add("All supported source render triangles become cloth. Source materials, vertex colours, placement metadata and collision/physics are replaced by the flag host; no source pole or rigid submesh is retained.");
            warnings.Add("All five output LODs use the same source topology. This preserves shape but does not simplify distant geometry.");
            warnings.Add("After native saving, use the package-aware saved-map repair step with this exact ZIP. File repair does not establish native rendering of a newly converted mesh.");
            if (options.Amplitude == 0) warnings.Add("Amplitude is zero: the exported sequence is a stationary animation control.");
            var working = Parse(resource[LongSeed]);
            ValidateFlagGraph(working);
            var prefab = Prefab(working.Node);
            var cloth = prefab.Ents![1];
            var mesh = ((CPlugDynaObjectModel)cloth.Model!).Mesh!;
            // The source coordinates already contain all static entity transforms. Do not leave
            // the template's elevated/rotated cloth rest transform on those item-space positions.
            var anchor = options.Anchor?.Vector ?? Vector3.Zero;
            if (options.Anchor is { } authoredAnchor)
            {
                warnings.Add($"The viewport anchor is authored at item-local ({authoredAnchor.X:R}, {authoredAnchor.Y:R}, {authoredAnchor.Z:R}); the selected geometry is rebased so that point follows the cloth host.");
                source = source with { Positions = source.Positions.Select(p => p - anchor).ToArray() };
            }
            cloth.Position = G(anchor);
            cloth.Rotation = Quat.Identity;
            prefab.Ents = [cloth];
            var timing = (NPlugDynaObjectModel_SInstanceParams)cloth.Params!;
            timing.PeriodSc = timing.PeriodScMax = options.PeriodSeconds;
            timing.Phase01 = timing.Phase01Max = 0;
            working.Node.Name = baseName + " - waving cloth";
            var expected = Bake(mesh, source, options);
            item = Save(working);
            VerifyConverted(item, source, options, expected);
            details = new { sourceGeometrySha256 = GeometryHash(source), verticesPerFrame = source.Positions.Length,
                trianglesPerFrame = source.Indices.Length / 3, source.MeshCount, uvChannels = source.Uvs.Length,
                framesPerLod = FrameCount, lods = 5, edge = options.Edge.ToString(), options.Amplitude,
                options.Wavelength, options.PeriodSeconds,
                selectionPaths = options.SelectionPaths,
                anchor = options.Anchor,
                displacement = "perpendicular horizontal axis; amplitude * (distance/extent)^2 * sin(2*pi*distance/wavelength - 2*pi*frame/85)",
                normals = "inverse-transpose deformation Jacobian, normalized; missing source normals reconstructed from triangle areas",
                originalBounds = new { min = Components(source.Positions.Aggregate(Vector3.Min)), max = Components(source.Positions.Aggregate(Vector3.Max)) },
                anchoredEdgeBitwiseFixed = true, loopEndpointsBitwiseEqual = true,
                topologyAndUvValuesPreserved = true, collision = "none", originalPoleRetained = false,
                status = "Offline serialization and geometry checks passed; new source mesh has not been tested in Trackmania." };
        }

        var payloads = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [itemFile] = item,
            [Placeholder] = resource[Placeholder], [Skin] = resource[Skin], [Modifier] = resource[Modifier]
        };
        var manifest = JsonSerializer.Serialize(new
        {
            formatVersion = 1, exporter = "TM Item Studio waving flag package", mode = options.Mode.ToString(),
            itemFile, storedItemId = "", identity = "Ordinary user-FID path identity; the output file supplies the inventory identity.",
            requiresNativeFlag16m = true, testedNativeAnchorOrder = "Native flag before custom flags",
            standaloneSingleItem = false, nativeSavedMapRepairRequired = true,
            newSourceMeshNativeVerified = false, geometry = details,
            referenceLayout = new { ancestorLevel = 0, itemMaterialUseFile = false, itemModifierUseFile = false,
                gameSkinSourceUseFile = true, modifierSkinUseFile = false, dependencies = "same folder, sibling basenames" },
            files = payloads.Select(p => new { file = p.Key, bytes = p.Value.Length, sha256 = Hash(p.Value) }),
            warnings
        }, Json);
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            foreach (var (name, bytes) in payloads) Entry(zip, folder + "/" + name, bytes);
            Entry(zip, folder + "/manifest.json", Encoding.UTF8.GetBytes(manifest));
            Entry(zip, folder + "/README.md", Encoding.UTF8.GetBytes(Readme(itemFile, options.Mode)));
        }
        return new(memory.ToArray(), folder + ".zip", itemFile, warnings.ToArray(), manifest);
    }

    /// <summary>
    /// Imports the validated Blender mesh sequence and writes it into the same
    /// audited native flag host used by SourceMesh. The Blender rest frame must
    /// match the selected Item Studio visual in item-local coordinates.
    /// </summary>
    public static Result ExportBakedSequence(Gbx<CGameItemModel> gbx, BakedClothSequence sequence,
        Options? options = null, string? preferredId = null)
    {
        ArgumentNullException.ThrowIfNull(gbx);
        ArgumentNullException.ThrowIfNull(sequence);
        options ??= new();
        if (options.Mode != Mode.SourceMesh) throw new ArgumentException("Baked sequence import uses SourceMesh mode.", nameof(options));
        ValidateOptions(options);
        sequence.Validate();
        var source = ReadSource(gbx, options.SelectionPaths);
        var sourcePositions = source.Positions;
        var sourceUvs = source.Uvs[0].Select(uv => new Vector2(uv.X, uv.Y)).ToArray();
        var geometry = sequence.MatchItemGeometry(sourcePositions, sourceUvs, source.Indices);
        var anchor = sequence.Anchor?.Position is { Length: 3 } a ? new Vector3(a[0], a[1], a[2]) : Vector3.Zero;
        var resource = Resources.Value;
        var working = Parse(resource[NativeSavedLongSeed]);
        ValidateNativeSavedFlagGraph(working);
        // The package path supplies the ordinary user identity. Retain the
        // native-saved body/reference layout while making the downloadable item
        // safe to install under any chosen filename.
        working.Node.Ident = working.Node.Ident! with { Id = "" };
        var prefab = Prefab(working.Node);
        var cloth = prefab.Ents![1];
        cloth.Position = G(anchor);
        cloth.Rotation = Quat.Identity;
        prefab.Ents = [cloth];
        var timing = (NPlugDynaObjectModel_SInstanceParams)cloth.Params!;
        timing.PeriodSc = timing.PeriodScMax = sequence.PeriodSeconds;
        timing.Phase01 = timing.Phase01Max = 0;
        var mesh = ((CPlugDynaObjectModel)cloth.Model!).Mesh!;
        var n = geometry.Positions[0].Length;
        var expected = new List<CPlugVisual3D.Vertex[]>();
        for (var lod = 0; lod < mesh.Visuals!.Count; lod++)
        {
            var visual = (CPlugVisualIndexedTriangles)mesh.Visuals[lod];
            var prototype = visual.Vertices[0];
            var vertices = new CPlugVisual3D.Vertex[n * FrameCount];
            for (var frame = 0; frame < FrameCount; frame++)
            for (var i = 0; i < n; i++)
            {
                var p = geometry.Positions[frame][i] - anchor;
                var normal = geometry.Normals[frame][i];
                vertices[frame * n + i] = prototype with { Position = G(p), Normal = G(normal) };
            }
            visual.Vertices = vertices;
            SetSerializedCount(visual, vertices.Length);
            visual.IndexBuffer!.Indices = geometry.Indices.ToArray();
            visual.SubVisuals = Enumerable.Range(0, FrameCount)
                .Select(frame => new Int3(frame * n, 0, geometry.Indices.Length)).ToArray();
            var uvPrototype = visual.TexCoords[0];
            visual.TexCoords = [new CPlugVisual.TexCoordSet
            {
                Version = uvPrototype.Version, Flags = uvPrototype.Flags, U01 = uvPrototype.U01?.ToArray(),
                TexCoords = Enumerable.Range(0, FrameCount)
                    .SelectMany(_ => source.Uvs[0].Select(uv => new CPlugVisual.TexCoord(uv, null, null))).ToArray()
            }];
            visual.BoundingBox = Bounds(vertices.Select(v => v.Position));
            expected.Add(vertices);
        }
        var baseName = SafeName(options.OutputName ?? preferredId ?? gbx.Node.Name ?? "Blender_Baked_Cloth");
        working.Node.Name = baseName + " - Blender baked cloth";
        var item = Save(working);
        ValidateBakedSequenceItem(item, sequence, geometry, expected);
        var itemFile = baseName + "_WavingFlag.Item.Gbx";
        var folder = baseName + "_WavingFlag";
        var warnings = CommonWarnings().ToList();
        warnings.Add("Imported Blender baked cloth into a native-saved flag archive template; native Trackmania playback of this new sequence still requires a game test.");
        warnings.Add("The Blender rest frame, topology and UV0 were matched against the selected Item Studio mesh.");
        warnings.Add("The package uses the audited native flag material dependencies and requires a genuine native Stadium 16 m flag when map-embedded.");
        var payloads = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [itemFile] = item, [Placeholder] = resource[Placeholder], [Skin] = resource[Skin], [Modifier] = resource[Modifier]
        };
        var details = new
        {
            source = "Blender .tmcloth.json",
            sequenceFrames = sequence.FrameCount,
            sequencePeriodSeconds = sequence.PeriodSeconds,
            verticesPerFrame = n,
            trianglesPerFrame = geometry.Indices.Length / 3,
            lods = mesh.Visuals.Count,
            selectionPaths = options.SelectionPaths,
            anchor,
            topologyAndUvValuesPreserved = true,
            loopEndpointsBitwiseEqual = expected.All(v => v[0].Position == v[^1].Position && v[0].Normal == v[^1].Normal),
            status = "Offline sequence import and native-saved archive serialization passed; native game rendering remains to be verified."
        };
        var manifest = JsonSerializer.Serialize(new
        {
            formatVersion = 1, exporter = "TM Item Studio Blender baked cloth importer", mode = "BlenderBakedSequence",
            itemFile, storedItemId = "", identity = "Ordinary user-FID path identity; the output file supplies the inventory identity.",
            requiresNativeFlag16m = true, testedNativeAnchorOrder = "Native flag before custom flags",
            standaloneSingleItem = false, nativeSavedTemplate = true, nativeSavedMapRepairRequired = true, newSourceMeshNativeVerified = false,
            geometry = details,
            referenceLayout = new { ancestorLevel = 0, itemMaterialUseFile = false, itemModifierUseFile = false,
                gameSkinSourceUseFile = true, modifierSkinUseFile = false, dependencies = "same folder, sibling basenames", archive = "native-saved uncompressed GBX body" },
            files = payloads.Select(p => new { file = p.Key, bytes = p.Value.Length, sha256 = Hash(p.Value) }), warnings
        }, Json);
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            foreach (var (name, bytes) in payloads) Entry(zip, folder + "/" + name, bytes);
            Entry(zip, folder + "/manifest.json", Encoding.UTF8.GetBytes(manifest));
            Entry(zip, folder + "/README.md", Encoding.UTF8.GetBytes(Readme(itemFile, Mode.SourceMesh)));
        }
        return new(memory.ToArray(), folder + ".zip", itemFile, warnings.ToArray(), manifest);
    }

    private static void ValidateBakedSequenceItem(byte[] bytes, BakedClothSequence sequence,
        SequenceGeometry geometry, IReadOnlyList<CPlugVisual3D.Vertex[]> expected)
    {
        var parsed = Parse(bytes);
        ValidateNativeSavedReferences(parsed);
        var prefab = Prefab(parsed.Node);
        Require(prefab.Ents is { Length: 1 } && prefab.Ents[0].Model is CPlugDynaObjectModel,
            "Imported baked cloth has an unexpected entity graph.");
        var cloth = (CPlugDynaObjectModel)prefab.Ents[0].Model!;
        var timing = prefab.Ents[0].Params as NPlugDynaObjectModel_SInstanceParams;
        Require(timing is { IsKinematic: false } && timing.PeriodSc == sequence.PeriodSeconds && timing.PeriodScMax == sequence.PeriodSeconds
            && timing.Phase01 == 0 && timing.Phase01Max == 0, "Imported baked timing changed during serialization.");
        Require(cloth.Mesh?.Visuals is { Count: 5 }, "Imported baked cloth lost one or more LODs.");
        for (var lod = 0; lod < cloth.Mesh!.Visuals!.Count; lod++)
        {
            var visual = (CPlugVisualIndexedTriangles)cloth.Mesh.Visuals[lod];
            Require(visual.Vertices.SequenceEqual(expected[lod]) && visual.IndexBuffer!.Indices.SequenceEqual(geometry.Indices), "Imported baked geometry changed during serialization.");
            Require(visual.SubVisuals is { Length: FrameCount } && visual.SubVisuals[^1] == new Int3((FrameCount - 1) * geometry.Positions[0].Length, 0, geometry.Indices.Length), "Imported baked frame table changed.");
            Require(visual.Vertices.Take(geometry.Positions[0].Length).SequenceEqual(visual.Vertices.Skip((FrameCount - 1) * geometry.Positions[0].Length).Take(geometry.Positions[0].Length)), "Imported baked loop is not closed.");
        }
    }

    private static IReadOnlyList<CPlugVisual3D.Vertex[]> Bake(CPlugSolid2Model mesh, Source source, Options options)
    {
        bool xAxis = options.Edge is AttachmentEdge.MinimumX or AttachmentEdge.MaximumX;
        bool reverse = options.Edge is AttachmentEdge.MaximumX or AttachmentEdge.MaximumZ;
        float low = source.Positions.Min(p => xAxis ? p.X : p.Z), high = source.Positions.Max(p => xAxis ? p.X : p.Z);
        float extent = high - low;
        if (!float.IsFinite(extent) || extent < .00001f)
            throw new NotSupportedException("The chosen attachment axis has no usable extent. Choose the other X/Z axis.");
        float origin = reverse ? high : low, sign = reverse ? -1 : 1;
        var expected = new List<CPlugVisual3D.Vertex[]>();
        foreach (var visual in mesh.Visuals!.Cast<CPlugVisualIndexedTriangles>())
        {
            var prototype = visual.Vertices[0];
            int n = source.Positions.Length;
            var vertices = new CPlugVisual3D.Vertex[n * FrameCount];
            for (var frame = 0; frame < FrameCount - 1; frame++)
            for (var i = 0; i < n; i++)
            {
                var p = source.Positions[i]; var normal = source.Normals[i];
                float distance = sign * ((xAxis ? p.X : p.Z) - origin);
                double u = distance / extent, k = 2 * Math.PI / options.Wavelength;
                double angle = k * distance - 2 * Math.PI * frame / (FrameCount - 1);
                float displacement = (float)(options.Amplitude * u * u * Math.Sin(angle));
                float derivative = (float)(sign * options.Amplitude * (2 * u / extent * Math.Sin(angle) + u * u * k * Math.Cos(angle)));
                // Preserve the fixed edge without floating-point arithmetic, including signed zeros.
                var moved = distance == 0 ? p : xAxis ? p + new Vector3(0, 0, displacement) : p + new Vector3(displacement, 0, 0);
                var adjusted = xAxis ? new Vector3(normal.X - derivative * normal.Z, normal.Y, normal.Z)
                    : new Vector3(normal.X, normal.Y, normal.Z - derivative * normal.X);
                adjusted = distance == 0 || derivative == 0 ? normal : Vector3.Normalize(adjusted);
                if (!Finite(moved) || !Finite(adjusted)) throw new NotSupportedException("The wave settings overflow this source geometry.");
                vertices[frame * n + i] = prototype with { Position = G(moved), Normal = G(adjusted) };
                if (distance == 0 && (vertices[frame * n + i].Position != G(p) || vertices[frame * n + i].Normal != G(normal)))
                    throw new InvalidOperationException("The fixed attachment edge changed.");
            }
            Array.Copy(vertices, 0, vertices, (FrameCount - 1) * n, n);
            visual.Vertices = vertices;
            SetSerializedCount(visual, vertices.Length);
            visual.IndexBuffer!.Indices = source.Indices.ToArray();
            visual.SubVisuals = Enumerable.Range(0, FrameCount).Select(f => new Int3(f * n, 0, source.Indices.Length)).ToArray();
            var uvPrototype = visual.TexCoords[0];
            visual.TexCoords = source.Uvs.Select(channel => new CPlugVisual.TexCoordSet
            {
                Version = uvPrototype.Version, Flags = uvPrototype.Flags, U01 = uvPrototype.U01?.ToArray(),
                TexCoords = Enumerable.Range(0, FrameCount).SelectMany(_ => channel.Select(uv => new CPlugVisual.TexCoord(uv, null, null))).ToArray()
            }).ToArray();
            visual.BoundingBox = Bounds(vertices.Select(v => v.Position));
            expected.Add(vertices);
        }
        return expected;
    }

    private static void VerifyConverted(byte[] bytes, Source source, Options options, IReadOnlyList<CPlugVisual3D.Vertex[]> expected)
    {
        var parsed = Parse(bytes);
        ValidateReferences(parsed);
        var prefab = Prefab(parsed.Node);
        Require(prefab.Ents is { Length: 1 }, "Converted cloth has an unexpected entity graph.");
        var cloth = prefab.Ents![0];
        var expectedAnchor = options.Anchor?.Vector ?? Vector3.Zero;
        Require(V(cloth.Position) == expectedAnchor && cloth.Rotation == Quat.Identity, "Converted cloth rest transform changed.");
        var dyna = cloth.Model as CPlugDynaObjectModel ?? throw new InvalidDataException("Converted cloth lost its ordinary Dyna host.");
        var timing = cloth.Params as NPlugDynaObjectModel_SInstanceParams;
        Require(timing is not null && !timing.IsKinematic && timing.PeriodSc == options.PeriodSeconds && timing.PeriodScMax == options.PeriodSeconds
            && timing.Phase01 == 0 && timing.Phase01Max == 0 && dyna.LocAnim is null && dyna.References.LocAnim.File is null
            && dyna.DynaShape is null && dyna.StaticShape is null, "Converted timing/physics graph changed.");
        var mesh = dyna.Mesh!;
        Require(mesh.Visuals is { Count: 5 }, "Converted cloth lost a LOD.");
        int n = source.Positions.Length;
        for (int lod = 0; lod < 5; lod++)
        {
            var v = (CPlugVisualIndexedTriangles)mesh.Visuals![lod];
            Require(v.Vertices.SequenceEqual(expected[lod]) && v.IndexBuffer!.Indices.SequenceEqual(source.Indices), "Vertex data or topology changed during serialization.");
            Require(v.SubVisuals is { Length: FrameCount } && v.VertexStreams.Count == 0 && v.TexCoords.Length == source.Uvs.Length,
                "Converted frame/UV layout changed during serialization.");
            for (int frame = 0; frame < FrameCount; frame++)
            {
                Require(v.SubVisuals![frame] == new Int3(frame * n, 0, source.Indices.Length), "Converted frame table changed.");
                for (int channel = 0; channel < source.Uvs.Length; channel++)
                    Require(v.TexCoords[channel].TexCoords.Length == n * FrameCount && v.TexCoords[channel].TexCoords.Skip(frame * n).Take(n).Select(t => t.UV).SequenceEqual(source.Uvs[channel]), "Source UV values changed.");
                for (int i = 0; i < n; i++)
                {
                    var p = v.Vertices[frame * n + i];
                    Require(Inside(v.BoundingBox, p.Position) && p.Normal is { } normal && Finite(V(normal)) && Math.Abs(V(normal).Length() - 1) < .00001,
                        "Converted bounds or normals are invalid.");
                }
                for (int i = 0; i < source.Indices.Length; i += 3)
                    Require(Area(V(v.Vertices[frame * n + source.Indices[i]].Position), V(v.Vertices[frame * n + source.Indices[i + 1]].Position), V(v.Vertices[frame * n + source.Indices[i + 2]].Position)) > 1e-12,
                        "The requested wave collapses a source triangle.");
            }
            for (int i = 0; i < n; i++) Require(v.Vertices[i] == v.Vertices[(FrameCount - 1) * n + i], "Converted loop is not bitwise closed.");
        }
    }

    private static Source ReadSource(Gbx<CGameItemModel> gbx, IReadOnlyList<string>? selectionPaths = null)
    {
        if (gbx.RefTable?.Files.Count > 0) throw new NotSupportedException("Source-mesh conversion requires fully inline geometry and materials; external references are not resolved.");
        var item = gbx.Node ?? throw new NotSupportedException("The source item has no readable node.");
        if (item.VisModelCustom is not null || item.References.VisModelCustom.File is not null || item.PhyModelCustom is not null || item.References.PhyModelCustom.File is not null
            || item.MaterialModifier is not null || item.References.MaterialModifier.File is not null || !string.IsNullOrEmpty(item.ArchetypeRef))
            throw new NotSupportedException("Custom legacy models, material modifiers, physics overrides and archetypes are outside the bounded source-mesh converter.");
        var builder = new SourceBuilder(selectionPaths);
        builder.Visit(item.EntityModel, Matrix4x4.Identity, 0, "entityModel");
        return builder.Finish();
    }

    private sealed record Source(Vector3[] Positions, Vector3[] Normals, int[] Indices, Vec2[][] Uvs, int MeshCount, string[] Warnings);
    private sealed class SourceBuilder
    {
        private readonly List<Vector3> positions = [], normals = [];
        private readonly List<int> indices = [];
        private List<Vec2>[]? uvs;
        private readonly HashSet<CMwNod> active = new(ReferenceEqualityComparer.Instance);
        private readonly List<string> warnings = [];
        private int meshes, occurrences;
        private readonly HashSet<string> selectionPaths;
        public SourceBuilder(IReadOnlyList<string>? selectionPaths)
        {
            this.selectionPaths = selectionPaths is null
                ? new HashSet<string>(StringComparer.Ordinal)
                : selectionPaths.Where(path => !string.IsNullOrWhiteSpace(path)).ToHashSet(StringComparer.Ordinal);
        }
        private bool IsSelected(string path)
            => selectionPaths.Count == 0 || selectionPaths.Any(target =>
                string.Equals(target, path, StringComparison.Ordinal)
                || target.EndsWith("/" + path, StringComparison.Ordinal)
                || path.EndsWith("/" + target, StringComparison.Ordinal));
        public void Visit(CMwNod? node, Matrix4x4 transform, int depth, string path)
        {
            if (node is null) throw new NotSupportedException("An inline source entity/mesh is missing; no external resolver is used.");
            if (depth > 16 || ++occurrences > 256 || !active.Add(node)) throw new NotSupportedException("The source graph is cyclic or exceeds the bounded traversal budget.");
            try
            {
                switch (node)
                {
                    case NPlugItem_SVariantList list:
                        if (list.Variants is not { Length: 1 }) throw new NotSupportedException("Source-mesh conversion requires exactly one concrete variant.");
                        var variant = list.Variants[0];
                        if (variant.References.EntityModel.File is not null) throw new NotSupportedException("External variant geometry is not supported.");
                        Visit(variant.EntityModel, transform, depth + 1, path + "/variant:0/entityModel"); break;
                    case CPlugPrefab prefab:
                        if (prefab.Ents is not { Length: > 0 }) throw new NotSupportedException("The source prefab is empty.");
                        for (var i = 0; i < prefab.Ents.Length; i++)
                        {
                            var ent = prefab.Ents[i];
                            if (ent is null || ent.References.Model.File is not null || ent.Params is not null) throw new NotSupportedException("External, parameterized or moving prefab entities are not supported.");
                            var q = new Quaternion(ent.Rotation.X, ent.Rotation.Y, ent.Rotation.Z, ent.Rotation.W);
                            if (!Finite(V(ent.Position)) || !float.IsFinite(q.LengthSquared()) || Math.Abs(q.LengthSquared() - 1) > .001)
                                throw new NotSupportedException("Source entity transforms must be finite rigid transforms with unit quaternions.");
                            var local = Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(q)) * Matrix4x4.CreateTranslation(V(ent.Position));
                            Visit(ent.Model, local * transform, depth + 1, path + $"/ent:{i}");
                        }
                        break;
                    case CGameCommonItemEntityModel common:
                        if (common.TriggerShape is not null || (common.PhyModel is not null && common.PhyModel is not CPlugSurface)
                            || (common.StaticObject is not null && common.VisModel is not null))
                            throw new NotSupportedException("Common-item triggers, non-static physics or simultaneous render representations are unsupported.");
                        if (common.PhyModel is not null) warnings.Add("Source static collision is omitted from the cloth package.");
                        if (common.StaticObject is not null) Visit(common.StaticObject, transform, depth + 1, path + "/staticObject");
                        else Visit(common.VisModel, transform, depth + 1, path + "/visModel");
                        break;
                    case CPlugStaticObjectModel model:
                        if (model.References.Mesh.File is not null || model.References.Shape.File is not null) throw new NotSupportedException("External static geometry/collision is unsupported.");
                        if (model.IsMeshCollidable || model.Shape is not null) warnings.Add("Source static collision is omitted from the cloth package.");
                        Visit(model.Mesh, transform, depth + 1, path + "/mesh"); break;
                    case CPlugSolid2Model solid: Solid(solid, transform, path); break;
                    default: throw new NotSupportedException($"{node.GetType().Name} is outside the supported inline static triangle graph. Moving, legacy tree and constraint nodes are refused.");
                }
            }
            finally { active.Remove(node); }
        }
        private void Solid(CPlugSolid2Model solid, Matrix4x4 transform, string path)
        {
            if (solid.Skel is not null || solid.PreLightGenerator is not null || solid.Joints is { Length: > 0 }
                || solid.Lights is { Length: > 0 } || solid.LightUserModels is { Count: > 0 } || solid.LightInsts is { Length: > 0 }
                || solid.FakeOccProjs is { Length: > 0 }) throw new NotSupportedException("Skinned, generated, lit or effect-bearing source meshes are unsupported.");
            if (solid.References.Materials?.Any(m => m?.File is not null) == true) throw new NotSupportedException("External source materials are not resolved.");
            if (solid.Visuals is not { Count: > 0 }) throw new NotSupportedException("The source mesh contains no visuals.");
            if (solid.LodMaxDistAtFov90 is { Length: > 1 }) throw new NotSupportedException("Source meshes with multiple LOD distances are unsupported; select a concrete static mesh first.");
            var shaded = solid.ShadedGeoms ?? [];
            if (shaded.Length > 0 && (shaded.Length != solid.Visuals.Count || shaded.Any(g => g is null || g.VisualIndex < 0 || g.VisualIndex >= solid.Visuals.Count)
                || shaded.Select(g => g.VisualIndex).Distinct().Count() != solid.Visuals.Count || shaded.Select(g => g.LodMask).Distinct().Count() != 1))
                throw new NotSupportedException("Ambiguous submesh/LOD mappings are unsupported.");
            if (shaded.Length == 0 && solid.Visuals.Count != 1) throw new NotSupportedException("Multiple source visuals require one unambiguous common-LOD mapping each.");
            meshes++;
            for (var visualIndex = 0; visualIndex < solid.Visuals.Count; visualIndex++)
            {
                var raw = solid.Visuals[visualIndex];
                if (!IsSelected(path + $"/visual:{visualIndex}")) continue;
                if (raw is not CPlugVisualIndexedTriangles v || v.VertexStreams.Count != 0 || v.SkinData is not null || v.MorphCount != 0
                    || v.SubVisuals is { Length: > 0 } || v.Splits is { Length: > 0 } || v.BitmapElemToPacks is { Length: > 0 } || v.UvGroups is { Length: > 0 }
                    || (v.Tangents?.Length ?? 0) != 0 || (v.BiTangents?.Length ?? 0) != 0
                    || v.Chunks.OfType<CPlugVisual3D.Chunk0902C004>().Any(c => c.Tangents1Count != 0 || c.Tangents2Count != 0))
                    throw new NotSupportedException("Only inline static indexed triangles are supported; streams, tangents, skin, morph, frames and split layouts are refused.");
                int count = v.Vertices.Length;
                var ix = v.IndexBuffer?.Indices;
                if (count < 3 || ix is not { Length: > 0 } || ix.Length % 3 != 0 || ix.Any(i => i < 0 || i >= count)) throw new NotSupportedException("Source triangle indices are missing, incomplete or outside the vertex buffer.");
                if (positions.Count + count > MaximumSourceVertices || indices.Count / 3 + ix.Length / 3 > MaximumSourceTriangles) throw new NotSupportedException($"Source exceeds the {MaximumSourceVertices}-vertex/{MaximumSourceTriangles}-triangle animation budget.");
                if (v.Vertices.Any(p => !Finite(V(p.Position)) || (p.Color is {} color && (!float.IsFinite(color.X) || !float.IsFinite(color.Y) || !float.IsFinite(color.Z) || !float.IsFinite(color.W))) || p.U08 is not null || p.U09 is not null)) throw new NotSupportedException("Source positions or optional vertex attributes are unsupported/nonfinite.");
                if (v.TexCoords.Length is < 1 or > 4 || v.TexCoords.Any(c => c is null || c.TexCoords.Length != count || c.U01 is { Length: > 0 }
                    || c.TexCoords.Any(t => !float.IsFinite(t.UV.X) || !float.IsFinite(t.UV.Y) || t.U01 is not null || t.U02 is not null)))
                    throw new NotSupportedException("Source requires one to four complete finite UV channels without auxiliary UV payloads.");
                uvs ??= Enumerable.Range(0, v.TexCoords.Length).Select(_ => new List<Vec2>()).ToArray();
                if (uvs.Length != v.TexCoords.Length) throw new NotSupportedException("Source submeshes must have the same UV channel count.");
                var points = v.Vertices.Select(p => Vector3.Transform(V(p.Position), transform)).ToArray();
                if (points.Any(p => !Finite(p) || Math.Max(Math.Abs(p.X), Math.Max(Math.Abs(p.Y), Math.Abs(p.Z))) > 1_000_000)) throw new NotSupportedException("Source transforms overflow the bounded geometry range.");
                var ns = new Vector3[count];
                bool supplied = v.Vertices.All(p => p.Normal.HasValue);
                if (!supplied && v.Vertices.Any(p => p.Normal.HasValue)) throw new NotSupportedException("Source normals are only partially present.");
                for (int i = 0; i < ix.Length; i += 3)
                {
                    var cross = Vector3.Cross(points[ix[i + 1]] - points[ix[i]], points[ix[i + 2]] - points[ix[i]]);
                    if (!Finite(cross) || cross.LengthSquared() <= 1e-24) throw new NotSupportedException("Source has a degenerate or overflowing triangle.");
                    if (!supplied) { ns[ix[i]] += cross; ns[ix[i + 1]] += cross; ns[ix[i + 2]] += cross; }
                }
                for (int i = 0; i < count; i++)
                {
                    var normal = supplied ? Vector3.TransformNormal(V(v.Vertices[i].Normal!.Value), transform) : ns[i];
                    if (!Finite(normal) || !float.IsFinite(normal.LengthSquared()) || normal.LengthSquared() <= 1e-20) throw new NotSupportedException("Source normals are zero/nonfinite or cannot be reconstructed for every vertex.");
                    ns[i] = Vector3.Normalize(normal);
                }
                if (!supplied) warnings.Add("Missing source normals were reconstructed from triangle areas.");
                int offset = positions.Count;
                positions.AddRange(points); normals.AddRange(ns); indices.AddRange(ix.Select(i => i + offset));
                for (int c = 0; c < uvs.Length; c++) uvs[c].AddRange(v.TexCoords[c].TexCoords.Select(t => t.UV));
            }
        }
        public Source Finish()
        {
            if (positions.Count == 0 || uvs is null) throw new NotSupportedException("The source has no supported triangle geometry.");
            return new(positions.ToArray(), normals.ToArray(), indices.ToArray(), uvs.Select(c => c.ToArray()).ToArray(), meshes, warnings.Distinct().ToArray());
        }
    }

    private static void ValidateOptions(Options o)
    {
        if (!Enum.IsDefined(o.Mode) || !Enum.IsDefined(o.Edge)) throw new ArgumentException("Unknown waving flag mode or attachment edge.");
        if (o.Mode != Mode.SourceMesh) return;
        if (!float.IsFinite(o.Amplitude) || o.Amplitude is < 0 or > 20) throw new ArgumentOutOfRangeException(nameof(o.Amplitude), "Amplitude must be between 0 and 20 metres.");
        if (!float.IsFinite(o.Wavelength) || o.Wavelength is < .01f or > 10000) throw new ArgumentOutOfRangeException(nameof(o.Wavelength), "Wavelength must be between 0.01 and 10000 metres.");
        if (!float.IsFinite(o.PeriodSeconds) || o.PeriodSeconds is < .25f or > 600) throw new ArgumentOutOfRangeException(nameof(o.PeriodSeconds), "Period must be between 0.25 and 600 seconds.");
        if (o.Anchor is { } anchor && (!float.IsFinite(anchor.X) || !float.IsFinite(anchor.Y) || !float.IsFinite(anchor.Z)
            || Math.Max(Math.Abs(anchor.X), Math.Max(Math.Abs(anchor.Y), Math.Abs(anchor.Z))) > 1_000_000))
            throw new ArgumentOutOfRangeException(nameof(o.Anchor), "The viewport anchor must be finite and within the bounded item coordinate range.");
        if (o.SelectionPaths.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Selection paths cannot be empty.", nameof(o.SelectionPaths));
    }
    private static IReadOnlyDictionary<string, byte[]> LoadResources()
    {
        var assembly = typeof(ItemWavingFlagExport).Assembly;
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (name, hash) in ResourceHashes)
        {
            var fullName = assembly.GetManifestResourceNames().SingleOrDefault(n => n.EndsWith(".Resources.Flags." + name, StringComparison.Ordinal));
            using var stream = fullName is null ? null : assembly.GetManifestResourceStream(fullName);
            if (stream is null) throw new InvalidOperationException($"Bundled waving flag resource '{name}' is missing. Embed Resources/Flags/*.Gbx when building the studio.");
            using var memory = new MemoryStream(); stream.CopyTo(memory);
            var bytes = memory.ToArray();
            if (Hash(bytes) != hash) throw new InvalidDataException($"Bundled waving flag resource '{name}' does not match the audited v6 bytes.");
            result.Add(name, bytes);
        }
        // Pinning the complete dependency bytes also preserves the GameSkin's genuine FID
        // reference (UseFile=true); do not regenerate it from a loaded material Node.
        var skin = Gbx.ParseHeader(new MemoryStream(result[Skin]));
        Require(skin.RefTable?.AncestorLevel == 0 && skin.RefTable.Files is { Count: 1 }
            && skin.RefTable.Files.Single().FilePath == Placeholder && skin.RefTable.Files.Single().UseFile, "GameSkin source is not the pinned sibling FID reference.");
        return result;
    }
    private static void ValidateFlagGraph(Gbx<CGameItemModel> document)
    {
        ValidateReferences(document);
        ValidateClothGraph(document, "The audited flag template graph changed.");
    }
    private static void ValidateNativeSavedFlagGraph(Gbx<CGameItemModel> document)
    {
        ValidateNativeSavedReferences(document);
        ValidateClothGraph(document, "The native-saved flag template graph changed.");
    }
    private static void ValidateClothGraph(Gbx<CGameItemModel> document, string message)
    {
        var prefab = Prefab(document.Node);
        Require(prefab.Ents is { Length: 2 } && prefab.Ents[0].Model is CPlugStaticObjectModel && prefab.Ents[1].Model is CPlugDynaObjectModel,
            message);
        var d = (CPlugDynaObjectModel)prefab.Ents![1].Model!;
        Require(d.LocAnim is null && d.References.LocAnim.File is null && d.DynaShape is null && d.StaticShape is null
            && prefab.Ents[1].Params is NPlugDynaObjectModel_SInstanceParams { IsKinematic: false }, "The template is not the audited non-kinematic cloth host.");
        Require(d.Mesh?.Visuals is { Count: 5 } && d.Mesh.Boxes is null, "The audited five-LOD cloth mesh changed.");
        foreach (var raw in d.Mesh!.Visuals!)
        {
            Require(raw is CPlugVisualIndexedTriangles, "The template visual type changed.");
            var v = (CPlugVisualIndexedTriangles)raw;
            Require(v.SubVisuals is { Length: FrameCount } && v.Vertices.Length % FrameCount == 0 && v.VertexStreams.Count == 0
                && (v.Tangents?.Length ?? 0) == 0 && (v.BiTangents?.Length ?? 0) == 0, "The template animation layout changed.");
        }
    }
    private static void ValidateReferences(Gbx<CGameItemModel> document)
    {
        Require(ItemStoredIdentity.Read(document.Node).Id == "" && document.Node.ArchetypeRef == "" && document.RefTable?.AncestorLevel == 0
            && document.RefTable.Files.Select(f => f.FilePath).SequenceEqual(new[] { Placeholder, Modifier })
            && document.RefTable.Files.All(f => f.Flags == 1 && !f.UseFile) && document.Node.References.MaterialModifier.File?.FilePath == Modifier,
            "The sibling-node material/modifier references or ordinary user identity changed.");
        var mesh = ((CPlugDynaObjectModel)Prefab(document.Node).Ents!.Last().Model!).Mesh!;
        Require(mesh.References.Materials is { Count: 1 } && mesh.References.Materials[0].File?.FilePath == Placeholder
            && (mesh.CustomMaterials?.Length ?? 0) == 0 && (mesh.MaterialInsts?.Count ?? 0) == 0, "The early placeholder material route changed.");
    }
    private static void ValidateNativeSavedReferences(Gbx<CGameItemModel> document)
    {
        Require(document.Node.ArchetypeRef == "" && document.RefTable?.AncestorLevel == 0
            && document.RefTable.Files.Select(f => f.FilePath).SequenceEqual(new[] { Placeholder, Modifier })
            && document.RefTable.Files.All(f => f.Flags == 1 && !f.UseFile)
            && document.Node.References.MaterialModifier.File?.FilePath == Modifier,
            "The native-saved sibling reference layout changed.");
        var mesh = ((CPlugDynaObjectModel)Prefab(document.Node).Ents!.Last().Model!).Mesh!;
        Require(mesh.References.Materials is { Count: 1 } && mesh.References.Materials[0].File?.FilePath == Placeholder
            && (mesh.CustomMaterials?.Length ?? 0) == 0 && (mesh.MaterialInsts?.Count ?? 0) == 0,
            "The native-saved placeholder material route changed.");
    }
    private static CPlugPrefab Prefab(CGameItemModel item) => item.EntityModel is NPlugItem_SVariantList { Variants.Length: 1 } list && list.Variants[0].EntityModel is CPlugPrefab p
        ? p : throw new InvalidDataException("Expected the audited single-variant prefab.");
    private static Gbx<CGameItemModel> Parse(byte[] bytes) => Gbx.Parse<CGameItemModel>(new MemoryStream(bytes));
    private static byte[] Save(Gbx<CGameItemModel> document)
    {
        using var stream = new MemoryStream();
        // Trackmania's native item-save path emits an uncompressed body for the
        // validated flag graph. Keep that container mode when authoring new
        // sequence geometry; changing only the vertex data must not fall back
        // to the compact authoring archive.
        new ReferenceDocument(document.Node, document.RefTable!) { BodyCompression = GbxCompression.Uncompressed }.Save(stream);
        return stream.ToArray();
    }
    private sealed class ReferenceDocument : Gbx<CGameItemModel> { public ReferenceDocument(CGameItemModel node, GbxRefTable table) : base(node) => RefTable = table; }
    [DynamicDependency(DynamicallyAccessedMemberTypes.NonPublicProperties, typeof(CPlugVisual))]
    private static void SetSerializedCount(CPlugVisual visual, int count)
    {
        // This pinned GBX.NET version stores Count independently of Vertices. The same measured
        // setter is used by the accepted FlagShapes fixtures; no game pointer/flag is edited.
        var property = typeof(CPlugVisual).GetProperty("Count", BindingFlags.Instance | BindingFlags.NonPublic);
        if (property?.PropertyType != typeof(int) || property.SetMethod is null) throw new NotSupportedException("This GBX.NET build cannot author the measured inline flag vertex count.");
        property.SetValue(visual, count);
        Require((int)property.GetValue(visual)! == count, "The serialized inline vertex count was not updated.");
    }
    private static string SafeName(string? source)
    {
        var value = new string((source ?? "CustomFlag").Where(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-').Take(48).ToArray());
        return value.Length == 0 ? "CustomFlag" : value;
    }
    private static IEnumerable<string> CommonWarnings()
    {
        yield return "This is a four-file GBX package, not a standalone Item.Gbx. Keep the three dependencies beside the Item.";
        yield return "The tested map setup requires a genuine native Flag16m placed before custom flags. Custom-only shader initialization is not established.";
        yield return "Native map saving can omit dependencies and rewrite embedded paths. Collect all four GBX files and repair embedded references after saving before sharing a map.";
        yield return "A second client, multiplayer and fresh native tests of this export have not been performed.";
    }
    private static string Readme(string file, Mode mode) => $"""
        # TM Item Studio waving flag package

        This folder contains `{file}` plus its three required material/skin/modifier GBX dependencies. Extract the whole folder under your Trackmania Items folder. Keep the four GBX files together. The manifest records every exact payload hash.

        Mode: {mode}. Presets retain the exact previously observed v6 graph. SourceMesh converts only supported inline static triangle geometry into a deterministic 86-frame non-kinematic cloth sequence, with a pinned edge and five complete LODs. SourceMesh exports have passed offline checks only; native loading, rendering and timing have not been tested. Wave controls apply only to SourceMesh. Source materials are replaced by the native flag material; UV coordinates and triangle topology are retained. Original physics/collision and rigid source parts are not carried into SourceMesh exports.

        Place a genuine native `Flag16m` in the map before the custom flags: that anchor is still required by the tested rendering route. The native flag is a game asset and is not included in this package.

        **Sharing a map requires another step.** Ordinary native saving can omit the three dependencies and rewrite embedded Item references. The complete four-file graph must be embedded and the saved map's sibling references/dependency closure repaired. Use the package-aware saved-map repair step with this exact ZIP; a compatible repair must match the saved Item's geometry and preserve map data. This ZIP is an authoring package, not a ready-to-share map. File repair does not establish native rendering of a newly converted mesh. Verify the final map in a fresh game with no loose dependencies or Openplanet before sharing. This export has no multiplayer or second-machine confirmation.

        This is vertex-tween animation, not an independent physical cloth solver, moving-collision model, or kinematic aircraft integration. No installation or game process was modified to create this package.
        """;
    private static void Entry(ZipArchive zip, string name, byte[] bytes) { var e = zip.CreateEntry(name, CompressionLevel.Optimal); e.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero); using var s = e.Open(); s.Write(bytes); }
    private static string GeometryHash(Source s) { using var m = new MemoryStream(); using (var w = new BinaryWriter(m, Encoding.UTF8, true)) { foreach (var p in s.Positions) { w.Write(p.X); w.Write(p.Y); w.Write(p.Z); } foreach (var n in s.Normals) { w.Write(n.X); w.Write(n.Y); w.Write(n.Z); } foreach (var i in s.Indices) w.Write(i); foreach (var uv in s.Uvs) foreach (var p in uv) { w.Write(p.X); w.Write(p.Y); } } return Hash(m.ToArray()); }
    private static BoxAligned Bounds(IEnumerable<Vec3> values) { var p = values.Select(V).ToArray(); var min = p.Aggregate(Vector3.Min); var max = p.Aggregate(Vector3.Max); var c = (min + max) / 2; var e = (max - min) / 2 + new Vector3(.0001f); return new(c.X, c.Y, c.Z, e.X, e.Y, e.Z); }
    private static bool Inside(BoxAligned b, Vec3 p) => Math.Abs(p.X - b.X) <= b.X2 && Math.Abs(p.Y - b.Y) <= b.Y2 && Math.Abs(p.Z - b.Z) <= b.Z2;
    private static float[] Components(Vector3 p) => [p.X, p.Y, p.Z];
    private static Vector3 V(Vec3 p) => new(p.X, p.Y, p.Z);
    private static Vec3 G(Vector3 p) => new(p.X, p.Y, p.Z);
    private static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
    private static double Area(Vector3 a, Vector3 b, Vector3 c) => Vector3.Cross(b - a, c - a).Length() * .5;
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}








/// <summary>Read serialized identity without GBX.NET 2.5's display-name fallback.</summary>
public static class ItemStoredIdentity
{
    public static Ident Read(CGameItemModel item)
    {
        using var output = new MemoryStream();
        using (var writer = new GBX.NET.Serialization.GbxWriter(output))
        using (var rw = new GBX.NET.Serialization.GbxReaderWriter(writer))
            new CGameCtnCollector.Chunk2E00100B().ReadWrite(item, rw);
        using var input = new MemoryStream(output.ToArray());
        using var reader = new GBX.NET.Serialization.GbxReader(input);
        return reader.ReadIdent();
    }
}
