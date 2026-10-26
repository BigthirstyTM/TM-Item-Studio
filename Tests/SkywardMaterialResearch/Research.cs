using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.Game;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.MwFoundations;
using GBX.NET.Engines.Plug;

// File-only research: no game staging, UI, process operations or source mutation.
static class Research
{
 const string Native="C:/Users/PC/OpenplanetNext/Extract/GameData/Stadium/Media/Material/ItemFlag.Material.Gbx";
 const string Placeholder="TMIS_MatRemap_Placeholder.Material.Gbx",Skin="TMIS_MatRemap.GameSkin.Gbx",Modifier="TMIS_MatRemap.TerrainModifier.Gbx";
 const uint Facade=0xFACADE01;
 static readonly JsonSerializerOptions Json=new(){WriteIndented=true,IncludeFields=true};
 static readonly GbxReadSettings MapSettings=new(){SkipChunkIds=[0x0304305B]};
 public static void Generate(string output,bool explicitFlip0=false,string? sourceMapPath=null,string? sourceItemPath=null,bool cloneOnly=false)
 {
  output=Path.GetFullPath(output);Require(!Directory.Exists(output)&&!File.Exists(output),"Choose new evidence directory");
  var sourceDir=Path.GetFullPath("artifacts/skyward-banner-20261023/static-controls-v1");
  sourceItemPath??=Path.Combine(sourceDir,"files/TMIS_Skyward_RemapWave.Item.Gbx");
  sourceMapPath??=Path.Combine(sourceDir,"TMIS_Skyward_RemapWave.Map.Gbx");
  var sourceItem=File.ReadAllBytes(sourceItemPath);
  var sourceMap=File.ReadAllBytes(sourceMapPath);
  var nativeBytes=File.ReadAllBytes(Native);
  var sourceDoc=Gbx.Parse<CGameItemModel>(new MemoryStream(sourceItem));
  var visualHashes=Cloth(sourceDoc.Node).Visuals!.Select(NodeHash).ToArray();
  var original=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Trackmania/Items/BF2_ASSETS/Skyward_DoubleDecker_Plane/PromoPlane_Skyward.Item.Gbx");
  var originalHash=Hash(File.ReadAllBytes(original));Require(originalHash=="7EEDD88D3417A59F08724F6755EE665ADC35CAF7CE2E46AEFC554C5443EE2FB6","Original changed");
  Directory.CreateDirectory(output);var rows=new List<object>();
  foreach(var mode in cloneOnly?new[]{"NativeClone"}:explicitFlip0?new[]{"PaletteNeutralFlip0"}:new[]{"NativeClone","PaletteKeepHue","PaletteNeutral","PaletteNeutralNoFlip"})
  {
   var folder="TMIS_SkywardMat_"+mode+"_"+Guid.NewGuid().ToString("N")[..10];
   var replacement="TMIS_Skyward_"+mode;
   var replacementFolder="MemoryTemp\\CurrentMap_EmbeddedFiles\\ContentLoaded\\Items\\"+folder+"\\";
   var material=Gbx.Parse<CPlugMaterial>(new MemoryStream(nativeBytes)).Node;
   var refs=new GbxRefTable{AncestorLevel=0};
   GbxRefTableFile R(string p,bool use)=>new(refs,1,use,"GameData\\"+p.TrimStart('\\'));
   material.ShaderFile=R(material.ShaderFile!.FilePath,false);
   foreach(var t in material.CustomMaterial!.Textures!)
   {
    var p=t.TextureFile!.FilePath;
    if(mode!="NativeClone"&&t.Name=="Diffuse")p="Stadium\\Media\\Texture\\ItemSpectator_D.Texture.gbx";
    if(mode!="NativeClone"&&t.Name=="RoughMetal")p="Stadium\\Media\\Texture\\ItemSpectator_R.Texture.gbx";
    t.TextureFile=R(p,true);
   }
   var c=material.Chunks.OfType<CPlugMaterial.Chunk09079015>().Single();
   for(var i=0;i<c.U03!.Length;i++){var e=c.U03[i];c.U03[i]=new(e.Node,e.File is null?null:R(e.File.FilePath,e.File.UseFile));}
   if(mode.StartsWith("PaletteNeutral"))
   {
    material.CustomMaterial.Textures=material.CustomMaterial.Textures!.Where(t=>t.Name!="BaseColorHueMask").ToArray();
    material.CustomMaterial.GpuFxs1=[];c.U03=[];
   }
   if(mode=="PaletteNeutralNoFlip")material.CustomMaterial.GpuFxs2=[];
   if(mode=="PaletteNeutralFlip0")material.CustomMaterial.GpuFxs2!.Single(f=>f.U01=="BackSide_FlipU").U03![0][0]=0;
   var materialBytes=Save(new RefDoc<CPlugMaterial>(material,refs));
   var checkMaterial=Gbx.Parse<CPlugMaterial>(new MemoryStream(materialBytes));
   Require(checkMaterial.Node.ShaderFile!.FilePath=="GameData\\Techno3\\Media\\Material\\Tech3_Warp_TDiffSpec_VertexTween.Material.gbx","VertexTween shader lost");
   Require(checkMaterial.RefTable!.AncestorLevel==0&&checkMaterial.RefTable.Files.All(f=>f.FilePath.StartsWith("GameData\\")),"Material refs not rooted");
   var files=new Dictionary<string,byte[]>{
    [Placeholder]=File.ReadAllBytes(Path.Combine(sourceDir,"files",Placeholder)),
    [replacement+".Material.Gbx"]=materialBytes,
    [Skin]=NativeArchive(0x090F4000,Placeholder,true,w=>{w.Write(0x090F4004u);w.Write((byte)3);S(w,"");S(w,"");S(w,"");w.Write((byte)1);w.Write(0x09079000u);S(w,replacement);w.Write(1);w.Write(1);w.Write(0);w.Write(Facade);}),
    [Modifier]=NativeArchive(0x0915D000,Skin,false,w=>{w.Write(0x0915D000u);w.Write(1);S(w,replacementFolder);w.Write(0x0915D001u);w.Write(3);w.Write(-1);w.Write(Facade);})};
   var item=Gbx.Parse<CGameItemModel>(new MemoryStream(sourceItem));item.Node.Name="TMIS Skyward Material "+mode;
   var itemBytes=Save(new RefDoc<CGameItemModel>(item.Node,item.RefTable!));var itemName="TMIS_Skyward_"+mode+".Item.Gbx";files[itemName]=itemBytes;
   Require(Cloth(Gbx.Parse<CGameItemModel>(new MemoryStream(itemBytes)).Node).Visuals!.Select(NodeHash).SequenceEqual(visualHashes),"Animation changed");
   var skinCheck=Gbx.Parse<CPlugGameSkin>(new MemoryStream(files[Skin]));Require(skinCheck.Node.CustomizableFids!.Single().Name==replacement&&skinCheck.RefTable!.Files.Single().UseFile,"Skin matching changed");
   var mapDoc=Gbx.Parse<CGameCtnChallenge>(new MemoryStream(sourceMap),MapSettings);var map=mapDoc.Node;
   Require(map.AnchoredObjects!.Count==2&&map.AnchoredObjects[0].ItemModel!.Id=="Flag16m","Native primer absent");
   var anchor=map.AnchoredObjects[1];var identity=folder+"\\"+itemName;anchor.ItemModel=anchor.ItemModel! with{Id=identity};anchor.AnchorTreeId=identity;
   map.MapName="TMIS Skyward Material "+mode;map.MapUid="TMISSKYM_"+Guid.NewGuid().ToString("N")[..18];map.EmbeddedZipData=null;
   map.UpdateEmbeddedZipData(zip=>{foreach(var f in files){using var s=zip.CreateEntry("Items/"+folder+"/"+f.Key,CompressionLevel.Optimal).Open();s.Write(f.Value);}});
   var mapBytes=Save(mapDoc);var mapCheck=Gbx.Parse<CGameCtnChallenge>(new MemoryStream(mapBytes),MapSettings).Node;
   Require(mapCheck.AnchoredObjects![1].ItemModel!.Id==identity&&mapCheck.ExpectedEmbeddedItemModels!.Single().Id==identity,"Map identity lost");
   using(var zip=mapCheck.OpenReadEmbeddedZipData()){Require(zip.Entries.Count==files.Count,"Embed count changed");foreach(var f in files){using var s=zip.GetEntry("Items/"+folder+"/"+f.Key)!.Open();using var m=new MemoryStream();s.CopyTo(m);Require(m.ToArray().AsSpan().SequenceEqual(f.Value),"Embed bytes changed");}}
   var destination=Path.Combine(output,mode);Directory.CreateDirectory(destination);Directory.CreateDirectory(Path.Combine(destination,"files"));foreach(var f in files)File.WriteAllBytes(Path.Combine(destination,"files",f.Key),f.Value);
   var mapFile="TMIS_Skyward_"+mode+".Map.Gbx";File.WriteAllBytes(Path.Combine(destination,mapFile),mapBytes);
   var row=new{mode,outputMap=Path.Combine(destination,mapFile),outputSha256=Hash(mapBytes),map.MapName,embeddedFolder=folder,replacementFolder,replacementShortFile=replacement,expectedEmbeddedItemIds=new[]{identity},nativePrimerPresent=true,nativePrimerIndex=0,anchoredObjectCount=2,embeddedFiles=files.Select(f=>new{zipPath="Items/"+folder+"/"+f.Key,sha256=Hash(f.Value),bytes=f.Value.Length}),animatedVisualHashes=visualHashes,material=Describe(checkMaterial)};
   File.WriteAllText(Path.Combine(destination,mapFile+".manifest.json"),JsonSerializer.Serialize(row,Json));rows.Add(row);
  }
  Require(Hash(File.ReadAllBytes(original))==originalHash,"Original mutated");
  File.WriteAllText(Path.Combine(output,"manifest.json"),JsonSerializer.Serialize(new{operation="offline-skyward-material-candidates",scope="File-only generation/reparse; no native lookup, rendering, palette, motion or portability verdict",sourceItemPath,sourceMapPath,sourceItemSha256=Hash(sourceItem),sourceMapSha256=Hash(sourceMap),nativeMaterialSha256=Hash(nativeBytes),originalHash,rows},Json));
  File.WriteAllText(Path.Combine(output,"README.md"),Report);
  Console.WriteLine(JsonSerializer.Serialize(new{output,offlineChecksPassed=true,runtimeTested=false,rows},Json));
 }
 static object Describe(Gbx<CPlugMaterial> doc){var m=doc.Node;return new{doc.RefTable?.AncestorLevel,references=doc.RefTable?.Files.Select(f=>new{f.FilePath,f.Flags,f.UseFile,f.NodeIndex}),shaderFile=m.ShaderFile?.FilePath,textures=m.CustomMaterial?.Textures?.Select(t=>new{t.Name,t.TextureFile?.FilePath,t.TextureFile?.UseFile}),gpuFx1=m.CustomMaterial?.GpuFxs1,gpuFx2=m.CustomMaterial?.GpuFxs2};}
 public static void GenerateWhitePatch(string output,string sourceMapPath,string sourceItemPath)
 {
  output=Path.GetFullPath(output);Require(!Directory.Exists(output)&&!File.Exists(output),"Choose new evidence directory");
  var sourceItem=File.ReadAllBytes(sourceItemPath);var sourceMap=File.ReadAllBytes(sourceMapPath);
  var doc=Gbx.Parse<CGameItemModel>(new MemoryStream(sourceItem));var cloth=Cloth(doc.Node);
  Require(cloth.Visuals!.Length==5,"Expected five LODs");
  var sourceHashes=cloth.Visuals.Select(NodeHash).ToArray();
  var oldUvs=new List<CPlugVisual.TexCoord[]>();
  var changed=new List<int>();
  foreach(var v in cloth.Visuals.Cast<CPlugVisualIndexedTriangles>())
  {
   Require(v.TexCoords.Length==1,"Expected one UV channel");var old=v.TexCoords[0].TexCoords.ToArray();oldUvs.Add(old);
   Require(old.All(t=>t.UV==new Vec2(.5f,.5f)||t.UV==new Vec2(.5f,.1f)),"Expected exact contrast UV centers");
   changed.Add(old.Count(t=>t.UV.Y==.5f));
   v.TexCoords[0].TexCoords=old.Select(t=>t.UV.Y==.5f?t with{UV=new Vec2(.25f,.5f)}:t).ToArray();
  }
  Require(changed.All(n=>n==1181*86),"Letter count changed");
  const string itemName="TMIS_Skyward_OneUV_WhitePatch.Item.Gbx",mapName="TMIS_Skyward_OneUV_WhitePatch.Map.Gbx";
  doc.Node.Name="TMIS Skyward OneUV Stable White Patch";var itemBytes=Save(new RefDoc<CGameItemModel>(doc.Node,doc.RefTable!));
  var parsed=Gbx.Parse<CGameItemModel>(new MemoryStream(itemBytes));var roundCloth=Cloth(parsed.Node);
  Require(roundCloth.Visuals!.Select(NodeHash).SequenceEqual(cloth.Visuals.Select(NodeHash)),"White patch serialization changed");
  for(var i=0;i<roundCloth.Visuals!.Length;i++)
  {
   var v=(CPlugVisualIndexedTriangles)roundCloth.Visuals[i];
   Require(v.TexCoords.Length==1&&v.TexCoords[0].TexCoords.Length==oldUvs[i].Length,"UV layout changed");
   for(var j=0;j<oldUvs[i].Length;j++)Require(v.TexCoords[0].TexCoords[j]==(oldUvs[i][j].UV.Y==.5f?oldUvs[i][j] with{UV=new Vec2(.25f,.5f)}:oldUvs[i][j]),"Unexpected UV change");
   v.TexCoords[0].TexCoords=oldUvs[i];Require(NodeHash(roundCloth.Visuals[i])==sourceHashes[i],"Visual fields other than selected UVs changed");
  }
  var files=new Dictionary<string,byte[]>{{itemName,itemBytes}};
  var mapDoc=Gbx.Parse<CGameCtnChallenge>(new MemoryStream(sourceMap),MapSettings);var map=mapDoc.Node;
  Require(map.AnchoredObjects!.Count==2&&map.AnchoredObjects[0].ItemModel!.Id=="Flag16m","Expected native primer + custom anchor");
  using(var zip=map.OpenReadEmbeddedZipData())foreach(var n in new[]{Placeholder,Skin,Modifier})
  {
   var entry=zip.Entries.Single(e=>e.Name==n);using var s=entry.Open();using var m=new MemoryStream();s.CopyTo(m);files[n]=m.ToArray();
  }
  Require(Hash(files[Skin])=="DC6118300D7C4FDBA9DAA737130B45249A5E46F1AA56AC17A6D7F658CFA6859E","Native material remap altered");
  var folder="TMIS_SkywardWhitePatch_"+Guid.NewGuid().ToString("N")[..14];var identity=folder+"\\"+itemName;
  var anchor=map.AnchoredObjects[1];anchor.ItemModel=anchor.ItemModel! with{Id=identity};anchor.AnchorTreeId=identity;
  map.MapName="TMIS Skyward OneUV Stable White Patch";map.MapUid="TMISSKYWP_"+Guid.NewGuid().ToString("N")[..17];map.EmbeddedZipData=null;
  map.UpdateEmbeddedZipData(zip=>{foreach(var f in files){using var s=zip.CreateEntry("Items/"+folder+"/"+f.Key,CompressionLevel.Optimal).Open();s.Write(f.Value);}});
  var bytes=Save(mapDoc);var check=Gbx.Parse<CGameCtnChallenge>(new MemoryStream(bytes),MapSettings).Node;
  Require(check.AnchoredObjects![1].ItemModel!.Id==identity&&check.ExpectedEmbeddedItemModels!.Single().Id==identity,"Map Item identity changed");
  using(var zip=check.OpenReadEmbeddedZipData()){Require(zip.Entries.Count==4,"Embed count changed");foreach(var f in files){using var s=zip.GetEntry("Items/"+folder+"/"+f.Key)!.Open();using var m=new MemoryStream();s.CopyTo(m);Require(m.ToArray().AsSpan().SequenceEqual(f.Value),"Embedded payload changed");}}
  Directory.CreateDirectory(output);Directory.CreateDirectory(Path.Combine(output,"files"));foreach(var f in files)File.WriteAllBytes(Path.Combine(output,"files",f.Key),f.Value);File.WriteAllBytes(Path.Combine(output,mapName),bytes);
  var manifest=new{operation="offline-skyward-native-stable-whitepatch",scope="UV0 control only; no native brightness/visibility verdict",outputMap=Path.Combine(output,mapName),outputSha256=Hash(bytes),map.MapName,embeddedFolder=folder,expectedEmbeddedItemIds=new[]{identity},nativePrimerPresent=true,nativePrimerIndex=0,anchoredObjectCount=2,sourceMapPath,sourceItemPath,sourceItemSha256=Hash(sourceItem),sourceMapSha256=Hash(sourceMap),letterUvFrom=new Vec2(.5f,.5f),letterUvTo=new Vec2(.25f,.5f),backgroundUv=new Vec2(.5f,.1f),changedVerticesPerLod=changed,sourceVisualHashes=sourceHashes,outputVisualHashes=cloth.Visuals.Select(NodeHash),otherVisualFieldsExact=true,materialRoute="Unchanged accepted v6 placeholder-to-genuine-native ItemFlag; no cloned material",embeddedFiles=files.Select(f=>new{zipPath="Items/"+folder+"/"+f.Key,sha256=Hash(f.Value),bytes=f.Value.Length})};
  File.WriteAllText(Path.Combine(output,mapName+".manifest.json"),JsonSerializer.Serialize(manifest,Json));File.WriteAllText(Path.Combine(output,"manifest.json"),JsonSerializer.Serialize(manifest,Json));
  File.WriteAllText(Path.Combine(output,"README.md"),"# Native flag stable white UV control\n\nOffline generation only. Letter UV0 changes from (0.5,0.5) to (0.25,0.5); green background UV0 remains (0.5,0.1). All five complete animated visuals are otherwise byte-exact after restoring selected UV values. The native ItemFlag diffuse DDS has a uniform RGB241 white patch around (0.25,0.5), including mirrored U=0.75, over UV offsets +/-0.015625. The previous (0.5,0.5) center is near a green logo boundary. This isolates texture filtering; it does not establish lighting or runtime brightness. The accepted genuine native ItemFlag remap sidecars remain exact. Native Flag16m stays first in the embedded editor fixture.\n");
  Console.WriteLine(JsonSerializer.Serialize(manifest,Json));
 }
 static CPlugSolid2Model Cloth(CGameItemModel item)=>((CPlugDynaObjectModel)((CPlugPrefab)((NPlugItem_SVariantList)item.EntityModel!).Variants![0].EntityModel!).Ents![1].Model!).Mesh!;
 public static void GenerateNativeWarpFlag(string output,string sourceMapPath,string sourceItemPath,string? outputToken=null)
 {
  output=Path.GetFullPath(output);Require(!Directory.Exists(output)&&!File.Exists(output),"Choose new evidence directory");
  var sourceItem=File.ReadAllBytes(sourceItemPath);var sourceMap=File.ReadAllBytes(sourceMapPath);
  var nativePath="C:/Users/PC/OpenplanetNext/Extract/GameData/Stadium256/Media/Material/WarpFlag.Material.Gbx";var nativeBytes=File.ReadAllBytes(nativePath);
  Require(Hash(nativeBytes)=="E343A879847E7D3321BEDFA962758BC039A0C6CCF5A8751588D781EF9053A858","Native WarpFlag material changed");
  var native=Gbx.Parse<CPlugMaterial>(new MemoryStream(nativeBytes)).Node;
  Require(native.ShaderFile!.FilePath=="Techno3\\Media\\Material\\Tech3_Warp_TDiffSpec_VertexTween.Material.gbx"&&native.CustomMaterial!.Textures!.All(t=>t.Name!="BaseColorHueMask"),"Native WarpFlag shader/mask changed");
  var doc=Gbx.Parse<CGameItemModel>(new MemoryStream(sourceItem));var hashes=Cloth(doc.Node).Visuals!.Select(NodeHash).ToArray();var entityGraphHash=NodeHash(doc.Node.EntityModel!);
  Require(Cloth(doc.Node).Visuals!.Cast<CPlugVisualIndexedTriangles>().All(v=>v.TexCoords.Length==1),"One-UV input required");
  var suffix=outputToken??"";Require(suffix.Length<=40&&suffix.All(char.IsAsciiLetterOrDigit),"Output token must be up to 40 ASCII letters/digits");
  var title="TMIS Skyward Genuine Native WarpFlag"+(suffix.Length>0?" "+suffix:"");
  doc.Node.Name=title;var itemName="TMIS_Skyward_NativeWarpFlag"+suffix+".Item.Gbx";var mapName="TMIS_Skyward_NativeWarpFlag"+suffix+".Map.Gbx";
  var itemBytes=Save(new RefDoc<CGameItemModel>(doc.Node,doc.RefTable!));
  Require(Cloth(Gbx.Parse<CGameItemModel>(new MemoryStream(itemBytes)).Node).Visuals!.Select(NodeHash).SequenceEqual(hashes),"Complete animated visuals changed");
  Require(NodeHash(Gbx.Parse<CGameItemModel>(new MemoryStream(itemBytes)).Node.EntityModel!)==entityGraphHash,"Complete entity graph/timing changed");
  const string replacementFolder="Stadium256\\Media\\Material\\",replacement="WarpFlag";
  var files=new Dictionary<string,byte[]>{[itemName]=itemBytes,
   [Placeholder]=File.ReadAllBytes("Resources/Flags/"+Placeholder),
   [Skin]=NativeArchive(0x090F4000,Placeholder,true,w=>{w.Write(0x090F4004u);w.Write((byte)3);S(w,"");S(w,"");S(w,"");w.Write((byte)1);w.Write(0x09079000u);S(w,replacement);w.Write(1);w.Write(1);w.Write(0);w.Write(Facade);}),
   [Modifier]=NativeArchive(0x0915D000,Skin,false,w=>{w.Write(0x0915D000u);w.Write(1);S(w,replacementFolder);w.Write(0x0915D001u);w.Write(3);w.Write(-1);w.Write(Facade);})};
  Require(Hash(files[Placeholder])=="CB3B703C2B5BED9F359919C59B24A08730EE833FC5E422C40062DA061CDA588A","Placeholder changed");
  var sk=Gbx.Parse<CPlugGameSkin>(new MemoryStream(files[Skin]));Require(sk.Node.CustomizableFids!.Single().Name==replacement&&sk.RefTable!.Files.Single().UseFile,"Skin FID gate changed");
  var mapDoc=Gbx.Parse<CGameCtnChallenge>(new MemoryStream(sourceMap),MapSettings);var map=mapDoc.Node;
  Require(map.AnchoredObjects!.Count==2&&map.AnchoredObjects[0].ItemModel!.Id=="Flag16m","Expected native primer plus custom anchor");
  var folder="TMIS_SkywardNativeWarpFlag_"+Guid.NewGuid().ToString("N")[..12];var identity=folder+"\\"+itemName;var anchor=map.AnchoredObjects[1];anchor.ItemModel=anchor.ItemModel! with{Id=identity};anchor.AnchorTreeId=identity;
  map.MapName=title;map.MapUid="TMISSKYWF_"+Guid.NewGuid().ToString("N")[..17];map.EmbeddedZipData=null;
  map.UpdateEmbeddedZipData(zip=>{foreach(var f in files){using var s=zip.CreateEntry("Items/"+folder+"/"+f.Key,CompressionLevel.Optimal).Open();s.Write(f.Value);}});
  var bytes=Save(mapDoc);var check=Gbx.Parse<CGameCtnChallenge>(new MemoryStream(bytes),MapSettings).Node;
  Require(check.AnchoredObjects![1].ItemModel!.Id==identity&&check.ExpectedEmbeddedItemModels!.Single().Id==identity,"Map identity changed");
  using(var zip=check.OpenReadEmbeddedZipData()){Require(zip.Entries.Count==4,"Unexpected embedded file count");foreach(var f in files){using var s=zip.GetEntry("Items/"+folder+"/"+f.Key)!.Open();using var m=new MemoryStream();s.CopyTo(m);Require(m.ToArray().AsSpan().SequenceEqual(f.Value),"Embedded bytes changed");}}
  Directory.CreateDirectory(output);Directory.CreateDirectory(Path.Combine(output,"files"));foreach(var f in files)File.WriteAllBytes(Path.Combine(output,"files",f.Key),f.Value);File.WriteAllBytes(Path.Combine(output,mapName),bytes);
  var manifest=new{operation="offline-skyward-genuine-native-warpflag",scope="Offline native material variant; no native availability/structured/appearance verdict",outputMap=Path.Combine(output,mapName),outputSha256=Hash(bytes),map.MapName,embeddedFolder=folder,expectedEmbeddedItemIds=new[]{identity},nativePrimerPresent=true,nativePrimerIndex=0,anchoredObjectCount=2,sourceMapPath,sourceItemPath,sourceItemSha256=Hash(sourceItem),sourceMapSha256=Hash(sourceMap),completeAnimatedVisualHashes=hashes,completeAnimatedVisualsExact=true,completeEntityGraphSha256=entityGraphHash,completeEntityGraphExact=true,nativeMaterialPath="GameData/Stadium256/Media/Material/WarpFlag.Material.Gbx",nativeMaterialSha256=Hash(nativeBytes),replacementFolder,replacement,materialRoute="Early v6 placeholder to genuine native WarpFlag; no embedded material clone or cross-drive shader refs",embeddedFiles=files.Select(f=>new{zipPath="Items/"+folder+"/"+f.Key,sha256=Hash(f.Value),bytes=f.Value.Length})};
  File.WriteAllText(Path.Combine(output,mapName+".manifest.json"),JsonSerializer.Serialize(manifest,Json));File.WriteAllText(Path.Combine(output,"manifest.json"),JsonSerializer.Serialize(manifest,Json));
  File.WriteAllText(Path.Combine(output,"README.md"),"# Genuine native WarpFlag control\n\nOffline generation only. All five complete animated visuals are exact to the chosen one-UV source Item named in the manifest. The only material mechanism change is GameSkin replacement short name WarpFlag and TerrainModifier folder Stadium256\\Media\\Material\\. Item/map names and packaging identities are unique. All other graph/timing fields are retained from the chosen input. The accepted placeholder stays exact; no cloned Material, shader, native texture, HLSL or GPU cache is embedded. Native WarpFlag uses the same VertexTween parent, has native Diffuse/RoughMetal and no authored hue-mask sampler/target override. Its effective default mask, texture-array layer, palette and current-build availability remain native tests for this exact fixture. A genuine Flag16m stays first. This is an embedded editor fixture, not a standalone single-Item or validated-race claim.\n");
  Console.WriteLine(JsonSerializer.Serialize(manifest,Json));
 }
 static byte[] Save(Gbx doc){using var m=new MemoryStream();doc.Save(m);return m.ToArray();}
 static string NodeHash<T>(T n) where T:CMwNod {using var m=new MemoryStream();new Gbx<T>(n).Save(m);return Hash(m.ToArray());}
 static string Hash(byte[] b)=>Convert.ToHexString(SHA256.HashData(b));
 static void Require(bool ok,string message){if(!ok)throw new InvalidDataException(message);}
 static void S(BinaryWriter w,string text){var b=Encoding.UTF8.GetBytes(text);w.Write(b.Length);w.Write(b);}
 static byte[] NativeArchive(uint cls,string dependency,bool useFile,Action<BinaryWriter> body){using var m=new MemoryStream();using var w=new BinaryWriter(m,Encoding.UTF8,true);w.Write(new byte[]{71,66,88,6,0,66,85,85,82});w.Write(cls);w.Write(0);w.Write(2);w.Write(1);w.Write(0);w.Write(0);w.Write(1);S(w,dependency);w.Write(1);w.Write(useFile?1:0);w.Write(0);body(w);return m.ToArray();}
 sealed class RefDoc<T>:Gbx<T> where T:CMwNod,new(){public RefDoc(T n,GbxRefTable t):base(n){RefTable=t;}}
 const string Report="""
 # Skyward VertexTween material candidates

 These are offline fixtures, not an in-game success report. The original Item is untouched. The selected source Item's exact animated 2,576-vertex banner geometry, both sides, UV channels and five LODs are preserved. See manifest sourceItemPath/sourceMapPath for the precise input; one-UV sources deliberately omit the original source UV1 before this material experiment.

 - NativeClone copies native ItemFlag without palette changes. It isolates custom embedded material lookup and native shader/texture references.
 - PaletteKeepHue changes only Diffuse/RoughMetal textures to native ItemSpectator D/R. Native hue-mask/color targeting and BackSide_FlipU remain.
 - PaletteNeutral removes BaseColorHueMask, BaseColorTarget and the color-target-table reference, keeping native BackSide_FlipU.
 - PaletteNeutralNoFlip also removes BackSide_FlipU so the separately authored front/back sheets use source UVs.
 - PaletteNeutralFlip0 (generated separately with --generate-flip0) explicitly sets BackSide_FlipU=0 instead of depending on the shader default after parameter removal.

 All candidates retain native Tech3_Warp_TDiffSpec_VertexTween. Native shader/texture refs are prefixed GameData with ancestor zero; resolution is unproven. TerrainModifier replacement-folder is the candidate's absolute MemoryTemp current-map embedded folder; this global-folder route is unproven too. Test NativeClone first and inspect effective material/shader before interpreting visibility failure as palette failure.

 Every map embeds Item, accepted v6 placeholder, GameSkin, TerrainModifier and custom material, and retains a genuine native Flag16m primer. No loose files are staged. Maps are editor fixtures, not validated races. Cloth is deterministic baked motion, not flight or wind physics.

 Required sequence: NativeClone -> PaletteKeepHue -> PaletteNeutral -> PaletteNeutralNoFlip. Compare fixed-camera appearance to OriginalStatic/RemapWave; inspect both sides, lettering, attachment and motion. Only after warm native success should cold embedded-only portability be tested. Preserve ItemSpectator atlas/contrast; no exact blue/white appearance claim is made.
 """;
}
