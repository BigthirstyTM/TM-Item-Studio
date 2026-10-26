using System.Security.Cryptography;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;

// Existing archives only; no GBX output or native operations.
internal static class FrozenControlsAudit
{
    public static void Run(string output)
    {
        if(Directory.Exists(output)||File.Exists(output))throw new IOException("Preserve earlier evidence");
        var files=new[]{
            "artifacts/flag-motion-embedded-20261022/kinematic/files-slow/TMIS_Kinematic_Stationary.Item.Gbx",
            "artifacts/flag-motion-embedded-20261022/phase-range/files/TMIS_KinematicRange_Stationary.Item.Gbx",
            "artifacts/flag-motion-embedded-20261022/nonkinematic-constraints/files/TMIS_ConstraintDyn_Stationary.Item.Gbx"};
        var rows=new List<object>();var meshHashes=new List<string[]>();
        foreach(var path in files)
        {
            var bytes=File.ReadAllBytes(path);var item=Gbx.Parse<CGameItemModel>(new MemoryStream(bytes)).Node;
            var prefab=(CPlugPrefab)((NPlugItem_SVariantList)item.EntityModel!).Variants![0].EntityModel!;
            var hashes=new List<string>();
            var bodies=prefab.Ents!.Select((e,i)=>(e,i)).Where(p=>p.e.Model is CPlugDynaObjectModel).Select(p=>{
                var body=(CPlugDynaObjectModel)p.e.Model!;var mesh=body.Mesh!;
                var timing=(NPlugDynaObjectModel_SInstanceParams)p.e.Params!;
                var hash=Hash(Save(mesh));hashes.Add(hash);
                return new{slot=p.i,body.IsStatic,body.DynamizeOnSpawn,bodyLocAnim=body.LocAnim?.GetType().Name,
                    timing.Version,timing.IsKinematic,timing.PeriodSc,timing.PeriodScMax,timing.Phase01,timing.Phase01Max,timing.TextureId,
                    completeMeshSha256=hash,visuals=mesh.Visuals!.Select(v=>new{type=v.GetType().Name,vertexCount=(v as CPlugVisual3D)?.Vertices.Length,frames=v.SubVisuals?.Length,uvChannels=v.TexCoords.Length}),
                    earlyMaterialCount=mesh.Materials?.Length,customMaterialLinks=mesh.CustomMaterials?.Select(m=>m.MaterialUserInst?.Link),legacyMaterialLinks=mesh.MaterialInsts?.Select(m=>m.Link)};
            }).ToArray();
            meshHashes.Add(hashes.ToArray());
            rows.Add(new{path,itemSha256=Hash(bytes),item.Name,constraintCount=prefab.Ents.Count(e=>e.Model is NPlugDyna_SKinematicConstraint),bodies});
        }
        var result=new{scope="File-only re-audit of prior native-tested frozen/nonkinematic controls. No active runtime binding or new native verdict.",rows,
            phaseRangeAndNonkinematicCompleteMeshHashesEqual=meshHashes[1].SequenceEqual(meshHashes[2]),
            standardAndPhaseRangeCompleteMeshHashesEqual=meshHashes[0].SequenceEqual(meshHashes[1])};
        Directory.CreateDirectory(output);var json=JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true});
        File.WriteAllText(Path.Combine(output,"prior-controls.json"),json+"\n");Console.WriteLine(json);
    }
    static byte[] Save<T>(T node)where T:GBX.NET.Engines.MwFoundations.CMwNod{using var m=new MemoryStream();new Gbx<T>(node).Save(m);return m.ToArray();}
    static string Hash(byte[] data)=>Convert.ToHexString(SHA256.HashData(data));
}
