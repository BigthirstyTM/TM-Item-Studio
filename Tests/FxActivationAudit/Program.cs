using System.Collections;
using System.Reflection;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.Plug;

Gbx.LZO = new GBX.NET.LZO.MiniLZO();
var reports = new List<object>();
foreach (var path in args)
{
    if (path.EndsWith(".Material.Gbx", StringComparison.OrdinalIgnoreCase))
    {
        var material = Gbx.Parse<CPlugMaterial>(path,
            new GbxReadSettings { IgnoreExceptionsInBody = true, SafeSkippableChunks = true });
        reports.Add(new { path, parseException = material.Body?.Exception?.ToString(), data = Values(material.Node, 0) });
        continue;
    }
    var document = Gbx.Parse<CPlugParticleEmitterModel>(path,
        new GbxReadSettings { IgnoreExceptionsInBody = true, SafeSkippableChunks = true });
    reports.Add(new { path, parseException = document.Body?.Exception?.ToString(), data = Values(document.Node, 0) });
}
Console.WriteLine(JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));

static object? Values(object? value, int depth)
{
    if (value is null || depth > 7) return null;
    if (value is string || value.GetType().IsPrimitive || value.GetType().IsEnum) return value;
    if (value is IEnumerable collection)
    {
        var entries = new List<object?>();
        foreach (var entry in collection) { if (entries.Count >= 128) break; entries.Add(Values(entry, depth + 1)); }
        return entries;
    }
    var report = new Dictionary<string, object?> { ["type"] = value.GetType().Name };
    foreach (var property in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
    {
        if (!property.CanRead || property.GetIndexParameters().Length > 0 || property.Name is "Chunks" or "Gbx" or "Properties") continue;
        try { report[property.Name] = Values(property.GetValue(value), depth + 1); } catch { }
    }
    foreach (var field in value.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        try { report[field.Name] = Values(field.GetValue(value), depth + 1); } catch { }
    return report;
}
