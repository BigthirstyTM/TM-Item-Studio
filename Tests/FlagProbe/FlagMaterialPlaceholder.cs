using System.Security.Cryptography;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Plug;

// Offline follow-up to the embedded-map scan of the shaderless placeholder.
// Preserve all four v3 item/remapping files byte-for-byte. Replace only the
// placeholder with a native ordinary depth material whose ShaderApply is inline.
// v4 preserves HLSL FIDs; v5 nulls scripts; v6 removes the pass array entirely.
// Runtime GPU compilation of this pre-remap placeholder is never claimed.
internal static class FlagMaterialPlaceholder
{
    const string NativeMaterial = @"C:\Users\PC\OpenplanetNext\Extract\GameData\Techno3\Media\Material\ZOnly_Water.Material.gbx";
    const string NativeShader = @"C:\Users\PC\OpenplanetNext\Extract\GameData\Techno3\Media\Shader\ZOnly_Water.Shader.Gbx";
    const string Placeholder = "TMIS_MatRemap_Placeholder.Material.Gbx";
    static readonly string[] Preserved = [
        "TMIS_MatRemap.GameSkin.Gbx",
        "TMIS_MatRemap.TerrainModifier.Gbx",
        "TMIS_MatRemap_Control.Item.Gbx",
        "TMIS_MatRemap_Candidate.Item.Gbx"
    ];
    static readonly string[] Scripts = [@"Shaders\Engines\ZOnly_p.hlsl", @"Shaders\Engines\ZOnly_v.hlsl"];

