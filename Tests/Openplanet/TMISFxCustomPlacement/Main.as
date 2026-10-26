// DEV-only, one-shot native placement probe. A deliberately written request
// is consumed before the engine call; a reload never retries it.
// Windowless engine-only fixture: the JSON result and fresh log are its test surface.
#if DEV
const string Prefix = "[TMIS-FX-CUSTOM] ";
const string Request = "TMItemStudioPlacementProbe/fx-request.json";
const string Result = "TMItemStudioPlacementProbe/fx-result.json";

void Main() {
    const string requestPath = IO::FromUserGameFolder(Request);
    if (!IO::FileExists(requestPath)) return;
    auto result = Json::Object();
    try {
        auto request = Json::FromFile(requestPath);
        IO::Delete(requestPath);
        if (!request.HasKey("armed") || !bool(request["armed"])) throw("request not armed");
        result["runId"] = string(request["runId"]);
        bool inspectOnly = request.HasKey("inspectOnly") && bool(request["inspectOnly"]);
        bool inspectAnchored = request.HasKey("inspectAnchoredIndex");
        if (inspectAnchored && !inspectOnly) throw("anchored-model inspection requires inspectOnly");
        yield(10);
        auto editor = cast<CGameCtnEditorFree>(GetApp().Editor);
        if (editor is null || editor.Challenge is null) throw("map editor required");
        CGameItemModel@ model;
        string path = "";
        if (inspectAnchored) {
            if (request["inspectAnchoredIndex"].GetType() != Json::Type::Number) throw("anchor index must be an integer");
            int index = int(request["inspectAnchoredIndex"]);
            if (double(request["inspectAnchoredIndex"]) != double(index)) throw("anchor index must be an integer");
            if (index < 0 || uint(index) >= editor.Challenge.AnchoredObjects.Length) throw("anchor index out of range");
            auto anchoredItem = editor.Challenge.AnchoredObjects[uint(index)];
            if (anchoredItem is null || anchoredItem.ItemModel is null) throw("anchor or its item model is null");
            @model = anchoredItem.ItemModel;
            result["modelSource"] = "editorAnchoredItem";
            result["inspectAnchoredIndex"] = index;
            trace(Prefix + "INSPECT ANCHORED index=" + index);
        } else {
            path = string(request["path"]);
            if (!path.StartsWith("Items/TMIS_FX_") || path.Contains("..")) throw("test item path required");
            result["modelSource"] = "userFidPreload";
            result["path"] = path;
            if (!inspectOnly && (editor.PluginMapType is null || !editor.PluginMapType.IsEditorReadyForRequest)) throw("editor blocked or not ready");
            auto fid = Fids::GetUser(path);
            if (fid is null) throw("user FID not found: " + path);
            trace(Prefix + "CALL PRELOAD " + path);
            @model = cast<CGameItemModel>(Fids::Preload(fid));
            if (model is null) throw("native preload failed: " + path);
        }
        result["probeVersion"] = "bitmap-runtime-v4-descriptor";
        result["idName"] = string(model.IdName);
        result["nativeName"] = string(model.Name);
        result["nativeAuthor"] = model.Author.GetName();
        result["nativeDescription"] = string(model.Description);
        bool inspectBitmap = request.HasKey("inspectBitmap") && bool(request["inspectBitmap"]);
        bool inspectRenderInfo = request.HasKey("inspectRenderInfo") && bool(request["inspectRenderInfo"]);
        result["fx"] = InspectFx(model, inspectBitmap, inspectRenderInfo);
        if (inspectOnly) {
            Json::ToFile(IO::FromUserGameFolder(Result), result);
            trace(Prefix + "RESULT " + Json::Write(result));
            return;
        }
        if (editor.PluginMapType is null || !editor.PluginMapType.IsEditorReadyForRequest) throw("editor blocked or not ready");
        result["before"] = editor.Challenge.AnchoredObjects.Length;
        vec3 pos(float(request["x"]), float(request["y"]), float(request["z"]));
        auto spec = Editor::MakeItemSpec(model, pos, vec3(0, 0, 0));
        Editor::ItemSpec@[] items = { spec };
        trace(Prefix + "CALL PLACE " + path + " id=" + string(model.IdName));
        result["placeReturned"] = Editor::PlaceItems(items, true);
        yield(2);
        result["after"] = editor.Challenge.AnchoredObjects.Length;
        auto anchored = Json::Array();
        for (uint i = 0; i < editor.Challenge.AnchoredObjects.Length; i++) {
            auto item = editor.Challenge.AnchoredObjects[i];
            if (item is null || item.ItemModel is null) continue;
            auto row = Json::Object();
            row["idName"] = string(item.ItemModel.IdName);
            row["x"] = item.AbsolutePositionInMap.x;
            row["y"] = item.AbsolutePositionInMap.y;
            row["z"] = item.AbsolutePositionInMap.z;
            anchored.Add(row);
        }
        result["anchored"] = anchored;
    } catch { result["error"] = getExceptionInfo(); }
    Json::ToFile(IO::FromUserGameFolder(Result), result);
    trace(Prefix + "RESULT " + Json::Write(result));
}

