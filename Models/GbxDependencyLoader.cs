using GBX.NET;
using GBX.NET.Components;

namespace TM_Item_Studio.Models;

/// <summary>
/// Connects a GBX document's external reference table to files supplied by the
/// browser. References are followed on demand by the caller's async resolver;
/// only the dependency closure of the selected item is parsed.
/// </summary>
public static class GbxDependencyLoader
{
    public sealed record ResolvedFile(string Key, byte[] Bytes);

    public sealed record Result(int LoadedCount, IReadOnlyList<string> Missing,
        IReadOnlyList<string> Failed)
    {
        public bool HasUnresolved => Missing.Count > 0 || Failed.Count > 0;
    }

    public static async Task<Result> HydrateAsync(Gbx root,
        Func<string, Task<ResolvedFile?>> resolve,
        GbxReadSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(resolve);
        var readSettings = settings ?? new GbxReadSettings { SafeSkippableChunks = true };

        var pending = new Queue<Gbx>();
        var visitedTables = new HashSet<GbxRefTable>(ReferenceEqualityComparer.Instance);
        var parsedByKey = new Dictionary<string, Gbx>(StringComparer.OrdinalIgnoreCase);
        var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        pending.Enqueue(root);

        while (pending.Count > 0)
        {
            var document = pending.Dequeue();
            var table = document.RefTable;
            if (table is null || !visitedTables.Add(table)) continue;

            var externalNodes = table.ExternalNodes ?? new Dictionary<string, Func<Gbx>>();
            table.ExternalNodes = externalNodes;
            foreach (var reference in table.Files)
            {
                var referencePath = reference.FilePath;
                if (string.IsNullOrWhiteSpace(referencePath) || externalNodes.ContainsKey(referencePath)) continue;
                // Ref tables also carry raw images and other resources. They
                // are consumed by the material/texture preview pipeline, not
                // by GBX node traversal, so never feed them to Gbx.Parse.
                if (!referencePath.EndsWith(".gbx", StringComparison.OrdinalIgnoreCase)) continue;
                // Shader programs, GPU buffers, sounds and texture wrappers are
                // not scene nodes. Bitmap paths are read from Material.Gbx.
                if (new[] { ".Shader.Gbx", ".Texture.Gbx", ".TextureArray.Gbx", ".Sound.Gbx", ".SoundMulti.Gbx", ".SoundEngine.Gbx", ".FxSystem.Gbx", ".RefBuffer.Gbx" }
                    .Any(suffix => referencePath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))) continue;

                ResolvedFile? resolved;
                try
                {
                    resolved = await resolve(referencePath);
                }
                catch (Exception ex)
                {
                    failed.Add($"{referencePath}: {ex.Message}");
                    continue;
                }
                if (resolved is null)
                {
                    missing.Add(referencePath);
                    continue;
                }

                var key = Normalize(resolved.Key);
                if (key.Length == 0) key = Normalize(referencePath);
                if (!parsedByKey.TryGetValue(key, out var dependency))
                {
                    try
                    {
                        dependency = Gbx.Parse(new MemoryStream(resolved.Bytes, writable: false), readSettings);
                        parsedByKey.Add(key, dependency);
                    }
                    catch (Exception ex)
                    {
                        failed.Add($"{referencePath}: {ex.Message}");
                        continue;
                    }
                }

                var captured = dependency;
                externalNodes[referencePath] = () => captured;
                pending.Enqueue(dependency);
            }
        }

        return new Result(parsedByKey.Count, missing.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            failed.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static string Normalize(string value) => value.Replace('/', '\\').TrimStart('\\').ToLowerInvariant();
}
