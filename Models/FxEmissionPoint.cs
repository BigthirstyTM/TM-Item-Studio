using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GBX.NET;
using GBX.NET.Engines.GameData;

namespace TM_Item_Studio.Models;

/// <summary>
/// Bounded native LocalOffsetExpr editing for embedded FX carriers. Unknown
/// particle models remain opaque; only validated constant expression strings
/// and their length words are spliced. Unsupported graphs fail closed.
/// </summary>
public static class FxEmissionPoint
{
    const int MaxBodyBytes = 268435456;
    const int MaxExpressionBytes = 2048;
    const uint Facade = 0xFACADE01;
    static readonly byte[] FxSignature = [0x00, 0xC0, 0x15, 0x09, 0x00, 0xC0, 0x15, 0x09];
    static readonly byte[] ModelEndSignature = [0x01, 0xDE, 0xCA, 0xFA, 0xFF, 0xFF, 0xFF, 0xFF];

    public sealed record Coordinates(float X, float Y, float Z);
    public sealed record Emitter(int LengthOffset, string Expression, Coordinates Position,
        string? WorldOffsetExpression = null);
    public sealed record Inspection(Coordinates Primary, IReadOnlyList<Emitter> Emitters,
        int FxGraphOffset, int SystemNodesVersion);
    public sealed record ExpressionPatch(int SourceLengthOffset, string Before, string After);
    public sealed record Evidence(bool HeaderAndRefTablePreserved, bool OriginalBodyRestorableExactly,
        bool CompressionModeRetained, string SourceBodySha256, string OutputBodySha256,
        int SourceBodyBytes, int OutputBodyBytes, IReadOnlyList<ExpressionPatch> Patches);
    public sealed record Result(byte[] ItemBytes, Inspection Before, Inspection After,
        Evidence Proof, bool NativeOffsetWritten);

    public static Inspection Read(ReadOnlyMemory<byte> itemBytes)
    {
        var archive = new Archive(itemBytes.ToArray());
        return Inspect(archive.Body, archive.NodeCount);
    }

    internal static bool BodiesEqual(ReadOnlyMemory<byte> first, ReadOnlyMemory<byte> second)
        => new Archive(first.ToArray()).Body.AsSpan().SequenceEqual(new Archive(second.ToArray()).Body);

    public static bool TryRead(ReadOnlyMemory<byte> itemBytes, out Inspection? inspection, out string? error)
    {
        try { inspection = Read(itemBytes); error = null; return true; }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { inspection = null; error = exception.Message; return false; }
    }

    public static Result Export(ReadOnlyMemory<byte> itemBytes, Coordinates primary)
    {
        ArgumentNullException.ThrowIfNull(primary);
        Require(IsSupported(primary), "The FX emission point must contain finite coordinates between -10000 and 10000 metres.");
        var archive = new Archive(itemBytes.ToArray());
        var before = Inspect(archive.Body, archive.NodeCount);
        var patches = new List<ExpressionPatch>();
        var expected = new List<Coordinates>();
        foreach (var emitter in before.Emitters)
        {
            // Apply one primary delta, preserving all secondary emitters' offsets.
            var point = new Coordinates(
                (float)(emitter.Position.X + ((double)primary.X - before.Primary.X)),
                (float)(emitter.Position.Y + ((double)primary.Y - before.Primary.Y)),
                (float)(emitter.Position.Z + ((double)primary.Z - before.Primary.Z)));
            Require(IsSupported(point), "Moving this FX system would put a secondary emitter outside the supported coordinate range.");
            expected.Add(point);
            var expression = primary == before.Primary ? emitter.Expression : Format(point);
            patches.Add(new(emitter.LengthOffset, emitter.Expression, expression));
        }
        var patched = Splice(archive.Body, patches, reverse: false);
        var restored = Splice(patched, patches, reverse: true);
        Require(restored.AsSpan().SequenceEqual(archive.Body), "The original complete FX body is not exactly restorable from the expression splices.");
        var result = patched.AsSpan().SequenceEqual(archive.Body) ? archive.Source.ToArray() : archive.Write(patched);
        var reopened = new Archive(result);
        Require(reopened.Prefix.AsSpan().SequenceEqual(archive.Prefix)
            && reopened.Body.AsSpan().SequenceEqual(patched), "The exported FX container differs from the verified raw splice.");
        var after = Inspect(reopened.Body, reopened.NodeCount);
        Require(after.Primary == primary && after.Emitters.Select(emitter => emitter.Position).SequenceEqual(expected),
            "The exported FX emission points did not reparse to the requested translation.");
        return new(result, before, after,
            new(true, true, archive.Source[7] == result[7], Hash(archive.Body), Hash(patched),
                archive.Body.Length, patched.Length, patches.AsReadOnly()), true);
    }