Json::Value@ InspectFx(CGameItemModel@ model, bool inspectBitmap, bool inspectRenderInfo) {
    auto rows = Json::Array();
    auto prefab = cast<CPlugPrefab>(model.EntityModel);
    if (prefab !is null) {
        AppendPrefabFx(prefab, rows, -1, inspectBitmap, inspectRenderInfo);
        return rows;
    }
    // Same typed traversal as TMItemStudioSmokeTest/src/Main.as; bounded here
    // to eight variants and one direct prefab per variant. No preload/resolve.
    auto variants = cast<NPlugItem_SVariantList>(model.EntityModel);
    if (variants is null) return rows;
    if (variants.Variants.Length > 8) {
        auto skipped = Json::Object();
        skipped["skipped"] = "variant count exceeds bounded inspection limit8";
        skipped["variantCount"] = variants.Variants.Length;
        rows.Add(skipped);
        return rows;
    }
    for (uint v = 0; v < variants.Variants.Length; v++) {
        auto variantPrefab = cast<CPlugPrefab>(variants.Variants[v].EntityModel);
        if (variantPrefab !is null) AppendPrefabFx(variantPrefab, rows, int(v), inspectBitmap, inspectRenderInfo);
    }
    return rows;
}

void AppendPrefabFx(CPlugPrefab@ prefab, Json::Value@ rows, int variant, bool inspectBitmap, bool inspectRenderInfo) {
    for (uint e = 0; e < prefab.Ents.Length; e++) {
        auto fx = cast<CPlugFxSystem>(prefab.Ents[e].Model);
        if (fx is null) continue;
        auto parallel = cast<CPlugFxSystemNode_Parallel>(fx.RootNode);
        if (parallel is null) continue;
        for (uint c = 0; c < parallel.Children.Length; c++) {
            auto emitter = cast<CPlugFxSystemNode_ParticleEmitter>(parallel.Children[c]);
            if (emitter is null || emitter.Model is null) continue;
            for (uint s = 0; s < emitter.Model.ParticleEmitterSubModels.Length; s++) {
                auto sub = emitter.Model.ParticleEmitterSubModels[s];
                if (sub is null || sub.GpuModel is null) continue;
                auto row = Json::Object();
                row["variant"] = variant;
                row["entity"] = e; row["child"] = c; row["submodel"] = s;
                row["localOffset"] = string(emitter.LocalOffsetExpr);
                // Read-only documented GPU bitmap slot (research/Foggers.md).
                auto bitmap = cast<CPlugBitmap>(Dev::GetOffsetNod(sub.GpuModel, 0x70));
                row["bitmapNull"] = bitmap is null;
                if (bitmap !is null) {
                    auto bitmapFid = cast<CSystemFidFile>(GetFidFromNod(bitmap));
                    row["bitmapFid"] = bitmapFid is null ? "" : string(bitmapFid.FileName);
                    if (inspectBitmap) row["bitmapRuntime"] = InspectBitmap(bitmap);
                    if (inspectRenderInfo) row["renderInfo"] = InspectRenderInfo(bitmap);
                }
                rows.Add(row);
            }
        }
    }
}

