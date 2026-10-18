// TM Item Studio native placement probe.
//
// Asks Trackmania's own editor to work with a Studio item, then reports what the engine did.
// Three rules exist because an earlier version crashed an editor session:
//
//   1. A request must be armed on purpose. An unarmed request logs SKIP and does nothing.
//   2. One request performs exactly one operation ("op"). Nothing runs a chain of engine calls,
//      so a crash can only ever implicate the single operation that was asked for.
//   3. Nothing runs until the editor has been present for `settleTicks` frames, so the probe
//      never touches a map while the editor is still loading it.
//
// Every engine call is announced with CALL before it happens and DONE after it returns.
// No offsets and no memory writes; the calls are the ones shipping editor plugins use.

const string RequestRel = "TMItemStudioPlacementProbe/placement-request.json";
const string ResultRel = "TMItemStudioPlacementProbe/placement-result.json";
const string LogPrefix = "[TMIS-PLACE] ";

const uint DefaultSettleTicks = 240;   // roughly four seconds of frames

string status = "Waiting for " + RequestRel + ".";
string lastRequestRaw = "";
bool windowVisible = false;
uint editorTicks = 0;

void Main() {
    while (true) {
        PollRequest();
        yield();
    }
}

void Log(const string &in message) {
    trace(LogPrefix + message);
}

string JsonString(Json::Value@ value, const string &in key) {
    if (value is null || !value.HasKey(key)) return "";
    return string(value[key]);
}

double JsonDouble(Json::Value@ value, const string &in key, double fallback) {
    if (value is null || !value.HasKey(key)) return fallback;
    return double(value[key]);
}

bool JsonBool(Json::Value@ value, const string &in key, bool fallback) {
    if (value is null || !value.HasKey(key)) return fallback;
    return bool(value[key]);
}

int PivotCount(CGameItemModel@ model) {
    auto placement = model is null ? null : model.DefaultPlacementParam_Content;
    return placement is null ? -1 : int(placement.m_PivotPositions.Length);
}

int MagnetCount(CGameItemModel@ model) {
    auto placement = model is null ? null : model.DefaultPlacementParam_Content;
    return placement is null ? -1 : int(placement.m_MagnetLocs_Degrees.Length);
}

Json::Value@ DescribeItem(CGameItemModel@ model) {
    auto entry = Json::Object();
    entry["itemModelNull"] = model is null;
    entry["itemModelIdName"] = model is null ? "" : model.IdName;
    entry["itemModelName"] = model is null ? "" : string(model.Name);
    entry["pivotCount"] = PivotCount(model);
    entry["magnetCount"] = MagnetCount(model);
    return entry;
}

Json::Value@ DescribeAnchoredObject(CGameCtnAnchoredObject@ anchored, CGameItemModel@ probeItem) {
    auto entry = DescribeItem(anchored.ItemModel);
    entry["isProbeItem"] = anchored.ItemModel !is null && anchored.ItemModel.IdName == probeItem.IdName;
    entry["x"] = double(anchored.AbsolutePositionInMap.x);
    entry["y"] = double(anchored.AbsolutePositionInMap.y);
    entry["z"] = double(anchored.AbsolutePositionInMap.z);
    entry["yawRad"] = double(anchored.Yaw);
    entry["yawDeg"] = double(anchored.Yaw) * 57.29577951308232;
    entry["pitchRad"] = double(anchored.Pitch);
    entry["rollRad"] = double(anchored.Roll);
    entry["scale"] = double(anchored.Scale);
    entry["variant"] = int(anchored.IVariant);
    entry["isFlying"] = anchored.IsFlying;
    auto placement = anchored.ItemModel is null ? null : anchored.ItemModel.DefaultPlacementParam_Content;
    if (placement !is null) {
        entry["yawOnly"] = placement.YawOnly;
        entry["notOnObject"] = placement.NotOnObject;
        entry["autoRotation"] = placement.AutoRotation;
        entry["switchPivotManually"] = placement.SwitchPivotManually;
        entry["gridSnapHStep"] = double(placement.GridSnap_HStep);
        entry["gridSnapVStep"] = double(placement.GridSnap_VStep);
        entry["pivotSnapDistance"] = double(placement.PivotSnap_Distance);
    }
    return entry;
}

