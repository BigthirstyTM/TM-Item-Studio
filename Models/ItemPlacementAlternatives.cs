using System.Buffers.Binary;
using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Serialization.Chunking;

namespace TM_Item_Studio.Models;

/// <summary>
/// The skippable <c>2E020004</c> placement-alternative list. GBX.NET keeps the chunk opaque
/// (Ignore = true), so the payload is decoded and encoded here instead of by the serializer:
///
/// <code>
/// int32 version (0)   int32 count   count x (Vec3 position, Vec3 rotation in degrees)
/// </code>
///
/// 24 bytes per entry. Game-authored items carry real lists (two for a gate, up to 24 measured);
/// items written by the importer carry the empty list (version 0, count 0 = 8 zero bytes).
/// Edits are refused rather than guessed: an unrecognised payload is never overwritten.
/// </summary>
public static class ItemPlacementAlternatives
{
    public const uint ChunkId = 0x2E020004;
    public const int EntrySize = 24;
    public const int HeaderSize = 8;
    public const int MaxEntries = 256;

    /// <summary>One placement alternative: a position in metres and a rotation in degrees.</summary>
    public readonly record struct Alternative(Vec3 Position, Vec3 RotationDegrees);

    /// <summary>Read the alternatives. An absent chunk is an empty list, not an error.</summary>
    public static Alternative[] Read(CGameItemPlacementParam placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        if (placement.Chunks.Get(ChunkId) is not { } chunk)
            return [];
        if (chunk is not ISkippableChunk { Data: { } data })
            throw new NotSupportedException("The 2E020004 chunk carries no payload to read.");
        return Decode(data);
    }

    /// <summary>
    /// Replace the alternative list. The chunk keeps its place because the serializer orders
    /// chunks itself; nothing else in the node is touched.
    /// </summary>
    public static void Write(CGameItemPlacementParam placement, IReadOnlyList<Alternative> alternatives)
    {
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(alternatives);
        if (alternatives.Count > MaxEntries)
            throw new ArgumentOutOfRangeException(nameof(alternatives),
                $"At most {MaxEntries} placement alternatives are supported; no edit was applied.");

        foreach (var alternative in alternatives)
            Validate(alternative);

        var existing = placement.Chunks.Get(ChunkId) as ISkippableChunk;
        // Never replace a payload we cannot read: an unknown layout is data, not an empty list.
        if (existing?.Data is { } current)
            _ = Decode(current);

        var payload = Encode(alternatives);
        if (existing is not null)
        {
            existing.Data = payload;
            return;
        }

        placement.Chunks.Add(new SkippableChunk(ChunkId) { Data = payload });
    }

    /// <summary>Add one alternative at the end of the list.</summary>
    public static void Add(CGameItemPlacementParam placement, Alternative alternative)
    {
        Validate(alternative);
        Write(placement, [.. Read(placement), alternative]);
    }

    /// <summary>Replace one alternative; the index is validated against the stored list.</summary>
    public static void Edit(CGameItemPlacementParam placement, int index, Alternative alternative)
    {
        Validate(alternative);
        var alternatives = Read(placement);
        ValidateIndex(index, alternatives.Length);
        alternatives[index] = alternative;
        Write(placement, alternatives);
    }

    /// <summary>Remove one alternative; an empty list keeps the chunk with count 0.</summary>
    public static void Remove(CGameItemPlacementParam placement, int index)
    {
        var alternatives = Read(placement);
        ValidateIndex(index, alternatives.Length);
        Write(placement, alternatives.Where((_, i) => i != index).ToArray());
    }

    /// <summary>Change only the position, leaving the stored rotation untouched.</summary>
    public static void EditPosition(CGameItemPlacementParam placement, int index, Vec3 position)
    {
        ArgumentNullException.ThrowIfNull(placement);
        ValidateFinite(position, nameof(position));
        var alternatives = Read(placement);
        ValidateIndex(index, alternatives.Length);
        var updated = (Alternative[])alternatives.Clone();
        updated[index] = alternatives[index] with { Position = position };
        Write(placement, updated);
    }

    /// <summary>Change only the rotation in degrees, leaving the stored position untouched.</summary>
    public static void EditRotation(CGameItemPlacementParam placement, int index, Vec3 rotationDegrees)
    {
        ArgumentNullException.ThrowIfNull(placement);
        ValidateFinite(rotationDegrees, nameof(rotationDegrees));
        var alternatives = Read(placement);
        ValidateIndex(index, alternatives.Length);
        var updated = (Alternative[])alternatives.Clone();
        updated[index] = alternatives[index] with { RotationDegrees = rotationDegrees };
        Write(placement, updated);
    }

    private static Alternative[] Decode(byte[] data)
    {
        if (data.Length < HeaderSize)
            throw new NotSupportedException(
                $"The 2E020004 payload is {data.Length} bytes, shorter than the {HeaderSize}-byte header.");

        var version = BinaryPrimitives.ReadInt32LittleEndian(data);
        var count = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4));
        if (version != 0)
            throw new NotSupportedException($"Unsupported 2E020004 version {version}; no edit was applied.");
        if (count < 0 || count > MaxEntries)
            throw new NotSupportedException($"The 2E020004 entry count {count} is outside the supported range.");
        var expected = HeaderSize + count * EntrySize;
        if (data.Length != expected)
            throw new NotSupportedException(
                $"The 2E020004 payload is {data.Length} bytes but {count} entries need {expected}.");

        var alternatives = new Alternative[count];
        for (var i = 0; i < count; i++)
        {
            var offset = HeaderSize + i * EntrySize;
            alternatives[i] = new Alternative(
                new Vec3(
                    BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset)),
                    BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset + 4)),
                    BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset + 8))),
                new Vec3(
                    BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset + 12)),
                    BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset + 16)),
                    BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset + 20))));
        }

        return alternatives;
    }

    private static byte[] Encode(IReadOnlyList<Alternative> alternatives)
    {
        var buffer = new byte[HeaderSize + alternatives.Count * EntrySize];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, 0);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4), alternatives.Count);
        for (var i = 0; i < alternatives.Count; i++)
        {
            var offset = HeaderSize + i * EntrySize;
            var alternative = alternatives[i];
            BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset), alternative.Position.X);
            BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset + 4), alternative.Position.Y);
            BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset + 8), alternative.Position.Z);
            BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset + 12), alternative.RotationDegrees.X);
            BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset + 16), alternative.RotationDegrees.Y);
            BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset + 20), alternative.RotationDegrees.Z);
        }

        return buffer;
    }

    private static void Validate(Alternative alternative)
    {
        ValidateFinite(alternative.Position, nameof(alternative));
        ValidateFinite(alternative.RotationDegrees, nameof(alternative));
    }

    private static void ValidateFinite(Vec3 value, string name)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            throw new ArgumentException("Placement alternative values must be finite.", name);
    }

    private static void ValidateIndex(int index, int length)
    {
        if (index < 0 || index >= length) throw new ArgumentOutOfRangeException(nameof(index));
    }
}
