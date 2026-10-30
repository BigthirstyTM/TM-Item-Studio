using System.Text.Json;
using System.Security.Cryptography;
using GBX.NET;
using GBX.NET.Engines.Plug;
Gbx.LZO = new GBX.NET.LZO.MiniLZO();
if(args.Length==3&&args[0]=="--audit-legacy-host") { LegacyGraphAudit.Run(args[1],args[2]); return; }
if(args.Length==2&&args[0]=="--audit-frozen-controls") { FrozenControlsAudit.Run(args[1]); return; }
if(args.Length==2&&args[0]=="--generate-kinematic-packed-control") { KinematicPackedControl.Generate(args[1]); return; }
if(args.Length==2&&args[0]=="--inspect-map-anchor") { KinematicPackedControl.InspectMapAnchor(args[1]); return; }
if(args.Length==4&&args[0]=="--generate-whitepatch") { Research.GenerateWhitePatch(args[1],args[2],args[3]); return; }
if(args.Length is 4 or 5&&args[0]=="--generate-native-warpflag") { Research.GenerateNativeWarpFlag(args[1],args[2],args[3],args.Length==5?args[4]:null); return; }
if(args.Length is 2 or 4&&args[0] is "--generate" or "--generate-flip0" or "--generate-clone")
{
 Research.Generate(args[1],args[0]=="--generate-flip0",args.Length==4?args[2]:null,args.Length==4?args[3]:null,args[0]=="--generate-clone");return;
}
var json=new JsonSerializerOptions{WriteIndented=true,IncludeFields=true};
foreach(var path in args) {
 var doc=Gbx.Parse<CPlugMaterial>(new MemoryStream(File.ReadAllBytes(path)));
 var m=doc.Node;
 Console.WriteLine(JsonSerializer.Serialize(new{path,doc.RefTable?.AncestorLevel,
 references=doc.RefTable?.Files.Select(f=>new{f.FilePath,f.Flags,f.UseFile,f.NodeIndex}),
 chunks=m.Chunks.Select(c=>new{id=c.Id.ToString("X8"),c.Ignore,chunk=c.GetType().GetFields().ToDictionary(p=>p.Name,p=>p.GetValue(c))}),
 shader=m.Shader?.GetType().Name,shaderFile=m.References.Shader.File?.FilePath,customChunks=m.CustomMaterial?.Chunks.Select(c=>new{id=c.Id.ToString("X8"),c.Ignore,fields=c.GetType().GetFields().ToDictionary(p=>p.Name,p=>p.GetValue(c))}),
 textures=m.CustomMaterial?.Textures?.Select(t=>new{t.Name,t.U01,t.U02,t.U03,t.References.Texture.File?.FilePath,t.References.Texture.File?.UseFile,t.Texture}),
 gpuFx1=m.CustomMaterial?.GpuFxs1,gpuFx2=m.CustomMaterial?.GpuFxs2},json));
}