    public static int Create(string inputFolder, string output, bool dependencyFree = false, bool noPasses = false)
    {
        Require(!noPasses || dependencyFree, "No-pass mode requires dependency-free mode");
        if (Directory.Exists(output) || File.Exists(output))
            throw new IOException("Preserve previous fixture: output must not exist");
        inputFolder = Path.GetFullPath(inputFolder);
        using var sourceManifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(inputFolder, "manifest.json")));
        Require(sourceManifest.RootElement.GetProperty("fixtureVersion").GetInt32() == 3,
            "Expected the accepted v3 user-identity fixture");
        var originalPlaceholder = File.ReadAllBytes(Path.Combine(inputFolder, Placeholder));
        var files = Preserved.ToDictionary(name => name, name => File.ReadAllBytes(Path.Combine(inputFolder, name)));
        foreach (var name in Preserved.Where(n => n.EndsWith(".Item.Gbx", StringComparison.OrdinalIgnoreCase)))
        {
            var item = Gbx.Parse<CGameItemModel>(new MemoryStream(files[name])).Node;
            Require(item.Ident?.Id == "", "Preserve ordinary user-FID identity");
        }

        var materialBytes = File.ReadAllBytes(NativeMaterial);
        var shaderBytes = File.ReadAllBytes(NativeShader);
        var material = Gbx.Parse<CPlugMaterial>(new MemoryStream(materialBytes)).Node;
        var shaderDocument = Gbx.Parse<CPlugShaderApply>(new MemoryStream(shaderBytes));
        var shader = shaderDocument.Node;
        Require(material.CustomMaterial is null && material.Shader is null && material.ShaderFile is null,
            "Unexpected ordinary native material graph");
        Require(material.DeviceMaterials?.Length == 1 && shader.Passes?.Length == 1,
            "Expected one native device material and one native shader pass");
        Require(shader.RequirementFlags == 0 && shader.VisibleIdFlags == 0,
            "Ordinary source shader must not request VertexTween structured data");
        ValidateOptionalShaderFields(shader);
        Require(shaderDocument.RefTable?.AncestorLevel == 3,
            "Unexpected native HLSL reference root");
        ValidateScripts(shaderDocument.RefTable!);
        var pipelines = Pipelines(shader);
        Require(pipelines.Count(p => p.ScriptFile is not null) == 2,
            "Expected exactly the two native HLSL pipeline FIDs");
        if (dependencyFree)
        {
            foreach (var pipeline in pipelines)
            {
                pipeline.Script = null;
                pipeline.ScriptFile = null;
            }
        }
        if (noPasses) shader.Passes = [];
        foreach (var device in material.DeviceMaterials!)
        {
            device.Shader1 = shader;
            // Native 0x015 reads this bool as FID-vs-inline for shader1.
            // Shader2/3 are ALWAYS FID references in the native reader even
            // though pinned GBX.NET models all three as generic NodeRefs.
            // Null tails serialize -1, valid under both interpretations.
            if (dependencyFree) device.U03 = false;
            device.Shader2 = device.Shader3 = dependencyFree ? null : shader;
            device.Shader1File = device.Shader2File = device.Shader3File = null;
        }
        // v5 keeps shader/pass/pipeline objects with null scripts. v6 keeps
        // ShaderApply metadata while serializing an empty pass array instead.
        using var stream = new MemoryStream();
        new MaterialDocument(material, dependencyFree ? new GbxRefTable { AncestorLevel = 0 } : shaderDocument.RefTable!).Save(stream);
        var placeholderBytes = stream.ToArray();
        var reparsed = Gbx.Parse<CPlugMaterial>(new MemoryStream(placeholderBytes));
        if (dependencyFree)
        {
            Require((reparsed.RefTable?.AncestorLevel ?? 0) == 0
                && (reparsed.RefTable?.Files.Count ?? 0) == 0
                && (reparsed.RefTable?.Resources.Count ?? 0) == 0,
                "Dependency-free placeholder must have no external files or resources");
        }
        else
        {
            Require(reparsed.RefTable?.AncestorLevel == 3, "Native HLSL ancestry changed");
            ValidateScripts(reparsed.RefTable!);
        }
        Require(reparsed.Node.DeviceMaterials?.Length == 1 && reparsed.Node.Shader is null
            && reparsed.Node.ShaderFile is null && reparsed.Node.CustomMaterial is null,
            "Unexpected reparsed source material graph");
        var deviceParsed = reparsed.Node.DeviceMaterials!.Single();
        Require(deviceParsed.Shader1File is null && deviceParsed.Shader2File is null && deviceParsed.Shader3File is null,
            "ShaderApply must be inline in all device slots");
        var inline = dependencyFree ? new[] { deviceParsed.Shader1 }
            : new[] { deviceParsed.Shader1, deviceParsed.Shader2, deviceParsed.Shader3 };
        Require(inline.All(s => s is not null && s.Passes?.Length == (noPasses ? 0 : 1) && s.RequirementFlags == 0),
            "An ordinary shader/pass was not preserved in a device slot");
        Require(inline.All(s => ReferenceEquals(s, inline[0])), "Native device slots must share one ShaderApply");
        if (dependencyFree)
        {
            Require(!deviceParsed.U03 && deviceParsed.Shader2 is null && deviceParsed.Shader3 is null,
                "Native device layout requires false+inline first shader and null FID tails");
            Require(inline[0] is CPlugShaderApply, "Expected the native ShaderApply class");
            var parsedShader = (CPlugShaderApply)inline[0]!;
            ValidateOptionalShaderFields(parsedShader);
            var afterPipelines = Pipelines(parsedShader);
            Require(afterPipelines.Count == (noPasses ? 0 : pipelines.Count)
                && afterPipelines.All(p => p.Script is null && p.ScriptFile is null),
                "Pipeline objects must remain while all script references are null");
        }
        files[Placeholder] = placeholderBytes;

        Directory.CreateDirectory(output);
        foreach (var (name, bytes) in files)
        {
            using var file = new FileStream(Path.Combine(output, name), FileMode.CreateNew, FileAccess.Write);
            file.Write(bytes);
        }
        File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(new {
            fixtureVersion = noPasses ? 6 : dependencyFree ? 5 : 4,
            scope = "Offline ordinary-shader placeholder candidate; no native preload, embedding, rendering or cold-load claim",
            inputFixture = new {
                path = inputFolder,
                manifestSha256 = Hash(File.ReadAllBytes(Path.Combine(inputFolder, "manifest.json"))),
                replacedShaderlessPlaceholderSha256 = Hash(originalPlaceholder)
            },
            nativeTemplates = new[] {
                new { path = NativeMaterial, bytes = materialBytes.Length, sha256 = Hash(materialBytes) },
                new { path = NativeShader, bytes = shaderBytes.Length, sha256 = Hash(shaderBytes) }
            },
            change = "Only the external placeholder file changes. All four item/GameSkin/modifier payloads are byte-for-byte v3 copies.",
            nativeDeviceLayout = dependencyFree ? new {
                shader1SelectorBoolean = false,
                shader1Encoding = "Inline NodeRef; native false branch 0x14040E8B6-0x14040E8BA",
                shader2Encoding = "Null FID reference index -1",
                shader3Encoding = "Null FID reference index -1",
                tailReader = "Native 0x14040E8D0-0x14040E8E4 always uses archive FID reader for both tails",
                pinnedSchemaCaveat = "GBX.NET models all three slots as NodeRef and preserves U03 independently. v4 retained true while inlining nodes; v5 explicitly fixes the selector and supplies null FID tails."
            } : null,
            placeholderGraph = new {
                material = "Native ZOnly_Water CPlugMaterial",
                shader = noPasses
                    ? "Native ZOnly_Water CPlugShaderApply metadata, empty pass array, inline in shader1 with false selector and null shader2/3 FID tails"
                    : dependencyFree
                    ? "Native ZOnly_Water CPlugShaderApply inline in shader1, explicit false selector, null shader2/3 FID tails"
                    : "Native ZOnly_Water CPlugShaderApply, inline and shared by all three device shader slots",
                nativeDeviceMaterialCount = 1,
                nativeTemplatePassCount = 1,
                serializedPassCount = noPasses ? 0 : 1,
                requirementFlags = 0,
                visibleIdFlags = 0,
                ancestorLevel = dependencyFree ? 0 : 3,
                pipelineObjectCount = noPasses ? 0 : pipelines.Count,
                hlslFids = dependencyFree ? Array.Empty<string>() : Scripts,
                hlslUseFile = !dependencyFree,
                hlslContent = noPasses
                    ? "The pass array is empty; no pass, pipeline or script graph is serialized. ShaderApply and its other native metadata remain. Native zero-pass post-load support requires independent static review before loading."
                    : dependencyFree
                    ? "All PipelineGpu.Script and ScriptFile fields are null; genuine ShaderApply/pass/pipeline objects and other metadata remain. This is a pre-remap scan placeholder, not a GPU-program claim."
                    : "Native HLSL script files are absent from this extraction; native FID paths and cache metadata are retained without claiming GPU program resolution"
            },
            staticChecks = new {
                fourNonPlaceholderFilesUnchanged = true,
                userIdentityPreserved = true,
                inlineShaderApplyInPrimaryDeviceSlot = true,
                nativeInlineSelectorFalseAndNullFidTails = dependencyFree,
                legacyOptionalShaderFieldsNull = true,
                nativePassesAndPipelineObjectsPreserved = !noPasses,
                emptyPassArray = noPasses,
                pipelineScriptReferencesCleared = dependencyFree && !noPasses,
                noExternalMaterialOrShaderArchives = true,
                externalReferenceCount = dependencyFree ? 0 : 2,
                supportedMaterialAndShaderBodyReparsed = true
            },
            files = files.Select(f => new { file = f.Key, bytes = f.Value.Length, sha256 = Hash(f.Value), unchangedFromV3 = f.Key != Placeholder }),
            runtimeGates = new[] {
                noPasses ? "Do not load until an independent native zero-pass post-load audit finds this serialized graph supported" : "Fixture remains native-runtime gated",
                "Read the source placeholder's material shader resolver output; require a non-null ordinary CPlugShaderApply",
                "Verify the GameSkin source FID still equals placeholder material+8",
                "Place normally, require the effective early material to equal native ItemFlag and all five structured visuals enabled",
                "Check native map save and the embedded loader's pre-remap scan before capturing motion",
                "Cold-load an embedded-only map with all loose fixture inputs absent and no companion or process writes"
            }
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Ordinary-shader placeholder offline fixture: {output}; four unchanged files, native inline ShaderApply; runtime untested");
        return 0;
    }

    static void ValidateOptionalShaderFields(CPlugShaderApply shader)
    {
        // Native base post-load 1403DB12F has a first-pass access behind an
        // optional legacy field. Native ZOnly keeps both 0900201C refs null.
        Require(shader.FuncShader is null && shader.FuncShaderFile is null,
            "Unexpected optional function shader");
        var chunk = shader.Chunks.OfType<CPlugShader.Chunk0900201C>().Single();
        Require(chunk.U01 is null && chunk.U01File is null && chunk.U02 is null && chunk.U02File is null,
            "Legacy optional shader fields must stay null for the zero-pass post-load gate");
    }

    static List<CPlugShaderPass.PipelineGpu> Pipelines(CPlugShaderApply shader)
    {
        var result = new List<CPlugShaderPass.PipelineGpu>();
        void Add(CPlugShaderPass.PipelineGpu? pipeline)
        {
            if (pipeline is not null && !result.Any(p => ReferenceEquals(p, pipeline))) result.Add(pipeline);
        }
        foreach (var pass in shader.Passes!)
        {
            Add(pass.VHlsl);
            Add(pass.PHlsl);
            foreach (var chunk in pass.Chunks.OfType<CPlugShaderPass.Chunk0906700B>())
            {
                Add(chunk.U01);
                Add(chunk.U02);
                Add(chunk.U03);
            }
        }
        return result;
    }

    static void ValidateScripts(GbxRefTable table)
    {
        Require(table.Files.Count == 2 && table.Resources.Count == 0, "Unexpected ordinary shader dependency count");
        Require(table.Files.All(f => f.UseFile)
            && table.Files.Select(f => f.FilePath).OrderBy(p => p, StringComparer.Ordinal).SequenceEqual(Scripts),
            "Native HLSL FIDs changed");
    }
    static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    static void Require(bool ok, string message)
    {
        if (!ok) throw new InvalidDataException(message);
    }
    sealed class MaterialDocument : Gbx<CPlugMaterial>
    {
        public MaterialDocument(CPlugMaterial material, GbxRefTable references) : base(material) => RefTable = references;
    }
}
