namespace Editor {
    import void OpenItemEditor(CGameCtnEditorFree@ editor, CGameItemModel@ model) from "Editor";
    import void SaveItemAsEditorAsync(const string &in path) from "Editor";
}

string sourcePath = "BF2_ASSETS/Collectable_Base.Item.Gbx";
string destinationPath = "TMItemStudio/Collectable_Base_Pivot.Item.Gbx";
int pivotIndex = 0;
float pivotX = 0;
float pivotY = 0;
float pivotZ = 0;
string status = "Open a map editor, then select a source and destination.";
bool savePending = false;
string pendingDestination;
bool windowVisible = false;
const string RequestPath = "TMItemStudioNativeBridge/native-pivot-request.json";

string NormalizeRelativeItemPath(const string &in path)
{
    auto normalized = path.Replace("\\", "/").Trim();
    while (normalized.StartsWith("/")) normalized = normalized.SubStr(1);
    if (normalized.StartsWith("Items/")) normalized = normalized.SubStr(6);
    return normalized;
}

void StartNativeSave()
{
    auto editor = cast<CGameCtnEditorFree>(GetApp().Editor);
    if (editor is null) {
        status = "FAIL: Open the map editor before applying a pivot.";
        return;
    }

    auto source = NormalizeRelativeItemPath(sourcePath);
    auto destination = NormalizeRelativeItemPath(destinationPath);
    if (source == destination) {
        status = "FAIL: Source and destination must differ.";
        return;
    }
    if (IO::FileExists(IO::FromUserGameFolder(destination))) {
        status = "FAIL: Destination already exists; choose a new filename.";
        return;
    }

    auto model = cast<CGameItemModel>(Fids::Preload(Fids::GetUser("Items/" + source)));
    if (model is null || model.DefaultPlacementParam_Content is null) {
        status = "FAIL: Source has no native item placement data.";
        return;
    }
    auto pivots = model.DefaultPlacementParam_Content.m_PivotPositions;
    if (pivotIndex < 0 || pivotIndex > int(pivots.Length)) {
        status = "FAIL: Pivot index can only replace an existing pivot or append the next pivot.";
        return;
    }

    auto requestedPivot = vec3(pivotX, pivotY, pivotZ);
    if (pivotIndex == int(pivots.Length)) {
        model.DefaultPlacementParam_Content.AddPivotPosition();
        model.DefaultPlacementParam_Content.m_PivotPositions[pivotIndex] = requestedPivot;
        status = "Added pivot " + pivotIndex + ". Opening the native Item Editor...";
    } else {
        pivots[pivotIndex] = requestedPivot;
        status = "Updated pivot " + pivotIndex + ". Opening the native Item Editor...";
    }
    pendingDestination = destination;
    savePending = true;
    Editor::OpenItemEditor(editor, model);
}

void LoadDownloadedRequest()
{
    auto fullPath = IO::FromUserGameFolder(RequestPath);
    if (!IO::FileExists(fullPath)) {
        status = "FAIL: Request not found at Documents/Trackmania/Items/" + RequestPath;
        return;
    }

    auto request = Json::Parse(IO::FileToString(fullPath));
    if (int(request["version"]) != 1) {
        status = "FAIL: Unsupported native pivot request version.";
        return;
    }

    sourcePath = string(request["sourcePath"]);
    destinationPath = string(request["destinationPath"]);
    pivotIndex = int(request["pivotIndex"]);
    pivotX = float(request["x"]);
    pivotY = float(request["y"]);
    pivotZ = float(request["z"]);
    status = "Loaded native pivot request. Review the values, then apply the native save.";
}

void Main()
{
    while (true) {
        if (savePending && cast<CGameEditorItem>(GetApp().Editor) !is null) {
            Editor::SaveItemAsEditorAsync(pendingDestination);
            savePending = false;
            status = "Native save started. Wait for completion, leave the Item Editor, then restart Trackmania before opening the map editor again.";
            trace("[TMIS-NATIVE] SAVE-STARTED " + pendingDestination);
        }
        yield();
    }
}

void RenderMenu()
{
    if (UI::MenuItem("TM Item Studio native pivot bridge")) {
        windowVisible = !windowVisible;
    }
}

void Render()
{
    if (!windowVisible) return;
    if (!UI::Begin("TM Item Studio native pivot bridge", windowVisible)) {
        UI::End();
        return;
    }

    UI::TextWrapped("Uses Editor++ to save through Trackmania's native item writer. Source and destination are relative to Documents/Trackmania/Items.");
    sourcePath = UI::InputText("Source", sourcePath);
    destinationPath = UI::InputText("Destination", destinationPath);
    pivotIndex = UI::InputInt("Pivot index", pivotIndex);
    pivotX = UI::InputFloat("Pivot X", pivotX, 0.1);
    pivotY = UI::InputFloat("Pivot Y", pivotY, 0.1);
    pivotZ = UI::InputFloat("Pivot Z", pivotZ, 0.1);
    if (UI::Button("Load downloaded request")) LoadDownloadedRequest();
    UI::SameLine();
    if (UI::Button("Apply pivot and save natively")) StartNativeSave();
    UI::Separator();
    UI::TextWrapped(status);
    UI::End();
}
