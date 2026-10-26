using System.Security.Cryptography;
using System.Reflection;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Game;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;
using TM_Item_Studio.Models;

// Research fixtures. A successful save/reparse is NOT an ingame rendering pass.
internal static class FlagShapes
{
    const string Source = @"C:\Users\PC\Documents\Trackmania\Items\XertrovItems\ztest-flag3.Item.Gbx";
    const string Vanilla = @"C:\Users\PC\OpenplanetNext\Extract\GameData\Stadium\Items\Flag16m.Item.Gbx";
    static CPlugPrefab Prefab(CGameItemModel item) =>
        (CPlugPrefab)((NPlugItem_SVariantList)item.EntityModel!).Variants![0].EntityModel!;
    static CPlugSolid2Model Mesh(CGameItemModel item) =>
        ((CPlugDynaObjectModel)Prefab(item).Ents![1].Model!).Mesh!;

    public static int VerifyMap(string path, string modelFolder, int itemCount = 4, int embeddedCount = 3)
    {
        var map = Gbx.Parse<CGameCtnChallenge>(path, new GbxReadSettings { SkipChunkIds = [0x0304305B] }).Node;
        if (map.AnchoredObjects?.Count != itemCount) throw new Exception($"Expected {itemCount} comparison flags");
        using var zip = map.OpenReadEmbeddedZipData();
        var count = 0;
        foreach (var entry in zip.Entries)
        {
            using var stream = entry.Open();
            var embedded = Gbx.Parse<CGameItemModel>(stream).Node;
            var source = Gbx.Parse<CGameItemModel>(Path.Combine(modelFolder, entry.Name)).Node;
            if (!string.IsNullOrEmpty(embedded.ArchetypeRef)) throw new Exception("Archetype reference in embedded model");
            for (var lod = 0; lod < 5; lod++)
            {
                var a = (CPlugVisualIndexedTriangles)Mesh(embedded).Visuals![lod];
                var b = (CPlugVisualIndexedTriangles)Mesh(source).Visuals![lod];
                if (!a.Vertices.Select(p => p.Position).SequenceEqual(b.Vertices.Select(p => p.Position))
                    || !a.SubVisuals!.SequenceEqual(b.SubVisuals!)
                    || !a.IndexBuffer!.Indices.SequenceEqual(b.IndexBuffer!.Indices)
                    || !a.VertexStreams.SelectMany(s => s.Positions ?? []).SequenceEqual(b.VertexStreams.SelectMany(s => s.Positions ?? [])))
                    throw new Exception($"Embedded shape differs: {entry.Name} LOD{lod}");
            }
            count++;
            Console.WriteLine($"PASS {entry.Name}: all five LODs, vertex positions, indices and 86-frame tables survived map embedding");
        }
        if (count != embeddedCount) throw new Exception($"Expected {embeddedCount} embedded custom models, got {count}");
        return 0;
    }

