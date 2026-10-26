using System.Security.Cryptography;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.Game;

// A separate camera-friendly map; never changes item bytes or the original map.
internal static class FlagPluginFreeMap
{
    public static int Create(string source, string output)
    {
        if (File.Exists(output)) throw new IOException("Preserve previous evidence");
        var doc = Gbx.Parse<CGameCtnChallenge>(source, new GbxReadSettings { SkipChunkIds = [0x0304305B] });
        var map = doc.Node;
        if (map.AnchoredObjects?.Count != 3 || (map.EmbeddedZipData?.Length ?? 0) != 0)
            throw new InvalidDataException("Expected the three accepted installation identities");
        map.MapName = "TMIS Plugin Free Flags Proof";
        map.MapUid = "TMISPluginFreeProof20261022";
        foreach (var item in map.AnchoredObjects)
        {
            var p = item.AbsolutePositionInMap;
            // Native default editor camera is (768,161.073,398.448), pitched
            // down by 22.5 degrees. Put the cloth about 55 metres ahead of it.
            item.AbsolutePositionInMap = new(p.X - 712, p.Y + 120, p.Z - 910);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        doc.Save(output);
        Gbx.Parse<CGameCtnChallenge>(output, new GbxReadSettings { SkipChunkIds = [0x0304305B] });
        Console.WriteLine(JsonSerializer.Serialize(new { source, output,
            sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(output))),
            scope = "Only map name/UID and three positions changed; model bytes unchanged",
            items = map.AnchoredObjects.Select(i => new { i.ItemModel, i.AbsolutePositionInMap })
        }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        return 0;
    }
}