void WriteResult(Json::Value@ result, const string &in finalStatus) {
    result["status"] = finalStatus;
    Json::ToFile(IO::FromUserGameFolder(ResultRel), result);
    status = finalStatus;
    Log("RESULT " + Json::Write(result));
}

// ---------------------------------------------------------------------------------------------
// Request handling

void PollRequest() {
    auto path = IO::FromUserGameFolder(RequestRel);
    if (!IO::FileExists(path)) return;

    IO::File file(path, IO::FileMode::Read);
    string raw = file.ReadToEnd();
    file.Close();
    if (raw.Length == 0 || raw == lastRequestRaw) return;

    Json::Value@ request = Json::Parse(raw);
    if (request is null || request.GetType() != Json::Type::Object) {
        lastRequestRaw = raw;
        Log("FAIL request-is-not-an-object");
        return;
    }

    string runId = JsonString(request, "runId");

    // Rule 1: nothing happens unless the request is armed.
    if (!JsonBool(request, "armed", false)) {
        lastRequestRaw = raw;
        status = "Request present but not armed; nothing will be executed.";
        Log("SKIP request-not-armed " + runId);
        return;
    }

    // The app handle is not valid during the first frames of the process, so it is checked
    // before anything else is touched.
    auto app = GetApp();
    if (app is null) {
        editorTicks = 0;
        return;
    }

    auto editor = cast<CGameCtnEditorFree>(app.Editor);
    if (editor is null) {
        editorTicks = 0;
        status = "Armed request found. Open the map editor to run it.";
        return;                       // the request is kept, not consumed
    }

    // Rule 3: never touch a map while the editor is still loading it.
    uint settleTicks = uint(JsonDouble(request, "settleTicks", double(DefaultSettleTicks)));
    editorTicks++;
    if (editorTicks < settleTicks) {
        status = "Waiting for the editor to settle: " + editorTicks + "/" + settleTicks + " frames.";
        return;
    }

    lastRequestRaw = raw;
    string op = JsonString(request, "op");
    if (op.Length == 0) op = "status";

    Log("BEGIN op=" + op + " run=" + runId + " settled=" + editorTicks + " frames");
    // Rule 2: exactly one operation per request.
    if (op == "status") RunStatus(editor, request);
    else if (op == "place") RunPlace(editor, request);
    else if (op == "instance-items") RunInstanceItems(editor, request);
    else if (op == "autosave") RunAutoSave(editor, request);
    else WriteResult(Result(editor, request), "unknown-op");
}

Json::Value@ Result(CGameCtnEditorFree@ editor, Json::Value@ request) {
    auto result = Json::Object();
    result["runId"] = JsonString(request, "runId");
    result["op"] = JsonString(request, "op");
    string mapName = string(editor.Challenge.MapName);
    result["mapName"] = mapName;
    result["blocks"] = int(editor.Challenge.Blocks.Length);
    result["objectsBefore"] = int(editor.Challenge.AnchoredObjects.Length);
    auto map = cast<CSmEditorPluginMapType>(editor.PluginMapType);
    result["pluginMapTypeAvailable"] = map !is null;
    if (map !is null) result["placeMode"] = int(map.PlaceMode);
    return result;
}

// ---------------------------------------------------------------------------------------------
// Operations. Each one is a single, separately armed step.