    public static int Create(string folder)
    {
        Directory.CreateDirectory(folder);
        var manifest = new List<object>();
        foreach (var shape in new[] { "Control", "Long250", "Swallowtail" })
        {
            var item = Gbx.Parse<CGameItemModel>(Source).Node;
            var id = "TMIS_Shape11_" + shape;
            ItemFlagConversion.Convert(item);
            ItemExportValidator.EnsureGameReady(item, id);
            item.Ident = new Ident("", item.Ident!.Collection, item.Ident.Author);
            item.ArchetypeRef = "";
            item.Name = "TMIS shape experiment - " + shape;
            item.Chunks.Add(Gbx.Parse<CGameItemModel>(Vanilla).Node.Chunks
                .OfType<CPlugGameSkin.HeaderChunk090F4000>().Single());
            var mesh = Mesh(item);
            var checks = 0;
            var stats = new List<object>();
            var previewFrames = new List<object>();
            for (var lod = 0; lod < 5; lod++)
            {
                var v = (CPlugVisualIndexedTriangles)mesh.Visuals![lod];
                v.IndexBuffer!.Flags = 2;
                // The static four-corner far LOD cannot express a notch. Retain its
                // static material/stream layout, using the nine-point LOD3 frame.
                if (lod == 4 && shape == "Swallowtail")
                {
                    var low = (CPlugVisualIndexedTriangles)Mesh(Gbx.Parse<CGameItemModel>(Source).Node).Visuals![3];
                    var count = low.Vertices.Length / 86;
                    var stream = v.VertexStreams.Single(s => s.Positions is { Length: > 0 });
                    stream.Positions = low.Vertices.Take(count).Select(p => p.Position).ToArray();
                    stream.Normals = low.Vertices.Take(count).Select(p => p.Normal!.Value).ToArray();
                    stream.UVs[0] = low.TexCoords[0].TexCoords.Take(count).Select(p => p.UV).ToArray();
                    typeof(CPlugVertexStream).GetField("count", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(stream, count);
                    typeof(CPlugVisual).GetProperty("Count", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(v, count);
                    v.IndexBuffer.Indices = low.IndexBuffer!.Indices.ToArray();
                }
                var uvs = v.Vertices.Length > 0 ? v.TexCoords[0].TexCoords.Select(p => p.UV).ToArray()
                    : v.VertexStreams.Single(s => s.Positions is { Length: > 0 }).UVs[0];
                var minV = uvs.Min(uv => uv.Y);
                var maxV = uvs.Max(uv => uv.Y);
                float Scale(Vec2 uv) => shape switch
                {
                    "Long250" => 2.5f,
                    "Swallowtail" => 0.6f + 1.15f * MathF.Abs(2 * (uv.Y - minV) / (maxV - minV) - 1),
                    _ => 1f
                };
                Vec3 Deform(Vec3 p, Vec2 uv) => new(p.X * Scale(uv), p.Y, p.Z);
                // Inverse-transpose Jacobian for X'=X*scale(V), V inferred from Y.
                // At a stationary frame the original 2.5 m height spans the UV range.
                Vec3 Normal(Vec3 n, Vec3 p, Vec2 uv)
                {
                    var derivative = shape == "Swallowtail" ?
                        -2.3f / 2.5f * MathF.Sign(2 * (uv.Y - minV) / (maxV - minV) - 1) : 0;
                    var nx = n.X / Scale(uv);
                    var ny = n.Y - p.X * derivative * nx;
                    var length = MathF.Sqrt(nx * nx + ny * ny + n.Z * n.Z);
                    return new(nx / length, ny / length, n.Z / length);
                }
                var expectedInline = v.Vertices.Select((p, i) => Deform(p.Position, uvs[i])).ToArray();
                v.Vertices = v.Vertices.Select((p, i) => p with
                { Position = expectedInline[i], Normal = p.Normal is {} n ? Normal(n, p.Position, uvs[i]) : null }).ToArray();
                foreach (var stream in v.VertexStreams.Where(s => s.Positions is { Length: > 0 }))
                {
                    var original = stream.Positions!;
                    stream.Normals = stream.Normals?.Select((n, i) => Normal(n, original[i], stream.UVs[0][i])).ToArray();
                    stream.Positions = original.Select((p, i) => Deform(p, stream.UVs[0][i])).ToArray();
                }
                var points = v.Vertices.Select(p => p.Position).Concat(v.VertexStreams.SelectMany(s => s.Positions ?? [])).ToArray();
                var min = new Vec3(points.Min(p => p.X), points.Min(p => p.Y), points.Min(p => p.Z));
                var max = new Vec3(points.Max(p => p.X), points.Max(p => p.Y), points.Max(p => p.Z));
                v.BoundingBox = new BoxAligned((min.X + max.X) / 2, (min.Y + max.Y) / 2, (min.Z + max.Z) / 2,
                    (max.X - min.X) / 2, (max.Y - min.Y) / 2, (max.Z - min.Z) / 2);
                stats.Add(new { lod, vertices = points.Length, frames = v.SubVisuals!.Length,
                    indices = v.IndexBuffer.Indices.Length, min, max });
                var frameCount = lod < 4 ? v.Vertices.Length / 86 : points.Length;
                previewFrames.Add(new { lod, positions = points.Take(frameCount).ToArray(),
                    uv = uvs.Take(frameCount).ToArray(), indices = v.IndexBuffer.Indices });
            }
            using var memory = new MemoryStream();
            new Gbx<CGameItemModel>(item).Save(memory);
            var bytes = memory.ToArray();
            var read = Gbx.Parse<CGameItemModel>(new MemoryStream(bytes)).Node;
            void Check(bool ok) { if (!ok) throw new Exception("Shape fixture roundtrip failed"); checks++; }
            Check(read.Ident!.Id == "" && read.ArchetypeRef == "");
            Check(ItemFlagConversion.Inspect(read).IsFlag);
            Check(Prefab(read).Ents!.Length == 2);
            for (var i = 0; i < 5; i++)
            {
                var a = (CPlugVisualIndexedTriangles)mesh.Visuals![i];
                var b = (CPlugVisualIndexedTriangles)Mesh(read).Visuals![i];
                Check(b.SubVisuals!.Length == (i < 4 ? 86 : 0));
                Check(a.SubVisuals!.SequenceEqual(b.SubVisuals));
                Check(a.IndexBuffer!.Indices.SequenceEqual(b.IndexBuffer!.Indices));
                Check(a.BoundingBox == b.BoundingBox);
                Check(a.Vertices.Length == b.Vertices.Length);
                for (var j = 0; j < a.Vertices.Length; j++)
                    Check(a.Vertices[j].Position == b.Vertices[j].Position);
                Check(a.VertexStreams.Count == b.VertexStreams.Count);
                for (var j = 0; j < a.VertexStreams.Count; j++)
                    Check((a.VertexStreams[j].Positions ?? []).SequenceEqual(b.VertexStreams[j].Positions ?? []));
                Check(b.Vertices.All(p => float.IsFinite(p.Position.X) && float.IsFinite(p.Position.Y) && float.IsFinite(p.Position.Z)));
                var vertexCount = i < 4 ? b.Vertices.Length / 86 : b.VertexStreams[0].Positions!.Length;
                Check(b.IndexBuffer.Indices.All(n => n >= 0 && n < vertexCount));
            }
            var path = Path.Combine(folder, id + ".Item.Gbx");
            File.WriteAllBytes(path, bytes);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            manifest.Add(new { shape, id, path, bytes = bytes.Length, sha256 = hash, checks, lods = stats,
                previewFrames, status = "candidate; ingame rendering not yet verified" });
            Console.WriteLine($"{id}: {bytes.Length} bytes, {checks} checks, SHA256={hash}");
        }
        File.WriteAllText(Path.Combine(folder, "shapes-manifest.json"), JsonSerializer.Serialize(manifest,
            new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
        return 0;
    }
}
