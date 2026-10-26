using System.Buffers.Binary;
using System.Text;
using TM_Item_Studio.Models.FlagPackaging;

namespace TM_Item_Studio.Models.FxPackaging;

// Chunk 054 has its own lookback scope. Existing definitions and all texture
// strings are copied verbatim; a new identity is appended after existing ones.
internal sealed class FxEmbeddedFiles
{
    internal sealed record Identity(string Id, int? CollectionNumber, string? CollectionString, string Author);
    static readonly UTF8Encoding Utf8 = new(false, true);
    public byte[] Payload { get; }
    public byte[] Zip { get; }
    public IReadOnlyList<Identity> Identities { get; }
    readonly int metadataEnd, zipEnd;
    readonly bool lookbackStarted;

    public FxEmbeddedFiles(byte[] payload)
    {
        Payload = payload;
        PackagingGuard.That(payload.Length is >= 24 and <= PackagingGuard.MaxMapBytes, "Unsupported embedded-files payload size");
        var p = 0;
        PackagingGuard.That(Take(payload, ref p) == 1, "FX packaging requires embedded-files chunk version 1; version 0 is unsupported");
        PackagingGuard.That(Take(payload, ref p) == 0 && Take(payload, ref p) == payload.Length - 12,
            "Unsupported embedded-files flags or enclosed size");
        var count = Count(payload, ref p);
        var definitions = new Dictionary<uint, string>(); var started = false;
        var identities = new List<Identity>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < count; i++)
        {
            var id = ReadId(payload, ref p, definitions, ref started);
            var collection = ReadId(payload, ref p, definitions, ref started);
            var author = ReadId(payload, ref p, definitions, ref started);
            PackagingGuard.That(id.Text is { Length: > 0 } && author.Text is { Length: > 0 }
                && !author.Text.Any(char.IsControl) && (collection.Number.HasValue || collection.Text is { Length: > 0 }),
                "Unsupported embedded identity");
            PackagingGuard.That(paths.Add(CanonicalIdentity(id.Text!)), "Duplicate or case-colliding embedded identity paths");
            identities.Add(new(id.Text!, collection.Number, collection.Text, author.Text!));
        }
        metadataEnd = p; lookbackStarted = started;
        var zipSize = Take(payload, ref p);
        PackagingGuard.That(zipSize <= PackagingGuard.MaxMapBytes && (long)p + zipSize <= payload.Length - 4, "Truncated embedded ZIP");
        Zip = payload.AsSpan(p, checked((int)zipSize)).ToArray(); p += checked((int)zipSize); zipEnd = p;
        var textures = Count(payload, ref p);
        for (var i = 0; i < textures; i++) Text(payload, ref p);
        PackagingGuard.That(p == payload.Length, "Unexpected embedded-files trailer");
        Identities = identities.AsReadOnly();
    }

    // A club identity is an engine identifier, not an Items-relative ZIP path.
    // Retain its namespace so it can never alias the selected local FX item.
    internal static string CanonicalIdentity(string id)
        => id.StartsWith("club:", StringComparison.Ordinal)
            ? "club:" + PackagingGuard.CanonicalPath(id[5..]) : PackagingGuard.CanonicalPath(id);

    public byte[] Replace(byte[] zip, Identity? addition)
    {
        PackagingGuard.That(zip.Length <= PackagingGuard.MaxMapBytes, "Embedded ZIP exceeds the supported size limit");
        using var result = new MemoryStream();
        result.Write(Payload, 0, 12);
        Write(result, checked((uint)(Identities.Count + (addition is null ? 0 : 1))));
        result.Write(Payload, 16, metadataEnd - 16);
        if (addition is not null)
        {
            PackagingGuard.That(addition.CollectionNumber == 26 && addition.CollectionString is null, "New FX identity must use numeric Stadium collection 26");
            if (!lookbackStarted) Write(result, 3);
            Definition(result, addition.Id); Write(result, 26); Definition(result, addition.Author);
        }
        Write(result, checked((uint)zip.Length)); result.Write(zip);
        result.Write(Payload, zipEnd, Payload.Length - zipEnd);
        PackagingGuard.That(result.Length <= PackagingGuard.MaxMapBytes, "Output embedded-files chunk exceeds the supported size limit");
        var output = result.ToArray(); BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(8), output.Length - 12);
        var check = new FxEmbeddedFiles(output);
        PackagingGuard.That(check.Zip.AsSpan().SequenceEqual(zip)
            && check.Identities.SequenceEqual(addition is null ? Identities : Identities.Append(addition)), "Embedded-files splice did not reparse exactly");
        return output;
    }

    readonly record struct IdValue(int? Number, string? Text);
    static IdValue ReadId(byte[] bytes, ref int p, Dictionary<uint, string> definitions, ref bool started)
    {
        if (!started) { PackagingGuard.That(Take(bytes, ref p) == 3, "Embedded identity lookback version must be 3"); started = true; }
        var tag = Take(bytes, ref p);
        PackagingGuard.That(tag is not (uint.MaxValue or 0xBFFFFFFF), "Null or unassigned embedded identity is unsupported");
        var flags = tag >> 30; var index = tag & 0x3FFFFFFF;
        // GBX.NET keeps the complete uint key, including the flag1/flag2
        // namespace. The definition ordinal is global across both forms.
        if (flags is 0 or 3) return new(unchecked((int)tag), null);
        if (index == 0)
        {
            var text = Text(bytes, ref p); var key = checked(tag + (uint)definitions.Count + 1);
            PackagingGuard.That(definitions.TryAdd(key, text), "Colliding embedded lookback definition"); return new(null, text);
        }
        PackagingGuard.That(definitions.TryGetValue(tag, out var reference), "Embedded identity lookback references an absent definition");
        return new(null, reference);
    }
    static string Text(byte[] bytes, ref int p)
    {
        var length = Take(bytes, ref p);
        PackagingGuard.That(length <= 4096 && (long)p + length <= bytes.Length, "Truncated or oversized embedded-files string");
        var value = Utf8.GetString(bytes, p, checked((int)length)); p += checked((int)length);
        PackagingGuard.That(!value.Any(char.IsControl), "Control character in embedded-files string"); return value;
    }
    static int Count(byte[] bytes, ref int p)
    { var count = Take(bytes, ref p); PackagingGuard.That(count <= 4096, "Embedded-files array exceeds the supported count"); return checked((int)count); }
    static uint Take(byte[] bytes, ref int p)
    {
        PackagingGuard.That(p >= 0 && p <= bytes.Length - 4, "Truncated embedded-files integer");
        var value = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(p)); p += 4; return value;
    }
    static void Write(Stream stream, uint value) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, value); stream.Write(b); }
    static void Definition(Stream stream, string value)
    { var bytes = Utf8.GetBytes(value); Write(stream, 0x40000000); Write(stream, checked((uint)bytes.Length)); stream.Write(bytes); }
}