// Read-only: what the engine knows about this item and this macroblock. No placement.
void RunStatus(CGameCtnEditorFree@ editor, Json::Value@ request) {
    auto result = Result(editor, request);

    Log("CALL Fids::GetUser (item)");
    auto itemFid = Fids::GetUser("Items/" + JsonString(request, "itemRelativePath"));
    Log("CALL Fids::Preload (item)");
    CGameItemModel@ probeItem = itemFid is null ? null : cast<CGameItemModel>(Fids::Preload(itemFid));
    result["source"] = DescribeItem(probeItem);

    Log("CALL Fids::GetUser (macroblock)");
    auto macroblockFid = Fids::GetUser(JsonString(request, "macroblockRelativePath"));
    Log("CALL Fids::Preload (macroblock)");
    CGameCtnMacroBlockInfo@ macroblock = macroblockFid is null
        ? null : cast<CGameCtnMacroBlockInfo>(Fids::Preload(macroblockFid));
    result["macroblockFromUserFid"] = macroblock !is null;

    if (macroblock is null) {
        auto map = cast<CSmEditorPluginMapType>(editor.PluginMapType);
        if (map is null) {
            Log("FAIL plugin-map-type-unavailable");
            WriteResult(result, "plugin-map-type-unavailable");
            return;
        }
        Log("CALL GetMacroblockModelFromFilePath");
        try {
            @macroblock = map.GetMacroblockModelFromFilePath(JsonString(request, "macroblockRelativePath"));
        } catch {
            result["macroblockError"] = getExceptionInfo();
        }
        result["macroblockFromFilePath"] = macroblock !is null;
    }

    Log("STATUS item=" + (probeItem is null ? "null" : probeItem.IdName)
        + " pivots=" + PivotCount(probeItem) + " magnets=" + MagnetCount(probeItem)
        + " macroblock=" + (macroblock is null ? "null" : "loaded"));
    WriteResult(result, "completed");
}

// The only operation that writes to the map.
void RunPlace(CGameCtnEditorFree@ editor, Json::Value@ request) {
    auto result = Result(editor, request);

    // Only a map that matches the requested name, or a map the game has not named yet that holds
    // no items. The block count is deliberately not used: a new map already has thousands of
    // blocks because Trackmania builds its own base platform.
    string mapName = string(editor.Challenge.MapName);
    uint objectsBefore = editor.Challenge.AnchoredObjects.Length;
    string prefix = JsonString(request, "mapNamePrefix");
    bool nameAllowed = prefix.Length == 0 || mapName.StartsWith(prefix);
    bool unnamedScratch = JsonBool(request, "acceptUnnamedMap", false)
        && (mapName.Length == 0 || mapName == "Unnamed") && objectsBefore == 0;
    if (!nameAllowed && !unnamedScratch) {
        Log("FAIL map-not-allowed '" + mapName + "' objects=" + objectsBefore);
        WriteResult(result, "map-name-not-allowed");
        return;
    }
    result["acceptedBy"] = nameAllowed ? "name" : "unnamed-scratch-map";

    Log("CALL Fids::Preload (macroblock)");
    auto macroblockFid = Fids::GetUser(JsonString(request, "macroblockRelativePath"));
    CGameCtnMacroBlockInfo@ macroblock = macroblockFid is null
        ? null : cast<CGameCtnMacroBlockInfo>(Fids::Preload(macroblockFid));
    if (macroblock is null) {
        Log("FAIL macroblock-not-loaded");
        WriteResult(result, "macroblock-not-loaded");
        return;
    }

    double tx = JsonDouble(request["targetCoord"], "x", 0.0);
    double ty = JsonDouble(request["targetCoord"], "y", 0.0);
    double tz = JsonDouble(request["targetCoord"], "z", 0.0);
    string directionName = JsonString(request, "direction");
    auto direction = CGameEditorPluginMap::ECardinalDirections::North;
    if (directionName == "East") direction = CGameEditorPluginMap::ECardinalDirections::East;
    else if (directionName == "South") direction = CGameEditorPluginMap::ECardinalDirections::South;
    else if (directionName == "West") direction = CGameEditorPluginMap::ECardinalDirections::West;

    auto map = cast<CSmEditorPluginMapType>(editor.PluginMapType);
    bool placed = false;
    Log("CALL PlaceMacroblock_NoDestruction");
    try {
        placed = map.PlaceMacroblock_NoDestruction(macroblock, int3(int(tx), int(ty), int(tz)), direction);
    } catch {
        result["placementError"] = getExceptionInfo();
    }
    Log("DONE PlaceMacroblock_NoDestruction=" + placed);
    result["placed"] = placed;
    result["objectsAfter"] = int(editor.Challenge.AnchoredObjects.Length);
    WriteResult(result, placed ? "completed" : "placement-refused");
}

