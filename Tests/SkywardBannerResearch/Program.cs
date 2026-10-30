using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.Game;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.MwFoundations;
using GBX.NET.Engines.Plug;
using TmEssentials;
using KC = GBX.NET.Engines.Meta.NPlugDyna_SKinematicConstraint;

// Isolated file-only research executable. Never starts/stops a process, stages an
// asset, edits the source Item or changes a live map/plugin/install directory.
Gbx.LZO = new GBX.NET.LZO.MiniLZO();
if(args.Length==2&&args[0]=="--generate-baked-host-control")
{
    Research.GenerateBakedHostControl(args[1]);return;
}
if(args.Length==2&&args[0]=="--generate-baked-material-control")
{
    Research.GenerateBakedMaterialControl(args[1]);return;
}
if(args.Length==2&&args[0]=="--generate-baked-material-anchor-control")
{
    Research.GenerateBakedMaterialControl(args[1], true);return;
}
if(args.Length==2&&args[0]=="--generate-flight-anchor-control")
{
    Research.GenerateFlightAnchorControl(args[1]);return;
}
if(args.Length==2&&args[0]=="--generate-shared-flight-anchor-control")
{
    Research.GenerateSharedFlightAnchorControl(args[1]);return;
}
if(args.Length==2&&args[0]=="--generate-shared-flight-anchor-kinematic-control")
{
    Research.GenerateSharedFlightAnchorControl(args[1], true);return;
}
if(args.Length==2&&args[0]=="--generate-shared-flight-parent-child-control")
{
    Research.GenerateSharedFlightParentChildControl(args[1]);return;
}
if(args.Length==2&&args[0]=="--generate-shared-flight-parent-child-interleaved-control")
{
    Research.GenerateSharedFlightParentChildInterleavedControl(args[1]);return;
}
if(args.Length==2&&args[0]=="--generate-shared-flight-parent-child-dynamic-control")
{
    Research.GenerateSharedFlightParentChildDynamicControl(args[1]);return;
}
if(args.Length==2&&args[0]=="--generate-shared-flight-parent-child-dynamize-control")
{
    Research.GenerateSharedFlightParentChildDynamizeControl(args[1]);return;
}
if(args.Length==2&&args[0]=="--generate-shared-flight-parent-child-anchor-param-control")
{
    Research.GenerateSharedFlightParentChildAnchorParamControl(args[1]);return;
}
if(args.Length==2&&args[0]=="--generate-shared-flight-nested-prefab-control")
{
    Research.GenerateSharedFlightNestedPrefabControl(args[1]);return;
}
if(args.Length==2&&args[0]=="--generate-shared-locanim-flight-control")
{
    Research.GenerateSharedLocAnimFlightControl(args[1]);return;
}
if(args.Length==2&&args[0]=="--generate-single-host-flight-control")
{
    Research.GenerateSingleHostFlightControl(args[1]);return;
}
if(args.Length==2&&args[0]=="--generate-single-host-flight-itemflag-control")
{
    Research.GenerateSingleHostFlightControl(args[1], true);return;
}
if(args.Length==2&&args[0]=="--generate-single-host-flight-itemflag-lod-control")
{
    Research.GenerateSingleHostFlightControl(args[1], true, true);return;
}
if(args.Length==2&&args[0]=="--generate-phase-controls")
{
    Research.GeneratePhaseControls(args[1]);return;
}
if(args.Length==2&&args[0]=="--generate-both-sentinel")
{
    Research.GeneratePhaseControls(args[1],true);return;
}
if(args.Length==3&&args[0]=="--verify-layout-pair")
{
    Research.VerifyLayoutPair(args[1],args[2]);return;
}
if (args.Length != 2 || args[0] is not ("--generate" or "--generate-contrast" or "--generate-flight" or "--generate-flight-contrast" or "--generate-oneuv" or "--generate-contrast-oneuv" or "--generate-flight-contrast-oneuv"))
    throw new ArgumentException("Use --generate[-flight][-contrast][-oneuv] <new-output-directory>");
Research.Generate(args[1],args[0].Contains("contrast"),args[0].Contains("flight"),args[0].Contains("oneuv"));