    static byte[] Splice(byte[] body, IReadOnlyList<ExpressionPatch> patches, bool reverse)
    {
        using var output = new MemoryStream();
        var cursor = 0;
        var delta = 0;
        foreach (var patch in patches)
        {
            var old = Encode(patch.Before);
            var replacement = Encode(patch.After);
            var offset = checked(patch.SourceLengthOffset + (reverse ? delta : 0));
            var input = reverse ? replacement : old;
            Require(offset >= cursor && offset <= body.Length - input.Length
                && body.AsSpan(offset, input.Length).SequenceEqual(input), "FX expression splice does not match its validated source bytes.");
            output.Write(body, cursor, offset - cursor);
            output.Write(reverse ? old : replacement);
            cursor = offset + input.Length;
            delta = checked(delta + replacement.Length - old.Length);
        }
        output.Write(body, cursor, body.Length - cursor);
        Require(output.Length <= MaxBodyBytes, "The FX body exceeds the supported size limit after editing.");
        return output.ToArray();
    }

    static Inspection Inspect(byte[] body, int nodeCount)
    {
        var offsets = new List<int>();
        for (var cursor = 0; cursor <= body.Length - FxSignature.Length;)
        {
            var relative = body.AsSpan(cursor).IndexOf(FxSignature);
            if (relative < 0) break;
            var found = cursor + relative;
            offsets.Add(found); cursor = found + 1;
        }
        Require(offsets.Count == 1, "A unique embedded CPlugFxSystem graph is required for native emission-point editing.");
        var graph = offsets[0];
        Require(graph >= 4 && UInt(body, graph - 4) > 0 && UInt(body, graph - 4) < nodeCount,
            "The FX graph does not have a valid embedded node reference.");
        var position = graph + 8;
        Require(TakeUInt(body, ref position) == 1, "Unsupported CPlugFxSystem archive version.");
        var systemVersion = checked((int)TakeUInt(body, ref position));
        Require(systemVersion is 8 or 10, "Unsupported FX system-node version; only the validated version 8 and 10 carriers can be edited.");
        Require(TakeUInt(body, ref position) == 0, "The supported FX graph must have a Parallel root.");
        SkipLookback(body, ref position);
        var count = TakeUInt(body, ref position);
        Require(count is > 0 and <= 16, "Unsupported FX Parallel child count.");
        var solutions = new List<List<Emitter>>();
        var attempts = 0;
        FindEmitters(body, nodeCount, position, checked((int)count), [], solutions, ref attempts);
        Require(attempts <= 128, "The FX graph exceeds the bounded validation scan; no emission fields were edited.");
        Require(solutions.Count == 1, "Cannot uniquely validate the FX particle-emitter graph and its constant local offsets.");
        var emitters = solutions[0].AsReadOnly();
        return new(emitters[0].Position, emitters, graph, systemVersion);
    }

    static void FindEmitters(byte[] body, int nodeCount, int position, int remaining,
        List<Emitter> preceding, List<List<Emitter>> solutions, ref int attempts)
    {
        if (solutions.Count > 1 || ++attempts > 128) return;
        if (remaining == 0)
        {
            // The supported graph has no extra contexts or graph variables.
            if (position <= body.Length - 20 && UInt(body, position) == uint.MaxValue
                && UInt(body, position + 4) == uint.MaxValue && UInt(body, position + 8) == 0
                && UInt(body, position + 12) == 55 && UInt(body, position + 16) == Facade)
                solutions.Add([.. preceding]);
            return;
        }
        try
        {
            if (TakeUInt(body, ref position) != 4) return; // ParticleEmitter only
            SkipLookback(body, ref position);
            var reference = TakeUInt(body, ref position);
            if (reference == 0 || reference >= nodeCount) return;
            if (TakeUInt(body, ref position) != 0x090B3000
                || TakeUInt(body, ref position) != 0x090B3000
                || TakeUInt(body, ref position) != 10) return;
        }
        catch (InvalidDataException) { return; }
        // Models may include opaque particle/bitmap/image nodes. Delimit them
        // by a complete emitter tail AND the recursively validated continuation,
        // not by the first FACADE word or an unanchored float3 text match.
        var search = position;
        var candidates = 0;
        while (search <= body.Length - ModelEndSignature.Length && candidates < 128 && solutions.Count <= 1)
        {
            var relative = body.AsSpan(search).IndexOf(ModelEndSignature);
            if (relative < 0) break;
            var end = search + relative;
            search = end + 1; candidates++;
            try
            {
                var tail = end + ModelEndSignature.Length; // no JointName
                var localOffset = tail;
                var local = TakeString(body, ref tail);
                if (!TryCoordinates(local, out var point)) continue;
                // Ten expression strings: local, world, velocity, frequency,
                // scale, ambient light, up, direction, opacity, water top.
                var worldOffset = TakeString(body, ref tail);
                for (var field = 2; field < 10; field++) TakeString(body, ref tail);
                if (TakeUInt(body, ref tail) > 1) continue;
                TakeString(body, ref tail); // linear hue
                TakeString(body, ref tail); // hue lightness
                preceding.Add(new(localOffset, local, point!, worldOffset));
                FindEmitters(body, nodeCount, tail, remaining - 1, preceding, solutions, ref attempts);
                preceding.RemoveAt(preceding.Count - 1);
            }
            catch (InvalidDataException) { }
        }
        if (candidates >= 128) attempts = 129;
    }

