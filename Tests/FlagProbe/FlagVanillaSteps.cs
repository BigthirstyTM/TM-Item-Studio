using System.Text.Json;
using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;

// Controlled native-writer baseline. Never converts the earlier XertroV fixture.
internal static class FlagVanillaSteps
{
    static CPlugPrefab Prefab(CGameItemModel item) =>
        (CPlugPrefab)((NPlugItem_SVariantList)item.EntityModel!).Variants![0].EntityModel!;
    static CPlugSolid2Model Mesh(CGameItemModel item) =>
        ((CPlugDynaObjectModel)Prefab(item).Ents![1].Model!).Mesh!;

    public static int Create(string source, string folder)
    {
        Directory.CreateDirectory(folder);
        var manifest = new List<object>();
        foreach (var mode in new[] { "02_Roundtrip", "03_Name", "04_Attachment025", "05_Width1025", "06_Long250", "07_Swallowtail", "08_Round" })
        {
            var item = Gbx.Parse<CGameItemModel>(source).Node;
            if (mode == "03_Name") item.Name = "TMIS native vanilla name only";
            if (mode == "04_Attachment025")
            {
                var p = Prefab(item).Ents![1].Position;
                Prefab(item).Ents![1].Position = new(p.X, p.Y + .25f, p.Z);
            }
            var deform = mode.StartsWith("05") || mode.StartsWith("06") || mode.StartsWith("07") || mode.StartsWith("08");
            var stats = new List<object>();
            foreach (var v in Mesh(item).Visuals!.OfType<CPlugVisualIndexedTriangles>())
            {
                var uvs = v.TexCoords[0].TexCoords.Select(x => x.UV).ToArray();
                var minV = uvs.Min(x => x.Y); var maxV = uvs.Max(x => x.Y);
                var minU = uvs.Min(x => x.X); var maxU = uvs.Max(x => x.X);
                float Scale(Vec2 uv) => mode switch {
                    "05_Width1025" => 1.025f,
                    "06_Long250" => 2.5f,
                    "07_Swallowtail" => .6f + 1.15f * MathF.Abs(2 * (uv.Y - minV) / (maxV - minV) - 1),
                    _ => 1f
                };
                Vec3 Deform(Vec3 p, Vec2 uv)
                {
                    if (mode != "08_Round") return new(p.X * Scale(uv), p.Y, p.Z);
                    // Square-to-disc mapping, with the original baked displacement
                    // retained. The circle touches the pole at its leftmost point.
                    float u = (uv.X - minU) / (maxU - minU), t = (uv.Y - minV) / (maxV - minV);
                    float a = 2 * u - 1, b = 2 * t - 1;
                    float dx = a * MathF.Sqrt(1 - b * b / 2), dy = b * MathF.Sqrt(1 - a * a / 2);
                    return new(p.X + 2.5f * (1 + dx) - 4f * u,
                        p.Y + 1.25f + 2.5f * dy - 2.5f * t, p.Z);
                }
                if (deform)
                {
                    v.Vertices = v.Vertices.Select((p, i) => p with { Position = Deform(p.Position, uvs[i]) }).ToArray();
                    foreach (var s in v.VertexStreams.Where(s => s.Positions is { Length: > 0 }))
                        s.Positions = s.Positions!.Select((p, i) => Deform(p, s.UVs[0][i])).ToArray();
                    if (mode == "08_Round")
                    {
                        var normals = RecalculateNormals(v.Vertices.Select(p => p.Position).ToArray(), v.IndexBuffer!.Indices, v.SubVisuals!.Length);
                        v.Vertices = v.Vertices.Select((p,i) => p with { Normal = normals[i] }).ToArray();
                        foreach (var s in v.VertexStreams.Where(s => s.Positions is { Length: > 0 }))
                            s.Normals = RecalculateNormals(s.Positions!, v.IndexBuffer.Indices, v.SubVisuals.Length);
                    }
                    var points = v.Vertices.Select(p => p.Position).Concat(v.VertexStreams.SelectMany(s => s.Positions ?? [])).ToArray();
                    var min = new Vec3(points.Min(p => p.X), points.Min(p => p.Y), points.Min(p => p.Z));
                    var max = new Vec3(points.Max(p => p.X), points.Max(p => p.Y), points.Max(p => p.Z));
                    v.BoundingBox = new((min.X + max.X) / 2, (min.Y + max.Y) / 2, (min.Z + max.Z) / 2,
                        (max.X - min.X) / 2, (max.Y - min.Y) / 2, (max.Z - min.Z) / 2);
                }
                stats.Add(new { vertices = v.Vertices.Length, streams = v.VertexStreams.Count,
                    frames = v.SubVisuals?.Length, indices = v.IndexBuffer!.Indices.Length,
                    minX = v.Vertices.Min(p => p.Position.X), maxX = v.Vertices.Max(p => p.Position.X) });
            }
            var path = Path.Combine(folder, "TMIS_Step" + mode + ".Item.Gbx");
            new Gbx<CGameItemModel>(item).Save(path);
            var read = Gbx.Parse<CGameItemModel>(path).Node;
            if (read.ArchetypeRef != "" || read.Ident!.Id != "") throw new Exception("Unexpected shared identity");
            var originalVisuals = Mesh(item).Visuals!;
            for (var i = 0; i < originalVisuals.Length; i++)
            {
                var a = (CPlugVisualIndexedTriangles)originalVisuals[i];
                var b = (CPlugVisualIndexedTriangles)Mesh(read).Visuals![i];
                if (!a.Vertices.Select(x => x.Position).SequenceEqual(b.Vertices.Select(x => x.Position))
                    || !a.Vertices.Select(x => x.Normal).SequenceEqual(b.Vertices.Select(x => x.Normal))
                    || !a.IndexBuffer!.Indices.SequenceEqual(b.IndexBuffer!.Indices)
                    || !a.VertexStreams.SelectMany(s => s.Positions ?? []).SequenceEqual(b.VertexStreams.SelectMany(s => s.Positions ?? []))
                    || !a.SubVisuals!.SequenceEqual(b.SubVisuals!) || a.BoundingBox != b.BoundingBox)
                    throw new Exception("Geometry did not survive roundtrip");
            }
            manifest.Add(new { mode, path, bytes = new FileInfo(path).Length, stats });
            Console.WriteLine($"PARSED {path}");
        }
        File.WriteAllText(Path.Combine(folder, "steps-manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    static Vec3[] RecalculateNormals(Vec3[] positions, int[] indices, int frames)
    {
        if (frames != 86 || positions.Length % frames != 0) throw new Exception("Unexpected frame layout");
        var count = positions.Length / frames;
        if (indices.Any(i => i < 0 || i >= count)) throw new Exception("Index outside a frame");
        var normals = new Vec3[positions.Length];
        for (var frame = 0; frame < frames; frame++)
            for (var i = 0; i < indices.Length; i += 3)
            {
                var a = frame * count + indices[i]; var b = frame * count + indices[i + 1]; var c = frame * count + indices[i + 2];
                var p = positions[a]; var q = positions[b]; var r = positions[c];
                float ux = q.X-p.X, uy = q.Y-p.Y, uz = q.Z-p.Z, vx = r.X-p.X, vy = r.Y-p.Y, vz = r.Z-p.Z;
                var n = new Vec3(uy*vz-uz*vy, uz*vx-ux*vz, ux*vy-uy*vx);
                foreach (var j in new[] { a,b,c }) normals[j] = new(normals[j].X+n.X, normals[j].Y+n.Y, normals[j].Z+n.Z);
            }
        return normals.Select(n => {
            var length = MathF.Sqrt(n.X*n.X+n.Y*n.Y+n.Z*n.Z);
            return length > 1e-9f ? new Vec3(n.X/length,n.Y/length,n.Z/length) : new Vec3(0,0,1);
        }).ToArray();
    }
}
