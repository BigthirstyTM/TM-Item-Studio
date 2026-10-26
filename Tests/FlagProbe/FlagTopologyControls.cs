using System.Security.Cryptography;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;

// Native-primer follow-up: preserve every animated position and vertex index;
// change only the triangle subset. This is not independent-buffer evidence.
internal static class FlagTopologyControls
{
    public static int Create(string seed, string output, string? nativeMesh = null)
    {
        if (Directory.Exists(output)) throw new IOException("Preserve previous fixture");
        Directory.CreateDirectory(output);
        var rows = new List<object>();
        foreach (var mode in new[] { "Rectangle", "Half", "Swallowtail", "Round", "NarrowRectangle" })
        {
            var item = Gbx.Parse<CGameItemModel>(seed).Node;
            item.Name = "TMIS topology " + mode;
            var prefab = (CPlugPrefab)((NPlugItem_SVariantList)item.EntityModel!).Variants![0].EntityModel!;
            var mesh = ((CPlugDynaObjectModel)prefab.Ents![1].Model!).Mesh!;
            if (nativeMesh is not null)
            {
                // Keep the authorable material, but restore the exact packed
                // geometry/frame numbering instead of the native writer output.
                var original = Gbx.Parse<CPlugSolid2Model>(nativeMesh).Node;
                mesh.Visuals = original.Visuals;
                mesh.ShadedGeoms = original.ShadedGeoms;
                mesh.LodMaxDistAtFov90 = original.LodMaxDistAtFov90;
                var secondItem = Gbx.Parse<CGameItemModel>(seed).Node;
                var secondPrefab = (CPlugPrefab)((NPlugItem_SVariantList)secondItem.EntityModel!).Variants![0].EntityModel!;
                var farMaterial = ((CPlugDynaObjectModel)secondPrefab.Ents![1].Model!).Mesh!.CustomMaterials![0];
                farMaterial.MaterialName = ""; // Nonempty names omit the UserInst node from GBX.
                farMaterial.MaterialUserInst!.Link = "Stadium\\Media\\Material\\ItemFlagNoAnim";
                mesh.CustomMaterials = [mesh.CustomMaterials![0], farMaterial];
            }
            var lods = new List<object>();
            foreach (var visual in mesh.Visuals!.OfType<CPlugVisualIndexedTriangles>())
            {
                var uv = visual.Vertices.Length > 0
                    ? visual.TexCoords[0].TexCoords.Select(t => t.UV).ToArray()
                    : visual.VertexStreams.Single(s => s.Positions is {Length: > 0}).UVs[0];
                var minU = uv.Min(t => t.X); var maxU = uv.Max(t => t.X);
                var minV = uv.Min(t => t.Y); var maxV = uv.Max(t => t.Y);
                bool Inside(int[] triangle)
                {
                    // Centroids retain meaningful coarse LOD controls; all-three
                    // tests erase the two-triangle final LOD of a round outline.
                    var u = triangle.Average(i => (uv[i].X - minU) / (maxU - minU));
                    var v = triangle.Average(i => (uv[i].Y - minV) / (maxV - minV));
                    const float e = 1e-5f;
                    return mode switch {
                        "Half" => u <= .5f + e,
                        "Swallowtail" => u <= 1f - .5f * (1f - MathF.Abs(2f*v-1f)) + e,
                        "Round" => (2f*u-1f)*(2f*u-1f) + (2f*v-1f)*(2f*v-1f) <= 1f + e,
                        "NarrowRectangle" => v >= .25f-e && v <= .75f+e,
                        _ => true
                    };
                }
                var original = visual.IndexBuffer!.Indices;
                var indices = original.Chunk(3).Where(Inside).SelectMany(t => t).ToArray();
                if (indices.Length == 0 || indices.Length % 3 != 0) throw new Exception("Invalid triangle subset");
                visual.IndexBuffer.Indices = indices;
                var sourceFrames = visual.SubVisuals!;
                visual.SubVisuals = sourceFrames.Select(s => new Int3(s.X, s.Y, indices.Length)).ToArray();
                lods.Add(new { originalIndices=original.Length, indices=indices.Length,
                    frames=visual.SubVisuals.Length, vertexCount=visual.Vertices.Length,
                    sourceFrameTable=sourceFrames.Take(3).Select(s=>new[]{s.X,s.Y,s.Z}).ToArray() });
            }
            var path = Path.Combine(output,(nativeMesh is null ? "TMIS_Topology_" : "TMIS_NativeTopology_") + mode + ".Item.Gbx");
            new Gbx<CGameItemModel>(item).Save(path);
            var parsed = Gbx.Parse<CGameItemModel>(path).Node;
            var parsedPrefab = (CPlugPrefab)((NPlugItem_SVariantList)parsed.EntityModel!).Variants![0].EntityModel!;
            var parsedMesh = ((CPlugDynaObjectModel)parsedPrefab.Ents![1].Model!).Mesh!;
            if (nativeMesh is not null && parsedMesh.CustomMaterials![1].MaterialUserInst?.Link != "Stadium\\Media\\Material\\ItemFlagNoAnim")
                throw new Exception("Far material node was not serialized");
            foreach (var (a,b) in mesh.Visuals!.Zip(parsedMesh.Visuals!))
            {
                var x=(CPlugVisualIndexedTriangles)a; var y=(CPlugVisualIndexedTriangles)b;
                if (!x.IndexBuffer!.Indices.SequenceEqual(y.IndexBuffer!.Indices)
                    || !x.Vertices.SequenceEqual(y.Vertices) || !x.SubVisuals!.SequenceEqual(y.SubVisuals!))
                    throw new Exception("Topology roundtrip changed data");
            }
            rows.Add(new {mode,path,lods,sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
                scope="Triangle-subset test with native primer, not a standalone success"});
        }
        File.WriteAllText(Path.Combine(output,"manifest.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions {WriteIndented=true}));
        return 0;
    }
}