    static bool TryCoordinates(string expression, out Coordinates? point)
    {
        point = null;
        var text = expression.Trim();
        if (!text.StartsWith("float3(", StringComparison.Ordinal) || !text.EndsWith(')')) return false;
        var components = text[7..^1].Split(',');
        if (components.Length is not (1 or 3)) return false;
        var values = new float[3];
        for (var i = 0; i < components.Length; i++)
            if (!float.TryParse(components[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out values[i])) return false;
        if (components.Length == 1) values[1] = values[2] = values[0];
        var parsed = new Coordinates(values[0], values[1], values[2]);
        if (!IsSupported(parsed)) return false;
        point = parsed; return true;
    }

    static bool IsSupported(Coordinates point) => float.IsFinite(point.X) && float.IsFinite(point.Y)
        && float.IsFinite(point.Z) && Math.Abs(point.X) <= 10000 && Math.Abs(point.Y) <= 10000 && Math.Abs(point.Z) <= 10000;
    static string Format(Coordinates point) => string.Create(CultureInfo.InvariantCulture, $"float3({point.X:R},{point.Y:R},{point.Z:R})");
    static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    static byte[] Encode(string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        var result = new byte[bytes.Length + 4];
        BinaryPrimitives.WriteInt32LittleEndian(result, bytes.Length);
        bytes.CopyTo(result, 4); return result;
    }
    static void SkipLookback(byte[] bytes, ref int position)
    {
        var tag = TakeUInt(bytes, ref position);
        if (tag is uint.MaxValue or 0xBFFFFFFF) return;
        var flags = tag >> 30; var index = tag & 0x3FFFFFFF;
        Require(flags is 1 or 2, "Unsupported FX name lookback string.");
        if (index == 0) TakeString(bytes, ref position);
    }
    static string TakeString(byte[] bytes, ref int position)
    {
        var length = TakeUInt(bytes, ref position);
        Require(length <= MaxExpressionBytes && (long)position + length <= bytes.Length, "Invalid FX expression length.");
        var start = position; position += checked((int)length);
        for (var i = start; i < position; i++) Require(bytes[i] is >= 32 and <= 126, "Unsupported FX expression/name encoding.");
        return Encoding.ASCII.GetString(bytes, start, checked((int)length));
    }
    static uint TakeUInt(byte[] bytes, ref int position) { var value = UInt(bytes, position); position += 4; return value; }
    static uint UInt(byte[] bytes, int position)
    {
        Require(position >= 0 && position <= bytes.Length - 4, "Truncated FX archive integer.");
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position, 4));
    }
    static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }

    internal sealed class Archive
    {
        public byte[] Source { get; }
        public byte[] Prefix { get; }
        public byte[] Body { get; }
        public int NodeCount { get; }
        public Archive(byte[] source)
        {
            Source = source;
            Require(source.Length is > 25 and <= MaxBodyBytes
                && source.AsSpan(0, 7).SequenceEqual("GBX\x06\0BU"u8) && source[7] is (byte)'C' or (byte)'U',
                "Only bounded binary GBX6 items with uncompressed reference tables support FX editing.");
            using var stream = new MemoryStream(source, writable: false);
            var header = Gbx.ParseHeader<CGameItemModel>(stream);
            Require(header.Header.ClassId == 0x2E002000, "The FX carrier must be a CGameItemModel item.");
            NodeCount = header.Header.NumNodes;
            Require(NodeCount is > 0 and <= 10000000, "Unsupported FX item node count.");
            // ParseHeader consumes compressed-body size words but not its data.
            var offset = checked((int)stream.Position - (source[7] == (byte)'C' ? 8 : 0));
            Require(offset >= 25 && offset <= source.Length - (source[7] == (byte)'C' ? 8 : 1), "Invalid FX body offset.");
            Prefix = source[..offset];
            if (source[7] == (byte)'C')
            {
                var size = UInt(source, offset); var compressedSize = UInt(source, offset + 4);
                Require(size is > 0 and <= MaxBodyBytes && compressedSize > 0
                    && (long)offset + 8 + compressedSize == source.Length, "Invalid or trailing compressed FX body.");
                Require(Gbx.LZO is not null, "LZO must be initialized before editing a compressed FX item.");
                Body = new byte[checked((int)size)];
                Gbx.LZO!.Decompress(source[(offset + 8)..], Body);
            }
            else
            {
                Require(source.Length - offset is > 0 and <= MaxBodyBytes, "Uncompressed FX body exceeds the size limit.");
                Body = source[offset..];
            }
        }
        public byte[] Write(byte[] body)
        {
            using var output = new MemoryStream(); output.Write(Prefix);
            if (Source[7] == (byte)'C')
            {
                var compressed = Gbx.LZO!.Compress(body);
                var sizes = new byte[8];
                BinaryPrimitives.WriteInt32LittleEndian(sizes, body.Length);
                BinaryPrimitives.WriteInt32LittleEndian(sizes.AsSpan(4), compressed.Length);
                output.Write(sizes); output.Write(compressed);
            }
            else output.Write(body);
            return output.ToArray();
        }
    }
}





