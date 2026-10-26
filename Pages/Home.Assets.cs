using GBX.NET;
using GBX.NET.Engines.GameData;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using TM_Item_Studio.Models;

namespace TM_Item_Studio.Pages;

public partial class Home
{
    private string viewportMood = "neutral";
    private string? moodStatus;
    private bool gameDataIndexed;
    private readonly List<string> assetLoadErrors = new();

    private async Task SetViewportMood(string mood)
    {
        try
        {
            await JS.InvokeVoidAsync("setStudioMood", mood);
            viewportMood = mood;
            moodStatus = mood == "neutral" ? null : $"Trackmania Stadium · {mood} · original GameData sky, environment and light settings";
        }
        catch (JSException ex) { moodStatus = ex.Message; }
    }

    private async Task LoadTextureFolder(InputFileChangeEventArgs _)
    {
        var selection = await JS.InvokeAsync<TextureDirectorySelection>("indexStudioGameData", "texture-folder-upload", false);
        textureDirectoryStatus = $"Using {selection.Name} ({selection.FileCount:N0} files indexed).";
        await UpdateScenePayload(authoredMotionScene);
    }

    private async Task<GbxDependencyLoader.ResolvedFile?> ResolveGameData(string reference)
    {
        if (!gameDataIndexed) return null;
        var bytes = await JS.InvokeAsync<byte[]?>("readStudioGameDataFile", reference);
        return bytes is null ? null : new(reference, bytes);
    }

    private async Task PrepareAssetPreview(ItemVariantSource source)
    {
        var document = source.Document;
        var assetPath = await JS.InvokeAsync<string?>("findStudioAssetPath", source.FileName + ".Gbx", source.OriginalAssetSize);
        if (assetPath is not null && assetPath.StartsWith("skins/", StringComparison.OrdinalIgnoreCase))
            source.TextureContext = assetPath[..assetPath.LastIndexOf('/')];
        var skins = document.RefTable?.Files.Where(f => f.FilePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            && f.FilePath.Contains("Skins", StringComparison.OrdinalIgnoreCase)).ToArray() ?? [];
        if (skins.Length == 0) return;
        foreach (var skin in skins)
        {
            var path = await JS.InvokeAsync<string?>("findStudioSkinMesh", skin.FilePath);
            if (path is null) continue;
            var resolved = await ResolveGameData(path);
            if (resolved is null) continue;
            var meshDocument = Gbx.Parse(new MemoryStream(resolved.Bytes), new GbxReadSettings { SafeSkippableChunks = true, IgnoreExceptionsInBody = true });
            if (ItemScene.Build(meshDocument.Node, 0, resolveExternal: true).Preview.Geometry.Count == 0)
                throw new InvalidOperationException($"The referenced skin model {path} contains no decoded mesh geometry. {meshDocument.Body.Exception?.Message}");
            await GbxDependencyLoader.HydrateAsync(meshDocument, ResolveGameData);
            source.PreviewOverride = meshDocument.Node;
            source.TextureContext = path[..path.LastIndexOf('/')];
            var partialArchive = meshDocument.Body?.Exception is not null || document.Body?.Exception is not null
                || source.PreviewDescription?.StartsWith("Native model parser:", StringComparison.Ordinal) == true;
            source.PreviewDescription = $"Skin render model: {path}. Static rest pose; vehicle physics and character animation are not simulated."
                + (partialArchive ? " Partial native archive; decoded skin geometry only." : "");
            return;
        }
    }
}
