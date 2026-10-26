using System.Text;
using GBX.NET;
using GBX.NET.Engines.GameData;

internal static class ItemArchive
{
    static readonly Dictionary<string, (int BodyBytes, string BodyHash)> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TMIS_MatRemap_Swallowtail.Item.Gbx"] = (962584, "ECA5FB604817BBF158793F00313B6329CC1B749A2911BC9A65939627F66E8B8F"),
        ["TMIS_MatRemap_Long250.Item.Gbx"] = (962576, "B0348A7EBC1C98E2AEBA44F8496025D516AD69ED99B77DCCE4712FA571FA713A"),
        ["TMIS_MatRemap_Round.Item.Gbx"] = (962572, "DE7CE6AD77CB1A6F71E4022D773F8689307F8E4E0487C2FC5E668AEA28E5CE46")
    };
    public static bool IsKnownName(string name) => Known.ContainsKey(name);
    public static (byte[] Bytes, object Evidence) Repair(string zipPath, byte[] source)
    {
        var name = zipPath.Replace('\\', '/').Split('/').Last(); var bound = Known[name];
        var old = Sections.Parse(source);
        Check.That(source.Length - old.BodyStart == bound.BodyBytes && Check.Hash(source[old.BodyStart..]) == bound.BodyHash, "Unknown native-written body for " + name + "; this tool supports only the three pinned research assets");
        var paths = zipPath.Replace('/', '\\').Split('\\');
        Check.That(paths.Length == 3 && paths[0] == "Items", "Known flag must be a direct child of one embedded Items folder");
        var folder = paths[1]; var required = new[] { "TMIS_MatRemap_Placeholder.Material.Gbx", "TMIS_MatRemap.TerrainModifier.Gbx" };
        Check.That(old.Records.Select(r => r.Name).SequenceEqual(required) && old.Records.Select(r => r.Node).SequenceEqual(new[] { 25, 26 })
            && old.Records.All(r => r.Flags == 1 && r.UseFile == 0), "Unknown flag external reference indices/order/modes");
        var dirs = new[] { "ContentLoaded", "ContentLoaded\\Items", "ContentLoaded\\Items\\" + folder };
        var fullDirs = dirs.Concat(new[] { "ContentToSave", "ContentToSave\\Items", "ContentToSave\\Items\\" + folder });
        var nativeLayout = old.Ancestor == 3 && old.Records.All(r => r.Directory == 3) && (old.Directories.SequenceEqual(dirs) || old.Directories.SequenceEqual(fullDirs));
        var siblingLayout = old.Ancestor == 0 && old.Directories.Count == 0 && old.Records.All(r => r.Directory == 0);
        Check.That(nativeLayout || siblingLayout, "Unknown known-flag reference layout; refusing a speculative rewrite");
        var before = Gbx.Parse<CGameItemModel>(new MemoryStream(source));
        var expectedId = string.Join('\\', paths.Skip(1));
        Check.That(before.Node.Ident?.Id == expectedId && before.Node.Ident.Author == "-oTBhm4_S1-UxnlnBizUDA" && before.Node.Ident.Collection.Number == 26, "Known Item stored identity does not match its ZIP location");
        using var table = new MemoryStream(); using (var writer = new BinaryWriter(table, Encoding.UTF8, true))
        {
            writer.Write(2); writer.Write(0); writer.Write(0);
            foreach (var r in old.Records) { writer.Write(r.Flags); Text(writer, r.Name); writer.Write(r.Node); writer.Write(r.UseFile); writer.Write(0); }
        }
        var patched = source[..old.HeaderEnd].Concat(table.ToArray()).Concat(source[old.BodyStart..]).ToArray(); var next = Sections.Parse(patched);
        Check.That(source.AsSpan(0, old.HeaderEnd).SequenceEqual(patched.AsSpan(0, next.HeaderEnd)) && source.AsSpan(old.BodyStart).SequenceEqual(patched.AsSpan(next.BodyStart)), "Item header or raw body invariant failed");
        Check.That(patched[..next.HeaderEnd].Concat(source[old.HeaderEnd..old.BodyStart]).Concat(patched[next.BodyStart..]).SequenceEqual(source), "Original Item is not exactly restorable");
        var after = Gbx.Parse<CGameItemModel>(new MemoryStream(patched));
        Check.That(after.Node.Ident == before.Node.Ident && after.RefTable!.AncestorLevel == 0 && after.RefTable.Files.Select(f => f.FilePath).SequenceEqual(required), "Patched Item failed independent GBX.NET parse");
        return (patched, new { zipPath, sourceSha256 = Check.Hash(source), outputSha256 = Check.Hash(patched), sourceBytes = source.Length, outputBytes = patched.Length,
            originalHeaderIncludingNodeCountBytes = old.HeaderEnd, originalHeaderSha256 = Check.Hash(source[..old.HeaderEnd]),
            originalRawBodyBytes = bound.BodyBytes, originalRawBodySha256 = bound.BodyHash, sourceBodyOffset = old.BodyStart, outputBodyOffset = next.BodyStart,
            originalAncestor = old.Ancestor, outputAncestor = 0, headerAndRawBodyByteExact = true, flagsNodeIndicesRecordOrderUseFileExact = true,
            originalEntireItemRestorableByOriginalRefTable = true, alreadySiblingNormalized = siblingLayout });
    }
    static void Text(BinaryWriter writer, string value) { var b = Encoding.UTF8.GetBytes(value); writer.Write(b.Length); writer.Write(b); }
    sealed record Ref(int Flags, string Name, int Node, int UseFile, int Directory);
    sealed class Sections
    {
        public int HeaderEnd, BodyStart, Ancestor;
        public List<string> Directories = [];
        public List<Ref> Records = [];
        public static Sections Parse(byte[] bytes)
        {
            using var memory = new MemoryStream(bytes); using var r = new BinaryReader(memory, Encoding.UTF8);
            Check.That(r.ReadBytes(9).SequenceEqual("GBX\x06\0BUUR"u8.ToArray()) && r.ReadUInt32() == 0x2E002000, "Known flag must be native uncompressed binary GBX6 Item");
            var user = r.ReadInt32(); Check.That(user >= 0 && user < bytes.Length - 25, "Bad Item header size"); memory.Position += user;
            Check.That(r.ReadInt32() == 29, "Unknown known-flag node count"); var result = new Sections { HeaderEnd = checked((int)memory.Position) };
            Check.That(r.ReadInt32() == 2, "Unknown known-flag external count"); result.Ancestor = r.ReadInt32(); Tree(r, "", result.Directories, 0);
            for (var i = 0; i < 2; i++) { var flags = r.ReadInt32(); Check.That((flags & 4) == 0, "Numeric resource not allowed"); result.Records.Add(new(flags, Text(r), r.ReadInt32(), r.ReadInt32(), r.ReadInt32())); }
            result.BodyStart = checked((int)memory.Position); return result;
        }
        static void Tree(BinaryReader r, string prefix, List<string> dirs, int depth)
        {
            var count = r.ReadInt32(); Check.That(depth < 8 && count is >= 0 and <= 8, "Unknown reference directory tree");
            for (var i = 0; i < count; i++) { var name = Text(r); var path = prefix.Length == 0 ? name : prefix + "\\" + name; dirs.Add(path); Tree(r, path, dirs, depth + 1); }
        }
        static string Text(BinaryReader r) { var size = r.ReadInt32(); Check.That(size is >= 0 and <= 4096, "Invalid reference string"); var b = r.ReadBytes(size); Check.That(b.Length == size, "Truncated reference string"); return new UTF8Encoding(false, true).GetString(b); }
    }
}
