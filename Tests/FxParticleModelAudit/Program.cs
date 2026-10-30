using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.Plug;

Gbx.LZO = new GBX.NET.LZO.MiniLZO();
var root = args.Length > 0 ? args[0] : @"C:\Users\PC\OpenplanetNext\Extract\GameData";
var output = args.Length > 1 ? args[1] : Path.Combine(Environment.CurrentDirectory, "fx-particle-model-audit.json");
var rows = new List<object>();
foreach (var path in Directory.EnumerateFiles(root, "*.ParticleModel.Gbx", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
{
    try
    {
        var source = File.ReadAllBytes(path);
        using var input = new MemoryStream(source, writable: false);
        var document = Gbx.Parse<CPlugParticleEmitterModel>(input, new GbxReadSettings { SafeSkippableChunks = true, ReadRawBody = true });
        using var roundtrip = new MemoryStream();
        document.Save(roundtrip);
        var saved = roundtrip.ToArray();
        var node = document.Node;
        rows.Add(new
        {
            path = Path.GetRelativePath(root, path).Replace('\\', '/'),
            bytes = source.Length,
            className = node?.GetType().Name,
            subModelCount = node?.ParticleEmitterSubModels?.Count ?? 0,
            refs = document.RefTable?.Files.Select(file => file.FilePath).Where(file => !string.IsNullOrWhiteSpace(file)).ToArray() ?? [],
            parsed = true,
            roundtripBytes = saved.Length,
            roundtripExact = source.AsSpan().SequenceEqual(saved),
            sourceSha256 = Convert.ToHexString(SHA256.HashData(source)),
            roundtripSha256 = Convert.ToHexString(SHA256.HashData(saved)),
            publicFields = PublicFields(node)
        });
    }
    catch (Exception error) when (error is not OutOfMemoryException)
    {
        rows.Add(new { path = Path.GetRelativePath(root, path).Replace('\\', '/'), parsed = false, error = error.Message });
    }
}

var report = new
{
    schemaVersion = 1,
    root = Path.GetFullPath(root),
    generatedAtUtc = DateTimeOffset.UtcNow,
    particleModelCount = rows.Count,
    parsedCount = rows.Count(row => row.GetType().GetProperty("parsed")?.GetValue(row) as bool? == true),
    rows
};
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(new { output = Path.GetFullPath(output), particleModelCount = rows.Count }, new JsonSerializerOptions { WriteIndented = true }));

static object PublicFields(CPlugParticleEmitterModel? model)
{
    if (model is null) return new { };
    var result = new Dictionary<string, object?>();
    foreach (var property in model.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
    {
        if (!property.CanRead || property.GetIndexParameters().Length != 0 || property.Name is "Chunks" or "Gbx") continue;
        try
        {
            var value = property.GetValue(model);
            var arrayValue = value as Array;
            if (value is null || arrayValue?.Length == 0) continue;
            result[property.Name] = arrayValue is not null ? new { count = arrayValue.Length } : value;
        }
        catch { }
    }
    return result;
}