// Read what a placed macroblock instance resolves its items to.
void RunInstanceItems(CGameCtnEditorFree@ editor, Json::Value@ request) {
    auto result = Result(editor, request);
    auto map = cast<CSmEditorPluginMapType>(editor.PluginMapType);

    auto itemFid = Fids::GetUser("Items/" + JsonString(request, "itemRelativePath"));
    CGameItemModel@ probeItem = itemFid is null ? null : cast<CGameItemModel>(Fids::Preload(itemFid));
    if (probeItem is null) {
        Log("FAIL probe-item-not-loaded");
        WriteResult(result, "probe-item-not-loaded");
        return;
    }

    Log("CALL GetLatestMacroblockInstance");
    auto instance = map.GetLatestMacroblockInstance();
    result["latestInstanceFound"] = instance !is null;

    auto items = Json::Array();
    if (instance !is null) {
        Log("CALL ComputeItemsForMacroblockInstance");
        try {
            map.ComputeItemsForMacroblockInstance(instance);
            Log("DONE ComputeItemsForMacroblockInstance");
            auto results = map.MacroblockInstanceItemsResults;
            for (uint i = 0; i < results.Length; i++) {
                if (results[i] is null) continue;
                auto entry = DescribeItem(results[i].ItemModel);
                entry["index"] = int(i);
                entry["x"] = double(results[i].Position.x);
                entry["y"] = double(results[i].Position.y);
                entry["z"] = double(results[i].Position.z);
                items.Add(entry);
            }
        } catch {
            result["instanceError"] = getExceptionInfo();
        }
    }
    result["instanceItems"] = items;
    result["instanceItemCount"] = int(items.Length);

    Log("INSTANCE ITEMS " + int(items.Length) + " of " + int(editor.Challenge.AnchoredObjects.Length) + " anchored");
    WriteResult(result, "completed");
}

void RunAutoSave(CGameCtnEditorFree@ editor, Json::Value@ request) {
    auto result = Result(editor, request);
    auto map = cast<CSmEditorPluginMapType>(editor.PluginMapType);
    if (map is null) {
        Log("FAIL plugin-map-type-unavailable");
        WriteResult(result, "plugin-map-type-unavailable");
        return;
    }
    bool saved = false;
    Log("CALL AutoSave");
    try {
        map.AutoSave();
        saved = true;
    } catch {
        result["autoSaveError"] = getExceptionInfo();
    }
    result["autoSaved"] = saved;
    Log("DONE AutoSave=" + saved);
    WriteResult(result, "completed");
}

void RenderMenu() {
    if (UI::MenuItem("TM Item Studio placement probe")) windowVisible = !windowVisible;
}

void Render() {
    if (!windowVisible) return;
    if (!UI::Begin("TM Item Studio placement probe", windowVisible)) {
        UI::End();
        return;
    }
    UI::TextWrapped("One operation per armed request. Every engine call is announced in Openplanet.log.");
    UI::Separator();
    UI::TextWrapped("Request : Documents/Trackmania/" + RequestRel);
    UI::TextWrapped("Result  : Documents/Trackmania/" + ResultRel);
    UI::Separator();
    UI::TextWrapped("Status: " + status);
    UI::TextWrapped("Editor ticks: " + editorTicks);
    if (UI::Button("Forget processed request")) {
        lastRequestRaw = "";
        status = "Will run the request file again on the next tick.";
    }
    UI::End();
}
