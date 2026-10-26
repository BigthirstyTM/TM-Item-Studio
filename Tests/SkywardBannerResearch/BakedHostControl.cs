using System.Security.Cryptography;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;

static partial class Research
{
    // Research alternative: all render geometry shares one ordinary VertexTween
    // instance. This deliberately replaces source materials and native physics.
    public static void GenerateBakedHostControl(string output)
    {
        output=Path.GetFullPath(output);
        Require(!Directory.Exists(output)&&!File.Exists(output),"Preserve evidence: choose a new directory");
        var root=Root();
        var sourcePath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Trackmania/Items/BF2_ASSETS/Skyward_DoubleDecker_Plane/PromoPlane_Skyward.Item.Gbx");
        var seedFolder=Path.Combine(root,"artifacts/skyward-banner-20261023/material-candidates-native-warpflag-oneuv-v1");
        var sourceBytes=File.ReadAllBytes(sourcePath);
        Require(Hash(sourceBytes)==PlaneHash,"Original source hash changed");
        var source=Parse(sourceBytes);
        var sourceMesh=((CPlugDynaObjectModel)((CPlugPrefab)source.Node.EntityModel!).Ents![0].Model!).Mesh!;
        Require(sourceMesh.Visuals is {Length:13},"Original visual count changed");
        var parts=sourceMesh.Visuals!.Cast<CPlugVisualIndexedTriangles>().Select(ReadBanner).ToArray();
        var offsets=new int[parts.Length];
        for(var i=1;i<parts.Length;i++)offsets[i]=offsets[i-1]+parts[i-1].Positions.Length;
        var count=parts.Sum(p=>p.Positions.Length);
        Require(count<=32768,"Bounded combined geometry exceeded");
        var indices=parts.SelectMany((p,i)=>p.Indices.Select(v=>v+offsets[i])).ToArray();
        var uv=parts.SelectMany((p,i)=>Enumerable.Range(0,p.Positions.Length).Select(v=>i==BannerIndex&&p.Uvs[0][v].Y<=.8f?new Vec2(.5f,.1f):new Vec2(.25f,.5f))).ToArray();
        var seedBytes=File.ReadAllBytes(Path.Combine(seedFolder,"files/TMIS_Skyward_NativeWarpFlag.Item.Gbx"));
        var mapBytes=File.ReadAllBytes(Path.Combine(seedFolder,"TMIS_Skyward_NativeWarpFlag.Map.Gbx"));
        Require(Hash(mapBytes)=="02703C4CE9A8A691D9F321D846A4A9F360C50CDBED4402E268FB2D2CBD174BB3","Bright seed map changed");
        var dependencies=DependencyHashes.Keys.ToDictionary(k=>k,k=>File.ReadAllBytes(Path.Combine(seedFolder,"files",k)));
        var folder="TMIS_SkywardBaked_"+Guid.NewGuid().ToString("N")[..16];
        Directory.CreateDirectory(output);Directory.CreateDirectory(Path.Combine(output,"files"));
        foreach(var pair in dependencies)File.WriteAllBytes(Path.Combine(output,"files",pair.Key),pair.Value);
        var rows=new List<object>();
        foreach(var amplitude in new[]{0f,Amplitude})
        {
            var label=amplitude==0?"BakedWholeZero":"BakedWholeWave";
            var doc=Parse(seedBytes);var prefab=FlagPrefab(doc.Node);
            var cloth=prefab.Ents![1];var body=(CPlugDynaObjectModel)cloth.Model!;
            cloth.Position=default;cloth.Rotation=Identity;
            body.DynaShape=null;body.StaticShape=null;body.DynaShapeFile=null;body.StaticShapeFile=null;body.LocAnim=null;body.LocAnimFile=null;
            var timing=(NPlugDynaObjectModel_SInstanceParams)cloth.Params!;
            timing.IsKinematic=false;timing.PeriodSc=timing.PeriodScMax=FlightPeriod;timing.Phase01=timing.Phase01Max=0;
            prefab.Ents=[cloth];doc.Node.Name="TMIS Skyward "+label;
            var mesh=body.Mesh!;
            // Every research LOD uses the same full geometry. Share its node
            // instead of serializing the same large frame table five times.
            var sharedVisual=mesh.Visuals![0];
            mesh.Visuals=Enumerable.Repeat(sharedVisual,5).ToArray();
            foreach(var visual in mesh.Visuals.Distinct().Cast<CPlugVisualIndexedTriangles>())
            {
                var prototype=visual.Vertices[0];var vertices=new CPlugVisual3D.Vertex[count*Frames];
                for(var f=0;f<Frames;f++)for(var p=0;p<parts.Length;p++)for(var v=0;v<parts[p].Positions.Length;v++)
                {
                    var pose=BakedWholePose(parts[p],p==BannerIndex?amplitude:0,f,v);
                    vertices[f*count+offsets[p]+v]=prototype with {Position=pose.position,Normal=pose.normal};
                }
                visual.Vertices=vertices;SetCount(visual,vertices.Length);
                visual.IndexBuffer!.Indices=indices.ToArray();
                visual.SubVisuals=Enumerable.Range(0,Frames).Select(f=>new Int3(f*count,0,indices.Length)).ToArray();
                var tex=visual.TexCoords[0];
                visual.TexCoords=[new CPlugVisual.TexCoordSet{Version=tex.Version,Flags=tex.Flags,U01=tex.U01?.ToArray(),TexCoords=Enumerable.Range(0,Frames).SelectMany(_=>uv.Select(v=>new CPlugVisual.TexCoord(v,null,null))).ToArray()}];
                visual.BoundingBox=Bounds(vertices.Select(v=>v.Position));
            }
            var bytes=Save(doc);var parsed=Parse(bytes);var checkPrefab=FlagPrefab(parsed.Node);
            Require(checkPrefab.Ents!.Length==1,"Unexpected native motion host");
            var checkedMesh=((CPlugDynaObjectModel)checkPrefab.Ents[0].Model!).Mesh!;
            Require(checkedMesh.Visuals!.Length==5,"Five LODs required");
            Require(checkedMesh.Visuals!.All(v=>ReferenceEquals(v,checkedMesh.Visuals[0])),"Shared LOD node identity lost");
            foreach(var visual in checkedMesh.Visuals!.Cast<CPlugVisualIndexedTriangles>())
            {
                Require(visual.Vertices.Length==count*Frames&&visual.SubVisuals!.Length==Frames&&visual.TexCoords.Length==1&&visual.VertexStreams.Count==0,"Animated layout changed");
                Require(visual.IndexBuffer!.Indices.SequenceEqual(indices),"Combined topology changed");
                for(var f=0;f<Frames;f++)
                {
                    Require(visual.SubVisuals[f]==new Int3(f*count,0,indices.Length),"Frame descriptor changed");
                    for(var p=0;p<parts.Length;p++)for(var v=0;v<parts[p].Positions.Length;v++)
                    {
                        var pose=BakedWholePose(parts[p],p==BannerIndex?amplitude:0,f,v);var index=f*count+offsets[p]+v;
                        Require(visual.Vertices[index].Position==pose.position&&visual.Vertices[index].Normal==pose.normal,"Pose round-trip changed");
                        Require(visual.TexCoords[0].TexCoords[index].UV==uv[offsets[p]+v]&&Inside(visual.BoundingBox,pose.position),"UV/bounds changed");
                    }
                }
                Require(visual.Vertices[0].Position!=visual.Vertices[(Frames-1)*count].Position,"Last cyclic pose must be distinct");
            }
            var itemName="TMIS_Skyward_"+label+".Item.Gbx";File.WriteAllBytes(Path.Combine(output,"files",itemName),bytes);
            var mapName=itemName.Replace(".Item.Gbx",".Map.Gbx",StringComparison.Ordinal);
            var map=Map(mapBytes,bytes,itemName,dependencies,folder,label,true);File.WriteAllBytes(Path.Combine(output,mapName),map);
            var embeddedFiles=dependencies.Append(new(itemName,bytes)).Select(x=>new{zipPath="Items/"+folder+"/"+x.Key,bytes=x.Value.Length,sha256=Hash(x.Value)}).ToArray();
            File.WriteAllText(Path.Combine(output,mapName+".manifest.json"),JsonSerializer.Serialize(new{manifestVersion=1,operation="offline-single-vertex-animation-flight-control",scope="No native success claimed",outputMap=Path.Combine(output,mapName),outputSha256=Hash(map),embeddedFolder=folder,embeddedFiles,expectedEmbeddedItemIds=new[]{folder+"\\"+itemName},nativePrimerPresent=true,nativePrimerIndex=0,nativeFlagEmbedded=false,anchoredObjectCount=2},Json));
            rows.Add(new{label,amplitude,itemName,itemSha256=Hash(bytes),itemBytes=bytes.Length,mapName,mapSha256=Hash(map),mapBytes=map.Length});
        }
        Require(File.ReadAllBytes(sourcePath).AsSpan().SequenceEqual(sourceBytes),"Original source changed");
        var manifest=new{operation="offline-baked-whole-aircraft-control",sourcePath,sourceSha256=PlaneHash,seedSha256=Hash(seedBytes),folder,verticesPerFrame=count,triangles=indices.Length/3,parts=parts.Select((p,i)=>new{sourceVisual=i,firstVertex=offsets[i],vertices=p.Positions.Length,triangles=p.Indices.Length/3,sourceVisualSha256=p.GeometryHash}),frames=Frames,lods=5,phase="frame/86, 86 distinct cyclic samples",periodSeconds=FlightPeriod,anchor=FlightAnchorPosition,originalMaterialsPreserved=false,rigidPalette="constant white native WarpFlag sample",bannerPalette="native blue background and white geometric letters",nativeKinematics=false,collision=false,sourceUnchanged=true,offlineChecksPassed=true,runtimeTested=false,rows};
        File.WriteAllText(Path.Combine(output,"manifest.json"),JsonSerializer.Serialize(manifest,Json));
        File.WriteAllText(Path.Combine(output,"README.md"),"# Single-clock baked aircraft control\n\nAll thirteen original source visuals, including the complete double-sided SKYWARD banner and rigid tow geometry, are concatenated into one ordinary VertexTween host. Each of five LODs retains the full combined topology and 86 distinct cyclic samples (frame/86). The whole aircraft has a twenty-second requested Y orbit after X+=115m; only visual9 receives five local wave cycles per orbit, with the leading half-metre pinned. Original geometry positions/normals/indices feed the bake; original material assignments, texture UVs, native kinematics, collision and 150/160-second flight are deliberately replaced. All rigid surfaces sample a constant white native WarpFlag patch; the banner uses the existing blue/white lettering classification. This is a motion/attachment diagnostic, not preservation of original appearance or production flight behavior.\n\nBoth zero-wave and nonzero-wave maps embed one generated Item and three exact seed dependencies, and contain a genuine native Flag16m initializer first. No standalone single-Item, cold-load, recipient or game result is claimed by the offline checks.\n");
        Console.WriteLine(JsonSerializer.Serialize(manifest,Json));
    }

