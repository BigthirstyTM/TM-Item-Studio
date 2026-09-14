using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;

if (args.Length is < 2 or > 4)
    throw new ArgumentException("Usage: WriterCompatibility <source.Item.Gbx> <destination.Item.Gbx> [pivot-index x]");

Gbx.LZO = new GBX.NET.LZO.MiniLZO();

var source = args[0];
var destination = args[1];
var gbx = Gbx.Parse<CGameItemModel>(source);

if (args.Length == 4)
{
    if (!int.TryParse(args[2], out var pivotIndex) || !float.TryParse(args[3], System.Globalization.CultureInfo.InvariantCulture, out var x))
        throw new ArgumentException("Pivot index and X coordinate must be invariant-culture numbers.");

    var pivots = gbx.Node.DefaultPlacement?.PivotPositions
        ?? throw new InvalidOperationException("Item has no placement pivot array.");
    if (pivotIndex < 0 || pivotIndex >= pivots.Length)
        throw new ArgumentOutOfRangeException(nameof(pivotIndex), "Pivot index is outside the authored pivot array.");

    var pivot = pivots[pivotIndex];
    pivots[pivotIndex] = new Vec3(x, pivot.Y, pivot.Z);
}

gbx.Save(destination);
Console.WriteLine($"Writer: {typeof(Gbx).Assembly.FullName}");
Console.WriteLine($"Output: {Path.GetFullPath(destination)}");
