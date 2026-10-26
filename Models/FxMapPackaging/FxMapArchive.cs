using System.Buffers.Binary;
using System.Text;
using GBX.NET;
using GBX.NET.Engines.Game;
using TM_Item_Studio.Models.FlagPackaging;

namespace TM_Item_Studio.Models.FxPackaging;

internal sealed class FxMapArchive
{
    public byte[] Source { get; }
    public byte[] Header { get; }
    public byte[] Body { get; }
    public CGameCtnChallenge Map { get; }
    public FxEmbeddedFiles Embedded { get; }
    public int ChunkStart { get; }
    public int ChunkEnd { get; }

    public FxMapArchive(byte[] source)
    {
        Source = source;
        PackagingGuard.That(source.Length is > 25 and <= PackagingGuard.MaxMapBytes
            && source.AsSpan(0, 7).SequenceEqual("GBX\x06\0BU"u8) && source[7] is (byte)'C' or (byte)'U',
            "Only bounded binary GBX6 maps with uncompressed reference tables are supported");
        using var stream = new MemoryStream(source, writable: false);
        var header = Gbx.ParseHeader<CGameCtnChallenge>(stream);
        PackagingGuard.That(header.Header.ClassId == 0x03043000, "Input is not a Trackmania map");
        var offset = checked((int)stream.Position - (source[7] == (byte)'C' ? 8 : 0)); Header = source[..offset];
        if (source[7] == (byte)'C')
        {
            var size = Int(source, offset); var compressed = Int(source, offset + 4);
            PackagingGuard.That(size is > 0 and <= PackagingGuard.MaxMapBytes && compressed > 0
                && (long)offset + 8 + compressed == source.Length, "Invalid or trailing compressed map body");
            Body = new byte[size]; Gbx.LZO!.Decompress(source[(offset + 8)..], Body);
        }
        else { PackagingGuard.That(source.Length - offset > 0, "Empty map body"); Body = source[offset..]; }
        Map = Gbx.Parse<CGameCtnChallenge>(new MemoryStream(source, writable: false),
            new GbxReadSettings { SkipChunkIds = [0x0304305B] }).Node;
        var candidates = new List<(int Start, int End, FxEmbeddedFiles Data)>();
        ReadOnlySpan<byte> signature = [0x54, 0x30, 0x04, 0x03, 0x50, 0x49, 0x4B, 0x53];
        for (var scan = 0; scan <= Body.Length - 12;)
        {
            var relative = Body.AsSpan(scan).IndexOf(signature); if (relative < 0) break;
            var start = scan + relative; scan = start + 1;
            var size = Int(Body, start + 8);
            if (size < 24 || (long)start + 12 + size > Body.Length) continue;
            try
            {
                var data = new FxEmbeddedFiles(Body.AsSpan(start + 12, size).ToArray());
                if (data.Zip.AsSpan().SequenceEqual(Map.EmbeddedZipData ?? []) && MetadataMatches(Map, data))
                    candidates.Add((start, start + 12 + size, data));
            }
            catch (Exception error) when (error is InvalidDataException or DecoderFallbackException) { }
        }
        PackagingGuard.That(candidates.Count == 1, "Cannot uniquely locate a supported embedded-files chunk matching the parsed map");
        (ChunkStart, ChunkEnd, Embedded) = candidates.Single();
    }

    public (byte[] Bytes, object Evidence) Replace(byte[] payload)
    {
        using var chunk = new MemoryStream(); chunk.Write(Body, ChunkStart, 8); chunk.Write(BitConverter.GetBytes(payload.Length)); chunk.Write(payload);
        var replacement = chunk.ToArray();
        var patched = Body[..ChunkStart].Concat(replacement).Concat(Body[ChunkEnd..]).ToArray();
        var restored = patched[..ChunkStart].Concat(Body[ChunkStart..ChunkEnd]).Concat(patched[(ChunkStart + replacement.Length)..]).ToArray();
        PackagingGuard.That(restored.AsSpan().SequenceEqual(Body), "Original complete map body cannot be restored exactly");
        using var output = new MemoryStream(); output.Write(Header);
        if (Source[7] == (byte)'C') { var compressed = Gbx.LZO!.Compress(patched); output.Write(BitConverter.GetBytes(patched.Length)); output.Write(BitConverter.GetBytes(compressed.Length)); output.Write(compressed); }
        else output.Write(patched);
        PackagingGuard.That(output.Length <= PackagingGuard.MaxMapBytes, "Output map exceeds the supported size limit");
        var bytes = output.ToArray(); var check = new FxMapArchive(bytes);
        PackagingGuard.That(check.Header.AsSpan().SequenceEqual(Header) && check.Body.AsSpan().SequenceEqual(patched)
            && check.Embedded.Payload.AsSpan().SequenceEqual(payload), "Output map does not match the exact raw splice");
        return (bytes, new { embeddedChunkOffset = ChunkStart, oldChunkBytes = ChunkEnd - ChunkStart, newChunkBytes = replacement.Length,
            headerAndReferenceTableByteExact = true, allUncompressedBytesOutsideEmbeddedChunkExact = true,
            originalCompleteUncompressedBodyRestorableExactly = true, mapNodeSerializationPerformed = false,
            compressionModeRetained = (char)Source[7], sourceBodySha256 = PackagingGuard.Hash(Body), outputBodySha256 = PackagingGuard.Hash(patched) });
    }

    static bool MetadataMatches(CGameCtnChallenge map, FxEmbeddedFiles data)
    {
        var expected = map.ExpectedEmbeddedItemModels?.ToArray() ?? [];
        return expected.Length == data.Identities.Count && expected.Zip(data.Identities).All(pair =>
            pair.First.Id == pair.Second.Id && pair.First.Author == pair.Second.Author
            && pair.First.Collection.Number == pair.Second.CollectionNumber && pair.First.Collection.String == pair.Second.CollectionString);
    }
    static int Int(byte[] bytes, int offset)
    { PackagingGuard.That(offset >= 0 && offset <= bytes.Length - 4, "Truncated map integer"); return BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset)); }
}
