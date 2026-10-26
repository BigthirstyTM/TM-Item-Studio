using System.IO.Compression;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.Game;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.MwFoundations;
using GBX.NET.Engines.Plug;

static partial class Research
{
    // Split the proven one-clock bake back into one animated visual per source
    // material slot. This tests texture preservation without changing the
    // shared frame clock or rigid/banner geometry.
    public static void GenerateBakedMaterialControl(string output, bool moveAnchor = false)
    {
        output=Path.GetFullPath(output);
        Require(!Directory.Exists(output)&&!File.Exists(output),"Preserve evidence: choose a new directory");
        var baseDir=output+"-base";
        GenerateBakedHostControl(baseDir);
        var sourcePath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Trackmania/Items/BF2_ASSETS/Skyward_DoubleDecker_Plane/PromoPlane_Skyward.Item.Gbx");
        var source=Parse(File.ReadAllBytes(sourcePath));
        var sourceMesh=((CPlugDynaObjectModel)((CPlugPrefab)source.Node.EntityModel!).Ents![0].Model!).Mesh!;
        var parts=sourceMesh.Visuals!.Cast<CPlugVisualIndexedTriangles>().Select(ReadBanner).ToArray();
        var offsets=new int[parts.Length];for(var i=1;i<parts.Length;i++)offsets[i]=offsets[i-1]+parts[i-1].Positions.Length;
        var total=parts.Sum(p=>p.Positions.Length);
        var itemPath=Path.Combine(baseDir,"files","TMIS_Skyward_BakedWholeWave.Item.Gbx");
        var item=Parse(File.ReadAllBytes(itemPath));
        var prefab=FlagPrefab(item);var body=(CPlugDynaObjectModel)prefab.Ents![0].Model!;var mesh=body.Mesh!;
        var sourceVisual=(CPlugVisualIndexedTriangles)mesh.Visuals![0];var originals=new List<CPlugVisualIndexedTriangles>();
        for(var p=0;p<parts.Length;p++)
        {
            var v=CloneNode(sourceVisual);
            var n=parts[p].Positions.Length;var vertices=new CPlugVisual3D.Vertex[n*Frames];
            var sourceAll=sourceVisual.Vertices;
            for(var f=0;f<Frames;f++) Array.Copy(sourceAll,f*total+offsets[p],vertices,f*n,n);
            v.Vertices=vertices;SetCount(v,vertices.Length);
            v.IndexBuffer!.Indices=parts[p].Indices.ToArray();
            v.SubVisuals=Enumerable.Range(0,Frames).Select(f=>new Int3(f*n,0,parts[p].Indices.Length)).ToArray();
            var uvProto=sourceVisual.TexCoords[0];
            v.TexCoords=[new CPlugVisual.TexCoordSet{Version=uvProto.Version,Flags=uvProto.Flags,U01=uvProto.U01?.ToArray(),TexCoords=Enumerable.Range(0,Frames).SelectMany(_=>parts[p].Uvs[0].Select(t=>new CPlugVisual.TexCoord(t,null,null))).ToArray()}];
            v.BoundingBox=Bounds(vertices.Select(x=>x.Position));
            originals.Add(v);
        }
        mesh.Visuals=originals.Cast<CPlugVisual>().ToArray();
        var edge=parts[BannerIndex].Positions.Where(p=>p.Z>=parts[BannerIndex].MaximumZ-.1f).ToArray();
        Require(edge.Length>0,"Banner attachment edge could not be identified");
        var attachment=new Vec3(edge.Average(p=>p.X)+FlightShiftX,edge.Average(p=>p.Y),edge.Average(p=>p.Z));
        if(moveAnchor)
        {
            // Rebase the single animated entity at the real rack/cloth contact.
            // Subtracting the same constant from every frame preserves world
            // geometry while making the entity's animation anchor explicit.
            foreach(var visual in originals)
            {
                var rebased=visual.Vertices.Select(v=>v with {Position=new Vec3(v.Position.X-attachment.X,v.Position.Y-attachment.Y,v.Position.Z-attachment.Z)}).ToArray();
                visual.Vertices=rebased;SetCount(visual,rebased.Length);visual.BoundingBox=Bounds(rebased.Select(v=>v.Position));
            }
            prefab.Ents![0].Position=attachment;
        }
        mesh.CustomMaterials=sourceMesh.CustomMaterials;
        mesh.ShadedGeoms=sourceMesh.ShadedGeoms;
        var bytes=Save(item);
        var parsed=Parse(bytes);var pm=FlagPrefab(parsed);var checkedMesh=((CPlugDynaObjectModel)pm.Ents![0].Model!).Mesh!;
        Require(checkedMesh.Visuals!.Length==parts.Length&&checkedMesh.CustomMaterials!.Length==sourceMesh.CustomMaterials!.Length,"Material split layout did not survive");
        Require(pm.Ents![0].Position==(moveAnchor?attachment:default),"Animated entity anchor changed unexpectedly");
        Require(checkedMesh.ShadedGeoms!.Select(g=>g.VisualIndex).SequenceEqual(sourceMesh.ShadedGeoms!.Select(g=>g.VisualIndex)),"Source visual/material mapping changed");
        for(var p=0;p<parts.Length;p++)
        {
            var v=(CPlugVisualIndexedTriangles)checkedMesh.Visuals[p];var n=parts[p].Positions.Length;
            Require(v.Vertices.Length==n*Frames&&v.SubVisuals!.Length==Frames&&v.TexCoords.Length==1,"Split frame layout changed");
            Require(v.IndexBuffer!.Indices.SequenceEqual(parts[p].Indices),"Split topology changed");
            var expected=sourceVisual.Vertices[offsets[p]].Position;
            if(moveAnchor)expected=new Vec3(expected.X-attachment.X,expected.Y-attachment.Y,expected.Z-attachment.Z);
            Require(v.Vertices[0].Position==expected,"Split position mismatch");
        }
        Directory.CreateDirectory(output);Directory.CreateDirectory(Path.Combine(output,"files"));
        var newItemName=moveAnchor?"TMIS_Skyward_BakedMaterialAnchorWave.Item.Gbx":"TMIS_Skyward_BakedMaterialWave.Item.Gbx";File.WriteAllBytes(Path.Combine(output,"files",newItemName),bytes);
        var baseMap=Gbx.Parse<CGameCtnChallenge>(new MemoryStream(File.ReadAllBytes(Path.Combine(baseDir,"TMIS_Skyward_BakedWholeWave.Map.Gbx"))),new GbxReadSettings{SkipChunkIds=[0x0304305B]});
        var map=baseMap.Node;var folder="TMIS_SkywardBakedMaterial_"+Guid.NewGuid().ToString("N")[..16];var identity=folder+"\\"+newItemName;
        map.AnchoredObjects![1].ItemModel=new Ident(identity,item.Node.Ident!.Collection,item.Node.Ident.Author);map.AnchoredObjects[1].AnchorTreeId=identity;map.MapName="TMIS Skyward Baked Material Wave";map.MapUid="TMISSKYMAT_"+Guid.NewGuid().ToString("N")[..17];map.EmbeddedZipData=null;
        var depNames=new[]{"TMIS_MatRemap_Placeholder.Material.Gbx","TMIS_MatRemap.GameSkin.Gbx","TMIS_MatRemap.TerrainModifier.Gbx"};
        var depBytes=depNames.ToDictionary(n=>n,n=>File.ReadAllBytes(Path.Combine(baseDir,"files",n)));
        foreach(var pair in depBytes)File.WriteAllBytes(Path.Combine(output,"files",pair.Key),pair.Value);
        map.UpdateEmbeddedZipData(zip=>{foreach(var f in depBytes.Append(new KeyValuePair<string,byte[]>(newItemName,bytes))){using var s=zip.CreateEntry("Items/"+folder+"/"+f.Key,CompressionLevel.Optimal).Open();s.Write(f.Value);}});
        using var outputStream=new MemoryStream();baseMap.Save(outputStream);var mapBytes=outputStream.ToArray();
        var checkMap=Gbx.Parse<CGameCtnChallenge>(new MemoryStream(mapBytes),new GbxReadSettings{SkipChunkIds=[0x0304305B]}).Node;
        Require(checkMap.AnchoredObjects!.Count==2&&checkMap.AnchoredObjects[1].ItemModel!.Id==identity&&checkMap.ExpectedEmbeddedItemModels!.Single().Id==identity,"Material map identity changed");
        var mapName=(moveAnchor?"TMIS_Skyward_BakedMaterialAnchorWave":"TMIS_Skyward_BakedMaterialWave")+".Map.Gbx";File.WriteAllBytes(Path.Combine(output,mapName),mapBytes);
        var manifest=new{operation=moveAnchor?"offline-single-clock-original-material-anchor-control":"offline-single-clock-original-material-control",sourceSha256=PlaneHash,baseBake=Path.Combine(baseDir,"TMIS_Skyward_BakedWholeWave.Item.Gbx"),itemName=newItemName,itemSha256=Hash(bytes),mapName,mapSha256=Hash(mapBytes),folder,sourceVisualCount=parts.Length,sourceMaterialCount=sourceMesh.CustomMaterials!.Length,verticesPerFrame=total,frames=Frames,sharedClock=true,originalMaterialSlotsRestored=true,sourceUv1Dropped=true,nativeKinematics=false,collision=false,internalEntityAnchorMoved=moveAnchor,bannerAttachmentLocal=attachment,bannerAttachmentWorldAtPhase0=new Vec3(FlightAnchorPosition.X+attachment.X,FlightAnchorPosition.Y+attachment.Y,FlightAnchorPosition.Z+attachment.Z),worldGeometryPreservedByPivotRebase=moveAnchor,runtimeTested=false,offlineChecksPassed=true,limitation="Native material shader compatibility with VertexTween remains to be tested."};
        File.WriteAllText(Path.Combine(output,"manifest.json"),JsonSerializer.Serialize(manifest,new JsonSerializerOptions{WriteIndented=true}));
        File.WriteAllText(Path.Combine(output,"README.md"),(moveAnchor?"# Single-clock original-material anchor control\n\n":"# Single-clock original-material control\n\n")+"This fixture starts from the successful one-clock baked aircraft, then splits the combined 13,380-vertex frame stream back into 13 animated visual nodes. Each node keeps its source material slot and source material-user instance. All visuals still share the same ordinary VertexTween host, 86 distinct cyclic frames and 20-second plane orbit; visual 9 alone receives the five-cycle cloth deformation. UV1 is intentionally omitted because the proven VertexTween structured layout requires one UV channel. "+(moveAnchor?"The single animated entity is rebased at the measured rack contact, computed from the banner leading edge at source Z=-9.020492 (world phase-zero approximately (767.5,128,441)). Every frame vertex is translated by the same constant and the entity position receives that constant, so rendered world geometry is unchanged while the internal animation anchor is at the attachment. This is an explicit pivot experiment for later native kinematic work. ":"")+"This is a native compatibility experiment: it does not claim that the original non-VertexTween material shaders will consume animated frames.\n");
        Console.WriteLine(JsonSerializer.Serialize(manifest,new JsonSerializerOptions{WriteIndented=true}));
    }

    static T CloneNode<T>(T node) where T:CMwNod,new()
    { using var m=new MemoryStream();new Gbx<T>(node).Save(m);return Gbx.Parse<T>(new MemoryStream(m.ToArray())).Node; }
}