static partial class Research
{
    const string PlaneHash = "7EEDD88D3417A59F08724F6755EE665ADC35CAF7CE2E46AEFC554C5443EE2FB6";
    const string SeedHash = "D4F44EB23EE07B0C59CC0E9A976EAE05405B4DDE139BB2673C429F327EAFE751";
    const int Frames = 86, BannerIndex = 9;
    const float Period = 8, Amplitude = .65f, Wavelength = 7, FixedBand = .5f, FlightPeriod = 20, FlightShiftX = 115;
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, IncludeFields = true };
    static readonly Vec3 AnchorPosition = new(903, 128, 450);
    static readonly Vec3 FlightAnchorPosition = new(788,128,450);
    static readonly Quat Identity = new(0, 0, 0, 1);
    static readonly Dictionary<string,string> DependencyHashes = new() {
        ["TMIS_MatRemap_Placeholder.Material.Gbx"] = "CB3B703C2B5BED9F359919C59B24A08730EE833FC5E422C40062DA061CDA588A",
        ["TMIS_MatRemap.GameSkin.Gbx"] = "DC6118300D7C4FDBA9DAA737130B45249A5E46F1AA56AC17A6D7F658CFA6859E",
        ["TMIS_MatRemap.TerrainModifier.Gbx"] = "D276FA3E545347EC01533401B9A44A8EC2254BCDE9C2C7BFA4FAFC7CE20D942B" };

    public static void VerifyLayoutPair(string twoUvPath,string oneUvPath)
    {
        var originalTwo=File.ReadAllBytes(twoUvPath);var originalOne=File.ReadAllBytes(oneUvPath);
        var two=Parse(originalTwo);var one=Parse(originalOne);
        var twoCloth=((CPlugDynaObjectModel)FlagPrefab(two.Node).Ents![1].Model!).Mesh!;
        var oneCloth=((CPlugDynaObjectModel)FlagPrefab(one.Node).Ents![1].Model!).Mesh!;
        Require(twoCloth.Visuals is {Count:5}&&oneCloth.Visuals is {Count:5},"Expected all five cloth LODs");
        Require(twoCloth.Visuals!.All(v=>v.TexCoords.Length==2)&&oneCloth.Visuals!.All(v=>v.TexCoords.Length==1),"Expected two-UV/one-UV pair");
        one.Node.Name=two.Node.Name;
        foreach(var visual in twoCloth.Visuals)visual.TexCoords=visual.TexCoords.Take(1).ToArray();
        var normalizedTwo=Save(two);var normalizedOne=Save(one);
        Require(normalizedTwo.AsSpan().SequenceEqual(normalizedOne),"Items differ beyond display name and UV1 removal");
        Require(File.ReadAllBytes(twoUvPath).AsSpan().SequenceEqual(originalTwo)&&File.ReadAllBytes(oneUvPath).AsSpan().SequenceEqual(originalOne),"A verified input changed");
        Console.WriteLine(JsonSerializer.Serialize(new {operation="offline-layout-pair-verification",twoUvPath=Path.GetFullPath(twoUvPath),twoUvSha256=Hash(originalTwo),oneUvPath=Path.GetFullPath(oneUvPath),oneUvSha256=Hash(originalOne),canonicalSha256AfterRemovingOnlyUv1AndNormalizingDisplayName=Hash(normalizedOne),completeItemGraphAndRefsEqualBeyondUv1AndName=true,inputsUnchanged=true,runtimeVerdict=false},Json));
    }

    public static void GeneratePhaseControls(string output,bool bothOnly=false)
    {
        output=Path.GetFullPath(output);Require(!Directory.Exists(output)&&!File.Exists(output),"Choose a new evidence directory");
        var root=Root();
        var sourcePath=Path.Combine(root,"artifacts/skyward-banner-20261023/flight-contrast-oneuv-v1/files/TMIS_Skyward_OneUVFlightContrastCircleWave.Item.Gbx");
        var sourceBytes=File.ReadAllBytes(sourcePath);
        Require(Hash(sourceBytes)=="443C76E135EDFCAE16759EE1E8C46019EB12E7548E2705660A2377456DD7DA71","Unexpected authored baseline CircleWave bytes");
        var templatePath=Path.Combine(root,"artifacts/skyward-banner-20261023/flight-contrast-oneuv-v1/TMIS_Skyward_OneUVFlightContrastCircleWave.Map.Gbx");
        var mapTemplate=File.ReadAllBytes(templatePath);
        Require(Hash(mapTemplate)=="AFB38F550E3A137A7F620295C6E5B40B146C31877D47278C278ED6D9497F75E6","Unexpected baseline CircleWave map");
        var dependencies=DependencyHashes.ToDictionary(p=>p.Key,p=>File.ReadAllBytes(Path.Combine(root,"Resources/Flags",p.Key)));
        foreach(var pair in dependencies)Require(Hash(pair.Value)==DependencyHashes[pair.Key],"Dependency changed");
        var source=Parse(sourceBytes);var sourceCanonical=Save(source);var sourcePrefab=FlagPrefab(source.Node);
        var meshes=sourcePrefab.Ents!.Where(e=>e.Model is CPlugDynaObjectModel).Select(e=>NodeHash(((CPlugDynaObjectModel)e.Model!).Mesh!)).ToArray();
        var folder="TMIS_SkywardPhase_"+Guid.NewGuid().ToString("N")[..16];var rows=new List<object>();
        Directory.CreateDirectory(output);Directory.CreateDirectory(Path.Combine(output,"files"));
        foreach(var pair in dependencies)File.WriteAllBytes(Path.Combine(output,"files",pair.Key),pair.Value);
        foreach(var label in bothOnly?new[]{"BothSentinel"}:new[]{"RigidSentinel","ClothSentinel"})
        {
            var rigidSentinel=label!="ClothSentinel";var clothSentinel=label!="RigidSentinel";
            var doc=Parse(sourceBytes);var prefab=FlagPrefab(doc.Node);
            for(var i=0;i<2;i++)if(i==0?rigidSentinel:clothSentinel)
            {
                var selected=(NPlugDynaObjectModel_SInstanceParams)prefab.Ents![i].Params!;
                selected.Phase01=selected.Phase01Max=-1;if(i==0)selected.PeriodScMax=-1;
            }
            doc.Node.Name="TMIS Skyward OneUV CircleWave "+label;
            var bytes=Save(doc);var check=Parse(bytes);var p=FlagPrefab(check.Node);
            Require(p.Ents!.Where(e=>e.Model is CPlugDynaObjectModel).Select(e=>NodeHash(((CPlugDynaObjectModel)e.Model!).Mesh!)).SequenceEqual(meshes),"A complete mesh changed in timing-only control");
            for(var i=0;i<2;i++)
            {
                var requested=(NPlugDynaObjectModel_SInstanceParams)p.Ents[i].Params!;
                var sentinel=i==0?rigidSentinel:clothSentinel;
                Require(requested.Phase01==(sentinel?-1:0)&&requested.Phase01Max==(sentinel?-1:0)&&requested.PeriodSc==(i==0?1:20)&&requested.PeriodScMax==(i==0?(rigidSentinel?-1:1):20),"Requested phase fields changed during serialization");
            }
            var timing=new {rigid=Timing((NPlugDynaObjectModel_SInstanceParams)p.Ents[0].Params!),cloth=Timing((NPlugDynaObjectModel_SInstanceParams)p.Ents[1].Params!)};
            check.Node.Name=source.Node.Name;
            for(var i=0;i<2;i++)p.Ents[i].Params=sourcePrefab.Ents[i].Params;
            Require(Save(check).AsSpan().SequenceEqual(sourceCanonical),"Control changed beyond displayName and explicitly replaced timing parameters");
            var itemName="TMIS_Skyward_OneUV_CircleWave_"+label+".Item.Gbx";
            var mapName=itemName.Replace(".Item.Gbx",".Map.Gbx",StringComparison.Ordinal);
            File.WriteAllBytes(Path.Combine(output,"files",itemName),bytes);
            var map=Map(mapTemplate,bytes,itemName,dependencies,folder,label,true);File.WriteAllBytes(Path.Combine(output,mapName),map);
            var embeddedFiles=dependencies.Append(new(itemName,bytes)).Select(f=>new {zipPath="Items/"+folder+"/"+f.Key,bytes=f.Value.Length,sha256=Hash(f.Value)}).ToArray();
            File.WriteAllText(Path.Combine(output,mapName+".manifest.json"),JsonSerializer.Serialize(new {manifestVersion=1,operation="offline-skyward-phase-sentinel-control",scope="Offline timing-only authoring; no native success verdict",outputMap=Path.Combine(output,mapName),outputSha256=Hash(map),embeddedFolder=folder,embeddedFiles,expectedEmbeddedItemIds=new[]{folder+"\\"+itemName},nativePrimerPresent=true,nativePrimerIndex=0,nativeFlagEmbedded=false,anchoredObjectCount=2},Json));
            rows.Add(new {label,itemFile=itemName,itemSha256=Hash(bytes),mapFile=mapName,mapSha256=Hash(map),timing,allCompleteMeshSerializationsExact=true,fullCanonicalItemEqualAfterRestoringOnlyNameAndTiming=true});
        }
        Require(File.ReadAllBytes(sourcePath).AsSpan().SequenceEqual(sourceBytes)&&File.ReadAllBytes(templatePath).AsSpan().SequenceEqual(mapTemplate),"Baseline Item/map changed");
        var manifest=new {manifestVersion=1,operation="offline-skyward-native-phase-sentinel-controls",scope="Timing hypotheses only; native clock consumers and continuous attachment require game observation",sourcePath,sourceSha256=Hash(sourceBytes),templatePath,templateSha256=Hash(mapTemplate),folder,anchor=FlightAnchorPosition,rows,sourcesUnchanged=true};
        File.WriteAllText(Path.Combine(output,"manifest.json"),JsonSerializer.Serialize(manifest,Json));
        File.WriteAllText(Path.Combine(output,"README.md"),"# Skyward phase-sentinel controls\n\nThese candidate maps change only the display name and specified timing fields of the exact OneUVFlightContrastCircleWave baseline. Every complete plane/cloth mesh serialization is byte-identical. Rest transforms, native 20,000ms constraint, both source banner sides, materials, UV0 palette, absent UV1, indices, all86frames/all5LODs, collision and conservative bounds remain exact. Restoring only displayName and the two instance parameter nodes yields a byte-identical canonical Item/reference serialization.\n\nRigidSentinel restores the source rigid timing values PeriodSc=1, PeriodScMax=-1, Phase01=-1, Phase01Max=-1; cloth remains20/20 and0/0. ClothSentinel retains baseline rigid1/1 and0/0, with clothPeriod20/20 and phases-1/-1. BothSentinel applies rigid1/-1/-1/-1 and cloth20/20/-1/-1 together. Negative values are native sentinel candidates; their inherited phase semantics are not assumed.\n\nAll maps use the same anchor(788,128,450), identity transforms and genuine nativeFlag16m. Test separately, with a fixed camera through at least two20-second loops; inspect leading-edge/tow attachment and compare rotation direction, phase and period. The source150/160-second aircraft motion remains deliberately replaced. No new native success is claimed by this generator.\n");
        Console.WriteLine(JsonSerializer.Serialize(manifest,Json));
    }
    static object Timing(NPlugDynaObjectModel_SInstanceParams p)=>new {p.Version,p.PeriodSc,p.PeriodScMax,p.Phase01,p.Phase01Max,p.IsKinematic,p.CastStaticShadow,p.TextureId};

    public static void Generate(string output,bool contrast=false,bool flight=false,bool oneUv=false)
    {
        output = Path.GetFullPath(output);
        Require(!Directory.Exists(output) && !File.Exists(output), "Choose a new evidence directory");
        var root = Root();
        var originalPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Trackmania/Items/BF2_ASSETS/Skyward_DoubleDecker_Plane/PromoPlane_Skyward.Item.Gbx");
        var seedPath = Path.Combine(root,"Resources/Flags/TMIS_MatRemap_Long250.Item.Gbx");
        var mapPath = Path.Combine(root,"artifacts/flag-selfcontained-20261022/material-remap-shapes-v6-near-native-primer/TMIS_Embedded_Flag_Shapes_v6_Near_NativePrimerFirst.Map.Gbx");
        var originalBytes = File.ReadAllBytes(originalPath);
        var seedBytes = File.ReadAllBytes(seedPath);
        var mapBytes = File.ReadAllBytes(mapPath);
        Require(Hash(originalBytes) == PlaneHash, "Original Skyward Item hash changed");
        Require(Hash(seedBytes) == SeedHash, "Flag seed hash changed");
        var dependencies = DependencyHashes.ToDictionary(p=>p.Key,p=>File.ReadAllBytes(Path.Combine(root,"Resources/Flags",p.Key)));
        foreach (var p in dependencies) Require(Hash(p.Value)==DependencyHashes[p.Key], "Dependency hash changed: "+p.Key);
        var source = Parse(originalBytes);
        var sourcePrefab = source.Node.EntityModel as CPlugPrefab ?? throw new InvalidDataException("Expected direct source prefab");
        Require(sourcePrefab.Ents is { Length: 2 } && sourcePrefab.Ents[0].Model is CPlugDynaObjectModel && sourcePrefab.Ents[1].Model is NPlugDyna_SKinematicConstraint,"Source flight graph changed");
        Require(sourcePrefab.Ents![0].Position==default && sourcePrefab.Ents[0].Rotation==Identity,"Source rest transform changed");
        var sourceMesh = ((CPlugDynaObjectModel)sourcePrefab.Ents[0].Model!).Mesh!;
        Require(sourceMesh.Visuals is {Count:13} && sourceMesh.CustomMaterials is {Length:13},"Source mesh changed");
        Require(sourceMesh.ShadedGeoms is {Length:13} && sourceMesh.ShadedGeoms.Select((g,i)=>g.VisualIndex==i&&g.MaterialIndex==i&&g.LodMask==1).All(x=>x),"Source mapping changed");
        var sourceBanner = ReadBanner((CPlugVisualIndexedTriangles)sourceMesh.Visuals[BannerIndex]);
        Require(sourceBanner.Positions.Length==2576 && sourceBanner.Indices.Length==7341,"Banner source topology changed");
        var banner = contrast ? sourceBanner with {Uvs=sourceBanner.Uvs.ToDictionary(u=>u.Key,u=>u.Key==0?u.Value.Select(v=>v.Y>.8f?new Vec2(.5f,.5f):new Vec2(.5f,.1f)).ToArray():u.Value.ToArray())} : sourceBanner;
        if(oneUv)banner=banner with {Uvs=banner.Uvs.Where(u=>u.Key==0).ToDictionary(u=>u.Key,u=>u.Value)};
        var sourceVisualHashes = sourceMesh.Visuals.Select(NodeHash).ToArray();
        var entries = new List<object>();
        var outputs = new Dictionary<string,byte[]>(StringComparer.Ordinal);
        var token=Guid.NewGuid().ToString("N")[..16];
        var folder="TMIS_SkywardBanner_"+token;
        Directory.CreateDirectory(output);
        Directory.CreateDirectory(Path.Combine(output,"files"));
        File.WriteAllText(Path.Combine(output,"source-banner-uv.json"),JsonSerializer.Serialize(new {positions=banner.Positions,normals=banner.Normals,indices=banner.Indices,uvs=banner.Uvs,streamProperties=banner.StreamProperties},Json));
        foreach (var p in dependencies) File.WriteAllBytes(Path.Combine(output,"files",p.Key),p.Value);
        var layoutPrefix=oneUv?"OneUV":"";
        var flightPrefix=layoutPrefix+(contrast?"FlightContrast":"Flight");
        var modes=flight?new[]{flightPrefix+"StationaryZero",flightPrefix+"StationaryWave",flightPrefix+"CircleZero",flightPrefix+"CircleWave"}:contrast?new[]{layoutPrefix+"ContrastZero",layoutPrefix+"ContrastWave"}:oneUv?new[]{"OneUVRemapZero","OneUVRemapWave"}:new[]{"OriginalStatic","RemapZero","RemapWave"};
        foreach (var mode in modes)
        {
            var moving=flight&&mode.Contains("Circle");
            var amplitude=mode.EndsWith("Wave")?Amplitude:0;
            var doc = Parse(seedBytes);
            var prefab=FlagPrefab(doc.Node);
            var clothEntry=prefab.Ents![1];
            var clothBody=(CPlugDynaObjectModel)clothEntry.Model!;
            var sourceCopy=Parse(originalBytes);
            var sourceEntries=((CPlugPrefab)sourceCopy.Node.EntityModel!).Ents!;
            var sourceBody=(CPlugDynaObjectModel)sourceEntries[0].Model!;
            var mesh=sourceBody.Mesh!;
            var planeMeshSourceHash=NodeHash(mesh);
            if(mode!="OriginalStatic")
            {
                mesh.Visuals=mesh.Visuals!.Where((_,i)=>i!=BannerIndex).ToArray();
                mesh.ShadedGeoms=mesh.ShadedGeoms!.Where(g=>g.VisualIndex!=BannerIndex).ToArray();
                foreach(var g in mesh.ShadedGeoms)if(g.VisualIndex>BannerIndex)g.VisualIndex--;
            }
            var planeStatic=new CPlugStaticObjectModel { Version=3, Mesh=mesh, Shape=null, IsMeshCollidable=false };
            var planeEntry=new CPlugPrefab.EntRef { Position=default,Rotation=Identity,Model=planeStatic };
            if(flight)
            {
                planeEntry=sourceEntries[0];
                ShiftRigid(sourceBody);
                var planeTiming=(NPlugDynaObjectModel_SInstanceParams)planeEntry.Params!;
                // Kinematic PeriodSc is the timeline rate scale in the established
                // authoring fixtures; ordinary cloth PeriodSc is its loop seconds.
                // Their native clock agreement is the subject of these controls.
                planeTiming.IsKinematic=true;planeTiming.PeriodSc=planeTiming.PeriodScMax=1;planeTiming.Phase01=planeTiming.Phase01Max=0;
                var constraint=(KC)sourceEntries[1].Model!;
                constraint.RotAxis=KC.EAxis.Y;constraint.AngleMinDeg=0;constraint.AngleMaxDeg=moving?360:0;
                constraint.TransAxis=KC.EAxis.Y;constraint.TransMin=constraint.TransMax=0;
                constraint.RotAnimFunc=FlightTimeline();constraint.TransAnimFunc=FlightTimeline();
                var constraintParams=(NPlugDyna_SPrefabConstraintParams)sourceEntries[1].Params!;
                Require(constraintParams.Ent1==-1&&constraintParams.Ent2==0&&constraintParams.Pos1==default&&constraintParams.Pos2==default,"Source pivots changed");
            }
            var expectedPlaneHash=NodeHash(mesh);
            var expectedPlaneBodyHash=flight?NodeHash(sourceBody):null;
            var expectedConstraintHash=flight?NodeHash((KC)sourceEntries[1].Model!):null;
            var expectedClothHashes=Array.Empty<string>();
            if(mode=="OriginalStatic") prefab.Ents=[planeEntry];
            else
            {
                clothEntry.Position=default;clothEntry.Rotation=Identity;
                var timing=(NPlugDynaObjectModel_SInstanceParams)clothEntry.Params!;
                timing.IsKinematic=false;timing.PeriodSc=timing.PeriodScMax=flight?FlightPeriod:Period;timing.Phase01=timing.Phase01Max=0;
                clothBody.DynaShape=null;clothBody.StaticShape=null;clothBody.LocAnim=null;if (clothBody.References.LocAnim.Descriptor is not null) clothBody.References.LocAnim.Clear();
                Bake(clothBody.Mesh!,banner,amplitude,flight,moving);
                expectedClothHashes=clothBody.Mesh!.Visuals!.Select(NodeHash).ToArray();
                prefab.Ents=flight?[planeEntry,clothEntry,sourceEntries[1]]:[planeEntry,clothEntry];
            }
            doc.Node.Name="TMIS Skyward Banner "+mode;
            Require(doc.Node.Ident?.Id=="","Flag seed stored ID must be empty");
            var bytes=Save(doc);
            var parsed=Parse(bytes);
            var p=FlagPrefab(parsed.Node);
            Require(p.Ents!.Length==(mode=="OriginalStatic"?1:flight?3:2),"Entity count did not survive");
            var parsedPlane=p.Ents[0].Model is CPlugStaticObjectModel st?st.Mesh!:((CPlugDynaObjectModel)p.Ents[0].Model!).Mesh!;
            Require(NodeHash(parsedPlane)==expectedPlaneHash,"Complete rigid mesh changed during serialization");
            if(!flight)
            {
                var selectedSource=sourceVisualHashes.Where((_,i)=>mode=="OriginalStatic"||i!=BannerIndex);
                Require(parsedPlane.Visuals!.Select(NodeHash).SequenceEqual(selectedSource),"Retained source visuals changed");
            }
            else
            {
                VerifyRigid(sourceMesh,parsedPlane);
                Require(NodeHash((CPlugDynaObjectModel)p.Ents[0].Model!)==expectedPlaneBodyHash,"Rigid body or normalized collision changed during serialization");
                Require(NodeHash((KC)p.Ents[2].Model!)==expectedConstraintHash,"Native flight constraint changed during serialization");
                var timing=(NPlugDynaObjectModel_SInstanceParams)p.Ents[0].Params!;
                Require(timing.IsKinematic&&timing.PeriodSc==1&&timing.PeriodScMax==1&&timing.Phase01==0&&timing.Phase01Max==0,"Rigid native clock fields changed");
            }
            if(mode=="OriginalStatic")Require(expectedPlaneHash==planeMeshSourceHash,"Original reference mesh changed");
            else
            {
                var cloth=((CPlugDynaObjectModel)p.Ents[1].Model!).Mesh!;
                Require(cloth.Visuals!.Select(NodeHash).SequenceEqual(expectedClothHashes),"Complete cloth visual serialization changed");
                Verify(cloth,banner,amplitude,flight,moving);
                Require(p.Ents[1].Position==default&&p.Ents[1].Rotation==Identity,"Attachment transform changed");
            }
            Require(p.Ents.Count(e=>e.Model is KC)==(flight?1:0),"Unexpected flight constraint count");
            var name="TMIS_Skyward_"+mode+".Item.Gbx";
            outputs[name]=bytes;
            File.WriteAllBytes(Path.Combine(output,"files",name),bytes);
            var mapFile="TMIS_Skyward_"+mode+".Map.Gbx";
            var mapOutput=Map(mapBytes,bytes,name,dependencies,folder,mode,flight);
            File.WriteAllBytes(Path.Combine(output,mapFile),mapOutput);
            var embeddedFiles=dependencies.Append(new(name,bytes)).Select(f=>new {zipPath="Items/"+folder+"/"+f.Key,bytes=f.Value.Length,sha256=Hash(f.Value)}).ToArray();
            var sidecar=new {manifestVersion=1,operation="offline-skyward-banner-control",scope="Offline fixture; no native rendering verdict",outputMap=Path.Combine(output,mapFile),outputSha256=Hash(mapOutput),embeddedFolder=folder,embeddedFiles,expectedEmbeddedItemIds=new[]{folder+"\\"+name},nativePrimerPresent=true,nativePrimerIndex=0,nativeFlagEmbedded=false,anchoredObjectCount=2};
            File.WriteAllText(Path.Combine(output,mapFile+".manifest.json"),JsonSerializer.Serialize(sidecar,Json));
            entries.Add(new {mode,itemFile=name,itemSha256=Hash(bytes),itemBytes=bytes.Length,mapFile,mapSha256=Hash(mapOutput),anchorPosition=flight?FlightAnchorPosition:AnchorPosition,yawPitchRoll=new Vec3(0,0,0),worldPlaneCenterAtPhase0=new Vec3(768,128,450),expectedPlaneHash,expectedPlaneBodyHash,expectedConstraintHash,bannerAnimated=amplitude>0,amplitude,periodSeconds=flight?FlightPeriod:Period,rotationDegrees=moving?360:0,waveCycles=flight?5:1,rigidPeriodScale=flight?(float?)1:null,material=mode=="OriginalStatic"?"Original native ItemSpectator":"Native ItemFlag through accepted v6 early remap; source appearance not guaranteed",sourceUv0Preserved=!contrast,uv1Preserved=!oneUv,sourceMeshVisualSerializationsPreserved=!flight,sourceRigidMaterialsPreserved=true,sourceRigidPositionsShiftedX=flight?FlightShiftX:0});
        }
        Require(File.ReadAllBytes(originalPath).AsSpan().SequenceEqual(originalBytes),"Original Item changed");
        Require(File.ReadAllBytes(seedPath).AsSpan().SequenceEqual(seedBytes),"Flag seed changed");
        Require(File.ReadAllBytes(mapPath).AsSpan().SequenceEqual(mapBytes),"Source map changed");
        var manifest=new {
            manifestVersion=2,operation=flight?"skyward-normalized-flight-banner-clock-controls":"skyward-static-banner-vertex-tween-controls",
            scope="Offline authoring/serialization only; no native loading, animation, texture or restart verdict",
            originalPath,originalSha256=PlaneHash,flagSeedPath=seedPath,flagSeedSha256=SeedHash,mapSource=mapPath,mapSourceSha256=Hash(mapBytes),folder,
            options=new {contrast,flight,oneUv},
            sourceBanner=new {visualIndex=BannerIndex,vertices=banner.Positions.Length,triangles=banner.Indices.Length/3,
                uv0Preserved=!contrast,uv1Preserved=!oneUv,
                uvChannels=banner.Uvs.Select(u=>new {slot=u.Key,count=u.Value.Length,minU=u.Value.Min(v=>v.X),maxU=u.Value.Max(v=>v.X),minV=u.Value.Min(v=>v.Y),maxV=u.Value.Max(v=>v.Y),distinct=u.Value.Distinct().Count()}),
                streamProperties=banner.StreamProperties,sourceNormLengthMin=banner.Normals.Min(Length),sourceNormLengthMax=banner.Normals.Max(Length),sourceGeometrySha256=banner.GeometryHash,
                leadingEdgeZ=banner.Positions.Max(p=>p.Z),trailingEdgeZ=banner.Positions.Min(p=>p.Z),fixedLeadingBandMetres=FixedBand},
            animation=new {frames=Frames,lods=5,periodSeconds=flight?FlightPeriod:Period,waveCycles=flight?5:1,amplitudeMetres=Amplitude,wavelengthMetres=Wavelength,
                displacement="X += A*u^2*sin(2*pi*d/wavelength - 2*pi*cycles*frame/85); d=max(0,maxZ-Z-fixedBand); u=d/(maxZ-minZ-fixedBand)",
                normals="Inverse-transpose Jacobian, normalized for deformed vertices. Unchanged positions/normals are copied exactly at fixed edge and zero amplitude. Flight rotates normals after deformation.",
                allOriginalTrianglesRetained=true,bothFrontBackComponentsRetained=true,sameTopologyEveryLod=true,originalFlightRetained=false,clothIsKinematic=false,rigidIsKinematic=flight,
                collision=flight?"Original rigid collision vertices normalized by X+=115; no cloth collision":"None in test assembly",
                normalizationX=flight?FlightShiftX:0,requestedRigidPeriodScale=flight?(float?)1:null,requestedPhase=0,
                rigidTimeline=flight?"One linear 20,000ms segment; Y0->360 for Circle, Y0->0 for Stationary; translation0; no original150/160s channels":null},
            dependencyHashes=DependencyHashes,entries,
            limitations=new[]{
                "Whole source visual9 is retained, including both apparent front/back components and geometric SKYWARD lettering.",
                "Banner uses native ItemFlag material instead of source ItemSpectator; original blue/white appearance is not established. UV changes are explicit in options/sourceBanner/entries.",
                flight?"All rigid material nodes/normals/UVs/topology are preserved. Rigid positions, bounds and source collision vertices receive X+=115; native source flight is replaced.":"Remaining12rigid visuals and all13material nodes remain exact; source collision/constraints and original150/160-second flight are absent.",
                "Wave is deterministic baked vertex animation; no wind physics. Native clock agreement between kinematic rigid body and ordinary cloth is unverified.",
                "Source vertices are already folded/nonplanar. Additional wave is applied to those rest positions.",
                "Five LODs duplicate the full2576-vertex banner. Distant performance/LOD behavior is unverified.",
                "A genuine native Flag16m anchor remains first. No single-Item or native-free initialization claim.",
                "All maps use the prior editor fixture template and are not validated race maps."}};
        File.WriteAllText(Path.Combine(output,"manifest.json"),JsonSerializer.Serialize(manifest,Json));
        File.WriteAllText(Path.Combine(output,"README.md"),(flight?FlightReport():Report())+$"\n\nContrast UV mapping applied: {contrast}. Flight control applied: {flight}. Single UV layout control applied: {oneUv}.\n"+(contrast?"UV0 is deliberately replaced: source V>0.8 maps to native Flag white UV(0.5,0.5); all other source UV0 maps to native Flag green UV(0.5,0.1). These are white/green contrast controls, not original blue/white appearance.\n":"")+(oneUv?"UV1 is deliberately removed in this control; only UV0 is serialized, matching the proven native flag layout. Source normals, positions, indices and complete triangle coverage are unchanged beyond the requested wave/flight transforms.\n":"UV1 remains exact.\n"));
        Console.WriteLine(JsonSerializer.Serialize(new {output,folder,anchor=flight?FlightAnchorPosition:AnchorPosition,focusPlane=new Vec3(768,129,450),focusBanner=new Vec3(767.5f,129,432),flightCircleCenter=flight?FlightAnchorPosition:(Vec3?)null,entries,sourceUnchanged=true,offlineChecksPassed=true,runtimeTested=false},Json));
    }

    sealed record Banner(Vec3[] Positions,Vec3[] Normals,int[] Indices,Dictionary<int,Vec2[]> Uvs,string GeometryHash,object[] StreamProperties,float MinimumZ,float MaximumZ);
    static KC.AnimFunc FlightTimeline()=>new() {IsDuration=true,SubFuncs=[new(){Ease=KC.AnimEase.Linear,Reverse=false,Duration=new TimeInt32((int)(FlightPeriod*1000))}]};
    static Vec3 Shift(Vec3 p)=>new(p.X+FlightShiftX,p.Y,p.Z);
    static Vec3 Yaw(Vec3 p,int frame)
    {
        if(frame is 0 or Frames-1)return p;
        var angle=2*Math.PI*frame/(Frames-1);var c=Math.Cos(angle);var s=Math.Sin(angle);
        return new((float)(c*p.X+s*p.Z),p.Y,(float)(-s*p.X+c*p.Z));
    }
    static (Vec3 position,Vec3 normal) Pose(Banner source,float amplitude,int frame,int vertex,bool flight,bool moving)
    {
        var (p,n)=Wave(source,amplitude,frame,vertex,flight?5:1);
        if(flight)p=Shift(p);
        return moving?(Yaw(p,frame),Yaw(n,frame)):(p,n);
    }
    static void ShiftRigid(CPlugDynaObjectModel body)
    {
        var mesh=body.Mesh!;
        Require(mesh.Boxes is null,"Unsupported rigid Solid2 boxes need explicit normalization");
        foreach(var v in mesh.Visuals!.Cast<CPlugVisualIndexedTriangles>())
        {
            Require(v.Vertices.Length==0&&v.VertexStreams.Count==1&&v.VertexStreams[0].Positions is {Length:>0},"Unexpected rigid source geometry");
            v.VertexStreams[0].Positions=v.VertexStreams[0].Positions!.Select(Shift).ToArray();
            var b=v.BoundingBox;v.BoundingBox=new(b.X+FlightShiftX,b.Y,b.Z,b.X2,b.Y2,b.Z2);
        }
        Require(body.References.StaticShape.File is null&&body.References.DynaShape.File is null,"Unsupported external source collision graph");
        foreach(var shape in new[]{body.StaticShape,body.DynaShape}.Where(s=>s is not null).Distinct())
        {
            Require(shape is {Surf:CPlugSurface.Mesh},"Expected inline source collision mesh");
            var collision=(CPlugSurface.Mesh)shape!.Surf!;
            collision.Vertices=collision.Vertices.Select(Shift).ToArray();
        }
    }
    static void VerifyRigid(CPlugSolid2Model source,CPlugSolid2Model target)
    {
        Require(target.Visuals!.Count==12&&target.CustomMaterials!.Length==source.CustomMaterials!.Length,"Rigid shape/material layout changed");
        Require(target.CustomMaterials.Select(m=>NodeHash(m.MaterialUserInst!)).SequenceEqual(source.CustomMaterials.Select(m=>NodeHash(m.MaterialUserInst!))),"Original rigid material serialization changed");
        var original=source.Visuals!.Where((_,i)=>i!=BannerIndex).Cast<CPlugVisualIndexedTriangles>().ToArray();
        for(var i=0;i<original.Length;i++)
        {
            var a=original[i];var b=(CPlugVisualIndexedTriangles)target.Visuals[i];
            var retained=b.VertexStreams.Single().Positions!;var bounds=b.BoundingBox;
            Require(retained.SequenceEqual(a.VertexStreams.Single().Positions!.Select(Shift)),"Rigid position normalization changed");
            b.VertexStreams[0].Positions=a.VertexStreams[0].Positions;b.BoundingBox=a.BoundingBox;
            Require(NodeHash(b)==NodeHash(a),"Rigid source visual changed beyond documented positions and bounds");
            b.VertexStreams[0].Positions=retained;b.BoundingBox=bounds;
        }
    }
    static string FlightReport()=>"""

        # Skyward normalized flight controls

        These are file-only research fixtures. Their construction/serialization checks do not prove native rendering, motion, clock agreement or portability. The original Item is untouched.

        Four maps isolate two variables: `StationaryZero`, `StationaryWave`, `CircleZero`, and `CircleWave`. They use the same source rigid Dyna body, its twelve remaining visuals and thirteen exact original material nodes. Complete visual 9 alone is reassigned to an ordinary nonkinematic flag host. Every retained rigid position and every original Dyna collision vertex receives X += 115 metres; normals, UVs, indices, material nodes, surface triangle data and source constraint pivots are retained. This changes the plane's approximate rotation radius from 135 to 20 metres. The complete banner is shifted identically before deformation/rotation. The original file is never modified.

        The source 150-second rotation and independent 160-second vertical travel are deliberately replaced. A single world-to-plane native constraint now has no translation and Y rotation 0 to 360 degrees over one linear 20,000 ms segment; stationary controls set the maximum angle to zero. Native rigid parameters request IsKinematic=true, PeriodSc=PeriodScMax=1 and Phase01=Phase01Max=0. A scale of 1 matches established native kinematic fixtures; it is deliberately distinct from the ordinary cloth's 20-second PeriodSc. Native clock semantics and agreement remain a runtime proof obligation.

        Banner frames bake the same authored rigid Y rotation, after five X-displacement wave cycles over each 20-second loop. The leading half-metre remains fixed relative to that rigid rotation. Both the native rigid body's and the cloth entity's rest transforms remain identity. There is no constraint or LocAnim on the cloth. All five cloth LODs contain the full 2,576-vertex/2,447-triangle visual and 86 animation frames; frame 85 is copied from frame 0 for an exact position/normal/UV loop endpoint. Linear vertex interpolation follows angular chords: at the banner's approximately 34-metre maximum radius, the theoretical midpoint sag between adjacent angle samples is about 2.4 centimetres. This is a sampling error, distinct from clock/phase drift.

        The flight item anchor and circle center are (788,128,450). At authored phase zero the plane center is near (768,129,450), and the banner near (767.5,129,432). Use a fixed overview camera focused on (788,130,450) for a complete orbit and a close camera for attachment/letter deformation. The source cloth's blue/white appearance is not preserved by the native ItemFlag material; contrast controls deliberately remap the source palette to white/green. Native screenshots and at least two full loops must establish rendering, motion, attachment, clock rate, orientation sign, seam, restart behavior and culling before any success claim. Generated maps retain a genuine native Flag16m initialization anchor and are editor fixtures, not validated race maps.
        """;
    static Banner ReadBanner(CPlugVisualIndexedTriangles v)
    {
        Require(v.Vertices.Length==0&&v.VertexStreams.Count==1&&v.SubVisuals is not {Length:>0},"Expected one unanimated source stream");
        var s=v.VertexStreams.Single();
        var p=s.Positions??throw new InvalidDataException("Missing source positions");
        var n=s.Normals??throw new InvalidDataException("Missing source normals");
        var ix=v.IndexBuffer?.Indices??throw new InvalidDataException("Missing source indices");
        Require(n.Length==p.Length&&p.All(Finite)&&n.All(Finite)&&n.All(x=>Length(x)>.5),"Invalid source position/normals");
        Require(ix.Length%3==0&&ix.All(i=>i>=0&&i<p.Length),"Invalid source indices");
        var uv=s.UVs.ToDictionary(x=>x.Key,x=>x.Value.ToArray());
        Require(uv.Count is >=1 and <=4&&uv.Keys.Order().SequenceEqual(Enumerable.Range(0,uv.Count))&&uv.Values.All(a=>a.Length==p.Length),"Unsupported UV layout");
        var props=s.GetType().GetProperties(BindingFlags.Public|BindingFlags.Instance).Select(x=>new {x.Name,type=x.PropertyType.Name,arrayLength=x.GetValue(s) is Array a?(int?)a.Length:null}).Cast<object>().ToArray();
        return new(p.ToArray(),n.ToArray(),ix.ToArray(),uv,NodeHash(v),props,p.Min(x=>x.Z),p.Max(x=>x.Z));
    }
    static void Bake(CPlugSolid2Model mesh,Banner source,float amplitude,bool flight=false,bool moving=false)
    {
        foreach(var v in mesh.Visuals!.Cast<CPlugVisualIndexedTriangles>())
        {
            var prototype=v.Vertices[0];var count=source.Positions.Length;
            var vertices=new CPlugVisual3D.Vertex[count*Frames];
            for(var frame=0;frame<Frames-1;frame++)for(var i=0;i<count;i++)
            {
                var (p,n)=Pose(source,amplitude,frame,i,flight,moving);
                vertices[frame*count+i]=prototype with {Position=p,Normal=n};
            }
            Array.Copy(vertices,0,vertices,(Frames-1)*count,count);
            v.Vertices=vertices;SetCount(v,vertices.Length);v.IndexBuffer!.Indices=source.Indices.ToArray();
            v.SubVisuals=Enumerable.Range(0,Frames).Select(f=>new Int3(f*count,0,source.Indices.Length)).ToArray();
            var texPrototype=v.TexCoords[0];
            v.TexCoords=source.Uvs.OrderBy(u=>u.Key).Select(u=>new CPlugVisual.TexCoordSet {Version=texPrototype.Version,Flags=texPrototype.Flags,U01=texPrototype.U01?.ToArray(),TexCoords=Enumerable.Range(0,Frames).SelectMany(_=>u.Value.Select(t=>new CPlugVisual.TexCoord(t,null,null))).ToArray()}).ToArray();
            v.BoundingBox=Bounds(vertices.Select(x=>x.Position));
        }
    }
    static (Vec3 position,Vec3 normal) Wave(Banner source,float amplitude,int frame,int i,int cycles=1)
    {
        var p=source.Positions[i];var n=source.Normals[i];
        var high=source.MaximumZ;var low=source.MinimumZ;
        var d=Math.Max(0,high-p.Z-FixedBand);var extent=high-low-FixedBand;
        if(d==0||amplitude==0)return(p,n);
        var u=d/extent;var k=2*Math.PI/Wavelength;var phase=k*d-2*Math.PI*cycles*frame/(Frames-1);
        var displacement=amplitude*u*u*Math.Sin(phase);
        var derivative=-amplitude*(2*u/extent*Math.Sin(phase)+u*u*k*Math.Cos(phase));
        var moved=new Vec3((float)(p.X+displacement),p.Y,p.Z);
        var adjusted=new Vec3(n.X,n.Y,(float)(n.Z-derivative*n.X));
        var length=Length(adjusted);adjusted=new(adjusted.X/(float)length,adjusted.Y/(float)length,adjusted.Z/(float)length);
        return(moved,adjusted);
    }
    static void Verify(CPlugSolid2Model mesh,Banner source,float amplitude,bool flight=false,bool moving=false)
    {
        Require(mesh.Visuals is {Count:5},"Expected complete five LODs");
        var count=source.Positions.Length;
        foreach(var v in mesh.Visuals.Cast<CPlugVisualIndexedTriangles>())
        {
            Require(v.Vertices.Length==count*Frames&&v.SubVisuals is {Length:Frames}&&v.VertexStreams.Count==0,"Cloth archive changed");
            Require(v.IndexBuffer!.Indices.SequenceEqual(source.Indices),"Source triangles changed");
            Require(v.TexCoords.Length==source.Uvs.Count,"UV channel count changed");
            for(var frame=0;frame<Frames;frame++)
            {
                Require(v.SubVisuals[frame]==new Int3(frame*count,0,source.Indices.Length),"Frame table changed");
                for(var i=0;i<count;i++)
                {
                    var (p,n)=Pose(source,amplitude,frame==Frames-1?0:frame,i,flight,moving);var actual=v.Vertices[frame*count+i];
                    Require(actual.Position==p&&actual.Normal==n,"Position/normal changed");
                    Require(Inside(v.BoundingBox,p),"Bounds do not cover animation");
                    foreach(var u in source.Uvs)Require(v.TexCoords[u.Key].TexCoords[frame*count+i].UV==u.Value[i],"Source UV changed");
                }
            }
            for(var i=0;i<count;i++)Require(v.Vertices[i]==v.Vertices[(Frames-1)*count+i],"Loop endpoint changed");
        }
    }
    static byte[] Map(byte[] template,byte[] item,string itemName,Dictionary<string,byte[]> dependencies,string folder,string mode,bool flight=false)
    {
        var doc=Gbx.Parse<CGameCtnChallenge>(new MemoryStream(template),new GbxReadSettings {SkipChunkIds=[0x0304305B]});var map=doc.Node;
        var native=map.AnchoredObjects!.Single(a=>a.ItemModel?.Id=="Flag16m"&&a.ItemModel.Author=="Nadeo");
        map.AnchoredObjects.Clear();map.AnchoredObjects.Add(native);
        var identity=folder+"\\"+itemName;
        var itemDoc=Parse(item);
        var anchor=new CGameCtnAnchoredObject {ItemModel=new Ident(identity,itemDoc.Node.Ident!.Collection,itemDoc.Node.Ident.Author),AnchorTreeId=identity,AbsolutePositionInMap=flight?FlightAnchorPosition:AnchorPosition,YawPitchRoll=default,PivotPosition=default,Scale=1};
        anchor.CreateChunk<CGameCtnAnchoredObject.Chunk03101002>().Version=8;anchor.CreateChunk<CGameCtnAnchoredObject.Chunk03101004>().Version=0;
        map.AnchoredObjects.Add(anchor);map.MapName="TMIS Skyward Banner "+mode;map.MapUid="TMISSKY_"+Guid.NewGuid().ToString("N")[..18];
        map.EmbeddedZipData=null;map.UpdateEmbeddedZipData(zip=>{foreach(var pair in dependencies.Append(new(itemName,item))){using var stream=zip.CreateEntry("Items/"+folder+"/"+pair.Key,CompressionLevel.Optimal).Open();stream.Write(pair.Value);}});
        using var output=new MemoryStream();doc.Save(output);var bytes=output.ToArray();
        var check=Gbx.Parse<CGameCtnChallenge>(new MemoryStream(bytes),new GbxReadSettings {SkipChunkIds=[0x0304305B]}).Node;
        Require(check.AnchoredObjects!.Count==2&&check.AnchoredObjects[0].ItemModel?.Id=="Flag16m"&&check.AnchoredObjects[1].ItemModel?.Id==identity,"Map anchors changed");
        Require(check.ExpectedEmbeddedItemModels!.Select(i=>i.Id).SequenceEqual(new[]{identity}),"Expected Item identity changed");
        using var zip=check.OpenReadEmbeddedZipData();Require(zip.Entries.Count==4,"Embedded count changed");
        foreach(var pair in dependencies.Append(new(itemName,item))){using var stream=zip.GetEntry("Items/"+folder+"/"+pair.Key)!.Open();using var memory=new MemoryStream();stream.CopyTo(memory);Require(memory.ToArray().AsSpan().SequenceEqual(pair.Value),"Embedded payload changed");}
        return bytes;
    }
    static string Report()=>"""
        # Skyward banner static controls

        These are file-only research fixtures, not an in-game success report. The original Item remains untouched.

        `OriginalStatic` preserves the complete original rigid source mesh and materials, freezes its flight and removes collision. `RemapZero` removes only complete source visual 9 from that rigid mesh and renders its exact 2,576 source vertices and 2,447 triangles through the accepted nonkinematic flag host, with zero extra deformation. `RemapWave` adds a deterministic X-displacement travelling along rest Z while the leading half-metre remains fixed. The source is already folded. Both apparent front/back components and its geometric SKYWARD lettering remain present.

        Source banner indices and source normals/positions in the zero-wave control are exact. UV changes, if requested, are explicitly listed below and in the manifest options. Remapped controls use the native ItemFlag material via the three pinned v6 sidecars, replacing ItemSpectator; identical appearance is not established. All 12 remaining plane visuals retain byte-exact source serializations and original material mappings. The static fixture uses no source collision, original flight constraints or LocAnim. All five banner LODs repeat the complete source topology.

        Each map contains one genuine native Flag16m first and one embedded complete assembly, plus the three v6 dependency archives. The custom anchor is `(903,128,450)`, identity yaw/pitch/roll. The rigid plane center is near `(768,129,450)` and the banner near `(767.5,129,432)`. No files are staged in the game Items directory.

        Required native sequence: original-static reference, remapped zero-wave control, remapped wave control; fixed-camera clips showing the letter silhouette and fixed leading attachment. If the zero-wave control differs visibly, separate material appearance from animation. Then test the wave in a fresh process and embedded-only context before any portability claim. Culling, LODs, source text appearance, normal shading, loop continuity and self-intersection require native observation. No game success is claimed by this generator.
        """;
    static Gbx<CGameItemModel> Parse(byte[] b)=>Gbx.Parse<CGameItemModel>(new MemoryStream(b));
    static CPlugPrefab FlagPrefab(CGameItemModel item)=>(CPlugPrefab)((NPlugItem_SVariantList)item.EntityModel!).Variants![0].EntityModel!;
    static byte[] Save(Gbx<CGameItemModel> d){using var m=new MemoryStream();new RefDocument(d.Node,d.RefTable!).Save(m);return m.ToArray();}
    static string NodeHash<T>(T node)where T:CMwNod{using var m=new MemoryStream();new Gbx<T>(node).Save(m);return Hash(m.ToArray());}
    static string Hash(byte[] b)=>Convert.ToHexString(SHA256.HashData(b));
    static bool Finite(Vec3 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z);
    static double Length(Vec3 v)=>Math.Sqrt((double)v.X*v.X+(double)v.Y*v.Y+(double)v.Z*v.Z);
    static BoxAligned Bounds(IEnumerable<Vec3> source){var p=source.ToArray();var a=new Vec3(p.Min(x=>x.X),p.Min(x=>x.Y),p.Min(x=>x.Z));var b=new Vec3(p.Max(x=>x.X),p.Max(x=>x.Y),p.Max(x=>x.Z));return new((a.X+b.X)/2,(a.Y+b.Y)/2,(a.Z+b.Z)/2,(b.X-a.X)/2+.0001f,(b.Y-a.Y)/2+.0001f,(b.Z-a.Z)/2+.0001f);}
    static bool Inside(BoxAligned b,Vec3 p)=>Math.Abs(p.X-b.X)<=b.X2&&Math.Abs(p.Y-b.Y)<=b.Y2&&Math.Abs(p.Z-b.Z)<=b.Z2;
    static void SetCount(CPlugVisual v,int count)=>typeof(CPlugVisual).GetProperty("Count",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(v,count);
    static void Require(bool condition,string message){if(!condition)throw new InvalidDataException(message);}
    static string Root(){for(var d=new DirectoryInfo(Environment.CurrentDirectory);d is not null;d=d.Parent)if(File.Exists(Path.Combine(d.FullName,"TM-Item-Studio.csproj")))return d.FullName;throw new DirectoryNotFoundException("Run within repository");}
    sealed class RefDocument:Gbx<CGameItemModel>{public RefDocument(CGameItemModel n,GbxRefTable t):base(n)=>RefTable=t;}
}
