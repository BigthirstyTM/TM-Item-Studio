using System.Text;
using GBX.NET;
using GBX.NET.Engines.GameData;

namespace TM_Item_Studio.Models.FlagPackaging;

/// <summary>Changes only the bounded two-file reference table, never an Item node or body.</summary>
internal static class FlagItemArchive
{
    internal const string Placeholder = "TMIS_MatRemap_Placeholder.Material.Gbx";
    internal const string Modifier = "TMIS_MatRemap.TerrainModifier.Gbx";
    internal static readonly string[] RequiredReferences = [Placeholder, Modifier];

    internal static Gbx<CGameItemModel> ValidatePackageItem(byte[] bytes)
    {
        var sections = Sections.Parse(bytes);
        ValidateRecords(sections);
        PackagingGuard.That(sections.Ancestor == 0 && sections.Directories.Count == 0
            && sections.Records.All(r => r.Directory == 0), "Flag package Item must use sibling references");
        var doc = Parse(bytes);
        PackagingGuard.That(ItemStoredIdentity.Read(doc.Node).Id == "" && doc.Node.ArchetypeRef == "",
            "Flag package Item must have an empty stored identity and archetype");
        return doc;
    }

    internal static (byte[] Bytes, object Evidence, Gbx<CGameItemModel> Document) Normalize(string zipPath, byte[] bytes)
    {
        var path = PackagingGuard.CanonicalPath(zipPath).Split('/');
        PackagingGuard.That(path.Length == 3 && path[0] == "Items" && path[2].EndsWith(".Item.Gbx", StringComparison.OrdinalIgnoreCase),
            "Embedded flag must be directly inside one Items ZIP folder");
        var old = Sections.Parse(bytes);
        ValidateRecords(old);
        var directories = new[] { "ContentLoaded", "ContentLoaded\\Items", "ContentLoaded\\Items\\" + path[1] };
        var fullDirectories = directories.Concat(new[] { "ContentToSave", "ContentToSave\\Items", "ContentToSave\\Items\\" + path[1] });
        var nativeLayout = old.Ancestor == 3 && old.Records.All(r => r.Directory == 3)
            && (old.Directories.SequenceEqual(directories) || old.Directories.SequenceEqual(fullDirectories));
        var siblingLayout = old.Ancestor == 0 && old.Directories.Count == 0 && old.Records.All(r => r.Directory == 0);
        PackagingGuard.That(nativeLayout || siblingLayout, "Unsupported flag reference directory layout; refusing a speculative rewrite");
        var original = Parse(bytes);
        var expectedId = string.Join('\\', path.Skip(1));
        PackagingGuard.That(original.Node.Ident?.Id == expectedId && IsStadium(original.Node),
            "Embedded Item stored identity does not match its ZIP location");

        using var table = new MemoryStream();
        using (var writer = new BinaryWriter(table, Encoding.UTF8, true))
        {
            writer.Write(2); writer.Write(0); writer.Write(0);
            foreach (var record in old.Records)
            {
                writer.Write(record.Flags); Text(writer, record.Name); writer.Write(record.Node);
                writer.Write(record.UseFile); writer.Write(0);
            }
        }
        var output = siblingLayout ? bytes.ToArray()
            : bytes[..old.HeaderEnd].Concat(table.ToArray()).Concat(bytes[old.BodyStart..]).ToArray();
        var next = Sections.Parse(output);
        PackagingGuard.That(bytes.AsSpan(0, old.HeaderEnd).SequenceEqual(output.AsSpan(0, next.HeaderEnd))
            && bytes.AsSpan(old.BodyStart).SequenceEqual(output.AsSpan(next.BodyStart)), "Item header or raw body preservation failed");
        var reopened = Parse(output);
        PackagingGuard.That(reopened.Node.Ident == original.Node.Ident && reopened.RefTable!.AncestorLevel == 0
            && reopened.RefTable.Files.Select(f => f.FilePath).SequenceEqual(old.Records.Select(r => r.Name)),
            "Normalized Item reference table failed independent parsing");
        return (output, new {
            zipPath, sourceSha256 = PackagingGuard.Hash(bytes), outputSha256 = PackagingGuard.Hash(output),
            originalHeaderIncludingNodeCountBytes = old.HeaderEnd, originalHeaderSha256 = PackagingGuard.Hash(bytes[..old.HeaderEnd]),
            originalRawBodySha256 = PackagingGuard.Hash(bytes[old.BodyStart..]), originalRawBodyBytes = bytes.Length - old.BodyStart,
            originalNodeCount = old.NodeCount, externalNodeIndices = old.Records.Select(r => r.Node),
            originalAncestor = old.Ancestor, outputAncestor = 0, headerAndRawBodyByteExact = true,
            flagsNodeIndicesRecordOrderUseFileExact = true, alreadySiblingNormalized = siblingLayout,
            itemNodeSerializationPerformed = false }, reopened);
    }

