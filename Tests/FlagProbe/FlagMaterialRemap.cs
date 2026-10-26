using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;

// Offline research fixture only. No game loading, installation or runtime writes.
// Native GameSkin v3 chunk 090F4004 deliberately stores a true int32 boolean
// and a FID index, rather than letting GBX.NET infer that boolean from Node.
internal static class FlagMaterialRemap
{
    const string NativeMaterial = @"C:\Users\PC\OpenplanetNext\Extract\GameData\Stadium\Media\Material\ItemFlag.Material.Gbx";
    const string Placeholder = "TMIS_MatRemap_Placeholder.Material.Gbx";
    const string Skin = "TMIS_MatRemap.GameSkin.Gbx";
    const string Modifier = "TMIS_MatRemap.TerrainModifier.Gbx";
    const string Control = "TMIS_MatRemap_Control.Item.Gbx";
    const string Candidate = "TMIS_MatRemap_Candidate.Item.Gbx";
    const string ReplacementFolder = @"Stadium\Media\Material\";
    const uint MaterialClass = 0x09079000;
    const uint SkinClass = 0x090F4000;
    const uint ModifierClass = 0x0915D000;
    const uint Facade = 0xFACADE01;

    public static int Create(string seed, string output, bool userIdentity = false)
    {
        if (Directory.Exists(output) || File.Exists(output))
            throw new IOException("Preserve previous fixture: output must not exist");
        var fullSeed = Path.GetFullPath(seed);
        var seedBytes = File.ReadAllBytes(fullSeed);
        var original = ParseItem(seedBytes);
        var originalMesh = Mesh(original);
        Require(originalMesh.Materials?.Length == 1
            && originalMesh.Materials[0].File?.FilePath.EndsWith("ItemFlag.Material.Gbx", StringComparison.OrdinalIgnoreCase) == true,
            "Expected the accepted adjacent seed with one early native ItemFlag material reference");
        Require((originalMesh.CustomMaterials?.Length ?? 0) == 0
            && (originalMesh.MaterialInsts?.Length ?? 0) == 0,
            "The adjacent seed must not use late custom/UserInst materials");
        Require(original.Chunks.OfType<CGameItemModel.Chunk2E002019>().Single().Version >= 15,
            "The item chunk must serialize MaterialModifier");
        var visualHashes = VisualHashes(originalMesh);
        Require(visualHashes.Length == 5, "Expected five preserved flag visuals");

        // Match the already accepted empty early material used by the bootstrap
        // controls. Its own archive has no texture, shader or material references.
        var nativeBytes = File.ReadAllBytes(NativeMaterial);
        var placeholder = Gbx.Parse<CPlugMaterial>(new MemoryStream(nativeBytes)).Node;
        foreach (var chunk in placeholder.Chunks.Where(c => c.Ignore).ToArray())
            placeholder.Chunks.Remove(chunk);
        placeholder.CustomMaterial = null;
        placeholder.Shader = null;
        placeholder.ShaderFile = null;
        placeholder.DeviceMaterials = [];
        var materialChunk = placeholder.Chunks.OfType<CPlugMaterial.Chunk09079015>().Single();
        materialChunk.U03 = [];
        materialChunk.U04 = null;
        var placeholderBytes = Save(new Gbx<CPlugMaterial>(placeholder));
        ValidatePlaceholder(placeholderBytes);

        // This is a FID reference, not a material node reference. The native
        // ref-table builder 1409033F5 stores FID when UseFile is true and
        // FID+80 (loaded node) when false. The mesh still needs false.
        var skinBytes = WriteNativeArchive(SkinClass, Placeholder, true, w => {
            w.Write(0x090F4004u); // unskippable native GameSkin body, v3
            w.Write((byte)3);
            WriteString(w, ""); // DirName
            WriteString(w, ""); // PainterTextureName
            WriteString(w, ""); // PainterSceneId
            w.Write((byte)1);   // customizable FID count
            w.Write(MaterialClass);
            WriteString(w, "ItemFlag"); // replacement short filename
            w.Write(1); // true, int32: next value is a FID reference index
            w.Write(1); // external source placeholder in THIS archive's ref table
            w.Write(0); // MipMaps, int32 boolean
            w.Write(Facade);
        });
        var modifierBytes = WriteNativeArchive(ModifierClass, Skin, false, w => {
            w.Write(0x0915D000u);
            w.Write(1); // external GameSkin NodeRef
            WriteString(w, ReplacementFolder); // native global-root folder serializer
            w.Write(0x0915D001u);
            w.Write(3);  // Id encoding version
            w.Write(-1); // empty Id, as in native None.TerrainModifier.Gbx
            w.Write(Facade);
        });
        var skinLayout = ValidateSkin(skinBytes);
        var modifierLayout = ValidateModifier(modifierBytes);

        // Only these two item properties and their external reference table
        // differ between control and candidate. Their geometry is identical.
        byte[] BuildItem(bool remap)
        {
            var item = ParseItem(seedBytes);
            var name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(remap ? Candidate : Control));
            item.Name = name;
            // Ordinary user items derive their identity from their FID path.
            // A native-style fixed ID needs a catalog entry and is not portable.
            item.Ident = item.Ident! with { Id = userIdentity ? "" : name };
            var table = new GbxRefTable { AncestorLevel = 0 };
            Mesh(item).Materials = [new External<CPlugMaterial>(null, new(table, 1, false, Placeholder))];
            item.MaterialModifier = null;
            item.MaterialModifierFile = remap ? new(table, 1, false, Modifier) : null;
            var bytes = Save(new ReferenceTableDocument(item, table));
            var parsed = Gbx.Parse<CGameItemModel>(new MemoryStream(bytes));
            Require(parsed.RefTable?.AncestorLevel == 0, "Item references must stay in the fixture folder");
            Require(parsed.RefTable!.Files.Count == (remap ? 2 : 1), "Unexpected item dependency count");
            Require(Mesh(parsed.Node).Materials!.Single().File!.FilePath == Placeholder, "Placeholder reference lost");
            Require(parsed.Node.MaterialModifierFile?.FilePath == (remap ? Modifier : null), "Modifier reference lost");
            Require(VisualHashes(Mesh(parsed.Node)).SequenceEqual(visualHashes), "Serialized visual data/topology changed");
            Require((Mesh(parsed.Node).CustomMaterials?.Length ?? 0) == 0
                && (Mesh(parsed.Node).MaterialInsts?.Length ?? 0) == 0, "Late materials unexpectedly introduced");
            return bytes;
        }
        var files = new Dictionary<string, byte[]> {
            [Placeholder] = placeholderBytes,
            [Skin] = skinBytes,
            [Modifier] = modifierBytes,
            [Control] = BuildItem(false),
            [Candidate] = BuildItem(true)
        };
        Directory.CreateDirectory(output);
        foreach (var (name, bytes) in files)
        {
            using var stream = new FileStream(Path.Combine(output, name), FileMode.CreateNew, FileAccess.Write);
            stream.Write(bytes);
        }
        File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(new {
            fixtureVersion = userIdentity ? 3 : 2,
            identity = userIdentity ? "Empty stored Id; user-FID path identity" : "Native-style fixed Id control",
            scope = "Offline native-material-remapping candidate; no native preload, placement or rendering claim",
            seed = new { path = fullSeed, bytes = seedBytes.Length, sha256 = Hash(seedBytes) },
            nativePlaceholderTemplate = new { path = NativeMaterial, sha256 = Hash(nativeBytes) },
            control = Control, candidate = Candidate, placeholder = Placeholder,
            replacement = new { folder = ReplacementFolder, shortFileName = "ItemFlag", classId = "0x09079000" },
            externalReferenceModes = new {
                gameSkinSourceUseFile = true,
                itemMeshMaterialUseFile = false,
                modifierGameSkinUseFile = false,
                rationale = "Native archive body table stores FID for UseFile=true, loaded node for false; GameSkin source is a FID, mesh material and modifier skin are nodes",
                nativeSelectionGate = "0x1409033F5-0x140903408"
            },
            visualSerializationSha256 = visualHashes,
            staticChecks = new {
                fiveVisualsPreservedByteForByte = true, noLateMaterials = true,
                sourcePlaceholderHasNoExternalDependencies = true, exactNativeSkinLayoutValidated = true,
                exactNativeModifierLayoutValidated = true, skinSupportedBodyReparsed = true,
                modifierHeaderAndReferenceTableReparsed = true,
                modifierBodyToolingSupport = "Native 0915D000 is unsupported by pinned GBX.NET; exact native byte layout validated independently"
            },
            skinLayout, modifierLayout,
            files = files.Select(f => new { file = f.Key, bytes = f.Value.Length, sha256 = Hash(f.Value) }),
            runtimeGates = new[] {
                "Preload candidate and control in a disposable test context; raw preload may not apply the modifier",
                "Observe item-instance builder completion and effective render mesh/visual identity",
                "Prove early native VertexTween material and structured flags on the effective mesh before drawing",
                "Check prefab Dyna/Static traversal; direct VisModelCustom host may need a separate fixture",
                "Then test visible animation, cold restart, and embedded-only map with loose files absent"
            }
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Material remap offline fixture: {output}; five files, five unchanged visuals; runtime untested");
        return 0;
    }

    static CGameItemModel ParseItem(byte[] bytes) => Gbx.Parse<CGameItemModel>(new MemoryStream(bytes)).Node;
    static CPlugSolid2Model Mesh(CGameItemModel item) =>
        ((CPlugDynaObjectModel)((CPlugPrefab)((NPlugItem_SVariantList)item.EntityModel!).Variants![0].EntityModel!).Ents![1].Model!).Mesh!;
    static byte[] Save(Gbx doc)
    {
        using var stream = new MemoryStream();
        doc.Save(stream);
        return stream.ToArray();
    }
    static string[] VisualHashes(CPlugSolid2Model mesh) => mesh.Visuals!
        .Cast<CPlugVisualIndexedTriangles>().Select(v => Hash(Save(new Gbx<CPlugVisualIndexedTriangles>(v)))).ToArray();
    static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    static void Require(bool ok, string message)
    {
        if (!ok) throw new InvalidDataException(message);
    }
    static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }
    static string ReadString(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        Require(length >= 0 && length <= reader.BaseStream.Length - reader.BaseStream.Position, "Invalid native string length");
        return Encoding.UTF8.GetString(reader.ReadBytes(length));
    }

