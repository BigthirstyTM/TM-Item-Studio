using GBX.NET;
using GBX.NET.Engines.GameData;
using System.Text.Json;

Gbx.LZO = new GBX.NET.LZO.MiniLZO();
if (args.Length == 2 && args[0] == "inspect")
{
    var item = Gbx.Parse<CGameItemModel>(args[1]).Node;
    Console.WriteLine(JsonSerializer.Serialize(new {
        number = item.Ident.Collection.Number, text = item.Ident.Collection.String,
        id = item.Ident.Id, author = item.Ident.Author
    }));
}
else if (args.Length == 5 && args[0] == "prepare")
{
    // Only the public bespoke fixture is used. Variations remain temporary
    // browser-test inputs, not contributions claiming native validation.
    var file = Gbx.Parse<CGameItemModel>(args[1]);
    var collection = args[3] switch {
        "number" => new Id(int.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture)),
        "text" => new Id(args[4]),
        _ => throw new ArgumentException("Collection kind must be number or text")
    };
    file.Node.Ident = file.Node.Ident with { Collection = collection };
    file.Save(args[2]);
}
else throw new ArgumentException("Usage: CollectionArchive inspect <file> | prepare <source> <destination> <number|text> <value>");
