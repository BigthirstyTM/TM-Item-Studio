using System.IO.Compression;
using System.Numerics;
using System.Text.Json;

namespace TM_Item_Studio.Models;

/// <summary>Validated mesh-sequence interchange emitted by the Blender add-on.</summary>
public sealed class BakedClothSequence
{
    public int FormatVersion { get; init; }
    public string? Exporter { get; init; }
    public int FrameCount { get; init; }
    public float PeriodSeconds { get; init; } = 12f;
    public int VertexCount { get; init; }
    public int TriangleCount { get; init; }
    public int[] Indices { get; init; } = Array.Empty<int>();
    public float[][] Uv0 { get; init; } = Array.Empty<float[]>();
    public int[] VertexIndices { get; init; } = Array.Empty<int>();
    public SequenceAnchor? Anchor { get; init; }
    public SequenceFrame[] Frames { get; init; } = Array.Empty<SequenceFrame>();
    public SequenceValidation? Validation { get; init; }

    public sealed class SequenceFrame
    {
        public float[][] Positions { get; init; } = Array.Empty<float[]>();
        public float[][] Normals { get; init; } = Array.Empty<float[]>();
    }

    public sealed class SequenceAnchor
    {
        public string? VertexGroup { get; init; }
        public float[] Position { get; init; } = Array.Empty<float>();
        public bool Pinned { get; init; }
    }