    static byte[] WriteNativeArchive(uint classId, string externalFile, bool useFile, Action<BinaryWriter> writeBody)
    {
        Require(Path.GetFileName(externalFile) == externalFile, "Native external fixture reference must be a sibling filename");
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write(new byte[] { (byte)'G', (byte)'B', (byte)'X', 6, 0, (byte)'B', (byte)'U', (byte)'U', (byte)'R' });
        writer.Write(classId);
        writer.Write(0); // no header user data
        writer.Write(2); // root index 0 and external index 1
        writer.Write(1); // one external reference
        writer.Write(0); // ancestor level: containing fixture folder
        writer.Write(0); // no child directories
        writer.Write(1); // ordinary file reference flags; no resource/pointer encoding
        WriteString(writer, externalFile);
        writer.Write(1); // external node index
        writer.Write(useFile ? 1 : 0); // true stores a FID; false stores the loaded node
        writer.Write(0); // directory index: current directory
        writeBody(writer);
        return stream.ToArray();
    }

    static void ValidateNativeHeader(BinaryReader reader, uint classId, string dependency, bool useFile)
    {
        Require(reader.ReadBytes(9).SequenceEqual(new byte[] { 71, 66, 88, 6, 0, 66, 85, 85, 82 }), "Unexpected raw GBX header");
        Require(reader.ReadUInt32() == classId && reader.ReadInt32() == 0, "Unexpected raw class/header data");
        Require(reader.ReadInt32() == 2 && reader.ReadInt32() == 1, "Unexpected raw node/reference count");
        Require(reader.ReadInt32() == 0 && reader.ReadInt32() == 0 && reader.ReadInt32() == 1, "Unexpected root/directory/reference flags");
        Require(ReadString(reader) == dependency, "Wrong native sibling dependency");
        Require(reader.ReadInt32() == 1 && reader.ReadInt32() == (useFile ? 1 : 0) && reader.ReadInt32() == 0,
            "Wrong FID index/useFile/directory");
    }
    static object ValidateSkin(byte[] bytes)
    {
        using var reader = new BinaryReader(new MemoryStream(bytes));
        ValidateNativeHeader(reader, SkinClass, Placeholder, true);
        var bodyOffset = reader.BaseStream.Position;
        Require(reader.ReadUInt32() == 0x090F4004 && reader.ReadByte() == 3, "Wrong native GameSkin chunk/version");
        Require(ReadString(reader) == "" && ReadString(reader) == "" && ReadString(reader) == "", "Unexpected GameSkin directory/painter fields");
        Require(reader.ReadByte() == 1 && reader.ReadUInt32() == MaterialClass && ReadString(reader) == "ItemFlag", "Wrong source mapping row");
        var trueBooleanOffset = reader.BaseStream.Position;
        Require(reader.ReadInt32() == 1 && reader.ReadInt32() == 1 && reader.ReadInt32() == 0, "Expected explicit true/FID-index/mipmaps int32 fields");
        Require(reader.ReadUInt32() == Facade && reader.BaseStream.Position == bytes.Length, "Unexpected GameSkin tail");
        var parsed = Gbx.Parse<CPlugGameSkin>(new MemoryStream(bytes));
        Require(parsed.RefTable?.Files.Count == 1 && parsed.RefTable.Files.Single().FilePath == Placeholder
            && parsed.RefTable.Files.Single().UseFile, "GameSkin ref-table parse differs");
        var fid = parsed.Node.CustomizableFids!.Single();
        Require(fid.ClassId == MaterialClass && fid.Name == "ItemFlag" && fid.NodeFile?.FilePath == Placeholder,
            "Supported GameSkin body parse did not preserve the external source reference");
        return new { bodyOffset, trueBooleanOffset, sourceFidIndex = 1, bodyChunk = "0x090F4004", version = 3,
            trueBooleanHex = Convert.ToHexString(bytes.AsSpan((int)trueBooleanOffset, 12)) };
    }
    static object ValidateModifier(byte[] bytes)
    {
        using var reader = new BinaryReader(new MemoryStream(bytes));
        ValidateNativeHeader(reader, ModifierClass, Skin, false);
        var bodyOffset = reader.BaseStream.Position;
        Require(reader.ReadUInt32() == ModifierClass && reader.ReadInt32() == 1, "Wrong modifier chunk/GameSkin NodeRef");
        Require(ReadString(reader) == ReplacementFolder, "Wrong global-root replacement folder");
        Require(reader.ReadUInt32() == 0x0915D001 && reader.ReadInt32() == 3 && reader.ReadInt32() == -1,
            "Wrong modifier Id chunk");
        Require(reader.ReadUInt32() == Facade && reader.BaseStream.Position == bytes.Length, "Unexpected modifier tail");
        var header = Gbx.ParseHeader(new MemoryStream(bytes));
        Require(header.Header.ClassId == ModifierClass && header.RefTable?.Files.Count == 1
            && header.RefTable.Files.Single().FilePath == Skin && !header.RefTable.Files.Single().UseFile,
            "Modifier supported header/ref-table parse differs");
        return new { bodyOffset, gameSkinFidIndex = 1, bodyHex = Convert.ToHexString(bytes.AsSpan((int)bodyOffset)) };
    }
    static void ValidatePlaceholder(byte[] bytes)
    {
        var parsed = Gbx.Parse<CPlugMaterial>(new MemoryStream(bytes));
        Require((parsed.RefTable?.Files.Count ?? 0) == 0 && (parsed.RefTable?.Resources.Count ?? 0) == 0,
            "Placeholder must not carry external references");
        Require(parsed.Node.ShaderFile is null && parsed.Node.Shader is null && parsed.Node.CustomMaterial is null
            && (parsed.Node.DeviceMaterials?.Length ?? 0) == 0, "Placeholder must not carry a shader/custom material");
    }
}
