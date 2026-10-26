using GBX.NET;
using GBX.NET.Engines.GameData;
Gbx.LZO = new GBX.NET.LZO.MiniLZO();
if (args.Length == 2 && args[0] == "--inspect-carrier")
{
    using var probe = File.OpenRead(args[1]);
    var carrier = Gbx.Parse<CGameItemModel>(probe, new GbxReadSettings { SafeSkippableChunks = true, IgnoreExceptionsInBody = true, ReadRawBody = true });
    Console.WriteLine($"opaque={TM_Item_Studio.Models.FxOpaqueExport.IsOpaqueFxCarrier(carrier)} raw={carrier.Body?.RawData.Length} ex={carrier.Body?.Exception?.Message}");
    return;
}
var sourcePath=args[0]; var outPath=args[1]; using var s=File.OpenRead(sourcePath);
var d=Gbx.Parse<CGameItemModel>(s,new GbxReadSettings{SafeSkippableChunks=true,IgnoreExceptionsInBody=true,ReadRawBody=true});
Console.WriteLine($"before name={d.Node?.Name} id={d.Node?.Ident?.Id} entity={d.Node?.EntityModel?.GetType().FullName} raw={d.Body?.RawData.Length} ex={d.Body?.Exception?.Message}");
Console.WriteLine($"before refs={string.Join(" | ", d.RefTable?.Files.Select(x => x.FilePath) ?? Array.Empty<string>())}");
Console.WriteLine($"before placement={(d.Node?.DefaultPlacement is null ? "null" : d.Node.DefaultPlacement.GetType().FullName)} pivots={string.Join(";", d.Node?.DefaultPlacement?.PivotPositions ?? [])} entityNull={d.Node?.EntityModel is null}");
d.Node!.Name="TMIS Embedded Fogger Clone"; d.Node.Description="TMIS Embedded Fogger Clone"; if(d.Node.Ident is not null) d.Node.Ident=d.Node.Ident with { Id="TMIS_EmbeddedFoggerClone" };
using(var o=File.Create(outPath)) d.Save(o);
using var r=File.OpenRead(outPath); var q=Gbx.Parse<CGameItemModel>(r,new GbxReadSettings{SafeSkippableChunks=true,IgnoreExceptionsInBody=true,ReadRawBody=true});
Console.WriteLine($"after name={q.Node?.Name} id={q.Node?.Ident?.Id} entity={q.Node?.EntityModel?.GetType().FullName} raw={q.Body?.RawData.Length} ex={q.Body?.Exception?.Message} bytes={new FileInfo(outPath).Length}");
Console.WriteLine($"after refs={string.Join(" | ", q.RefTable?.Files.Select(x => x.FilePath) ?? Array.Empty<string>())}");