    public sealed class SequenceValidation
    {
        public bool TopologyChecked { get; init; }
        public int CheckedFrameCount { get; init; }
        public bool UvLayoutChecked { get; init; }
    }

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static BakedClothSequence Read(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length < 2) throw new InvalidDataException("The baked cloth file is empty.");
        if (bytes[0] == (byte)'P' && bytes[1] == (byte)'K')
        {
            using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read, leaveOpen: false);
            var entry = archive.Entries.SingleOrDefault(e => e.FullName.EndsWith(".tmcloth.json", StringComparison.OrdinalIgnoreCase));
            if (entry is null) throw new InvalidDataException("The ZIP contains no .tmcloth.json mesh sequence.");
            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            bytes = memory.ToArray();
        }

        var value = JsonSerializer.Deserialize<BakedClothSequence>(bytes, Json)
            ?? throw new InvalidDataException("The baked cloth JSON is empty or invalid.");
        value.Validate();
        return value;
    }

    public void Validate()
    {
        if (FormatVersion != 1) throw new InvalidDataException($"Unsupported baked cloth format version {FormatVersion}.");
        if (FrameCount != 86) throw new InvalidDataException($"Trackmania flag export currently requires exactly 86 frames; received {FrameCount}.");
        if (!float.IsFinite(PeriodSeconds) || PeriodSeconds < .25f || PeriodSeconds > 60f)
            throw new InvalidDataException("The baked cloth playback period must be between 0.25 and 60 seconds.");
        if (VertexCount is < 3 or > ItemWavingFlagExport.MaximumSourceVertices * 4)
            throw new InvalidDataException("The baked cloth vertex count is outside the supported range.");
        if (TriangleCount <= 0 || Indices.Length != TriangleCount * 3)
            throw new InvalidDataException("The baked cloth index stream is incomplete.");
        if (Frames.Length != FrameCount) throw new InvalidDataException("The baked cloth frame count does not match the frame array.");
        if (VertexIndices.Length != VertexCount) throw new InvalidDataException("The baked cloth vertex mapping is missing or has the wrong length.");
        if (Uv0.Length != VertexCount || Uv0.Any(uv => uv.Length != 2))
            throw new InvalidDataException("The baked cloth UV0 stream must contain one UV pair per sampled loop vertex.");
        if (Indices.Any(i => i < 0 || i >= VertexCount)) throw new InvalidDataException("The baked cloth index stream is out of range.");
        if (VertexIndices.Any(i => i < 0 || i >= VertexCount)) throw new InvalidDataException("The baked cloth vertex mapping is out of range.");
        for (var frame = 0; frame < Frames.Length; frame++)
        {
            var value = Frames[frame];
            if (value.Positions.Length != VertexCount || value.Normals.Length != VertexCount)
                throw new InvalidDataException($"Frame {frame} does not contain one position and normal per loop vertex.");
            for (var i = 0; i < VertexCount; i++)
            {
                if (value.Positions[i].Length != 3 || value.Normals[i].Length != 3
                    || value.Positions[i].Any(v => !float.IsFinite(v)) || value.Normals[i].Any(v => !float.IsFinite(v)))
                    throw new InvalidDataException($"Frame {frame} contains an invalid position or normal.");
            }
        }
        if (Validation is not { TopologyChecked: true, CheckedFrameCount: 86, UvLayoutChecked: true })
            throw new InvalidDataException("The sequence was not marked as fully topology-validated by the exporter.");
        if (Anchor is { Position.Length: > 0 } anchor && anchor.Position.Length != 3)
            throw new InvalidDataException("The anchor position must contain three coordinates.");
    }

    /// <summary>Collapses Blender's loop-expanded sequence onto the selected Item Studio vertex stream.</summary>
    public SequenceGeometry MatchItemGeometry(Vector3[] sourcePositions, Vector2[] sourceUvs, int[] sourceIndices)
    {
        Validate();
        if (sourcePositions.Length < 3 || sourceUvs.Length != sourcePositions.Length)
            throw new InvalidDataException("The selected Item Studio mesh has incomplete geometry or UV data.");
        var rowsBySource = Enumerable.Range(0, VertexCount).GroupBy(i => VertexIndices[i]).ToDictionary(g => g.Key, g => g.ToArray());
        if (rowsBySource.Count != sourcePositions.Length || rowsBySource.Keys.Any(k => k < 0 || k >= sourcePositions.Length))
            throw new InvalidDataException("The Blender sequence vertex mapping does not match the selected Item Studio mesh vertex count.");

        var rowToSource = new int[VertexCount];
        foreach (var pair in rowsBySource)
        {
            var source = pair.Key;
            foreach (var row in pair.Value)
            {
                rowToSource[row] = source;
                var p = new Vector3(Frames[0].Positions[row][0], Frames[0].Positions[row][1], Frames[0].Positions[row][2]);
                var uv = new Vector2(Uv0[row][0], Uv0[row][1]);
                if (Vector3.Distance(p, sourcePositions[source]) > 0.0005f || Vector2.Distance(uv, sourceUvs[source]) > 0.0005f)
                    throw new InvalidDataException($"Blender rest frame does not match selected Item Studio vertex {source}.");
            }
        }
        var remappedIndices = Indices.Select(i => rowToSource[i]).ToArray();
        if (!remappedIndices.SequenceEqual(sourceIndices))
            throw new InvalidDataException("Blender triangle order does not match the selected Item Studio mesh.");

        var positions = new Vector3[FrameCount][];
        var normals = new Vector3[FrameCount][];
        for (var frame = 0; frame < FrameCount; frame++)
        {
            positions[frame] = new Vector3[sourcePositions.Length];
            normals[frame] = new Vector3[sourcePositions.Length];
            foreach (var pair in rowsBySource)
            {
                var source = pair.Key;
                var rows = pair.Value;
                var first = rows[0];
                var p = new Vector3(Frames[frame].Positions[first][0], Frames[frame].Positions[first][1], Frames[frame].Positions[first][2]);
                var n = Vector3.Zero;
                foreach (var row in rows)
                {
                    var candidate = new Vector3(Frames[frame].Positions[row][0], Frames[frame].Positions[row][1], Frames[frame].Positions[row][2]);
                    if (Vector3.Distance(candidate, p) > 0.0005f)
                        throw new InvalidDataException($"Frame {frame} splits one Item Studio vertex into different positions.");
                    n += new Vector3(Frames[frame].Normals[row][0], Frames[frame].Normals[row][1], Frames[frame].Normals[row][2]);
                }
                if (n.LengthSquared() < 1e-12f) throw new InvalidDataException($"Frame {frame} has a zero normal at vertex {source}.");
                positions[frame][source] = p;
                normals[frame][source] = Vector3.Normalize(n);
            }
        }
        return new SequenceGeometry(positions, normals, remappedIndices);
    }
}

public sealed record SequenceGeometry(Vector3[][] Positions, Vector3[][] Normals, int[] Indices);





