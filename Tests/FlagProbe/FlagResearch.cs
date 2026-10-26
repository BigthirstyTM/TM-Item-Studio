using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Game;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;
using TM_Item_Studio.Models;

internal static class FlagResearch
{
    const string Data = @"C:\Users\PC\OpenplanetNext\Extract\GameData\Stadium";
    const string Source = @"C:\Users\PC\Documents\Trackmania\Items\XertrovItems\ztest-flag3.Item.Gbx";
    static readonly JsonSerializerOptions Json = new() { IncludeFields = true };

    public static int Run(string[] args)
    {
        if (args.Length == 0) throw new ArgumentException("compare | final <folder> | verify <folder> | create <folder> | refs <files>");
        if (args[0] == "verify") return VerifyFinal(args[1]);
        if (args[0] == "resource-audit") return FlagResourceAudit.Run(args[1], args[2]);
        if (args[0] == "material-remap") return FlagMaterialRemap.Create(args[1], args[2]);
        if (args[0] == "material-remap-user") return FlagMaterialRemap.Create(args[1], args[2], true);
        if (args[0] == "material-placeholder") return FlagMaterialPlaceholder.Create(args[1], args[2]);
        if (args[0] == "material-placeholder-local") return FlagMaterialPlaceholder.Create(args[1], args[2], true);
        if (args[0] == "material-placeholder-no-passes") return FlagMaterialPlaceholder.Create(args[1], args[2], true, true);
        if (args[0] == "material-remap-map") return FlagMaterialRemapMap.Create(args[1], args[2], args[3]);
        if (args[0] == "material-remap-native-primer-map") return FlagMaterialRemapMap.Create(args[1], args[2], args[3], nativePrimer: true);
        if (args[0] == "material-remap-empty-map") return FlagMaterialRemapMap.Create(args[1], args[2], args[3], true);
        if (args[0] == "material-remap-shapes-map") return FlagMaterialRemapShapes.Create(args[1], args[2], args[3], args[4]);
        if (args[0] == "material-remap-camera-near") return FlagMaterialRemapShapes.CameraFriendlyClone(args[1], args[2]);
        if (args[0] == "material-remap-shapes-native-primer") return FlagMaterialRemapShapes.NativePrimerClone(args[1], args[2]);
        if (args[0] == "material-remap-shapes-closeup-pair") return FlagMaterialRemapShapes.CloseupFramingClones(args[1], args[2]);
        if (args[0] == "material-remap-shapes-native-last") return FlagMaterialRemapShapes.NativeLastClone(args[1], args[2]);
        if (args[0] == "material-remap-external-mesh-only") return FlagMaterialRemapExternalMesh.Create(args[1], args[2], args[3]);
        if (args[0] == "vanilla-steps") return FlagVanillaSteps.Create(args[1], args[2]);
        if (args[0] == "gpu-materials") return FlagGpuMaterials.Create(args[1], args[2]);
        if (args[0] == "gpu-warmup") return FlagGpuWarmup.Create(args[1], args[2]);
        if (args[0] == "material-isolation") return FlagMaterialIsolation.Create(args[1], args[2]);
        if (args[0] == "reference-resolution") return FlagReferenceResolution.Create(args[1], args[2]);
        if (args[0] == "shader-paths") return FlagShaderPaths.Create(args[1], args[2]);
        if (args[0] == "standalone-routes") return FlagStandaloneRoutes.Create(args[1], args[2], args[3]);
        if (args[0] == "material-names") return FlagMaterialNames.Create(args[1], args[2]);
        if (args[0] == "material-bootstrap") return FlagMaterialBootstrap.Create(args[1], args[2]);
        if (args[0] == "installed-files") return FlagInstalledFiles.Create(args[1], args[2]);
        if (args[0] == "adjacent-files") return FlagInstalledFiles.Create(args[1], args[2], true);
        if (args[0] == "native-ladder") return FlagNativeLadder.Create(args[1]);
        if (args[0] == "native-chain") return FlagNativeLadder.Create(args[1], true);
        if (args[0] == "native-chain-fixed") return FlagNativeLadder.Create(args[1], true, true);
        if (args[0] == "native-package") return FlagNativePackage.Create(args[1]);
        if (args[0] == "embedded-context") return FlagEmbeddedContext.Create(args[1],args[2],args[3]);
        if (args[0] == "plugin-free-map") return FlagPluginFreeMap.Create(args[1], args[2]);
        if (args[0] == "kinematic-flags") return FlagMotionResearch.Kinematic(args[1], args[2], args[3], args[4]);
        if (args[0] == "kinematic-phase-range") return FlagMotionResearch.Kinematic(args[1], args[2], args[3], args[4], true);
        if (args[0] == "nonkinematic-constraints") return FlagMotionResearch.Kinematic(args[1], args[2], args[3], args[4], phaseRange: true, constraintBodyKinematic: false);
        if (args[0] == "local-motion-flags") return FlagMotionResearch.Kinematic(args[1], args[2], args[3], args[4], true, true);
        if (args[0] == "local-motion-v3") return FlagMotionResearch.Kinematic(args[1], args[2], args[3], args[4], true, true, 3);
        if (args[0] == "local-motion-v3-func1") return FlagMotionResearch.Kinematic(args[1], args[2], args[3], args[4], true, true, 3, 1);
        if (args[0] == "external-graph") return FlagExternalGraph.Create(args[1], args[2], args[3]);
        // The legacy Inst8 reader crashed on native preload. Retain its source
        // for diagnosis, but do not expose rejected fixtures as an active route.
        if (args[0] == "topology-controls") return FlagTopologyControls.Create(args[1], args[2]);
        if (args[0] == "native-topology-controls") return FlagTopologyControls.Create(args[1], args[2], args[3]);
        if (args[0] == "flag-file-stats") return FlagStandaloneRoutes.Stats(args[1..]);
        if (args[0] == "inspect-embedded")
        {
            Directory.CreateDirectory(args[2]);
            var map = Gbx.Parse<CGameCtnChallenge>(args[1], new GbxReadSettings { SkipChunkIds = [0x0304305B] }).Node;
            using var zip = map.OpenReadEmbeddedZipData();
            var entries = new List<object>();
            foreach(var entry in zip.Entries)
            {
                using var input = entry.Open();
                using var memory = new MemoryStream(); input.CopyTo(memory);
                var bytes = memory.ToArray();
                // Flatten names only for this isolated evidence extraction.
                var target = Path.Combine(args[2], entry.Name);
                if(File.Exists(target)) throw new IOException("Refuse evidence overwrite: " + target);
                File.WriteAllBytes(target, bytes);
                entries.Add(new { entry.FullName, file=target, bytes=bytes.Length, sha256=Convert.ToHexString(SHA256.HashData(bytes)) });
            }
            Console.WriteLine(JsonSerializer.Serialize(new {items=map.AnchoredObjects?.Count,entries}, new JsonSerializerOptions {WriteIndented=true}));
            return 0;
        }
        if (args[0] == "map-file-state")
        {
            var map=Gbx.Parse<CGameCtnChallenge>(args[1],new GbxReadSettings {SkipChunkIds=[0x0304305B]}).Node;
            Console.WriteLine(JsonSerializer.Serialize(new {map.MapName,items=map.AnchoredObjects?.Select(o=>new {o.ItemModel,o.AbsolutePositionInMap}),embeddedZipBytes=map.EmbeddedZipData?.Length??0},new JsonSerializerOptions {WriteIndented=true,IncludeFields=true}));
            return 0;
        }
        if (args[0] == "decompress") { Gbx.Decompress(args[1], args[2]); return 0; }
        if (args[0] == "compress") { Gbx.Compress(args[1], args[2]); return 0; }
        if (args[0] == "shader-decompress")
        {
            foreach (var file in Directory.GetFiles(args[1], "*.GpuCache.Gbx"))
                Gbx.Decompress(file, file + ".raw");
            return 0;
        }
        if (args[0] == "shapes") return FlagShapes.Create(args[1]);
        if (args[0] == "shapes-map") return FlagShapes.VerifyMap(args[1], args[2]);
        if (args[0] == "vanilla-map") return FlagShapes.VerifyMap(args[1], args[2], 8, 7);
        if (args[0] == "probes6") return CreateProbes6(args[1]);
        if (args[0] == "probes7") return CreateProbes7(args[1]);
        if (args[0] == "probes8") return CreateProbes8(args[1]);
        if (args[0] == "probes9") return CreateProbes9(args[1]);
        if (args[0] == "probes10") return CreateProbes10(args[1]);
        if (args[0] == "diagnostics") return CreateDiagnostics(args[1]);
        if (args[0] == "probes2")
        {
            Directory.CreateDirectory(args[1]);
            foreach (var mode in new[] { "TallNoAnim", "OffsetCloth", "TallFreshVisualIds" })
            {
                var item = Gbx.Parse<CGameItemModel>(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "Trackmania/Items/TMIS_FlagResearch/TMIS_Diagnostic_Tall.Item.Gbx")).Node;
                item.Name = "TMIS probe " + mode;
                var prefab = Prefab(item);
                var cloth = ((CPlugDynaObjectModel)prefab.Ents![1].Model!).Mesh!;
                if (mode == "TallNoAnim") cloth.CustomMaterials![0].MaterialUserInst!.Link = "ItemFlagNoAnim";
                if (mode == "OffsetCloth") prefab.Ents[1].Position = new Vec3(0, 16, 0.13515112f);
                if (mode == "TallFreshVisualIds")
                    for (var i=0; i<cloth.Visuals!.Length; i++)
                        cloth.Visuals[i].Chunks.OfType<CPlugVisual.Chunk09006001>().Single().U01 = "TMIS_Tall_20261021_" + i;
                using var memory = new MemoryStream(); new Gbx<CGameItemModel>(item).Save(memory);
                Gbx.Parse<CGameItemModel>(new MemoryStream(memory.ToArray()));
                File.WriteAllBytes(Path.Combine(args[1], "TMIS_Probe_" + mode + ".Item.Gbx"), memory.ToArray());
            }
            return 0;
        }
        if (args[0] == "probes3")
        {
            Directory.CreateDirectory(args[1]);
            foreach (var mode in new[] { "TallFar", "TallBounds", "FreshMesh", "Shift20" })
            {
                var item = Gbx.Parse<CGameItemModel>(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "Trackmania/Items/TMIS_FlagResearch/TMIS_Diagnostic_Tall.Item.Gbx")).Node;
                item.Name = "TMIS probe3 " + mode;
                var cloth = ((CPlugDynaObjectModel)Prefab(item).Ents![1].Model!).Mesh!;
                if (mode == "TallFar") cloth.LodMaxDistAtFov90 = [0, 0, 0, 0];
                if (mode == "FreshMesh")
                {
                    cloth.Chunks.OfType<CPlugSolid2Model.Chunk090BB000>().Single().U01 = "TMIS_FreshMesh20261021";
                    cloth.FileWriteTime = new DateTime(2026,10,21,12,0,0,DateTimeKind.Utc);
                }
                foreach (var v in cloth.Visuals!.OfType<CPlugVisualIndexedTriangles>())
                {
                    if (mode == "Shift20")
                    {
                        v.Vertices = v.Vertices.Select(p => p with { Position = new Vec3(p.Position.X + 20, p.Position.Y, p.Position.Z) }).ToArray();
                        foreach (var stream in v.VertexStreams)
                            stream.Positions = stream.Positions?.Select(p => new Vec3(p.X + 20,p.Y,p.Z)).ToArray();
                    }
                    var points = v.Vertices.Select(p => p.Position).Concat(v.VertexStreams.SelectMany(s => s.Positions ?? [])).ToArray();
                    var min = new Vec3(points.Min(p=>p.X),points.Min(p=>p.Y),points.Min(p=>p.Z));
                    var max = new Vec3(points.Max(p=>p.X),points.Max(p=>p.Y),points.Max(p=>p.Z));
                    // BoxAligned is center + half extent, not min + max.
                    v.BoundingBox = new BoxAligned((min.X+max.X)/2,(min.Y+max.Y)/2,(min.Z+max.Z)/2,
                        (max.X-min.X)/2,(max.Y-min.Y)/2,(max.Z-min.Z)/2);
                }
                using var memory = new MemoryStream(); new Gbx<CGameItemModel>(item).Save(memory);
                Gbx.Parse<CGameItemModel>(new MemoryStream(memory.ToArray()));
                File.WriteAllBytes(Path.Combine(args[1], "TMIS_Probe3_" + mode + ".Item.Gbx"), memory.ToArray());
            }
            return 0;
        }
        if (args[0] == "probes4")
        {
            Directory.CreateDirectory(args[1]);
            foreach (var mode in new[] { "WideUniqueMat", "WideGameMat", "WideDynamic", "WideFrameStride" })
            {
                var item = Gbx.Parse<CGameItemModel>(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "Trackmania/Items/TMIS_CustomFlags/TMIS_CustomFlag_WideStandalone.Item.Gbx")).Node;
                item.Name = "TMIS probe4 " + mode;
                var cloth = ((CPlugDynaObjectModel)Prefab(item).Ents![1].Model!).Mesh!;
                foreach (var mat in cloth.CustomMaterials!)
                {
                    if (mode == "WideUniqueMat") mat.MaterialUserInst!.MaterialName = "TMIS_Unique_" + mat.MaterialUserInst.Link;
                    if (mode == "WideGameMat")
                    {
                        var inst=mat.MaterialUserInst!;
                        inst.Chunks.OfType<CPlugMaterialUserInst.Chunk090FD000>().Single().Version=11;
                        inst.IsUsingGameMaterial=true; inst.Link=@"Stadium\Media\Material\" + inst.Link;
                    }
                }
                foreach (var v in cloth.Visuals!.OfType<CPlugVisualIndexedTriangles>())
                {
                    if (mode == "WideDynamic") v.IsGeometryStatic=false;
                    if (mode == "WideFrameStride" && v.SubVisuals!.Length > 0)
                    {
                        var stride=v.Vertices.Length/v.SubVisuals.Length;
                        v.Vertices=Enumerable.Range(0,v.SubVisuals.Length).SelectMany(i=>v.Vertices.Skip(i*stride).Take(stride).Append(v.Vertices[(i+1)*stride-1])).ToArray();
                        foreach(var uv in v.TexCoords)
                            uv.TexCoords=Enumerable.Range(0,v.SubVisuals.Length).SelectMany(i=>uv.TexCoords.Skip(i*stride).Take(stride).Append(uv.TexCoords[(i+1)*stride-1])).ToArray();
                        v.SubVisuals=v.SubVisuals.Select((s,i)=>new Int3(i*(stride+1),s.Y,s.Z)).ToArray();
                        typeof(CPlugVisual).GetProperty("Count",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(v,v.Vertices.Length);
                    }
                }
                using var memory = new MemoryStream(); new Gbx<CGameItemModel>(item).Save(memory);
                Gbx.Parse<CGameItemModel>(new MemoryStream(memory.ToArray()));
                File.WriteAllBytes(Path.Combine(args[1], "TMIS_Probe4_" + mode + ".Item.Gbx"), memory.ToArray());
            }
            return 0;
        }
        if (args[0] == "probes5")
        {
            Directory.CreateDirectory(args[1]);
            foreach (var mode in new[] { "WideStream", "WideClearU13" })
            {
                var item = Gbx.Parse<CGameItemModel>(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "Trackmania/Items/TMIS_CustomFlags/TMIS_CustomFlag_WideStandalone.Item.Gbx")).Node;
                item.Name = "TMIS probe5 " + mode;
                var cloth = ((CPlugDynaObjectModel)Prefab(item).Ents![1].Model!).Mesh!;
                if (mode == "WideClearU13") cloth.Chunks.OfType<CPlugSolid2Model.Chunk090BB000>().Single().U13=0;
                if (mode == "WideStream")
                    foreach (var v in cloth.Visuals!.OfType<CPlugVisualIndexedTriangles>().Where(v=>v.Vertices.Length>0))
                    {
                        // Reuse the vanilla far stream's known declarations, but give
                        // each animated LOD its own stream with all of its frames.
                        var seed=Gbx.Parse<CGameItemModel>(Source).Node;
                        var stream=((CPlugDynaObjectModel)Prefab(seed).Ents![1].Model!).Mesh!.Visuals![4].VertexStreams[0];
                        typeof(CPlugVertexStream).GetField("count",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(stream,v.Vertices.Length);
                        stream.Positions=v.Vertices.Select(p=>p.Position).ToArray();
                        stream.Normals=v.Vertices.Select(p=>p.Normal!.Value).ToArray();
                        stream.UVs[0]=v.TexCoords[0].TexCoords.Select(p=>p.UV).ToArray();
                        v.VertexStreams=[stream]; v.TexCoords=[]; v.Vertices=[];
                    }
                using var memory = new MemoryStream(); new Gbx<CGameItemModel>(item).Save(memory);
                Gbx.Parse<CGameItemModel>(new MemoryStream(memory.ToArray()));
                File.WriteAllBytes(Path.Combine(args[1], "TMIS_Probe5_" + mode + ".Item.Gbx"), memory.ToArray());
            }
            return 0;
        }
        if (args[0] == "deterministic")
        {
            Directory.CreateDirectory(args[1]);
            foreach (var mode in new[] { "Control", "Wide", "Tall", "Shift20", "Grid", "OneLod", "Stream" })
            {
                var folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Trackmania/Items");
                var seed=mode=="Tall"?"TMIS_FlagResearch/TMIS_Diagnostic_Tall.Item.Gbx":
                    mode=="Stream"?"TMIS_FlagResearch/TMIS_Probe5_WideStream.Item.Gbx":
                    mode is "Wide" or "OneLod"?"TMIS_CustomFlags/TMIS_CustomFlag_WideStandalone.Item.Gbx":
                    "TMIS_CustomFlags/TMIS_CustomFlag_Standalone.Item.Gbx";
                var item=Gbx.Parse<CGameItemModel>(Path.Combine(folder,seed)).Node;
                item.Name="TMIS deterministic "+mode;
                var prefab=Prefab(item);var entry=prefab.Ents![1];
                var param=(NPlugDynaObjectModel_SInstanceParams)entry.Params!;
                param.PeriodSc=8;param.PeriodScMax=8;param.Phase01=0;param.Phase01Max=0;
                var cloth=((CPlugDynaObjectModel)entry.Model!).Mesh!;
                if(mode=="OneLod") {cloth.Visuals=[cloth.Visuals![0]];cloth.ShadedGeoms=[cloth.ShadedGeoms![0]];cloth.LodMaxDistAtFov90=[];}
                foreach(var v in cloth.Visuals!.OfType<CPlugVisualIndexedTriangles>())
                {
                    if(mode=="Shift20")
                    {
                        v.Vertices=v.Vertices.Select(p=>p with {Position=new Vec3(p.Position.X+20,p.Position.Y,p.Position.Z)}).ToArray();
                        foreach(var stream in v.VertexStreams)stream.Positions=stream.Positions?.Select(p=>new Vec3(p.X+20,p.Y,p.Z)).ToArray();
                    }
                    if(mode=="Grid" && v.Vertices.Length>0)
                    {
                        var stride=v.Vertices.Length/v.SubVisuals!.Length;
                        v.Vertices=v.Vertices.Select((p,i)=>{
                            var uv=v.TexCoords[0].TexCoords[i].UV;var phase=(i/stride)*MathF.Tau/86;
                            return p with{Position=new Vec3(uv.X*6,1.136364f+(uv.Y-1.0101f)*4,
                                uv.X*MathF.Sin(uv.X*5-phase)*0.6f),Normal=new Vec3(0,0,1)};
                        }).ToArray();
                    }
                }
                using var memory=new MemoryStream();new Gbx<CGameItemModel>(item).Save(memory);
                Gbx.Parse<CGameItemModel>(new MemoryStream(memory.ToArray()));
                File.WriteAllBytes(Path.Combine(args[1],"TMIS_Deterministic_"+mode+".Item.Gbx"),memory.ToArray());
            }
            return 0;
        }
        if (args[0] == "graph")
        {
            var item = Gbx.Parse<CGameItemModel>(args[1]).Node;
            Props(item);
            var prefab = Prefab(item);
            foreach (var entry in prefab.Ents!)
            {
                Console.WriteLine($"entry {entry.Position} {entry.Rotation} {entry.Model?.GetType().Name}");
                Props(entry.Model!);
                if (entry.Model is CPlugDynaObjectModel dyna)
                {
                    foreach (var chunk in dyna.Chunks) { Console.WriteLine($"chunk {chunk.Id:X8}"); Props(chunk); }
                    foreach (var chunk in dyna.Mesh!.Chunks) { Console.WriteLine($"mesh chunk {chunk.Id:X8}"); Props(chunk); }
                    foreach (var visual in dyna.Mesh!.Visuals!)
                    {
                        Console.WriteLine($"visual {visual.GetType().Name}");
                        foreach (var chunk in visual.Chunks) { Console.WriteLine($"chunk {chunk.Id:X8}"); Props(chunk); }
                    }
                }
            }
            return 0;
        }
        if (args[0] == "material")
        {
            foreach (var path in args.Skip(1))
            {
                var gbx = Gbx.Parse<CPlugMaterial>(path);
                Console.WriteLine(path);
                Props(gbx.Node);
                if (gbx.Node.CustomMaterial is {} material) Props(material);
                foreach (var file in gbx.RefTable?.Files ?? []) Console.WriteLine(file);
            }
            return 0;
        }
        if (args[0] == "map")
        {
            var map = Gbx.Parse<CGameCtnChallenge>(args[1], new GbxReadSettings { SkipChunkIds = [0x0304305B] }).Node;
            Console.WriteLine($"Anchored={map.AnchoredObjects?.Count}, zip bytes={map.EmbeddedZipData?.Length}");
            foreach (var item in map.AnchoredObjects ?? [])
                Console.WriteLine($"{item.ItemModel} {item.AbsolutePositionInMap} bg={JsonSerializer.Serialize(item.PackDesc, Json)} fg={JsonSerializer.Serialize(item.ForegroundPackDesc, Json)}");
            if (map.EmbeddedZipData is { Length: >0 })
            {
                using var zip = map.OpenReadEmbeddedZipData();
                foreach (var e in zip.Entries)
                {
                    using var stream = e.Open(); using var memory = new MemoryStream(); stream.CopyTo(memory);
                    var asset = Gbx.Parse<CGameItemModel>(new MemoryStream(memory.ToArray())).Node;
                    Console.WriteLine($"embedded {e.FullName} {e.Length} archetype={asset.ArchetypeRef} entity={asset.EntityModel?.GetType().Name}");
                    File.WriteAllBytes(Path.Combine("artifacts/flag-research-20261021", "map-embedded-" + e.Name), memory.ToArray());
                    if (args.Contains("--require-standalone"))
                    {
                        if (!string.IsNullOrEmpty(asset.ArchetypeRef)) throw new Exception("Embedded archetype must be empty");
                        var expected = Gbx.Parse<CGameItemModel>(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                            "Trackmania/Items/TMIS_CustomFlags", e.Name)).Node;
                        var embeddedCloth = ((CPlugDynaObjectModel)Prefab(asset).Ents![1].Model!).Mesh!;
                        var expectedCloth = ((CPlugDynaObjectModel)Prefab(expected).Ents![1].Model!).Mesh!;
                        for (var i = 0; i < 5; i++)
                        {
                            var a = (CPlugVisualIndexedTriangles)embeddedCloth.Visuals![i];
                            var b = (CPlugVisualIndexedTriangles)expectedCloth.Visuals![i];
                            if (!a.Vertices.Select(v => v.Position).SequenceEqual(b.Vertices.Select(v => v.Position)))
                                throw new Exception($"Embedded LOD{i} positions differ from the tested asset");
                            if (!a.SubVisuals!.SequenceEqual(b.SubVisuals!)) throw new Exception("Embedded frames differ");
                            if (i == 4 && !a.VertexStreams[0].Positions!.SequenceEqual(b.VertexStreams[0].Positions!))
                                throw new Exception("Embedded far stream differs");
                            Console.WriteLine($"  PASS LOD{i}: {a.Vertices.Length} inline vertices, {a.SubVisuals!.Length} frames, disk geometry preserved");
                        }
                    }
                }
            }
            return 0;
        }
        if (args[0] == "skin")
        {
            foreach (var path in args.Skip(1))
            {
                var item = Gbx.Parse<CGameItemModel>(path).Node;
                Console.WriteLine(path);
                Console.WriteLine($"Ident={item.Ident} ArchetypeRef={item.ArchetypeRef} SkinDirectory={item.SkinDirectory} Edition={item.EntityModelEdition?.GetType().Name}");
                if (item.GameSkin is {} skin) Props(skin); else Console.WriteLine("GameSkin=null");
                foreach (var c in item.Chunks) { if (c.Id == 0x090F4000) Props(c); }
            }
            return 0;
        }
        if (args[0] == "refs")
        {
            foreach (var path in args.Skip(1))
            {
                var file = Gbx.Parse(path);
                Console.WriteLine($"{path}: ancestor={file.RefTable?.AncestorLevel}");
                foreach (var reference in file.RefTable?.Files ?? []) Console.WriteLine($"{reference} flags={reference.Flags} useFile={reference.UseFile}");
            }
            return 0;
        }
        if(args[0]=="file-identity")
        {
            Console.WriteLine(JsonSerializer.Serialize(args.Skip(1).Select(path=>new {path,ident=Gbx.Parse<CGameItemModel>(path).Node.Ident}),new JsonSerializerOptions{WriteIndented=true}));
            return 0;
        }
        if (args[0] == "final") return CreateFinal(args[1]);
        if (args[0] == "compare")
        {
            var item = Gbx.Parse<CGameItemModel>(Source).Node;
            var prefab = (CPlugPrefab)((NPlugItem_SVariantList)item.EntityModel!).Variants![0].EntityModel!;
            var custom = ((CPlugDynaObjectModel)prefab.Ents![1].Model!).Mesh!;
            var vanilla = Gbx.Parse<CPlugSolid2Model>(Path.Combine(Data, @"Media\Dyna\Flag\Flag.Mesh.Gbx")).Node;
            Describe("vanilla", vanilla);
            Describe("xertrov", custom);
            return 0;
        }
        if (args[0] != "create") throw new ArgumentException("compare | create <output-folder>");
        Directory.CreateDirectory(args[1]);
        // Only candidates that survived live placement belong in this generator.
        // Rejected crash experiments are recorded in the report, not regenerated.
        foreach (var mode in new[] { "baseline", "vanilla-mesh", "index-flags", "game-material" })
        {
            var item = Gbx.Parse<CGameItemModel>(Source).Node;
            ItemFlagConversion.Convert(item);
            var id = "TMIS_Flag_" + mode.Replace('-', '_') + "_r1";
            ItemExportValidator.EnsureGameReady(item, id);
            var prefab = (CPlugPrefab)((NPlugItem_SVariantList)item.EntityModel!).Variants![0].EntityModel!;
            var entry = prefab.Ents![1];
            var dyna = (CPlugDynaObjectModel)entry.Model!;
            var refs = new GbxRefTable { AncestorLevel = 0 };
            if (mode == "index-flags")
                foreach (var v in dyna.Mesh!.Visuals!.OfType<CPlugVisualIndexedTriangles>()) v.IndexBuffer!.Flags = 2;
            if (mode == "game-material")
                foreach (var material in dyna.Mesh!.CustomMaterials!)
                {
                    var inst = material.MaterialUserInst!;
                    inst.Chunks.OfType<CPlugMaterialUserInst.Chunk090FD000>().Single().Version = 11;
                    inst.IsUsingGameMaterial = true;
                    inst.Link = @"Stadium\Media\Material\" + inst.Link;
                }
            if (mode == "vanilla-mesh")
            {
                var mesh = Gbx.Parse<CPlugSolid2Model>(Path.Combine(Data, @"Media\Dyna\Flag\Flag.Mesh.Gbx")).Node;
                mesh.Materials = null;
                mesh.CustomMaterials = dyna.Mesh!.CustomMaterials;
                dyna.Mesh = mesh;
                dyna.MeshFile = null;
            }
            var dest = Path.Combine(args[1], id + ".Item.Gbx");
            using var stream = new MemoryStream();
            if (mode.StartsWith("game-")) new ReferenceTableDocument(item, refs).Save(stream);
            else new Gbx<CGameItemModel>(item).Save(stream);
            var reopened = Gbx.Parse<CGameItemModel>(new MemoryStream(stream.ToArray())).Node;
            if (!ItemFlagConversion.Inspect(reopened).IsFlag) throw new Exception("Flag signature failed");
            File.WriteAllBytes(dest, stream.ToArray());
            Console.WriteLine($"{dest}: {stream.Length} bytes sha256={Convert.ToHexString(SHA256.HashData(stream.ToArray()))}");
        }
        return 0;
    }

    static CPlugPrefab Prefab(CGameItemModel item) =>
        (CPlugPrefab)((NPlugItem_SVariantList)item.EntityModel!).Variants![0].EntityModel!;

    static int CreateProbes6(string folder)
    {
        Directory.CreateDirectory(folder);
        // Removing the frame table while keeping the tween shader crashed the
        // native renderer. Keep that rejected fixture out of the live generator.
        foreach (var mode in new[] { "Control", "Grid", "GridIds", "GridUv" })
        {
            var seed = mode == "Control" ? "TMIS_Deterministic_Control" : "TMIS_Deterministic_Grid";
            var item = Gbx.Parse<CGameItemModel>(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Trackmania/Items/TMIS_FlagResearch", seed + ".Item.Gbx")).Node;
            item.Name = "TMIS probe6 " + mode;
            var entry = Prefab(item).Ents![1];
            var param = (NPlugDynaObjectModel_SInstanceParams)entry.Params!;
            param.PeriodSc=8; param.PeriodScMax=-1; param.Phase01=0; param.Phase01Max=-1;
            var mesh = ((CPlugDynaObjectModel)entry.Model!).Mesh!;
            if (mode == "GridIds")
            {
                mesh.Chunks.OfType<CPlugSolid2Model.Chunk090BB000>().Single().U01 = "TMIS_Probe6_Grid_20261021";
                mesh.FileWriteTime = new DateTime(2026,10,21,16,0,0,DateTimeKind.Utc);
            }
            foreach (var (v, i) in mesh.Visuals!.OfType<CPlugVisualIndexedTriangles>().Select((v,i)=>(v,i)))
            {
                if(mode == "GridIds") v.Chunks.OfType<CPlugVisual.Chunk09006001>().Single().U01 = "TMIS_Probe6_Grid_" + i;
                if(mode == "GridUv") foreach(var uv in v.TexCoords)
                    uv.TexCoords = uv.TexCoords.Select(t=>t with {UV = new Vec2(t.UV.X * 0.5f, t.UV.Y * 0.5f)}).ToArray();
                var points = v.Vertices.Select(p=>p.Position).Concat(v.VertexStreams.SelectMany(s=>s.Positions??[])).ToArray();
                var min = new Vec3(points.Min(p=>p.X),points.Min(p=>p.Y),points.Min(p=>p.Z));
                var max = new Vec3(points.Max(p=>p.X),points.Max(p=>p.Y),points.Max(p=>p.Z));
                v.BoundingBox = new BoxAligned((min.X+max.X)/2,(min.Y+max.Y)/2,(min.Z+max.Z)/2,
                    (max.X-min.X)/2,(max.Y-min.Y)/2,(max.Z-min.Z)/2);
            }
            using var memory = new MemoryStream(); new Gbx<CGameItemModel>(item).Save(memory);
            Gbx.Parse<CGameItemModel>(new MemoryStream(memory.ToArray()));
            File.WriteAllBytes(Path.Combine(folder,"TMIS_Probe6_"+mode+".Item.Gbx"),memory.ToArray());
        }
        return 0;
    }

    static int CreateProbes7(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var root in new[] { false, true })
        {
            var item = Gbx.Parse<CGameItemModel>(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Trackmania/Items/TMIS_FlagResearch/TMIS_Probe6_GridIds.Item.Gbx")).Node;
            item.Name = "TMIS explicit native material " + root;
            var cloth = ((CPlugDynaObjectModel)Prefab(item).Ents![1].Model!).Mesh!;
            var materials = new List<External<CPlugMaterial>>();
            foreach(var name in new[]{"ItemFlag", "ItemFlagNoAnim"})
            {
                // Parse from bytes so external shader/texture references stay
                // external; do not inline a resolved graph with opaque nod refs.
                var doc=Gbx.Parse<CPlugMaterial>(new MemoryStream(File.ReadAllBytes(Path.Combine(Data,"Media/Material",name+".Material.Gbx"))));
                var material=doc.Node;
                foreach(var chunk in material.Chunks.Where(c=>c.Ignore).ToArray()) material.Chunks.Remove(chunk);
                if(root)
                {
                    if(material.ShaderFile is {} shader) shader.FilePath="GameData\\"+shader.FilePath;
                    foreach(var texture in material.CustomMaterial!.Textures!)
                        if(texture.TextureFile is {} tex) tex.FilePath="GameData\\"+tex.FilePath;
                }
                materials.Add(new External<CPlugMaterial>(material,null));
            }
            cloth.Materials=materials.ToArray(); cloth.CustomMaterials=null;
            using var memory=new MemoryStream();
            new ReferenceTableDocument(item,new GbxRefTable{AncestorLevel=0}).Save(memory);
            var reopened=Gbx.Parse<CGameItemModel>(new MemoryStream(memory.ToArray()));
            var mode=root?"Root":"Relative";
            var path=Path.Combine(folder,"TMIS_Probe7_CleanNativeMat"+mode+".Item.Gbx");
            File.WriteAllBytes(path,memory.ToArray());
            Console.WriteLine(path + " " + memory.Length);
            foreach(var file in reopened.RefTable!.Files)Console.WriteLine(file);
        }
        return 0;
    }

    static int CreateProbes8(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var mode in new[] {"Cut", "GridRandom", "NativeSave", "OneLodGrid"})
        {
            var item=Gbx.Parse<CGameItemModel>(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Trackmania/Items/TMIS_FlagResearch",mode=="Cut"?"TMIS_Deterministic_Control.Item.Gbx":"TMIS_Probe6_GridIds.Item.Gbx")).Node;
            item.Name="TMIS probe8 "+mode;
            var entry=Prefab(item).Ents![1];
            var param=(NPlugDynaObjectModel_SInstanceParams)entry.Params!;
            param.PeriodSc=8;param.PeriodScMax=16;param.Phase01=0;param.Phase01Max=1;
            var mesh=((CPlugDynaObjectModel)entry.Model!).Mesh!;
            if(mode=="OneLodGrid")
            {mesh.Visuals=[mesh.Visuals![0]];mesh.ShadedGeoms=[mesh.ShadedGeoms![0]];mesh.LodMaxDistAtFov90=[];}
            if(mode=="Cut")
            foreach(var v in mesh.Visuals!.OfType<CPlugVisualIndexedTriangles>().Where(v=>v.Vertices.Length>0))
            {
                var indices=v.IndexBuffer!.Indices.Chunk(3).Where(t=>t.Average(i=>v.TexCoords[0].TexCoords[i].UV.X)<0.5).SelectMany(t=>t).ToArray();
                v.IndexBuffer.Indices=indices;
                v.SubVisuals=v.SubVisuals!.Select(s=>new Int3(s.X,0,indices.Length)).ToArray();
                Console.WriteLine($"Cut indices={indices.Length}");
            }
            using var memory=new MemoryStream();new Gbx<CGameItemModel>(item).Save(memory);
            Gbx.Parse<CGameItemModel>(new MemoryStream(memory.ToArray()));
            File.WriteAllBytes(Path.Combine(folder,"TMIS_Probe8_"+mode+".Item.Gbx"),memory.ToArray());
        }
        return 0;
    }

    static int CreateProbes9(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var mode in new[] { "GridClean", "GridExternal", "ControlExternal" })
        {
            var seed = mode == "ControlExternal" ? "TMIS_CustomFlags/TMIS_CustomFlag_Standalone.Item.Gbx"
                : "TMIS_FlagResearch/TMIS_Probe8_GridRandom.Item.Gbx";
            var item = Gbx.Parse<CGameItemModel>(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Trackmania/Items", seed)).Node;
            item.Name = "TMIS isolated " + mode;
            ((NPlugItem_SVariantList)item.EntityModel!).Variants![0].Tags!.Clear();
            var mesh = ((CPlugDynaObjectModel)Prefab(item).Ents![1].Model!).Mesh!;
            mesh.Chunks.OfType<CPlugSolid2Model.Chunk090BB000>().Single().U01 = "TMIS_Probe9_" + mode;
            mesh.FileWriteTime = new DateTime(2026,10,21,20,0,0,DateTimeKind.Utc);
            mesh.FileImageBytes = [];
            foreach (var (v,i) in mesh.Visuals!.Select((v,i)=>(v,i)))
                v.Chunks.OfType<CPlugVisual.Chunk09006001>().Single().U01 = "TMIS_Probe9_" + mode + "_" + i;
            var table = new GbxRefTable { AncestorLevel = 0 };
            if (mode.EndsWith("External"))
            {
                mesh.Materials = new[] { "ItemFlag", "ItemFlagNoAnim" }.Select(n =>
                    new External<CPlugMaterial>(null, new GbxRefTableFile(table,0,true,"GameData\\Stadium\\Media\\Material\\"+n+".Material.Gbx"))).ToArray();
                mesh.CustomMaterials = null;
            }
            else
                foreach(var mat in mesh.CustomMaterials!) mat.MaterialUserInst!.MaterialName = "TMIS_Probe9_" + mat.MaterialUserInst.MaterialName;
            using var memory = new MemoryStream();
            new ReferenceTableDocument(item,table).Save(memory);
            Gbx.Parse<CGameItemModel>(new MemoryStream(memory.ToArray()));
            var path=Path.Combine(folder,"TMIS_Probe9_"+mode+".Item.Gbx");
            File.WriteAllBytes(path,memory.ToArray());
            Console.WriteLine(path+" "+memory.Length);
        }
        return 0;
    }

    static int CreateProbes10(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach(var mode in new[]{"UserInst15", "Custom28", "GameLink11", "ExternalFlag1"})
        {
            var item=Gbx.Parse<CGameItemModel>(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Trackmania/Items/TMIS_FlagResearch/TMIS_Probe9_GridClean.Item.Gbx")).Node;
            item.Name="TMIS material storage "+mode;
            var mesh=((CPlugDynaObjectModel)Prefab(item).Ents![1].Model!).Mesh!;
            var chunk=mesh.Chunks.OfType<CPlugSolid2Model.Chunk090BB000>().Single();
            chunk.U01="TMIS_Probe10_"+mode;
            var table=new GbxRefTable{AncestorLevel=0};
            if(mode=="UserInst15")
            {
                chunk.Version=15;
                mesh.Materials=[];
                mesh.MaterialInsts=mesh.CustomMaterials!.Select(m=>m.MaterialUserInst!).ToArray();
                mesh.CustomMaterials=null;
            }
            if(mode=="Custom28") {chunk.Version=28;mesh.Materials=[];}
            if(mode=="GameLink11")
                foreach(var mat in mesh.CustomMaterials!)
                {
                    var inst=mat.MaterialUserInst!;
                    inst.Chunks.OfType<CPlugMaterialUserInst.Chunk090FD000>().Single().Version=11;
                    inst.IsUsingGameMaterial=true;
                    inst.Link="Stadium\\Media\\Material\\"+inst.Link;
                }
            if(mode=="ExternalFlag1")
            {
                mesh.Materials=new[]{"ItemFlag","ItemFlagNoAnim"}.Select(n=>new External<CPlugMaterial>(null,
                    new GbxRefTableFile(table,1,false,"GameData\\Stadium\\Media\\Material\\"+n+".Material.Gbx"))).ToArray();
                mesh.CustomMaterials=null;
            }
            using var memory=new MemoryStream();new ReferenceTableDocument(item,table).Save(memory);
            Gbx.Parse<CGameItemModel>(new MemoryStream(memory.ToArray()));
            var path=Path.Combine(folder,"TMIS_Probe10_"+mode+".Item.Gbx");File.WriteAllBytes(path,memory.ToArray());
            Console.WriteLine(path+" "+memory.Length);
        }
        return 0;
    }

    static int CreateDiagnostics(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var mode in new[] { "WideFixedBounds", "Tall", "NoTagWide" })
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Trackmania/Items/TMIS_CustomFlags",
                mode == "Tall" ? "TMIS_CustomFlag_Standalone.Item.Gbx" : "TMIS_CustomFlag_WideStandalone.Item.Gbx");
            var item = Gbx.Parse<CGameItemModel>(path).Node;
            item.Name = "TMIS diagnostic " + mode;
            if (mode == "NoTagWide") ((NPlugItem_SVariantList)item.EntityModel!).Variants![0].Tags!.Remove("Type");
            var mesh = ((CPlugDynaObjectModel)Prefab(item).Ents![1].Model!).Mesh!;
            var original = ((CPlugDynaObjectModel)Prefab(Gbx.Parse<CGameItemModel>(Source).Node).Ents![1].Model!).Mesh!;
            foreach (var (v, i) in mesh.Visuals!.OfType<CPlugVisualIndexedTriangles>().Select((v, i) => (v, i)))
            {
                if (mode == "WideFixedBounds") v.BoundingBox = original.Visuals![i].BoundingBox;
                if (mode == "Tall")
                {
                    Vec3 Tall(Vec3 p) => new(p.X, 1.136364f + (p.Y - 1.136364f) * 2f, p.Z);
                    Vec3? Normal(Vec3? n)
                    {
                        if (n is not {} a) return null;
                        var length = MathF.Sqrt(a.X * a.X + a.Y * a.Y / 4f + a.Z * a.Z);
                        return new(a.X / length, a.Y / 2f / length, a.Z / length);
                    }
                    v.Vertices = v.Vertices.Select(p => p with { Position = Tall(p.Position), Normal = Normal(p.Normal) }).ToArray();
                    foreach (var stream in v.VertexStreams)
                    { stream.Positions = stream.Positions?.Select(Tall).ToArray(); stream.Normals = stream.Normals?.Select(n => Normal(n)!.Value).ToArray(); }
                }
            }
            using var memory = new MemoryStream(); new Gbx<CGameItemModel>(item).Save(memory);
            Gbx.Parse<CGameItemModel>(new MemoryStream(memory.ToArray()));
            File.WriteAllBytes(Path.Combine(folder, "TMIS_Diagnostic_" + mode + ".Item.Gbx"), memory.ToArray());
        }
        return 0;
    }

    static int VerifyFinal(string folder)
    {
        var checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
        bool Same(object? a, object? b) => JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(b, Json);
        var source = Gbx.Parse<CGameItemModel>(Source).Node;
        var originalPrefab = Prefab(source);
        var originalCloth = ((CPlugDynaObjectModel)originalPrefab.Ents![1].Model!).Mesh!;
        foreach (var custom in new[] { false, true })
        {
            var id = custom ? "TMIS_CustomFlag_WideStandalone" : "TMIS_CustomFlag_Standalone";
            var file = Gbx.Parse<CGameItemModel>(Path.Combine(folder, id + ".Item.Gbx"));
            var item = file.Node;
            var prefab = Prefab(item);
            var dyna = (CPlugDynaObjectModel)prefab.Ents![1].Model!;
            var cloth = dyna.Mesh!;
            Check(item.Ident?.Id == "", "let native loader assign the installed file identity");
            Check(item.ArchetypeRef == "", "standalone graph, no self-referencing archetype");
            var vanilla = Gbx.Parse<CGameItemModel>(Path.Combine(Data, @"Items\Flag16m.Item.Gbx")).Node;
            Check(Same(item.Chunks.OfType<CPlugGameSkin.HeaderChunk090F4000>().Single().Node,
                       vanilla.Chunks.OfType<CPlugGameSkin.HeaderChunk090F4000>().Single().Node), "vanilla skin declaration preserved");
            Check(ItemFlagConversion.Inspect(item).IsFlag, "vanilla classification and placement");
            Check(prefab.Ents.Length == 2, "pole plus dynamic cloth");
            Check(Same(prefab.Ents[0], originalPrefab.Ents[0]), "pole mesh and collision unchanged");
            Check(Same(prefab.Ents[1].Params, originalPrefab.Ents[1].Params), "animation periods and phases unchanged");
            Check(prefab.Ents[1].Position == originalPrefab.Ents[1].Position &&
                  prefab.Ents[1].Rotation == originalPrefab.Ents[1].Rotation, "cloth attachment unchanged");
            Check(Same(cloth.LodMaxDistAtFov90, originalCloth.LodMaxDistAtFov90), "LOD distances unchanged");
            Check(Same(cloth.ShadedGeoms, originalCloth.ShadedGeoms), "LOD and material mapping unchanged");
            Check(Same(cloth.CustomMaterials, originalCloth.CustomMaterials), "game material links unchanged");
            Check(cloth.Visuals!.Length == 5, "all five cloth LODs");
            for (var i = 0; i < cloth.Visuals.Length; i++)
            {
                var visual = (CPlugVisualIndexedTriangles)cloth.Visuals[i];
                var original = (CPlugVisualIndexedTriangles)originalCloth.Visuals![i];
                Check(visual.IndexBuffer!.Flags == 2, "vanilla index flags");
                Check(visual.IndexBuffer.Indices.SequenceEqual(original.IndexBuffer!.Indices), "topology unchanged");
                Check(Same(visual.TexCoords, original.TexCoords), "UV mapping unchanged");
                Check(Same(visual.SubVisuals, original.SubVisuals), "frame offsets unchanged");
                Check(visual.SubVisuals!.Length == (i < 4 ? 86 : 0), "86 frames in each animated LOD");
                Check(visual.Vertices.Length == original.Vertices.Length, "vertex counts unchanged");
                for (var v = 0; v < visual.Vertices.Length; v++)
                {
                    var expected = custom ? Wide(original.Vertices[v].Position) : original.Vertices[v].Position;
                    Check(visual.Vertices[v].Position == expected, $"{id} LOD{i} vertex{v} persisted deformation");
                    var p = visual.Vertices[v].Position;
                    Check(float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z), "finite vertices");
                    var n = visual.Vertices[v].Normal!.Value;
                    Check(float.IsFinite(n.X) && float.IsFinite(n.Y) && float.IsFinite(n.Z), "finite normals");
                }
                Check(visual.VertexStreams.Count == original.VertexStreams.Count, "vertex streams preserved");
                for (var s = 0; s < visual.VertexStreams.Count; s++)
                {
                    var expected = original.VertexStreams[s].Positions?.Select(p => custom ? Wide(p) : p).ToArray();
                    Check(Same(visual.VertexStreams[s].Positions, expected), "far-LOD deformation persisted");
                }
                var points = visual.Vertices.Select(v => v.Position)
                    .Concat(visual.VertexStreams.SelectMany(s => s.Positions ?? [])).ToArray();
                Console.WriteLine($"{id} LOD{i}: frames={visual.SubVisuals.Length}, vertices={points.Length}, "
                    + $"min=[{points.Min(p=>p.X)},{points.Min(p=>p.Y)},{points.Min(p=>p.Z)}], "
                    + $"max=[{points.Max(p=>p.X)},{points.Max(p=>p.Y)},{points.Max(p=>p.Z)}]");
            }
        }
        Console.WriteLine($"PASS: {checks} asset invariants (source fixture required).");
        return 0;
    }

    static int CreateFinal(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var custom in new[] { false, true })
        {
            var item = Gbx.Parse<CGameItemModel>(Source).Node;
            var id = custom ? "TMIS_CustomFlag_WideStandalone" : "TMIS_CustomFlag_Standalone";
            ItemFlagConversion.Convert(item);
            ItemExportValidator.EnsureGameReady(item, id);
            // These are standalone models, not derived archetypes. A self-reference
            // makes the native map writer embed a ~1.5 KB shell WITHOUT the mesh.
            // Match the original custom item's empty disk identity: the native
            // loader assigns the actual installed relative filename to each model.
            item.Ident = new Ident("", item.Ident!.Collection, item.Ident.Author);
            item.ArchetypeRef = "";
            item.Name = custom ? "TMIS Custom Flag - Wide Cloth" : "TMIS Custom Flag - Vanilla Parity";
            // XertroV's inline source omitted the skin header. Retain vanilla's
            // customizable texture names and skin directory, rather than guessing.
            var vanilla = Gbx.Parse<CGameItemModel>(Path.Combine(Data, @"Items\Flag16m.Item.Gbx")).Node;
            item.Chunks.Add(vanilla.Chunks.OfType<CPlugGameSkin.HeaderChunk090F4000>().Single());
            var prefab = (CPlugPrefab)((NPlugItem_SVariantList)item.EntityModel!).Variants![0].EntityModel!;
            var cloth = ((CPlugDynaObjectModel)prefab.Ents![1].Model!).Mesh!;
            foreach (var visual in cloth.Visuals!.OfType<CPlugVisualIndexedTriangles>())
            {
                visual.IndexBuffer!.Flags = 2;
                if (!custom) continue;
                // Affine deformation of EVERY animation frame and every LOD, including
                // the nonanimated far stream. Keep the attachment edge x=0 stationary.
                visual.Vertices = visual.Vertices.Select(v => v with
                { Position = Wide(v.Position), Normal = WideNormal(v.Normal) }).ToArray();
                foreach (var stream in visual.VertexStreams)
                {
                    stream.Positions = stream.Positions?.Select(Wide).ToArray();
                    stream.Normals = stream.Normals?.Select(n => WideNormal(n)!.Value).ToArray();
                }
                var b = visual.BoundingBox;
                visual.BoundingBox = new BoxAligned(b.X * 1.5f, b.Y, b.Z, b.X2 * 1.5f, b.Y2, b.Z2);
            }
            using var memory = new MemoryStream();
            new Gbx<CGameItemModel>(item).Save(memory);
            var bytes = memory.ToArray();
            var result = Gbx.Parse<CGameItemModel>(new MemoryStream(bytes)).Node;
            if (!ItemFlagConversion.Inspect(result).IsFlag) throw new Exception("Flag signature failed");
            var target = Path.Combine(folder, id + ".Item.Gbx");
            File.WriteAllBytes(target, bytes);
            Console.WriteLine($"{target}: {bytes.Length} bytes SHA256={Convert.ToHexString(SHA256.HashData(bytes))}");
        }
        return 0;
    }

    static Vec3 Wide(Vec3 p) => new(p.X * 1.5f, p.Y, p.Z);
    static Vec3? WideNormal(Vec3? n)
    {
        if (n is not { } v) return null;
        var x = v.X / 1.5f;
        var length = MathF.Sqrt(x * x + v.Y * v.Y + v.Z * v.Z);
        return new Vec3(x / length, v.Y / length, v.Z / length);
    }

    static void Describe(string name, CPlugSolid2Model mesh)
    {
        Console.WriteLine(name);
        Props(mesh);
        foreach (var (visual, i) in mesh.Visuals!.Select((v, i) => (v, i)))
        {
            Console.WriteLine($"  visual[{i}]");
            Props(visual);
            foreach (var (stream, j) in visual.VertexStreams.Select((v, j) => (v, j)))
            { Console.WriteLine($"    stream[{j}]"); Props(stream); }
            foreach (var (uv, j) in visual.TexCoords.Select((v, j) => (v, j)))
            { Console.WriteLine($"    uv[{j}]"); Props(uv); }
        }
    }

    static void Props(object value)
    {
        foreach (var field in value.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            var child = field.GetValue(value);
            Console.WriteLine($"    field {field.Name}={JsonSerializer.Serialize(child, child?.GetType() ?? typeof(object), Json)}");
        }
        foreach (var property in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(p => p.Name))
        {
            if (property.Name is "Chunks" or "Gbx" || property.GetIndexParameters().Length > 0) continue;
            try
            {
                var child = property.GetValue(value);
                if (child is null) { Console.WriteLine($"    {property.Name}=null"); continue; }
                if (child is IEnumerable list and not string)
                {
                    var entries = list.Cast<object>().ToArray();
                    if (entries.Any(x => x is GBX.NET.Engines.MwFoundations.CMwNod))
                        Console.WriteLine($"    {property.Name} count={entries.Length}");
                    else
                    {
                        var bytes = JsonSerializer.SerializeToUtf8Bytes(child, child.GetType(), Json);
                        Console.WriteLine($"    {property.Name} count={entries.Length} sha256={Convert.ToHexString(SHA256.HashData(bytes))} sample={System.Text.Encoding.UTF8.GetString(bytes)[..Math.Min(bytes.Length, 140)]}");
                    }
                }
                else Console.WriteLine($"    {property.Name}={JsonSerializer.Serialize(child, child.GetType(), Json)}");
            }
            catch (Exception ex) { Console.WriteLine($"    {property.Name}: {ex.GetType().Name}"); }
        }
    }
}