    static (Vec3 position,Vec3 normal) BakedWholePose(Banner source,float amplitude,int frame,int vertex)
    {
        var p=source.Positions[vertex];var n=source.Normals[vertex];
        var d=Math.Max(0,source.MaximumZ-p.Z-FixedBand);
        if(amplitude!=0&&d>0)
        {
            var extent=source.MaximumZ-source.MinimumZ-FixedBand;var u=d/extent;var k=2*Math.PI/Wavelength;
            var angle=k*d-2*Math.PI*5*frame/Frames;
            p=new((float)(p.X+amplitude*u*u*Math.Sin(angle)),p.Y,p.Z);
            var derivative=-amplitude*(2*u/extent*Math.Sin(angle)+u*u*k*Math.Cos(angle));
            n=new(n.X,n.Y,(float)(n.Z-derivative*n.X));var length=Length(n);n=new(n.X/(float)length,n.Y/(float)length,n.Z/(float)length);
        }
        p=Shift(p);
        var yaw=2*Math.PI*frame/Frames;var c=Math.Cos(yaw);var s=Math.Sin(yaw);
        Vec3 Turn(Vec3 v)=>new((float)(c*v.X+s*v.Z),v.Y,(float)(-s*v.X+c*v.Z));
        return(Turn(p),Turn(n));
    }
}
