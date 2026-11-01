using System.IO.Compression;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Plug;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using TM_Item_Studio.Models;

namespace TM_Item_Studio.Pages;
public partial class Home
{
    private sealed record ProjectManifest(int Version,string FileName,int Variant,MotionPathPlan? Route,
        DrawPathPointDto[] Points,bool Smooth,string Anchor,float Smoothness,int SmoothDetail,
        JsonElement Authoring);
    private sealed record PendingProject(ProjectManifest Manifest);
    private PendingProject? pendingProject;
    private async Task SaveStudioProject()
    {
        try {
            if(SelectedSource is null||itemModel is null||SelectedSource.IsReadOnly||IsOpaqueFxCarrier)throw new InvalidOperationException("Project saving currently requires an editable parsed item.");
            if(editablePathPlan is {} draft&&!ItemMotionPathProfile.Compile(draft).Success)throw new InvalidOperationException("Fix the route validation error before saving this project.");
            UpdateIdent();SyncPivotsToGbx();for(int i=0;i<detectedLights.Count;i++)SyncLightsToGbx(i);ApplyMeshPartConfigurationToVariant();
            using var source=new MemoryStream();SelectedSource.Save(source);
            _=Gbx.Parse<CGameItemModel>(new MemoryStream(source.ToArray()));
            var authoring=JsonSerializer.Deserialize<JsonElement>(await JS.InvokeAsync<string>("getPathAuthoringState"));
            var stateError=await JS.InvokeAsync<string?>("validatePathAuthoringState",authoring);
            if(stateError is not null)throw new InvalidDataException(stateError);
            if(source.Length>50*1024*1024)throw new InvalidDataException("Project item exceeds the 50 MB import limit.");
            var manifest=new ProjectManifest(1,SelectedSource.FileName+".Gbx",SelectedSource.VariantNumber,editablePathPlan,
                await JS.InvokeAsync<DrawPathPointDto[]>("getDrawnPathPoints"),smoothPathPreview,drawPathAnchorMode,smoothPathPercent,smoothPathDetail,authoring);
            using var output=new MemoryStream();
            using(var zip=new ZipArchive(output,ZipArchiveMode.Create,true)){
                void Add(string name,byte[] bytes){if(bytes.Length>64*1024*1024)throw new InvalidDataException("Project entry exceeds 64 MB: "+name);using var entry=zip.CreateEntry(name,CompressionLevel.Optimal).Open();entry.Write(bytes);}
                Add("project.json",JsonSerializer.SerializeToUtf8Bytes(manifest,PathJsonOptions));Add("item.gbx",source.ToArray());
                if(drawPathOriginal is not null){var editable=Gbx.Parse<CGameItemModel>(new MemoryStream(source.ToArray()));editable.Node.EntityModel=drawPathOriginal;using var original=new MemoryStream();editable.Save(original);Add("editable-source.gbx",original.ToArray());}
            }
            await JS.InvokeVoidAsync("downloadBinaryGbx",(string.IsNullOrWhiteSpace(identId)?"ItemStudio":identId)+".tmisproject",Convert.ToBase64String(output.ToArray()));
            statusMessage="Project saved: selected item, route, handles, look-at, reference geometry and clearance settings. External textures/dependencies are not bundled.";
        }catch(Exception e){errorMessage="Could not save project: "+e.Message;}
    }
    private async Task LoadStudioProject(InputFileChangeEventArgs e)
    {
        try {
            using var input=new MemoryStream();await e.File.OpenReadStream(150*1024*1024).CopyToAsync(input);input.Position=0;
            using var zip=new ZipArchive(input,ZipArchiveMode.Read);
            if(zip.Entries.Count>3)throw new InvalidDataException("Unexpected project entries.");
            byte[] Read(string name,bool required=true){var entries=zip.Entries.Where(x=>x.FullName==name).ToArray();if(entries.Length==0&&!required)return [];if(entries.Length!=1||entries[0].Length>64*1024*1024)throw new InvalidDataException("Missing, duplicate or oversized project entry: "+name);using var stream=entries[0].Open();using var bytes=new MemoryStream();stream.CopyTo(bytes);return bytes.ToArray();}
            var manifest=JsonSerializer.Deserialize<ProjectManifest>(Read("project.json"),PathJsonOptions)??throw new InvalidDataException("Empty project.");
            if(string.IsNullOrWhiteSpace(manifest.FileName)||manifest.FileName.Length>255||manifest.Version!=1||manifest.Variant<1||manifest.Points is null||manifest.Points.Length>129||manifest.Anchor is not ("centre" or "origin")||!float.IsFinite(manifest.Smoothness)||manifest.Smoothness is <0 or >100||manifest.SmoothDetail is <2 or >16)throw new InvalidDataException("Unsupported or invalid project settings.");
            if(manifest.Points.Any(p=>!float.IsFinite(p.X)||!float.IsFinite(p.Y)||!float.IsFinite(p.Z)||new System.Numerics.Vector3(p.X,p.Y,p.Z).Length()>10000))throw new InvalidDataException("Invalid route coordinates.");
            if(manifest.Route is {} route&&!ItemMotionPathProfile.Compile(route).Success)throw new InvalidDataException("Invalid project route.");
            // Validate guide/handle payload before replacing the open item.
            var valid=await JS.InvokeAsync<string?>("validatePathAuthoringState",manifest.Authoring);
            if(valid is not null)throw new InvalidDataException(valid);
            var handles=JsonSerializer.Deserialize<BezierSettings>(manifest.Authoring.GetProperty("bezier"),PathJsonOptions);
            if(handles?.Enabled==true&&handles.Handles?.Length!=manifest.Points.Length)throw new InvalidDataException("Bezier handles do not match the project control points.");
            var savedBytes=Read("item.gbx");_=Gbx.Parse<CGameItemModel>(new MemoryStream(savedBytes));
            var editableBytes=Read("editable-source.gbx",false);var itemBytes=editableBytes.Length>0?editableBytes:savedBytes;var parsed=Gbx.Parse<CGameItemModel>(new MemoryStream(itemBytes));
            if(itemBytes.Length>50*1024*1024)throw new InvalidDataException("Project item exceeds the 50 MB import limit.");
            var choices=ItemVariantSource.FromFile(manifest.FileName,parsed);if(manifest.Variant>choices.Count)throw new InvalidDataException("Invalid project variant.");
            await LoadFileSet([new ProjectBrowserFile(Path.GetFileName(manifest.FileName),itemBytes)]);
            if(itemModel is null)throw new InvalidDataException("Project item could not be loaded.");
            if(manifest.Variant>1)await SelectVariant(manifest.Variant-1);
            pendingProject=new(manifest);if(isViewerInitialized)await CompleteProjectLoad();else StateHasChanged();
        }catch(Exception ex){pendingProject=null;errorMessage="Could not open project: "+ex.Message;}
    }
    private async Task CompleteProjectLoad()
    {
        if(pendingProject is not {} pending)return;pendingProject=null;var p=pending.Manifest;
        try {
            drawPathAnchorMode=p.Anchor;smoothPathPreview=p.Smooth;smoothPathPercent=p.Smoothness;smoothPathDetail=p.SmoothDetail;
            await JS.InvokeVoidAsync("setDrawnPathPoints",(object)p.Points);
            await JS.InvokeVoidAsync("restorePathAuthoringState",p.Authoring);
            if(p.Authoring.TryGetProperty("drawing",out var drawing)){drawPathPlane=drawing.GetProperty("plane").GetString()!;drawPathPlaneOffset=drawing.GetProperty("offset").GetSingle();drawPathView=drawing.GetProperty("view").GetString()!;drawPathCaptureEnabled=drawing.GetProperty("enabled").GetBoolean();await UpdateDrawPlane();await JS.InvokeVoidAsync("setDrawPathView",drawPathView);}
            bezierSettings=JsonSerializer.Deserialize<BezierSettings>(await JS.InvokeAsync<string>("getBezierState"),PathJsonOptions)??new();
            editablePathPlan=p.Route;
            if(p.Route is {} plan){drawPathUse=plan.Use;drawPathRepeat=plan.Repeat;drawPathEase=plan.Ease;drawPathPauseMs=plan.PauseMs;drawRelayDelay=plan.StartDelayMs;drawRelayParking=plan.ParkingDistance;drawPathMetersPerSecond=plan.Legs.Sum(l=>l.Delta.Length())/(plan.Legs.Sum(l=>l.MoveMs)/1000f);}
            authoringRevision++;
            await ReadDrawPathCoordinates();ResetPathHistory();await RecordPathEdit();activeTab="gameplay";
            if(editablePathPlan is not null)await PreviewPlannedPath(editablePathPlan,smoothPathPreview);
            await JS.InvokeVoidAsync("restoreProjectView",p.Authoring);
            statusMessage="Project restored. The route remains editable; Apply path updates the saved item. Reload external textures/dependencies if required.";
            StateHasChanged();
        }catch(Exception ex){errorMessage="Project item loaded, but authoring state could not be restored: "+ex.Message;}
    }
    private sealed class ProjectBrowserFile(string name,byte[] bytes):IBrowserFile
    {
        public string Name=>name;public DateTimeOffset LastModified=>DateTimeOffset.UtcNow;public long Size=>bytes.Length;public string ContentType=>"application/octet-stream";
        public Stream OpenReadStream(long maxAllowedSize=512000,CancellationToken cancellationToken=default){if(bytes.Length>maxAllowedSize)throw new IOException("Project item is too large.");return new MemoryStream(bytes,false);}
    }
}