// Typed read-only API from current Openplanet.h, CPlugBitmap/CPlugFileImg/
// CPlugFileGen. No ReGenerate, setters, loading, format conversion or writes.
Json::Value@ InspectBitmap(CPlugBitmap@ bitmap) {
    auto report = Json::Object();
    report["usage"] = int(bitmap.Usage);
    report["ldrColorSpace"] = int(bitmap.LdrColorSpace);
    report["wantedColorDepth"] = int(bitmap.WantedColorDepth);
    report["pixelUpdate"] = int(bitmap.PixelUpdate);
    report["dynamic"] = int(bitmap.Dynamic);
    report["texFilter"] = int(bitmap.TexFilter);
    report["wantMipMapping"] = bitmap.WantMipMapping;
    report["floatRequireFiltering"] = bitmap.FloatRequireFiltering;
    report["canBeCompressedInVideoMemory"] = bitmap.CanBeCompressedInVideoMemory;
    report["canBeDeletedFromSystemMemory"] = bitmap.CanBeDeletedFromSystemMemory;
    report["renderTexelsMustPersist"] = bitmap.RenderTexelsMustPersist;
    report["force1stPixelAlpha0"] = bitmap.Force1stPixelAlpha0;
    report["ignoreImageAlpha01"] = bitmap.IgnoreImageAlpha01;
    auto image = bitmap.Image;
    report["imageNull"] = image is null;
    if (image is null) return report;
    auto imageReport = Json::Object();
    auto imageType = Reflection::TypeOf(image);
    imageReport["className"] = imageType is null ? "" : string(imageType.Name);
    imageReport["width"] = image.Width;
    imageReport["height"] = image.Height;
    imageReport["depth"] = image.Depth;
    imageReport["arraySize"] = image.ArraySize;
    imageReport["components"] = image.NbComp;
    imageReport["dimension"] = int(image.Dimension);
    imageReport["format"] = int(image.Format);
    imageReport["channelType"] = int(image.ChannelType);
    imageReport["mipLevels"] = image.cMipLevel;
    imageReport["mipLevelsSkippedAtLoad"] = image.cMipLevelSkipAtLoad;
    imageReport["originIsTop"] = image.OriginIsTop;
    imageReport["isInSystemMemory"] = image.IsInSystemMemory;
    imageReport["systemKb"] = image.SystemKb;
    auto generator = cast<CPlugFileGen>(image);
    imageReport["isFileGen"] = generator !is null;
    if (generator !is null) imageReport["genKind"] = int(generator.GenKind);
    report["image"] = imageReport;
    return report;
}

// Optional research-only resource presence observation. Off by default.
// Exact existing E++ documentation, not inferred class-member offsets:
// src/DevStructs/Plug/CPlugBitmap.as: native CPlugBitmap size448, RenderInfo+A8;
// src/DevStructs/Game/Viewport.as: DRenderInfo.Texture+258.
// Additional native RE: research/2026-08-24-ControlQuadBitmapBind.md and
// research/2026-08-24-CalculatingShadowsDetect.md, Trackmania.exe imagebase
// 0x140000000, observed 2026-08-24: CPlugBitmap+178 GPU descriptor;
// CPlugBitmap_GetGpuWidth/Height reads descriptor+28/+2C.
// These offsets are candidate old-build layout evidence, not established by
// the448-byte guard alone. Safe reads are optional and never dereference/write
// raw pointers in AngelScript. Nonzero addresses/dimensions do not prove pixels.
// Safe reads report address presence only; they do not prove image contents,
// upload success, a valid GPU view, particle spawning or visible rendering.
Json::Value@ InspectRenderInfo(CPlugBitmap@ bitmap) {
    auto report = Json::Object();
    report["readOnly"] = true;
    auto bitmapType = Reflection::TypeOf(bitmap);
    if (bitmapType is null || bitmapType.Size != 448) {
        report["skipped"] = "CPlugBitmap does not match documented448-byte native layout";
        return report;
    }
    try {
        uint64 info = Dev::GetOffsetUint64(bitmap, 0xA8);
        report["renderInfoNull"] = info == 0;
        if (info != 0) {
            uint64 texture = Dev::SafeReadUInt64(info + 0x258);
            report["textureNull"] = texture == 0;
        }
        report["descriptorOffsetEvidence"] = "E++ native RE2026-08-24; CPlugBitmap+178, size+28/+2C; current build offsets unproven";
        uint64 descriptor = Dev::GetOffsetUint64(bitmap, 0x178);
        report["gpuDescriptorNull"] = descriptor == 0;
        if (descriptor != 0) {
            uint width = Dev::SafeReadUInt32(descriptor + 0x28);
            uint height = Dev::SafeReadUInt32(descriptor + 0x2C);
            report["gpuDescriptorWidth"] = width;
            report["gpuDescriptorHeight"] = height;
            report["gpuDescriptorDimensionsBounded"] = width > 0 && width <= 16384 && height > 0 && height <= 16384;
        }
    } catch { report["error"] = getExceptionInfo(); }
    return report;
}
#endif