    internal static bool IsStadium(CGameItemModel item) => item.Ident is {} id
        && (id.Collection.Number == 26 || id.Collection.String == "Stadium2020");

    static Gbx<CGameItemModel> Parse(byte[] bytes) => Gbx.Parse<CGameItemModel>(new MemoryStream(bytes), new GbxReadSettings { SafeSkippableChunks = true });
    static void ValidateRecords(Sections s)
    {
        PackagingGuard.That(s.Records.Select(r => r.Name).Order(StringComparer.Ordinal).SequenceEqual(RequiredReferences.Order(StringComparer.Ordinal))
            && s.Records.All(r => r.Flags == 1 && r.UseFile == 0 && r.Node > 0 && r.Node < s.NodeCount)
            && s.Records.Select(r => r.Node).Distinct().Count() == 2,
            "Flag Item must contain exactly the two expected ordinary node references");
    }
    static void Text(BinaryWriter writer, string value) { var data = Encoding.UTF8.GetBytes(value); writer.Write(data.Length); writer.Write(data); }
    sealed record Reference(int Flags, string Name, int Node, int UseFile, int Directory);
    sealed class Sections
    {
        internal int HeaderEnd, BodyStart, Ancestor, NodeCount;
        internal List<string> Directories = [];
        internal List<Reference> Records = [];
        internal static Sections Parse(byte[] bytes)
        {
            PackagingGuard.That(bytes.Length is >= 33 and <= PackagingGuard.MaxFileBytes
                && bytes.AsSpan(0, 7).SequenceEqual("GBX\x06\0BU"u8) && bytes[7] is (byte)'C' or (byte)'U' && bytes[8] == (byte)'R',
                "Only binary GBX6 Items with uncompressed reference tables are supported");
            using var stream = new MemoryStream(bytes); using var reader = new BinaryReader(stream, Encoding.UTF8);
            stream.Position = 9;
            PackagingGuard.That(reader.ReadUInt32() == 0x2E002000, "Input flag is not an Item GBX");
            var userSize = reader.ReadInt32();
            PackagingGuard.That(userSize >= 0 && (long)stream.Position + userSize + 8 <= bytes.Length, "Truncated Item header");
            stream.Position += userSize;
            var result = new Sections { NodeCount = reader.ReadInt32(), HeaderEnd = checked((int)stream.Position) };
            PackagingGuard.That(result.NodeCount is >= 3 and <= 4096, "Unsupported Item node count");
            PackagingGuard.That(reader.ReadInt32() == 2, "Flag Item must contain exactly two external references");
            result.Ancestor = reader.ReadInt32();
            Tree(reader, "", result.Directories, 0);
            for (var i = 0; i < 2; i++) result.Records.Add(new(reader.ReadInt32(), Text(reader), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32()));
            result.BodyStart = checked((int)stream.Position);
            PackagingGuard.That(result.BodyStart < bytes.Length, "Item body is absent");
            if (bytes[7] == (byte)'C')
            {
                var raw = reader.ReadInt32(); var compressed = reader.ReadInt32();
                PackagingGuard.That(raw is > 0 and <= PackagingGuard.MaxFileBytes && compressed > 0
                    && stream.Position + compressed == bytes.Length, "Unsupported or truncated compressed Item body");
            }
            return result;
        }
        static void Tree(BinaryReader reader, string prefix, List<string> paths, int depth)
        {
            var count = reader.ReadInt32();
            PackagingGuard.That(depth < 5 && count is >= 0 and <= 8 && paths.Count <= 16, "Unsupported reference directory tree");
            for (var i = 0; i < count; i++)
            {
                var name = Text(reader);
                PackagingGuard.That(name.Length > 0 && !name.Contains('\\') && !name.Contains('/') && name is not ("." or "..")
                    && !name.Contains(':') && !name.Any(char.IsControl), "Invalid reference directory name");
                var path = prefix.Length == 0 ? name : prefix + "\\" + name;
                paths.Add(path); Tree(reader, path, paths, depth + 1);
            }
        }
        static string Text(BinaryReader reader)
        {
            var size = reader.ReadInt32();
            PackagingGuard.That(size is >= 0 and <= 4096 && size <= reader.BaseStream.Length - reader.BaseStream.Position, "Invalid reference string length");
            return new UTF8Encoding(false, true).GetString(reader.ReadBytes(size));
        }
    }
}





