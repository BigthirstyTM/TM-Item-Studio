using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using GBX.NET;
using GBX.NET.Engines.GameData;
using Microsoft.JSInterop;
using TM_Item_Studio.Models;

namespace TM_Item_Studio.Pages;

public partial class Home
{
    private sealed record DebugDependency(string Kind, string Reference, string Status);
    private sealed record DebugMessage(string Code, string Message);
    private List<DebugMessage> modelDiagnostics = new();
    private static readonly JsonSerializerOptions DebugJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private (ItemScenePreview? Scene, List<DebugDependency> Dependencies, List<DebugMessage> Messages) InspectDebugScene()
    {
        var dependencies = new List<DebugDependency>();
        var messages = new List<DebugMessage>();
        if (itemModel is null) return (null, dependencies, messages);
        ItemScenePreview? preview = null;
        try
        {
            var source = variantSources[activeVariantIndex];
            var scene = ItemScene.Build(source.PreviewRoot, 0);
            preview = scene.Preview;
            messages.AddRange(preview.Diagnostics.Take(200).Select(d => new DebugMessage(d.Code, d.Message)));
            // String-based legacy resource references are not GBX reference-table
            // entries. Inventory them explicitly, without resolving any files.
            void Dependency(string kind, string? reference)
            {
                if (!string.IsNullOrEmpty(reference)) dependencies.Add(new(kind, reference, "not-loaded"));
            }
            foreach (var node in scene.Handles.Sources.Values)
            {
                if (node is CGameObjectVisModel visual)
                {
                    Dependency("mesh", visual.Mesh);
                    Dependency("solid", visual.SolidRef);
                }
                if (node is CGameObjectPhyModel physical)
                {
                    Dependency("collision", physical.MoveShape);
                    Dependency("collision", physical.HitShape);
                    Dependency("trigger", physical.TriggerShape);
                }
            }
            if (gbxFile?.RefTable is { } references)
                foreach (var file in references.Files.Take(200)) Dependency("gbx-reference", file.FilePath);
        }
        catch (Exception ex)
        {
            messages.Add(new("inspection-failed", ex.Message));
        }
        if (dependencies.Count > 0)
            messages.Add(new("dependencies-not-loaded", "Referenced files are not loaded by this upload. Their presence on your disk has not been checked."));
        if (modelParts.Count == 0)
            messages.Add(new("no-mesh-geometry", "No mesh geometry available. A fallback material cannot replace an external or unsupported mesh."));
        else
            messages.Add(new("textures-not-loaded", "Neutral preview — textures are not loaded. Game materials and texture files are not evaluated by this viewer."));
        return (preview, dependencies, messages);
    }

    [JSInvokable]
    public string GetDebugReportJson()
    {
        var snapshot = InspectDebugScene();
        var collection = itemModel?.Ident?.Collection;
        var report = new
        {
            schemaVersion = 1,
            generatedAtUtc = DateTimeOffset.UtcNow,
            status = isLoading ? "loading" : errorMessage is not null ? "error" : itemModel is null ? "empty" : "loaded",
            error = errorMessage,
            privacy = "Local report only. Contains model names, authors and referenced filenames; review before sharing. No GBX bytes, vertex buffers or texture pixels are included.",
            parser = new
            {
                informationalVersion = typeof(Gbx).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                assemblyVersion = typeof(Gbx).Assembly.GetName().Version?.ToString(),
                moduleVersionId = typeof(Gbx).Module.ModuleVersionId
            },
            selection = itemModel is null ? null : variantSources[activeVariantIndex].Name,
            views = variantSources.Select((s, i) => new { index = i, name = s.Name, active = i == activeVariantIndex }),
            identity = itemModel is null ? null : new
            {
                id = itemModel.Ident?.Id,
                author = itemModel.Ident?.Author,
                collectionNumber = collection?.Number,
                collectionString = collection?.String,
                itemType = itemModel.ItemType.ToString()
            },
            viewer = new
            {
                meshParts = modelParts.Count,
                vertices = modelParts.Sum(p => p.Positions.Length / 3),
                triangles = modelParts.Sum(p => p.Indices.Length / 3),
                movingParts = modelParts.Count(p => p.IsMoving),
                pivots = detectedPivots.Count, lights = detectedLights.Count, sockets = detectedSockets.Count,
                playing = isPlaying, materialMode = "neutral-untextured",
                note = "Viewer counts describe the displayed payload; typed scene inventory below may have different coverage."
            },
            dependencies = snapshot.Dependencies.Take(200),
            diagnostics = snapshot.Messages,
            scene = snapshot.Scene is null ? null : new
            {
                nodeCount = snapshot.Scene.Nodes.Count,
                nodes = snapshot.Scene.Nodes.Take(2000).Select(n => new { n.Path, n.ParentPath, n.Type, n.Kind, n.State }),
                materials = snapshot.Scene.Materials.Take(200),
                mappings = snapshot.Scene.Mappings.Take(500),
                collisions = snapshot.Scene.Collisions.Take(200),
                diagnostics = snapshot.Scene.Diagnostics.Take(200)
            },
            limits = new { nodes = 2000, materials = 200, mappings = 500, dependencies = 200, sceneDiagnostics = 200 },
            totals = new { dependencies = snapshot.Dependencies.Count, materials = snapshot.Scene?.Materials.Count ?? 0,
                mappings = snapshot.Scene?.Mappings.Count ?? 0, sceneDiagnostics = snapshot.Scene?.Diagnostics.Count ?? 0 }
        };
        return JsonSerializer.Serialize(report, DebugJson);
    }

    private async Task RefreshDebugDiagnostics()
    {
        modelDiagnostics = InspectDebugScene().Messages;
        await JS.InvokeVoidAsync("studioDebug.logDiagnostics", modelDiagnostics.Select(d => d.Code).Distinct());
    }

    private async Task DownloadDebugReport() => await JS.InvokeVoidAsync("studioDebug.downloadReport");
}
